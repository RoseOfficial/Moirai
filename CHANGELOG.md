# Changelog

All notable changes to Moirai will be documented in this file.

<!-- LATEST-START -->
## v0.3.3 — 2026-09-04

### Fixed
- Rescue FATEs no longer target the captives: a FATE's friendly NPCs, such as the Abducted Ala Mhigans, are told apart from its enemies by the game's own attack check instead of by NPC kind
- Targeting no longer fights RotationSolver Reborn for the target: any FATE enemy it switches to is accepted, and a foreign or friendly target is replaced with a FATE enemy rather than cleared, so the two never trade the target back and forth
- Being pulled or knocked past the ring edge mid-fight no longer freezes the run; it walks back into the ring and keeps fighting
- Stray aggro on the way to a FATE is now fought off: the defensive combat switch was dropped whenever the rotation was already on, and RotationSolver's manual mode attacks nothing without a held target
- A character at or below a FATE's level cap no longer waits for a level sync the game never offers
<!-- LATEST-END -->

## v0.3.2 — 2026-09-04

### Fixed
- The mount no longer hovers over a FATE forever: a standstill while mounted inside the ring now dismounts, and a dismount that never takes re-rolls the landing point through the recovery ladder
- Ring membership is measured on the ground plane, so FATEs on hills and ridges register as entered on arrival, in combat, and for the nearby override
- A standstill on the way to a FATE now feeds the recovery ladder instead of re-issuing the same path indefinitely

## v0.3.1 — 2026-09-04

### Fixed
- "Skip NPC-started fates" now also skips unopened collect FATEs such as Hide and Seek; they were classified as collect FATEs before the skip could see them
- With the skip off, an unopened collect FATE is started at its hand-in NPC before the collect loop takes over

## v0.3.0 — 2026-09-03

### New — First Public Release
- Farms the FATEs in your current zone: ranks them by a ladder you can reorder (progress, bonus, time left, distance), travels there, syncs, and fights each one to completion
- Handles battle, boss, defend, escort, and collect FATEs, and can open NPC-started FATEs; boss FATEs are joined only past a progress threshold so you never solo-tank from zero
- Recovers on its own: a bounded re-path and re-roll ladder for stuck states, death handling with a per-session death cap, and a reward latch that never leaves before a FATE pays out
- Keeps your chocobo companion out with Gysahl Greens and in the stance you pick, topping up the timer before it lapses and never spending a green while mounted or in combat; optionally stops the run when you are out of greens
- Settings window with General, Selection, Movement, Combat, and About tabs, including a fate-id blacklist and a reorderable ranking ladder
- `/moirai start`, `/moirai stop`, and `/moirai config` for macro-driven control
- Requires vnavmesh for movement and RotationSolver Reborn for combat; the overlay says so when either is missing
