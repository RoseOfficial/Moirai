# Moirai — Design Specification

**Date:** 2026-08-28
**Status:** Approved design, pre-implementation
**Repo:** RoseOfficial/Moirai

---

## 1. Overview

Moirai is a Dalamud plugin that automates FATE farming in FFXIV. It selects the best available FATE, travels to it, syncs, fights it to gold credit, and repeats — across zones, with full recovery from death, stuck states, and interruptions. Its flagship v1 mode automates the Yo-kai Watch event end to end: minion management, legendary medal tracking per yokai, and zone rotation until every selected yokai is capped.

Moirai is named for the three Fates of Greek myth and ships under the RoseOfficial brand alongside Olympus, Ariadne, and Komos.

### Goals (v1)

- A robust core FATE loop covering battle, boss, defend, escort, collect, and NPC-started FATEs.
- The Yo-kai module: fully automatic medal farming with per-yokai zone rotation (event ends 2026-10-05).
- A decision core that is pure and unit-tested — every known edge case in this category encoded as a test, not a bug report.
- Zone and FATE knowledge as updatable data files, so game patches are data fixes, not plugin releases.

### Non-goals (v1, deliberately deferred)

Chocobo companion management, consumables (food/potion), gear repair, bicolor gemstone shopping, Atma/relic collection modes, leveling mode, instance switching, retainer/GC housekeeping. All are proven bolt-ons in comparable tools and none block the Yo-kai use case.

### Constraints

- Dependencies (required): vnavmesh (navigation), a combat backend (see §6), Lifestream (teleport), TextAdvance (dialog). Optional: BossMod Reborn (dodging).
- Reference material (autofate, AutoDuty, the pot0to FATE script) is all-rights-reserved. Concepts and observed facts only; no code is copied. The `reference/` folder is untracked.
- Single plugin project; ECommons for IPC plumbing, throttling, and config.

---

## 2. Architecture

The core principle: **observe, decide, and act are separate layers, and the deciding layer is pure.**

```
 Framework.Update tick
        │
        ▼
 ┌─────────────┐    ┌──────────────────────────┐    ┌──────────────┐
 │  Snapshot    │──▶│  Planner (pure)           │──▶│  Executors    │
 │  (all game   │    │  Director → Module →     │    │  (game + IPC │
 │  reads, once │    │  Behaviors → FateSelector │    │  calls, throt-│
 │  per tick)   │    │  emits Intents            │    │  tled)        │
 └─────────────┘    └──────────────────────────┘    └──────────────┘
```

### 2.1 Snapshot layer

Once per tick, one component reads everything the planner may need and produces an immutable `WorldSnapshot`: player (position, level, job, mounted/flying/casting/dead/in-combat, active statuses), a projection of the FATE table (id, name, position, radius, progress, time model, state, classification inputs), current target, relevant inventory counts (medal items, event items), zone/instance, and condition flags. No other component reads game state for decision-making.

### 2.2 Planner layer (pure)

Consumes `(WorldSnapshot, Configuration, PlanState)` and returns intents. No game calls, no statics, clock injected. Composed of:

- **Director** — lifecycle (Idle / Running / Paused / Stopped-with-reason) and the interrupt ladder (§3). Delegates strategy to the active module and tactics to behaviors.
- **Module (`IFarmModule`)** — strategy: which zone to farm, when to rotate, when the session is done. v1 ships `YokaiModule` (§8) and `SingleZoneModule`; `ZoneRotationModule` (Appendix A, G-series) farms a user's zone list in turn. The Director tells the module each tick how long selection has come up empty in the current zone (`ModuleContext.IdleSeconds`, G6).
- **Behaviors** — tactical units, each a small explicit state machine with enter/tick/exit and bounded retries: `TravelBehavior`, `EngageBehavior`, `CollectBehavior`, `NpcStartBehavior`, `RecoverBehavior`, `IdleBehavior`. One behavior is active at a time; the Director owns transitions.
- **FateSelector** — pure scoring over the snapshot's FATE projection (§4).

