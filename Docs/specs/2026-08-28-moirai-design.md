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
- **Module (`IFarmModule`)** — strategy: which zone to farm, when to rotate, when the session is done. v1 ships `YokaiModule` (§8) and `SingleZoneModule`.
- **Behaviors** — tactical units, each a small explicit state machine with enter/tick/exit and bounded retries: `TravelBehavior`, `EngageBehavior`, `CollectBehavior`, `NpcStartBehavior`, `RecoverBehavior`, `IdleBehavior`. One behavior is active at a time; the Director owns transitions.
- **FateSelector** — pure scoring over the snapshot's FATE projection (§4).

Intents (the planner's entire output vocabulary): `GoTo(pos, fly, tolerance)`, `Mount`, `Dismount`, `Engage(targetId)`, `ClearTarget`, `Sync`, `Interact(objectId)`, `Teleport(aetheryteId)`, `ChangeZone(territoryId)`, `SwapMinion(companionId)`, `EquipItem(itemId)`, `SetCombat(on/off, mode)`, `Stop(reason)`, `Wait(ms)`, `None`.

### 2.3 Executor layer

Thin adapters, one per concern, that turn intents into game/IPC calls: `MovementExecutor` (vnavmesh), `CombatExecutor` (backend of §6), `ActionExecutor` (mount/dismount/sync/interact/minion via `ActionManager`/`FateManager`/`TargetSystem`), `TravelExecutor` (Lifestream/Telepo). Executors own throttling (named throttles per action class), idempotence (re-issuing an in-flight intent is a no-op), and report per-intent outcome back into `PlanState` so the planner sees failures.

**Single-owner targeting rule:** exactly one system commands the target at any moment. When a collect turn-in or NPC interaction owns targeting, the combat backend is disengaged first; when combat owns it, nothing else issues `/target`-class actions. This rule prevents the target tug-of-war that plagues comparable tools.

---

## 3. Interrupts and the busy guard

Evaluated every tick before any planning, in priority order:

1. **Busy guard** — no intent is executed while the player is casting, between areas, jumping, being moved, mid-mount/dismount animation, occupied by a game UI, or while Lifestream reports busy. The planner still observes; the executors hold.
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

Hard gates applied before ranking: time remaining below threshold (default 180 s) → skip; progress above threshold (default 80 %) → skip; zero-coordinate FATEs (not yet registered) → skip; blacklisted → skip; died in this session (D9) → skip; level above player + configured margin → skip; boss FATEs below their join threshold (§5) → skip. A nearby override (inside or within ~50 y of a ring) takes the nearest eligible FATE immediately.

Classification is ID-based from the Lumina `Fate` sheet — never by localized name. An event item (`EventItem`, `TurnInEventItem` or `ReqEventItem`) marks a collect FATE; otherwise the `Rule` column names collect (2), escort (3) and defend (4), and the plain kill rule (1) splits into battle and boss by the sheet's `Icon` column alone (60722 = boss). Higher rules are special content and fall back to the icon (B12).

---

## 5. FATE type handling

- **Battle** (default): engage the FATE's boss when one stands among adds (B13), otherwise the nearest FATE enemy; sticky targeting (keep target until dead/invalid); melee/ranged stop distances by job category; never path into hitboxes; while in combat, in-fight repositioning belongs to the dodge layer, not navigation.
- **Boss**: join only at/above a progress threshold (default 0 %; a higher default for special bosses, the achievement and world bosses the sheet marks with its big-boss banner, B14) so the player never solo-tanks a special boss from zero. No navigation while the fight runs — the dodge layer owns movement.
- **Continuations**: when a completed FATE chains, wait at the site for the follow-up; adopt it when it spawns; give up after 30 s.
- **Defend**: peel logic — prefer enemies whose target is a protected friendly.
- **Escort**: follow the objective NPC with follow/stop hysteresis; navigation owns movement (dodge-layer movement disabled); target scope locked to the FATE.
- **Collect (hand-in)**: gather ground items owned by the FATE, hand in batches at the objective NPC (7 items = full credit; partial hand-in when short). Combat is disengaged for pickup/hand-in (single-owner rule), re-engaged for fighting. Dialog via TextAdvance with a manual addon fallback.
- **NPC-started**: detect via zero progress + no active enemies; find the starter NPC; dismount, settle, interact; accept only the FATE-start dialog (recognized by its level-recommendation text pattern), reject unrelated prompts.
- **Ring knockback**: outside the ring with the FATE still running → path back to center.
- **Reward latch**: after completion, do not leave the zone or teleport until the reward payout registers.

---

## 6. Combat backends

`ICombatBackend { IsAvailable, Engage(mode), Disengage(), SetAoe(on), SetMaxDistance(d) }` with three implementations, auto-detected and user-orderable, **Olympus preferred**:

- **OlympusBackend** — drives RoseOfficial's own rotation plugin. Requires a small IPC surface added to Olympus (enable/disable auto mode at minimum); tracked as a side task in the Olympus repo. Until it lands, RSR is the working default so Moirai is never blocked.
- **RotationSolverBackend** — RSR via its IPC/commands.
- **WrathComboBackend** — Wrath's lease model: register for a lease, set auto-rotation state and FATE-priority options through it, release on stop, and handle lease-revocation callbacks (re-register or degrade gracefully).

**Dodging**: BossMod Reborn's AI, when present, is toggled alongside any backend for AOE avoidance; its movement authority is granted only in-combat and revoked for escort/collect (navigation-owned phases). Missing dependencies degrade specific capabilities with a visible status reason — never a silent no-op.

---

## 7. Movement, recovery, and death

### 7.1 Movement

vnavmesh for all pathing (`PathfindAndMoveCloseTo` with tolerance; floor/nearest-mesh queries for landable points). Mount when the leg exceeds a threshold and mounting is legal; fly when unlocked *and* the zone's data file permits (per-zone no-fly overrides exist because some zone geometry breaks flight pathing); sprint on foot. Landing targets are randomized points within the ring resolved to the mesh floor — never the raw center, which may be unlandable. Altitude-ceiling errors during flight fall back to teleporting.

### 7.2 Recovery ladder

Bounded and escalating; every rung has a retry cap, and exhausting the ladder stops with a stated reason:

1. Re-path to the same destination.
2. Re-roll the destination (new landable point).
3. Vertical escape (fly up, retry) or ground escape (jump + sideways nudge) depending on mounted state.
4. Return to the nearest aetheryte and re-approach.
5. Stop with `StuckExhausted`, position and state logged.

Stuck detection: no meaningful movement over a sampling window while a path is running, with the sampler suppressed during the mount cast. A stall while mounted inside the ring is treated as a landing attempt before it counts as stuck, so the final descent never reads as a false positive.

### 7.3 Death

Accept the return prompt, wait through the revive, teleport back to the farming zone if displaced, and resume planning from scratch (current FATE forfeited, stats record a death — never a completion). A FATE the player died inside is avoided for the rest of the session (D9). A configurable deaths-per-session cap stops the run.

### 7.4 Accounting

Completed, failed, and abandoned FATEs are counted distinctly. A failed FATE must never increment completion or quota counters.

---

## 8. Yo-kai module (v1 flagship)

Data: a 17-row yokai table — minion id, companion action, legendary medal item id, and the designated zones where that yokai's legendary medals drop — shipped as a data file (§9).

Per tick, from the snapshot: current medal counts per yokai (regular medal, per-yokai legendary medals) via inventory item ids.

Rotation logic (pure, in the module):

1. Verify the Yo-kai Watch is equipped; equip it if owned, else stop with `WatchMissing`.
2. Target yokai = first entry in the user's priority list under its legendary cap (10).
3. Ensure that yokai's minion is summoned (`SwapMinion` intent).
4. If not in one of its designated zones, `ChangeZone` to the nearest (Lifestream/teleport).
5. Farm FATEs there (core loop). Regular medals accrue anywhere; legendary medals only in designated zones — the module only ever farms designated zones for the active yokai.
6. On cap: advance to the next yokai (announce progress). All capped → stop with a session summary (per-yokai counts, FATEs done, elapsed time).

Vendor turn-ins (weapons, mount) remain manual in v1. The module surfaces per-yokai progress in the overlay (§10).

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

Every terminal stop carries a typed `StopReason` (`UserRequested`, `AllYokaiCapped`, `WatchMissing`, `StuckExhausted`, `DeathCapReached`, `DependencyLost`, `DataMissing`, …) surfaced in the overlay and log. Mid-run dependency loss (a required plugin unloads) pauses rather than stops when recoverable, with the reason displayed. No unbounded retry exists anywhere: every loop has a cap, every wait a timeout.

---

## 12. Testing

- **Unit tests (xUnit, `tests/Moirai.Tests`)** target the planner layer exclusively: FateSelector ranking and gates, module rotation logic, behavior transitions, recovery ladder escalation, interrupt precedence, accounting. Snapshots are constructed by builders; the edge-case catalog (Appendix A) is the test list.
- **Executors and IPC adapters** are thin by design and verified in-game via dev-plugin loading; they contain no branching logic worth mocking.
- CI runs the test suite on push.

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
- A6. Bonus FATEs outrank non-bonus when the Bonus criterion is reached.
- A7. Bonus-only mode selects nothing when no bonus FATE exists (idle, don't roam).
- A8. Unopened NPC FATEs (start time 0) assume 900 s remaining and rank only against each other unless bonus.
- A9. Nearby override: inside or within ~50 y of an eligible ring → take it immediately.
- A10. Post-completion grace window: prefer a chained/nearby spawn over a distant FATE for ~5 s.
- A11. Tie-break by lowest FATE id (determinism).
- A12. Teleport-cost model can prefer aetheryte + short hop over long direct flight.

**Types**
- B1. Collect: 7 items = full credit; batch hand-ins; partial hand-in when short.
- B2. Collect: combat disengaged during pickup/hand-in; re-engaged for Fight goal.
- B3. Collect: event-item id may populate ~1 s after FATE start — tolerate the delay.
- B4. NPC-start: accept only the FATE-start yes/no (level-recommendation text); reject all other prompts.
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
- C8. Grounded stuck → jump + sideways nudge, then escalate the ladder.
- C9. A stall while mounted inside the ring is a landing attempt, never a stuck failure (vertical-descent false positive).
- C10. Melee stop distance ≈ 2.5 y (larger breaks auto-attack range); ranged ≈ 8 y.
- C11. Sync only once actually inside the ring.
- C12. Mount only when leg length exceeds threshold and mounting is legal (not in combat/housing).

**Interrupts & lifecycle**
- D1. Death overrides all states; accept return; teleport back if displaced; resume fresh.
- D2. Death never counts as completion; failed FATE never counts as completion.
- D3. Unexpected combat outside the FATE → defensive clear, then resume. The clear stops movement, targets the nearest stray on us (sticky while it stays on us), closes to engage range on foot, and stands the rotation down once nothing is on us; the leg's stall sampler is re-anchored so the standstill is not a stuck. A stray is anything alive and attacking us or our companion that is not the current FATE's enemy: the FATE's own mobs are never fought outside the ring (the rotation cannot attack them there) and are carried in. Mounted, nothing is fought; the leg carries on and the leash ends it. Inside a running FATE combat past the ring edge (pulled mobs, knockbacks) is that FATE's own and the engage behavior walks back in (C5), but a stray on us while none of the FATE's enemies is gets the same clear, after which the FATE's behavior starts over so its own rotation mode comes back; a FATE enemy on us always outranks the stray.
- D4. Busy guard: no intents while casting/between-areas/jumping/being-moved/occupied/Lifestream-busy.
- D5. Navmesh not ready → hold movement, keep observing.
- D6. Reward latch: no zone change/teleport until FATE payout registers.
- D7. Every retry loop bounded; ladder exhaustion stops with a typed reason.
- D8. Dependency lost mid-run → pause with reason if recoverable, stop if not.
- D9. A FATE the player died in, or died inside the ring of on the way in, is not selected again in the same session, not even by the nearby override. A solo death leaves a boss at full health and progress at 0, so the ranking would send the player straight back (Lazy for You: three deaths to the cap in ten minutes, its 30-minute timer winning the TimeLeft rung every time). A death on the road does not condemn the FATE.

**Yo-kai**
- E1. Watch unequipped → equip if owned, else stop `WatchMissing`.
- E2. Active yokai = first priority-list entry under legendary cap (10).
- E3. Minion must match active yokai before farming counts — verify each tick, resummon after death/zone change (minions dismiss on some transitions).
- E4. Legendary medals only accrue in the yokai's designated zones — never farm outside them in Yo-kai mode.
- E5. On cap: advance and announce; all capped → stop with summary.
- E6. Medal counts read by item id from inventory each tick (no cached assumptions across hand-ins).

## Appendix B — Verified dependency IPC surface

Gate names verified against current plugin versions (2026-08).

**vnavmesh**: `Nav.IsReady`, `Nav.BuildProgress`, `Path.IsRunning`, `Path.Stop`, `Path.MoveTo(List<Vector3>, bool fly)`, `SimpleMove.PathfindAndMoveTo(Vector3, bool)`, `SimpleMove.PathfindAndMoveCloseTo(Vector3, bool, float)`, `SimpleMove.PathfindInProgress`, `Query.Mesh.PointOnFloor(Vector3, bool, float)`, `Query.Mesh.NearestPoint(Vector3, float, float)`.

**Lifestream**: `Lifestream.IsBusy()`, `Lifestream.AethernetTeleportById(uint)`, `/li <aetheryte>` command surface.

**TextAdvance**: `TextAdvance.IsInExternalControl()`, `TextAdvance.EnableExternalControl(string owner, config)`, `TextAdvance.DisableExternalControl(string owner)`; config flags: TalkSkip, RequestFill, RequestHandin, RewardPick, CutsceneEsc, CutsceneSkipConfirm (QuestAccept/Complete and AutoInteract deliberately off).

**BossMod / Reborn**: `BossMod.Presets.SetActive/ClearActive/GetActive/Create/Get`, `BossMod.Presets.AddTransientStrategy(preset, module, option, value)`; Reborn-only: `BossMod.Hints.ForbiddenZonesCount`, `BossMod.Hints.ForbiddenZonesNextActivation`, `BossMod.AI.IsNavigating`; AI toggles via `/bmrai` (Reborn) or `/vbm` (vanilla) command families.

**RotationSolver Reborn**: auto via `RotationSolverReborn.AutodutyChangeOperatingMode(StateCommandType, TargetingType)` with `AutoDuty` (4) and `HighMaxHP` (6), enums crossing Dalamud IPC by value; the state sets a transient targeting override that `off` clears, never toggles, and is the one state RSR does not switch off on its own out of combat, in cutscenes or between areas; a build without the gate gets `/rotation auto` and RSR's own targeting order. Manual and off via `/rotation manual|off`; state via `RotationSolverReborn.AutorotationActive() → bool` (on in any mode, AutoDuty included; absent on older builds, in which case memory decides); settings via `/rotation settings <Name> <value>` (in memory only, not saved); `RotationSolverReborn.AddPriorityNameID(uint)` / `RemovePriorityNameID(uint)` only widen what RSR may attack and do not order its targets. Auto lets RSR pick among the FATE's enemies in max-HP order (B13); Defensive maps to `manual`, which attacks the held target alone, and the planner holds the stray. RSR switches itself off on its own (out of combat for 30 s by default, death, zone change), so every switch asks it first, and a mode command while it is on toggles it off or cycles its targeting type, so a change of mode goes through `off`. RSR's attackable check runs in every mode and, with its default "Ignore Non-Fate targets while in a Fate" (`IgnoreNonFateInFate`), refuses FATE mobs whenever the game does not consider the player inside a FATE (outside the ring, or above the cap without sync) and refuses non-FATE mobs whenever it does; sync and ring re-entry precede targeting, and the option is set to `false` for a defensive clear and back to `true` afterwards.

**Wrath Combo**: lease model — `RegisterForLeaseWithCallback(internalName, pluginName, callbackPrefix) → Guid`, `SetAutoRotationState(Guid, bool)`, `SetCurrentJobAutoRotationReady(Guid)`, `SetAutoRotationConfigState(Guid, option, value)` (incl. `FATEPriority`), `ReleaseControl(Guid)`, revocation callback handling.

**Olympus** (to be added): minimal surface — `Olympus.SetAutoMode(bool)`, ideally `Olympus.IsReady()`. Tracked in the Olympus repo.

**Game APIs**: `FateManager.LevelSync()` (sync), `ActionType.Mount` / GeneralAction 9 (mount roulette) / GeneralAction 4 (sprint) / GeneralAction 2 (jump), `TargetSystem.InteractWithObject`, `InventoryManager` item counts, `PlayerState.IsMountUnlocked`.
