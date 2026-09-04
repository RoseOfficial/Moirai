# Changelog

All notable changes to Moirai will be documented in this file.

<!-- LATEST-START -->
## v0.3.1 — 2026-09-04

### Fixed
- "Skip NPC-started fates" now also skips unopened collect FATEs such as Hide and Seek; they were classified as collect FATEs before the skip could see them
- With the skip off, an unopened collect FATE is started at its hand-in NPC before the collect loop takes over
<!-- LATEST-END -->

## v0.3.0 — 2026-09-03

### New — First Public Release
- Farms the FATEs in your current zone: ranks them by a ladder you can reorder (progress, bonus, time left, distance), travels there, syncs, and fights each one to completion
- Handles battle, boss, defend, escort, and collect FATEs, and can open NPC-started FATEs; boss FATEs are joined only past a progress threshold so you never solo-tank from zero
- Recovers on its own: a bounded re-path and re-roll ladder for stuck states, death handling with a per-session death cap, and a reward latch that never leaves before a FATE pays out
- Keeps your chocobo companion out with Gysahl Greens and in the stance you pick, topping up the timer before it lapses and never spending a green while mounted or in combat; optionally stops the run when you are out of greens
- Settings window with General, Selection, Movement, Combat, and About tabs, including a fate-id blacklist and a reorderable ranking ladder
- `/moirai start`, `/moirai stop`, and `/moirai config` for macro-driven control
- Requires vnavmesh for movement and RotationSolver Reborn for combat; the overlay says so when either is missing
