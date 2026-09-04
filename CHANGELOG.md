# Changelog

All notable changes to Moirai will be documented in this file.

<!-- LATEST-START -->
## v0.3.6 — 2026-09-04

### Fixed
- Escort, defend, and boss FATEs are now recognized as what they are. The FATE sheet's rule column was read as a kind, but rule 2 is collect, 3 is escort, 4 is defend, and rule 1 covers both plain kill FATEs and bosses, which only the map icon tells apart. Until now escort FATEs were fought as plain battles with no following, defend FATEs were run as bosses (no walking while in combat, and the boss join threshold gated them), and bosses were never recognized at all. A collect FATE is also collect by its rule now, so the moment before its event item populates no longer reads as a battle
- The debug report shows the sheet icon next to the rule
- A boss is fought over its adds. In Revenge of the Worms the sandworms were targeted while Ulhuadshi stood there: RotationSolver's default targeting picks the lowest-HP enemy, and Moirai accepted whatever it held. The FATE sheet does not name the objective, so an enemy with at least twice the max HP of the smallest in the FATE now counts as its boss. Moirai engages it first, and RotationSolver is switched on through its AutoDuty entry point with a highest-max-HP targeting order so the two agree. That entry point also stops RotationSolver switching itself off out of combat. A RotationSolver build without it gets the old chat command
- The debug report shows each enemy's max HP
<!-- LATEST-END -->

## v0.3.5 — 2026-09-04

### Fixed
- Stray aggro is fought back for real. RotationSolver Reborn switches itself off on its own (thirty seconds out of combat by default, on death, on a zone change), and Moirai assumed it was still on, so the next time something jumped the character the rotation stayed off. Moirai now asks RotationSolver whether it is on before every switch, changes modes through off so a repeated command never toggles it off or cycles its targeting, and re-asserts the mode while fighting, so a rotation that switched itself off during a lull comes back for the next wave
- Whatever is hitting the character, or its chocobo, is now targeted and fought where it stands, on the way to a FATE and inside one whenever none of the FATE's own enemies is attacking. RotationSolver ignores anything that is not the FATE's while the game counts the character inside one, so its "Ignore Non-Fate targets while in a Fate" option is set aside for the clear and put back afterwards. Mounted, Moirai keeps riding and lets the mobs leash
- Standing still for a stray fight no longer reads as a stuck leg afterwards
- Collect FATEs fought their second wave with the rotation off after a pickup had turned it off
- The debug report now lists the enemies Moirai sees, whether each is on the character, and whether RotationSolver reports itself on

## v0.3.4 — 2026-09-04

### Fixed
- Collect FATEs waiting at their NPC are no longer treated as open: the game can report a start time while a FATE is still in preparation, so Moirai now goes by the FATE's phase and starts it at the NPC instead of trying to pick up its items first
- If a FATE's classification changes while Moirai is inside it, the matching behavior takes over immediately

### Added
- `/moirai debug` and a matching button on the About tab copy a plain-text report of every FATE in the zone and how Moirai reads it, for bug reports

## v0.3.3 — 2026-09-04

### Fixed
- Rescue FATEs no longer target the captives: a FATE's friendly NPCs, such as the Abducted Ala Mhigans, are told apart from its enemies by the game's own attack check instead of by NPC kind
- Targeting no longer fights RotationSolver Reborn for the target: any FATE enemy it switches to is accepted, and a foreign or friendly target is replaced with a FATE enemy rather than cleared, so the two never trade the target back and forth
- Being pulled or knocked past the ring edge mid-fight no longer freezes the run; it walks back into the ring and keeps fighting
- Stray aggro on the way to a FATE is now fought off: the defensive combat switch was dropped whenever the rotation was already on, and RotationSolver's manual mode attacks nothing without a held target
- A character at or below a FATE's level cap no longer waits for a level sync the game never offers

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
