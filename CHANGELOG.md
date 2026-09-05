# Changelog

All notable changes to Moirai will be documented in this file.

<!-- LATEST-START -->
## v0.3.12 — 2026-09-05

### Added
- Bonus FATEs are recognized. The game flags a FATE with a bonus marker now and then, and finishing one pays out more experience, gil, seals, and bicolor gemstones. Moirai reads that flag, so the Bonus rung of the ranking ladder puts them first when it sits high, and a new "Bonus FATEs only" switch on the Selection tab idles the run until one is up. The overlay marks a bonus FATE and the debug report shows the flag
<!-- LATEST-END -->

## v0.3.11 — 2026-09-05

### Added
- Aetherytes are part of what Moirai sees: the current zone's attuned ones, with positions from the game's data. The "Distance, teleport-aware" ranking rung, which had nothing to work with until now, ranks a FATE by the cheaper of the direct path and a teleport to the aetheryte nearest it, and travel honors the same choice: a leg starts with a teleport when that beats the ride by more than the margin on the Movement tab, 200 yalms by default. A teleport that does not land within twenty seconds is given up on and the leg goes on directly
- The recovery ladder's fourth rung works: a character that stays stuck teleports to the nearest attuned aetheryte and approaches the same FATE afresh; a further stall abandons the FATE as before
- The debug report lists the aetherytes Moirai sees, with positions

### Changed
- A teleport the leg wants is held until the previous FATE's payout registers, like a zone change

## v0.3.10 — 2026-09-05

### Added
- Recordings for bug reports. While a run is on, Moirai keeps the last minute of what it saw and decided on every tick, in memory. `/moirai record`, or the new button on the About tab, saves it to the plugin's config folder as a small `.json.gz` file, and a run that stops on its own (stuck, death cap, a plugin gone) saves one without being asked. A recording replays through Moirai's planner in the test suite, tick for tick, so the exact decision that went wrong can be reproduced and fixed without the game. It holds positions, ids, and counts, no names. A switch on the General tab turns it off
- The debug report carries a timeline of the last fifty status changes with the time of each, which is usually enough to see where a run went wrong

## v0.3.9 — 2026-09-05

### Fixed
- NPC-started FATEs and collect hand-ins no longer depend on luck. Nothing in Moirai confirmed the FATE-start prompt or advanced the dialogue behind it, and the busy guard held the run while any window was open, so both only ever worked when TextAdvance happened to be installed and set up. TextAdvance is now a stated requirement, shown beside vnavmesh and RotationSolver in the overlay and the About tab: Moirai takes its external control for the run so Talk windows and the hand-in window advance on their own, and releases it at Stop. The FATE-start prompt, which TextAdvance never confirms, is confirmed by Moirai itself, and only right after Moirai talked to the starter; a prompt open at any other time is left alone. After confirming, Moirai waits for the FATE to open instead of talking to the starter again
- A required plugin going away mid-run is no longer silent. The run pauses with the reason in the overlay and resumes when the plugin is back; RotationSolver or TextAdvance missing for a minute stops the run with the reason. vnavmesh not being ready pauses for as long as it takes, since a mesh may still be building

## v0.3.8 — 2026-09-05

### Fixed
- The recovery ladder does what it says. The re-path rung issued nothing, and a path vnavmesh still considered running was never re-issued, so a re-path only ever happened once vnavmesh had already given up on its own. The vertical escape was overwritten by the next travel tick within half a second. Now the re-path rung drops the running path so the next leg is issued fresh, and the escape is held for a second and a half before travel resumes: straight up when mounted with flight, otherwise a few yalms sideways with a jump, the ground escape the design always called for
- A FATE the ladder gives up on no longer ends the run. It is counted as abandoned, skipped for the rest of the session, and the next FATE is chosen. The run still stops as stuck when the character is truly wedged: three exhausted ladders in a row without moving more than ten yalms between them
- A character with no mount no longer stands still forever. Moirai asked to mount for every leg over thirty yalms and treated the wait as the mount cast, so nothing ever counted as a stall. Whether the zone allows mounts and whether a mount is owned are now read from the game, and a mount that does not take within six seconds is given up on for that leg, which is then walked
- Flight is used only where the zone's aether currents are attuned. Until now every mounted leg was flown, and in a zone without flight unlocked the mount could not follow the airborne path, so every leg fed the recovery ladder
- The debug report shows why each FATE is skipped, which FATEs are ruled out for the session and why, and whether mounting and flying are available

## v0.3.7 — 2026-09-05

### Fixed
- Special boss FATEs are recognized from the game's own data and are no longer joined from zero. The achievement and world-boss FATEs such as Lazy for You, Behemoth, and Odin open with the game's big-boss banner where an ordinary boss FATE does not, and Moirai now reads that banner from the FATE sheet, so they take the special boss join threshold at any level. Until now only bosses at level 60 and above counted as special, so a level 20 character was sent in against Lazy Laurence alone, at full health, three times in a row
- A FATE the character dies in is not chosen again for the rest of the run. A solo death leaves a boss at full health and the FATE at 0%, so the ranking kept picking it straight back. A death on the road to a FATE does not rule the FATE out
- The debug report shows each FATE's banner and whether Moirai treats it as a special boss

### Changed
- The settings hint under the boss thresholds says what a special boss is

## v0.3.6 — 2026-09-04

### Fixed
- Escort, defend, and boss FATEs are now recognized as what they are. The FATE sheet's rule column was read as a kind, but rule 2 is collect, 3 is escort, 4 is defend, and rule 1 covers both plain kill FATEs and bosses, which only the map icon tells apart. Until now escort FATEs were fought as plain battles with no following, defend FATEs were run as bosses (no walking while in combat, and the boss join threshold gated them), and bosses were never recognized at all. A collect FATE is also collect by its rule now, so the moment before its event item populates no longer reads as a battle
- The debug report shows the sheet icon next to the rule
- A boss is fought over its adds. In Revenge of the Worms the sandworms were targeted while Ulhuadshi stood there: RotationSolver's default targeting picks the lowest-HP enemy, and Moirai accepted whatever it held. The FATE sheet does not name the objective, so an enemy with at least twice the max HP of the smallest in the FATE now counts as its boss. Moirai engages it first, and RotationSolver is switched on through its AutoDuty entry point with a highest-max-HP targeting order so the two agree. That entry point also stops RotationSolver switching itself off out of combat. A RotationSolver build without it gets the old chat command
- The debug report shows each enemy's max HP

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
