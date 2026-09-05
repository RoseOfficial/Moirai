# Moirai

![Downloads](https://img.shields.io/github/downloads/RoseOfficial/Moirai/total)

Automated FATE farming for FFXIV, built on a pure, unit-tested decision core.

Moirai picks the best FATE in your zone, travels there, syncs, fights it to
completion, and repeats, recovering from deaths and stuck states along the way.
It is named for the three Fates of Greek myth and ships under RoseOfficial
alongside Olympus and Komos.

## What it does

- Ranks FATEs by a ladder you can reorder: progress, bonus, time left, and
  distance. Hard gates skip FATEs that are nearly over, too far along, above
  your level, or on your blacklist.
- Handles battle, boss, defend, escort, and collect FATEs, and can open
  NPC-started FATEs.
- Joins boss FATEs only once their progress passes a threshold, so you never
  solo-tank from zero.
- Mounts when the leg is worth it and a mount is available, flies where the
  zone's aether currents are attuned, and walks in zones whose geometry breaks
  flight pathing.
- Recovers from stuck states through a bounded ladder: re-path, a new landing
  point, then an escape (straight up when flying, otherwise a sideways nudge
  with a jump). A FATE that stays out of reach is abandoned and skipped for the
  session; the run stops with a stated reason only when the character is
  wedged in place.
- Fights off stray aggro: anything that jumps you or your chocobo on the way to
  a FATE, or inside one between its waves, is targeted and killed where you
  stand before the run carries on. Mounted, it keeps riding and lets the mobs
  leash.
- Accepts the return prompt on death, counts it, and resumes; stops at a death
  cap you choose.
- Keeps your chocobo companion summoned with Gysahl Greens, in the stance you
  pick, and tops up its timer before it lapses. Greens are never spent while
  mounted or in combat.
- `/moirai` opens the overlay; `/moirai help` lists chat commands.
- `/moirai debug` copies a plain-text report of every FATE in the zone and how
  Moirai reads it. Paste it into a bug report.

## What it does not do (yet)

- Farms only the zone you are standing in. Zone rotation and event modes are
  planned for a later release.
- No food, gear repair, or gemstone shopping.
- Bonus (Twist of Fate) detection and continuation chains are not wired yet, so
  those rungs of the ladder have no effect for now.

## Requirements

- [vnavmesh](https://github.com/awgil/ffxiv_navmesh) for all movement.
- [RotationSolver Reborn](https://github.com/FFXIV-CombatReborn/RotationSolverReborn)
  for combat, driven through its `/rotation` commands. While Moirai clears
  stray aggro it sets RotationSolver's "Ignore Non-Fate targets while in a
  Fate" option aside and puts it back (on) afterwards; the change is never
  saved to RotationSolver's config.
- [TextAdvance](https://github.com/NightmareXIV/TextAdvance) for NPC dialogue.
  Moirai holds its external control while a run is on so Talk windows and the
  collect hand-in window advance on their own, and releases it at Stop. The
  FATE-start prompt is confirmed by Moirai itself, and only right after Moirai
  talked to the starter.

The overlay warns when any of the three is missing, and a run pauses with the
reason if one goes away mid-session. Keep Gysahl Greens in your inventory
if you want the companion kept out; that part is optional and switchable.

## Installation

Moirai ships through the Olympus plugin repository. In Dalamud, open Settings →
Experimental → Custom Plugin Repositories, add

    https://raw.githubusercontent.com/RoseOfficial/Olympus/main/repo.json

save, then install Moirai from the plugin installer. If you already use Olympus
or Komos, the repository is already there and Moirai simply appears in the list.

To build a sideload copy instead, run a Release build of `src/Moirai/Moirai.csproj`;
the ready-to-install zip lands at `src/Moirai/bin/Release/Moirai/latest.zip`.

## Bug reports

- `/moirai debug` copies a plain-text report: every FATE in the zone as the
  game reports it and as Moirai reads it, what Moirai sees around the
  character, and a timeline of the last fifty status changes. Paste it in.
- `/moirai record` saves the last minute of what Moirai saw and decided, tick
  by tick, to the plugin's config folder as a small `.json.gz` file. A run that
  stops on its own (stuck, death cap, a plugin gone) saves one without being
  asked. Attach it: the file replays through Moirai's planner in the test
  suite, so the exact decision can be reproduced and fixed without the game.
  It holds positions, ids, and counts, no names or chat. The General tab has
  a switch to turn recording off.

## Safety notes

Moirai automates movement, targeting, and FATE participation on your behalf.
Every stop carries a reason in the overlay, every retry is bounded, and the
death cap ends a run that keeps going wrong. Watch the first few runs in a new
zone before leaving it alone.

As with any third-party tool, use Moirai at your own discretion and avoid
discussing plugins in-game.