Intents (the planner's entire output vocabulary): `GoTo(pos, fly, tolerance)`, `Mount`, `Dismount`, `Engage(targetId)`, `ClearTarget`, `Sync`, `Interact(objectId)`, `Teleport(aetheryteId)`, `ChangeZone(territoryId)`, `SwapMinion(companionId)`, `EquipItem(itemId)`, `SetCombat(on/off, mode)`, `Stop(reason)`, `Wait(ms)`, `None`.

### 2.3 Executor layer

Thin adapters, one per concern, that turn intents into game/IPC calls: `MovementExecutor` (vnavmesh), `CombatExecutor` (backend of §6), `ActionExecutor` (mount/dismount/sync/interact/minion via `ActionManager`/`FateManager`/`TargetSystem`), `TravelExecutor` (Lifestream/Telepo). Executors own throttling (named throttles per action class), idempotence (re-issuing an in-flight intent is a no-op), and report per-intent outcome back into `PlanState` so the planner sees failures.

**Single-owner targeting rule:** exactly one system commands the target at any moment. When a collect turn-in or NPC interaction owns targeting, the combat backend is disengaged first; when combat owns it, nothing else issues `/target`-class actions. This rule prevents the target tug-of-war that plagues comparable tools.

---

## 3. Interrupts and the busy guard

Evaluated every tick before any planning, in priority order:

1. **Busy guard** — no intent is executed while the player is casting, between areas, jumping, being moved, mid-mount/dismount animation, occupied by a game UI, or while Lifestream reports busy. The planner still observes; the executors hold. An open dialog (Talk, yes/no, hand-in window) is the exception while the NPC-start or collect behavior is active: those handle it themselves (B4).
2. **Death** — overrides everything (§7.3).
3. **Unexpected combat** — in combat while not inside the current FATE (stray aggro, adds that followed): stop movement, target whatever is on us (or our companion), engage defensively until nothing is, stand the rotation down, then resume. Only strays are fought: the current FATE's own mobs are carried into the ring, and a mounted character keeps riding until the leash ends it. Inside a running FATE the same clear runs whenever a stray is on us and none of the FATE's enemies is; the FATE's behavior then starts over in its own mode.
4. **Navmesh not ready** — hold all movement intents until vnavmesh reports ready.

---

## 4. FATE selection

Selection is a **configurable priority ladder** compared criterion by criterion; the first differing criterion decides, with lowest FATE id as the final tie-break. Default order: `Progress → Bonus → TimeLeft → DistanceTeleport`.

- **Progress**: higher first (finish nearly-done FATEs for fast credit).
- **Bonus**: twist-of-fate bonus FATEs beat non-bonus.
- **TimeLeft**: more remaining time first, derived from the Eorzea-time model (24 Eorzea hours = 70 real minutes); unopened NPC FATEs (start time 0) assume a 900 s budget.
- **Distance / DistanceTeleport**: straight-line distance, or `min(direct, nearest-aetheryte-to-FATE + teleport penalty)` with a configurable penalty (default 200) so a teleport-then-fly route can beat a long direct flight.

Hard gates applied before ranking: time remaining below threshold (default 180 s) → skip; progress above threshold (default 80 %) → skip; zero-coordinate FATEs (not yet registered) → skip; blacklisted → skip; ruled out this session (died in, D9; unreachable, D10) → skip; level above player + configured margin → skip; boss FATEs below their join threshold (§5) → skip. A nearby override (inside or within ~50 y of a ring) takes the nearest eligible FATE immediately.

Classification is ID-based from the Lumina `Fate` sheet — never by localized name. An event item (`EventItem`, `TurnInEventItem` or `ReqEventItem`) marks a collect FATE; otherwise the `Rule` column names collect (2), escort (3) and defend (4), and the plain kill rule (1) splits into battle and boss by the sheet's `Icon` column alone (60722 = boss). Higher rules are special content and fall back to the icon (B12).

---

## 5. FATE type handling

- **Battle** (default): engage the FATE's boss when one stands among adds (B13), otherwise the nearest FATE enemy; sticky targeting (keep target until dead/invalid); melee/ranged stop distances by job category; never path into hitboxes; while in combat, in-fight repositioning belongs to the dodge layer, not navigation.
- **Boss**: join only at/above a progress threshold (default 0 %; a higher default for special bosses, the achievement and world bosses the sheet marks with its big-boss banner, B14) so the player never solo-tanks a special boss from zero. No navigation while the fight runs — the dodge layer owns movement.
- **Continuations**: when a completed FATE chains, wait at the site for the follow-up; adopt it when it spawns; give up after 30 s.
- **Defend**: peel logic — prefer enemies whose target is a protected friendly.
- **Escort**: follow the objective NPC with follow/stop hysteresis; navigation owns movement (dodge-layer movement disabled); target scope locked to the FATE.
- **Collect (hand-in)**: gather ground items owned by the FATE, hand in batches at the objective NPC (7 items = full credit; partial hand-in when short). Combat is disengaged for pickup/hand-in (single-owner rule), re-engaged for fighting. Dialog via TextAdvance with a manual addon fallback.
- **NPC-started**: detect via zero progress + no active enemies; find the starter NPC; dismount, settle, interact; confirm only the FATE-start prompt that follows our own interact (B4), leave any other prompt alone.
- **Ring knockback**: outside the ring with the FATE still running → path back to center.
- **Reward latch**: after completion, do not leave the zone or teleport until the reward payout registers.

---

## 6. Combat backends

`ICombatBackend { IsAvailable, Engage(mode), Disengage(), SetAoe(on), SetMaxDistance(d) }` with three implementations, auto-detected and user-orderable, **Olympus preferred**:

- **OlympusBackend** — drives RoseOfficial's own rotation plugin. Requires a small IPC surface added to Olympus (enable/disable auto mode at minimum); tracked as a side task in the Olympus repo. Until it lands, RSR is the working default so Moirai is never blocked.
- **RotationSolverBackend** — RSR via its IPC/commands.
- **WrathComboBackend** — Wrath's lease model: register for a lease, set auto-rotation state and FATE-priority options through it, release on stop, and handle lease-revocation callbacks (re-register or degrade gracefully).

**Dodging**: BossMod Reborn's AI, when present, is switched on with the combat backend and off with it (H1), with its actions forbidden (the rotation is the backend's), its follow modes off, and its movement forbidden by default so navigation keeps moving the character. While Reborn reports danger, its AI steering the character or a marked zone going off within 3 s, the planner stops its own path and hands movement to Reborn (H2); a 1 s settle window after the danger clears precedes navigation taking over again (H3). Without Reborn nothing changes (H4). Missing dependencies degrade specific capabilities with a visible status reason — never a silent no-op.

---

## 7. Movement, recovery, and death

### 7.1 Movement

vnavmesh for all pathing (`PathfindAndMoveCloseTo` with tolerance; floor/nearest-mesh queries for landable points). Mount when the leg exceeds a threshold and mounting is legal, asking for at most a bounded window before walking the leg (C15); fly when unlocked (the zone's aether currents attuned, C16) *and* the zone's data file permits (per-zone no-fly overrides exist because some zone geometry breaks flight pathing); sprint on foot. Landing targets are randomized points within the ring resolved to the mesh floor — never the raw center, which may be unlandable. Altitude-ceiling errors during flight fall back to teleporting. A leg starts with a teleport when an attuned aetheryte's route beats the direct path by the teleport penalty (C17), the same cost model the A12 ranking uses; the teleport is held until the character stands at the aetheryte, or given up on after 20 s.

### 7.2 Recovery ladder

Bounded and escalating; every rung has a retry cap, and exhausting the ladder gives the FATE up:

1. Re-path to the same destination: the running path is dropped first, so the next leg is issued as a fresh path (C13).
2. Re-roll the destination (new landable point).
3. Escape, held for a short window so the leg cannot overwrite it on the next tick (C14): mounted with flight, climb straight up; otherwise a sideways nudge of a few yalms with a jump (C8).
4. Teleport to the nearest attuned aetheryte and re-approach the same FATE on a fresh leg; the ladder is not reset, so a later stall on that FATE abandons it (D10). Without an attuned aetheryte in the zone the rung falls through.
5. Exhausted: the FATE is abandoned and skipped for the session, and selection goes on. Three exhaustions in a row without moving between them mean the character itself is wedged: stop with `StuckExhausted` (D10).

Stuck detection: no meaningful movement over a sampling window while a path is running, with the sampler suppressed during the mount cast. A stall while mounted inside the ring is treated as a landing attempt before it counts as stuck, so the final descent never reads as a false positive.

### 7.3 Death

Accept the return prompt, wait through the revive, teleport back to the farming zone if displaced, and resume planning from scratch (current FATE forfeited, stats record a death — never a completion). A FATE the player died inside is avoided for the rest of the session (D9). A configurable deaths-per-session cap stops the run.

### 7.4 Accounting

Completed, failed, and abandoned FATEs are counted distinctly. A failed FATE must never increment completion or quota counters.

---

## 8. Yo-kai module (v1 flagship)

Data: a 17-row yokai table — minion id, companion action, legendary medal item id, and the designated zones where that yokai's legendary medals drop — shipped as a data file (§9).

Per tick, from the snapshot: current medal counts per yokai (regular medal, per-yokai legendary medals) via inventory item ids.

Rotation logic (pure, in the module; implemented in v0.4.0 as E1–E8):

1. Target yokai = first entry in the user's priority list under its legendary cap (10) whose minion is owned (E2); unowned ones are skipped and listed.
2. The Yo-kai Watch is worn when owned, judged by the equipped slot (E1, E8); it earns regular medals only, so a missing watch is a note, never a stop.
3. The yokai's designated zones are a zone rotation (G-series): the current one when listed, the next when quiet, unreachable ones skipped (E4, E7).
4. In a designated zone, the yokai's minion is summoned in a settled moment (E3); a minion that never appears through a 20 s grace of settled asking is given up on for the session and the next yokai taken (E8).
5. Farm FATEs there (core loop). Regular medals accrue anywhere; legendary medals only in designated zones — the module only ever farms designated zones for the active yokai.
6. On cap: advance to the next yokai (announce progress). All capped → stop with a session summary (per-yokai counts, FATEs done, elapsed time).

7. Buying (E9, off by default): an unowned yokai whose price the regular medals cover (one for the first event minion, three after, as a hint; the shop is the authority) is bought before it is skipped. The shell's purchaser runs outside the busy guard: teleport to the Gold Saucer, walk to Nohi (ENpcResident 1017247/1017528, found in view or by his Level-sheet placement), interact, TextAdvance carries the talk, the menu entries are picked by a configured index path, the exchange row by the minion's roster position plus a configured offset, the dialog or yes/no confirmed, the window closed, the item used to learn the minion. Nohi's only handler is a Story row, so his menu order is not in the sheets: the debug report dumps every open menu and exchange window value by value, and a report pins the indices. Any step past 20 s fails with a chat line and switches auto-buy off for the run (`AutoBuyReady`), so the shopping list stop (E5) takes over.

Vendor turn-ins (weapons, mount) remain manual. The module surfaces the active yokai's count and its notes in the overlay (§10).

---

## 9. Data layer

Two kinds of knowledge, kept strictly apart:

- **Game-derived facts** — FATE classification, event items, aetheryte lists/positions, zone metadata — read from Lumina Excel sheets at runtime, by row id. Nothing is matched by localized string.
- **Curated knowledge** — per-zone flight overrides, categorized FATE blacklists (with a reason code: escort-pathing, mechanic-gimmick, terrain, tank-check), special-boss join thresholds, continuation chains, aetheryte wait spots, and the yokai table — shipped as versioned JSON files.

Curated files live in the plugin config directory, are hot-reloaded on change, and are updatable from the RoseOfficial repo via a manifest (per-file hash compare, download only what changed), so a patch-day fix is a data push, not a plugin release. A per-file user opt-out protects local edits. The plugin binary carries a baseline copy of every file for first run and offline use.

---

## 10. UI

- **Overlay** — compact, borderless, movable: run state, current action (one canonical status string), current FATE name + progress, session stats, and in Yo-kai mode a per-yokai medal progress line. Start/pause/stop buttons.
- **Config window** — tabs: Mode (module choice + module settings, yokai priority list), Selection (priority ladder, gates, blacklist editor), Combat (backend order, distances, AoE), Movement (mount/flight prefs), Data (update/reload, per-file pin), About.
- Everything the planner decides is explainable: the overlay's status line always states *what* and *why* (e.g. skip reasons, recovery rung, stop reason).

---

## 11. Error handling and stop reasons

A pause (§2.2 Paused, `/moirai pause`, the overlay button) stands movement and combat down over the next two ticks and keeps the session: the ledger, the session skips, the zone rotation's position, and the module's state. Resume picks up from selection; the FATE under way is not counted as anything. Every terminal stop carries a typed `StopReason` (`UserRequested`, `AllYokaiCapped`, `WatchMissing`, `StuckExhausted`, `DeathCapReached`, `DependencyLost`, `DataMissing`, …) surfaced in the overlay and log. Mid-run dependency loss (a required plugin unloads) pauses rather than stops when recoverable, with the reason displayed. No unbounded retry exists anywhere: every loop has a cap, every wait a timeout.

---

## 12. Testing

- **Unit tests (xUnit, `tests/Moirai.Tests`)** target the planner layer exclusively: FateSelector ranking and gates, module rotation logic, behavior transitions, recovery ladder escalation, interrupt precedence, accounting. Snapshots are constructed by builders; the edge-case catalog (Appendix A) is the test list.
- **Executors and IPC adapters** are thin by design and verified in-game via dev-plugin loading; they contain no branching logic worth mocking.
- CI runs the test suite on push.
- **Replay.** The planner's only inputs are the snapshot, the random source, and the landing resolver, so a run is reproducible from a log of them. `Recorder` sits between the Director and those two services and keeps the newest frames of a run (a minute at the frame rate): each tick's snapshot, random draws, landing queries with their answers, zone flight flag, status, and intent, with the run's `RunSettings` and the plugin version. `/moirai record` writes it as gzipped JSON to the config directory, and a run that stops for any reason but the user's saves one on its own. `Replayer` rebuilds the Director from the recording's settings through `DirectorFactory`, the one place a Director is assembled and shared with the plugin, feeds every frame back, and lines up recorded and replayed status and intent; a frame that asks for an input the recording does not carry is reported as the divergence and ends the replay. Recordings dropped into `tests/Moirai.Tests/Recordings/` replay without divergence as part of the suite. The debug report carries a timeline of the last fifty status changes for the reader who does not need the frames.

---

## 13. Repository layout

```
Moirai/
  Moirai.sln
  src/Moirai.Core/            pure decision core — NO Dalamud/ECommons references (compiler-enforced)
    Model/                    WorldSnapshot and projections
    Intents/                  the planner's output vocabulary
    Planning/                 selection gates/ranker, interrupts, recovery
    Behaviors/                travel, engage, escort, collect, npc-start
    Session/                  ledger, reward latch, continuation
    Modules/                  IFarmModule, YokaiModule, SingleZoneModule
    Director.cs
  src/Moirai/                 Dalamud plugin shell
    Plugin.cs                 Dalamud entry, service wiring
    Configuration.cs
    Snapshot/                 WorldSnapshot builder over game state
    Execution/                Movement/Combat/Action/Travel executors
    Ipc/                      vnavmesh, Lifestream, TextAdvance, BossMod, RSR, Wrath, Olympus adapters
    Data/                     data-file models, loader, updater; baseline JSON as embedded resources
    UI/                       overlay + config window
  tests/Moirai.Tests/         xUnit over Moirai.Core only — runs on CI with no game libraries
  Docs/
    specs/
    plans/
  repo.json                   Dalamud custom-repo manifest
  reference/                  untracked reference clones
```

The two-project split exists so the planner's purity rule (§2.2) is enforced by the compiler — `Moirai.Core` cannot reference game libraries even by accident — and so the test suite runs on CI without Dalamud assemblies.

---

## Appendix A — Edge-case requirements catalog

Baseline distilled from years of field fixes in comparable tools. Each item is a planner test.

**Selection**
- A1. Skip FATEs with < 180 s remaining (configurable).
- A2. Skip FATEs above 80 % progress (configurable).
- A3. Skip FATEs whose coordinates are (0, 0) — not yet registered by the client.
- A4. Skip FATEs above player level + margin; no lower-level floor.
- A5. Boss FATEs ineligible below join threshold; special bosses use their own threshold.
- A6. Bonus FATEs outrank non-bonus when the Bonus criterion is reached. The flag is the game's own bonus marker on the FATE (`HasBonus`), read into the snapshot; the bonus-only mode (A7) is a Selection-tab switch.
- A7. Bonus-only mode selects nothing when no bonus FATE exists (idle, don't roam).
- A8. Unopened NPC FATEs (start time 0) assume 900 s remaining and rank only against each other unless bonus.
- A9. Nearby override: inside or within ~50 y of an eligible ring → take it immediately.
- A10. Post-completion grace window: prefer a chained/nearby spawn over a distant FATE for ~5 s.
- A11. Tie-break by lowest FATE id (determinism).
- A12. Teleport-cost model can prefer aetheryte + short hop over long direct flight. Aetherytes are the current zone's attuned ones, by row id, positioned from the sheet's `Level` link; the penalty is one setting shared with the C17 leg so ranking and travel never disagree.
- A13. The pick is looked at again on the way. A FATE that no longer passes the gates (others pushed it past the progress cap, its time ran down, it was blacklisted) is dropped for a fresh selection, unless the character is already within the nearby radius, where the credit is quick; this is a change of pick, not an abandonment, and the ledger does not count it. An eligible FATE inside the nearby radius that is not the current one is switched to at once, as A9 would do at selection. Neither applies while a teleport is in flight (C17), since the character would land far from the newcomer. Until then travel committed to its pick until arrival.
- A14. "Skip collect FATEs" leaves every collect FATE alone, open or not, judged by the sheet kind (`SheetKind`) rather than the opened-state kind, since an unopened collect FATE reads as NPC-start (B11). "Skip NPC-started FATEs" hides only FATEs still waiting at their starter NPC, so a collect FATE another player opened was taken (What's Your Poison, fate 601, event item 2001053) until this switch existed.
- A15. "Skip NPC-started FATEs" covers kill FATEs waiting at a starter NPC and exempts collect FATEs, which open at their hand-in NPC (B11) and follow the collect switch: with collect on, an unopened collect FATE is opened first and then collected. Both switches are selection gates with their own skip reasons; the snapshot carries every FATE in the zone.

**Types**
- B1. Collect: 7 items = full credit; batch hand-ins; partial hand-in when short.
- B2. Collect: combat disengaged during pickup/hand-in; re-engaged for Fight goal.
- B3. Collect: event-item id may populate ~1 s after FATE start — tolerate the delay.
- B4. NPC-start: the FATE-start yes/no is confirmed only by the NPC-start behavior, only after its own interact with the starter, and only while the FATE is still unopened; a prompt open at any other time is not ours and is left alone. Recognition is by context, never by the prompt's localized text. The Talk window before the prompt is TextAdvance's to advance (it never confirms the FATE-start prompt itself), so the behavior waits it out; after confirming it waits up to 10 s for the FATE to open before interacting again, since the game opens it a few seconds later. The snapshot carries the open dialog (Talk, yes/no, hand-in window) and the busy guard does not count an open dialog as busy while the NPC-start or collect behavior is active (D4).
- B5. NPC-start: starter NPC may report FATE id 0 before starting — match by proximity + nameplate icon.
- B6. Escort: follow hysteresis (start ~5 y, stop ~2.5 y); dodge-layer movement disabled; target scope locked to FATE.
- B7. Defend: prefer enemies targeting protected friendlies.
- B8. Continuation: wait at site, adopt successor by new id at same location, give up after 30 s.
- B9. Boss fights: no navigation while in combat; dodge layer owns movement.
- B10. Never target the FATE's own friendly NPC in combat (hostility comes from the game's own can-attack test, never from NPC sub-kind). A held target that is not one of the FATE's enemies — another FATE's mob, the friendly, a corpse — is replaced with a FATE enemy while one exists and cleared only when none does; a FATE enemy the combat backend switched to is accepted as the sticky target, so the two never trade the target back and forth.
- B11. NPC-start: a FATE still in its preparation phase, or with neither a start time nor progress, is unopened and therefore an NPC-start FATE whatever its sheet kind; the start-time field alone is not trusted because it can be set while a FATE still waits at its NPC. An unopened collect FATE opens at the same NPC it hands in to, so the starter search falls back to the FATE's objective NPC, and the Director re-dispatches when the current FATE's classification changes.
- B12. Classification: the sheet's `Rule` column is not a kind enum. Rule 2 = collect, 3 = escort, 4 = defend; rule 1 covers both plain kill FATEs and bosses, and only the sheet `Icon` column (60722) tells a boss apart. Rules above 4 are special content (Diadem, Eureka, Bozja, Occult) and fall back to the icon. An event item marks collect whatever the rule, and a collect rule without an event item is still collect (B3).
- B13. Boss with adds: the FATE sheet never names the objective (Revenge of the Worms is rule 1 with the plain kill icon, and Ulhuadshi's `BNpcBase` rank is 0 like its sandworms'), so the enemy whose max HP is at least twice the smallest in the FATE's pool is the objective and is engaged over the adds, after B7 peel and before whatever attacks the player. The combat backend is driven with the same order (RSR's AutoDuty entry point with the `HighMaxHP` targeting override; its default sorts by lowest HP, which in a boss fight is always an add), so the planner's pick and the backend's switches agree and B10's sticky rule holds.
- B14. Special boss: the sheet has no kind for the achievement and world bosses. Lazy for You is rule 1 with the boss icon like Jack of All Trades, and its `SpecialFate` column is false (that column marks quest FATEs such as The Mandragoras). Every one of them opens with `ScreenImageAccept` row 37, the big-boss banner, where an ordinary FATE uses row 33: 72 rows, from Steel Reign and Lazy for You to The Serpentlord Seethes and Mascot Murder. A boss FATE with that banner is special and takes the special join threshold; a chain's battle or defend step opens with the banner too and keeps its own kind.

**Movement**
- C1. Per-zone no-fly override honored even when flight is unlocked.
- C2. Altitude-ceiling error during flight → abort flight, teleport instead.
- C3. Landing point = randomized in-ring point resolved to mesh floor; never raw center.
- C4. Unlandable hover: no movement after jump-down → re-roll nearby mesh point.
- C5. Knocked out of ring, FATE running → path back to center.
- C6. Do not issue movement while casting (never cancel a cast).
- C7. Mounted + stationary inside ring beyond window → re-roll dropoff.
- C8. Grounded stuck (on foot, or mounted without flight) → a sideways nudge of a few yalms with a jump, held for the escape window, then escalate the ladder.
- C9. A stall while mounted inside the ring is a landing attempt, never a stuck failure (vertical-descent false positive).
- C10. Melee stop distance ≈ 2.5 y (larger breaks auto-attack range); ranged ≈ 8 y.
- C11. Sync only once actually inside the ring.
- C12. Mount only when leg length exceeds threshold and mounting is legal (not in combat/housing). Legality is read from the game: the territory allows mounts and the player owns one.
- C13. The re-path rung drops the running path (`StopMoving`) so the next leg is issued as a fresh path; re-issuing the same destination into a path the navmesh still considers running is a no-op, so without the drop the rung did nothing.
- C14. The escape rung is held for its window (1.5 s) before the leg resumes with its stall sampler re-anchored; mounted with flight it climbs straight up. Without the hold the next travel tick overwrote it within half a second.
- C15. Mounting is asked for at most 6 s in a row; then the leg is walked and the stall sampler starts fresh. A mount that took clears the budget, so a knock off the mount mid-leg mounts again. Without the budget a character who cannot mount stood still forever, the wait suppressing the sampler as a mount cast.
- C16. Flight only where the zone's aether currents are all attuned, read from the game; C1's overrides apply on top. An airborne path a ground mount cannot follow fed the ladder on every leg.
- C17. Teleport leg: at the start of a leg, an attuned aetheryte whose distance to the dropoff plus the teleport penalty is below the direct distance starts the leg with a teleport to it. Decided once per leg, never in combat; held with the stall sampler suppressed until the character stands within 25 y of the aetheryte, or given up on after 20 s (no gil, refused), after which the leg goes on from where it stands. A travel teleport is held while the reward latch is pending (D6).

**Interrupts & lifecycle**
- D1. Death overrides all states; accept return; teleport back if displaced; resume fresh. The single-zone module remembers the zone the run started in and emits a zone change whenever the snapshot shows another, so a return to a home point elsewhere goes back to the farming zone, after any pending payout (D6). Until then the run farmed wherever it woke up, or idled in a city.
- D2. Death never counts as completion; failed FATE never counts as completion.
- D3. Unexpected combat outside the FATE → defensive clear, then resume. The clear stops movement, targets the nearest stray on us (sticky while it stays on us), closes to engage range on foot, and stands the rotation down once nothing is on us; the leg's stall sampler is re-anchored so the standstill is not a stuck. A stray is anything alive and attacking us or our companion that is not the current FATE's enemy: the FATE's own mobs are never fought outside the ring (the rotation cannot attack them there) and are carried in. Mounted, nothing is fought; the leg carries on and the leash ends it. Inside a running FATE combat past the ring edge (pulled mobs, knockbacks) is that FATE's own and the engage behavior walks back in (C5), but a stray on us while none of the FATE's enemies is gets the same clear, after which the FATE's behavior starts over so its own rotation mode comes back; a FATE enemy on us always outranks the stray.
- D4. Busy guard: no intents while casting/between-areas/jumping/being-moved/occupied/Lifestream-busy.
- D5. Navmesh not ready → hold with the reason shown, keep observing, never stop: a mesh may still be building.
- D6. Reward latch: no zone change/teleport until FATE payout registers.
- D7. Every retry loop bounded; ladder exhaustion abandons the FATE (D10), and a wedged character stops with a typed reason.
- D8. Dependency lost mid-run → pause with the reason in the status line; resume when it is back; the combat backend or TextAdvance missing for the grace period (60 s) stops with `DependencyLost`. Until then the overlay only warned while not running, and a run with the backend unloaded stood in FATEs doing nothing.
- D9. A FATE the player died in, or died inside the ring of on the way in, is not selected again in the same session, not even by the nearby override. A solo death leaves a boss at full health and progress at 0, so the ranking would send the player straight back (Lazy for You: three deaths to the cap in ten minutes, its 30-minute timer winning the TimeLeft rung every time). A death on the road does not condemn the FATE.
- D10. Ladder exhaustion abandons the FATE, counts it as abandoned, and skips it for the session (`Unreachable`); selection goes on. Three exhaustions in a row without the character moving more than 10 y between them mean the character is wedged, not the FATE: stop with `StuckExhausted`. Moving between exhaustions resets the count. Until then one unreachable FATE ended the whole run.

**Zones**
- G1. The rotation's target is the starting zone when it is listed, otherwise the first listed zone; the module steers to the target whenever the snapshot shows another zone (which also covers D1 displacement).
- G2. A zone with no eligible FATE for the quiet period (default 120 s) is left for the next listed zone, wrapping around.
- G3. A single listed zone is a pin: quiet or not, the run stays.
- G4. A zone change that has not landed within the move timeout (60 s; an unattuned aetheryte, a refused teleport) skips that zone for the next.
- G5. Every listed zone failing in a row stops with `ZonesUnreachable`; landing anywhere clears the count.
- G6. The Director's idle clock starts when selection first comes up empty and resets on a pick or a zone change; the module hears it each tick. The reward latch (D6) holds every zone change.

**Dodging**
- H1. BossMod Reborn's AI is switched on with the combat backend and off with it, with actions forbidden, follow modes off, and movement forbidden by default, so navigation keeps moving the character. Executor-side, de-duplicated.
- H2. Danger (Reborn's AI navigating, or a marked zone going off within 3 s) makes the engage behavior hand movement to the dodge layer and emit no path of its own, before the ring re-entry, so it never paths into a marker. Only while its combat switch is on, since the AI is on with it.
- H3. After danger clears, a settle window (1 s) passes before movement is handed back to navigation, once.
- H4. Without Reborn the danger flag is never set and nothing changes.

**Yo-kai**
- E1. Watch unequipped → equip if owned, else stop `WatchMissing`.
- E2. Active yokai = first priority-list entry under legendary cap (10).
- E3. Minion must match active yokai before farming counts — verify each tick, resummon after death/zone change (minions dismiss on some transitions).
- E4. Legendary medals only accrue in the yokai's designated zones — never farm outside them in Yo-kai mode.
- E5. On cap: advance and announce; all capped → stop with summary.
- E6. Medal counts read by item id from inventory each tick (no cached assumptions across hand-ins).
- E7. The active yokai's designated zones are farmed as a zone rotation (G1–G5); a new active yokai gets its own rotation. Until then the module sat in the first designated zone.
- E9. Auto-buy: with the switch on and the purchaser not failed, an unowned yokai is bought when the regular medals cover the price hint (1 for the first event minion, 3 after); otherwise it is skipped and listed. The buy directive becomes an intent for the shell's purchaser, held while a payout is pending (D6); the purchaser picks menus and rows by index only, and a failure switches auto-buy off for the run.
- E8. The watch is optional (legendary medals need the minion, not the watch): equipped from the bags when owned, judged by the equipped slot rather than an item count, given up on after a 10 s grace, noted on the overlay. Summons and equips are asked for in settled moments only (the Director's `ModuleContext.Settled`), and a minion that never appears through a 20 s settled grace is given up on for the session.

## Appendix B — Verified dependency IPC surface

Gate names verified against current plugin versions (2026-08).

**vnavmesh**: `Nav.IsReady`, `Nav.BuildProgress`, `Path.IsRunning`, `Path.Stop`, `Path.MoveTo(List<Vector3>, bool fly)`, `SimpleMove.PathfindAndMoveTo(Vector3, bool)`, `SimpleMove.PathfindAndMoveCloseTo(Vector3, bool, float)`, `SimpleMove.PathfindInProgress`, `Query.Mesh.PointOnFloor(Vector3, bool, float)`, `Query.Mesh.NearestPoint(Vector3, float, float)`.

**Lifestream**: `Lifestream.IsBusy()`, `Lifestream.AethernetTeleportById(uint)`, `/li <aetheryte>` command surface.

**TextAdvance**: `TextAdvance.IsInExternalControl()`, `TextAdvance.EnableExternalControl(string owner, config)`, `TextAdvance.DisableExternalControl(string owner)`; config flags: TalkSkip, RequestFill, RequestHandin, RewardPick, CutsceneEsc, CutsceneSkipConfirm (QuestAccept/Complete and AutoInteract deliberately off). The config crosses Dalamud IPC as JSON, so a local class with the same field names (`EnableTalkSkip`, …, nullable bools) is enough. Control is taken at Start, re-asserted while running (TextAdvance drops it after a zone change), and released at Stop. TextAdvance never confirms the FATE-start `SelectYesno`; that is Moirai's (B4).

**BossMod / Reborn**: `BossMod.Presets.SetActive/ClearActive/GetActive/Create/Get`, `BossMod.Presets.AddTransientStrategy(preset, module, option, value)`; Reborn-only: `BossMod.Hints.ForbiddenZonesCount`, `BossMod.Hints.ForbiddenZonesNextActivation`, `BossMod.AI.IsNavigating`; AI toggles via `/bmrai` (Reborn) or `/vbm` (vanilla) command families. Moirai drives Reborn only: `/bmrai on|off`, `/bmrai forbidactions on`, `/bmrai followtarget|followcombat|followoutofcombat off`, `/bmrai forbidmovement on|off` (H1–H3).

**RotationSolver Reborn**: auto via `RotationSolverReborn.AutodutyChangeOperatingMode(StateCommandType, TargetingType)` with `AutoDuty` (4) and `HighMaxHP` (6), enums crossing Dalamud IPC by value; the state sets a transient targeting override that `off` clears, never toggles, and is the one state RSR does not switch off on its own out of combat, in cutscenes or between areas; a build without the gate gets `/rotation auto` and RSR's own targeting order. Manual and off via `/rotation manual|off`; state via `RotationSolverReborn.AutorotationActive() → bool` (on in any mode, AutoDuty included; absent on older builds, in which case memory decides); settings via `/rotation settings <Name> <value>` (in memory only, not saved); `RotationSolverReborn.AddPriorityNameID(uint)` / `RemovePriorityNameID(uint)` only widen what RSR may attack and do not order its targets. Auto lets RSR pick among the FATE's enemies in max-HP order (B13); Defensive maps to `manual`, which attacks the held target alone, and the planner holds the stray. RSR switches itself off on its own (out of combat for 30 s by default, death, zone change), so every switch asks it first, and a mode command while it is on toggles it off or cycles its targeting type, so a change of mode goes through `off`. RSR's attackable check runs in every mode and, with its default "Ignore Non-Fate targets while in a Fate" (`IgnoreNonFateInFate`), refuses FATE mobs whenever the game does not consider the player inside a FATE (outside the ring, or above the cap without sync) and refuses non-FATE mobs whenever it does; sync and ring re-entry precede targeting, and the option is set to `false` for a defensive clear and back to `true` afterwards.

**Wrath Combo**: lease model — `RegisterForLeaseWithCallback(internalName, pluginName, callbackPrefix) → Guid`, `SetAutoRotationState(Guid, bool)`, `SetCurrentJobAutoRotationReady(Guid)`, `SetAutoRotationConfigState(Guid, option, value)` (incl. `FATEPriority`), `ReleaseControl(Guid)`, revocation callback handling.

**Olympus** (to be added): minimal surface — `Olympus.SetAutoMode(bool)`, ideally `Olympus.IsReady()`. Tracked in the Olympus repo.

**Game APIs**: `FateManager.LevelSync()` (sync), `ActionType.Mount` / GeneralAction 9 (mount roulette) / GeneralAction 4 (sprint) / GeneralAction 2 (jump), `TargetSystem.InteractWithObject`, `InventoryManager` item counts, `PlayerState.IsMountUnlocked`.
