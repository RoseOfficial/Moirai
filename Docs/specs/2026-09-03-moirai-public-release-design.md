# Moirai — Public Release Design

**Date:** 2026-09-03
**Status:** Implemented for v0.3.0; scope decisions recorded for owner review
**Scope:** Everything between "farms FATEs from a dev build" and "installable from the Olympus plugin repository" for the first public version, 0.3.0. Follows the core design of 2026-08-28.

## Overview

Moirai ships its core FATE loop first. The Yo-kai Watch event module described in §8 of the core design is deferred to a later release: its code leaves `main` so the shipped plugin contains only what it advertises, and is preserved on the `yokai` branch for that release. Publishing follows the model Komos established on 2026-09-02: a plugin repository of its own for source, CI, and GitHub Releases, and a tag-triggered workflow that writes the Moirai entry into the Olympus aggregator manifest so the manifest never drifts from what shipped.

One robustness fix rides along because it changes what a stranger experiences on first use, and one small feature the core design deferred, chocobo companion upkeep (§6), was pulled forward on 2026-09-04 because solo farming without it is slower and riskier than it needs to be. No other behavior changes.

## 1. Deferring the Yo-kai module

- Removed from `main`: `YokaiModule`, the `Yokai` record, the `EnsureMinion`, `BuyMinion`, and `EnsureWatch` directives, the `SummonMinion`, `AcquireMinion`, and `EquipWatch` intents, the `AllYokaiCapped`, `WatchMissing`, and `MinionsMissing` stop reasons, the minion and watch fields on `PlayerSnapshot` and `WorldSnapshot`, the minion purchaser, the yokai data table, and every yokai control in the UI and configuration.
- Kept: the module abstraction (`IFarmModule`, `FarmHere`, `MoveToTerritory`, `StopSession`) and the `ChangeZone` intent, which any zone-rotating module needs, and the per-zone no-fly overrides, now in `Data/ZoneData.cs` as generic curated knowledge.
- Preserved: the pre-removal `main` is the `yokai` branch. Re-adding the module later is a merge of that branch plus whatever the core learned in between.
- Tests: the fifteen yokai tests leave with the module; seventy remain and pass. Appendix A's E-series in the core design stays as the requirement list for the later release.

## 2. Robustness fix: collect hand-ins

The snapshot builder counted only yokai medal items, so `CollectBehavior` always saw zero of a collect FATE's event item and never reached its hand-in branch. The builder now counts the event item of every visible collect FATE. Plugin-side, verified in-game; the behavior itself is already covered by B1–B3 tests.

## 3. Settings and overlay

Spec §10 asks for a tabbed settings window and an overlay whose status line always states what and why. This release delivers:

- **General**: death cap, skip NPC-started FATEs.
- **Selection**: the gates (min time left, max progress, level margin, boss and special-boss join thresholds), the ranking ladder as a reorderable list with a reset, and a fate-id blacklist with an "add current" shortcut. Names are looked up from the `Fate` sheet for display only; the blacklist is keyed by id. The Bonus rung is labeled inert until bonus detection lands with the data layer.
- **Movement**: flight toggle, mount leg threshold, arrival tolerance.
- **Combat**: melee and ranged stop distances, whether RotationSolver Reborn is loaded, and the chocobo companion section (§6): enable, stance, top-up threshold, stop-when-out-of-greens.
- **About**: version, required plugins with live status, commands, repository link.
- **Overlay**: Start/Stop, a Settings button, the phase in plain words, warnings when vnavmesh is not ready or the combat backend is missing, the current FATE by name with kind, progress, and time left, the session ledger, and the stop reason in plain words.
- **Commands**: `/moirai` toggles the overlay; `start`, `stop`, `config`, and `help` verbs; `debug` prints the planner status and visible UI for support.

Configuration moves to version 2. Removed fields are ignored on load; new fields take their defaults.

## 4. Publishing model

Identical to Komos with the names changed:

- Repository `https://github.com/RoseOfficial/Moirai`, default branch `main`.
- Release: a git tag `vX.Y.Z` whose version equals `<Version>` in `src/Moirai/Moirai.csproj`. One asset, `Moirai.zip`, which is the SDK's packaged `src/Moirai/bin/Release/Moirai/latest.zip` renamed. The zip carries `Moirai.dll`, `Moirai.Core.dll`, and the packager-stamped `Moirai.json`.
- Download links pin to the tag. Icon at `https://raw.githubusercontent.com/RoseOfficial/Moirai/main/src/Moirai/images/icon.png`; dev-folder installs read the `images/icon.png` the csproj copies next to the DLL.
- The manifest entry is generated at release time from the packaged `Moirai.json` plus `RepoUrl`, `IconUrl`, and the three download links. The Olympus `repo.json` update drops any element whose `InternalName` is `Moirai` and appends the fresh entry; every other entry is untouched. Commit message `Update repo.json for Moirai vX.Y.Z`.
- Secrets in the Moirai repository: `OLYMPUS_REPO_TOKEN` (fine-grained, Contents read and write on `RoseOfficial/Olympus` only) and `DISCORD_WEBHOOK`. Either missing degrades to a warning; the GitHub Release still publishes and the entry is uploaded as a workflow artifact for pasting by hand.

## 5. Repository changes

- `src/Moirai/Moirai.csproj` moves from a hand-rolled `Microsoft.NET.Sdk` project with hint paths to `Dalamud.NET.Sdk/15.0.0`, version `0.3.0`, `net10.0-windows`. Output lands in `bin/<Configuration>/` (no `x64` segment); dev-plugin paths need updating once.
- `.github/workflows/ci.yml` and `release.yml` replace `test.yml`, which ran `dotnet test` on the whole solution and so needed game libraries CI never had.
- `CHANGELOG.md` in the Olympus format with the `LATEST-START`/`LATEST-END` markers the release workflow reads. First entry: v0.3.0.
- `README.md`: what it does, what it does not do yet, requirements (vnavmesh, RotationSolver Reborn), the Olympus repository URL to add, a sideload note, safety notes.
- `LICENSE`: MIT, copyright RoseOfficial. `Moirai.json` names the same author, so no personal name appears in the repository, the packaged manifest, or the installer. Git author identity is the RoseOfficial account with its noreply email.
- `Docs/plans/` becomes untracked local working notes, as in Komos.

## 6. Chocobo companion

Snapshot: three player facts read once per tick, whether the companion is out (its timer is above zero), seconds left on that timer, and the BuddyAction row id of its active stance; plus the Gysahl Greens count in the item table, keyed by item id like every other consumable.

Planner: `CompanionUpkeep` in `Planning/`, a pure class the Director consults after the interrupt ladder and before the module, in every phase except Traveling (a summon mid-leg only stalls the leg). It emits `SummonCompanion(greensItemId)` when the companion is missing or its timer is under the configured threshold, and `SetCompanionStance(actionId)` when the active stance differs from the configured one. Stance 0 means leave the player's choice alone. Bounds: one greens use per six-second cooldown, at most three summon attempts and three stance attempts before it gives up with a note, and a zone change or a companion that shows up resets the budget. Never acts while mounted, in combat, or casting. With "stop when out of greens" on, the Director stops with the typed reason `OutOfGreens` once the companion is gone and no greens remain; otherwise the run continues and the overlay carries the note.

Executor: greens through the item action with the navmesh path stopped first, stance through the buddy action type. Both throttled.

Catalog (tests in `CompanionUpkeepTests` and `DirectorTests`):

- F1. No companion out and greens held: summon.
- F2. Companion out, timer healthy, stance matches: nothing.
- F3. Timer under the threshold: greens again to extend.
- F4. Never while mounted or in combat.
- F5. No greens: nothing issued, reason surfaced; a companion still out is not yet "out of greens".
- F6. Stance set when it differs, before any top-up; stance 0 never changes it.
- F7. One greens use per cooldown window.
- F8. Bounded attempts; a zone change or a successful summon restores the budget.
- F9. Director consults upkeep while selecting or in a fate, never while traveling.
- F10. Out of greens stops the run only when configured; otherwise it carries on.

## 7. Owner steps

1. Update the Dalamud dev-plugin location to `src\Moirai\bin\Debug\Moirai.dll` under the repository's current path.
2. Create the empty public repository `RoseOfficial/Moirai`; `origin` is already set; push `main` and the `yokai` branch.
3. Add the Actions secrets `OLYMPUS_REPO_TOKEN` and `DISCORD_WEBHOOK` to `RoseOfficial/Moirai`.
4. Confirm the CI run on `main` is green.
5. Tag `v0.3.0` and push the tag. Confirm the Release carries `Moirai.zip` and the Olympus manifest gained the Moirai entry.
6. In-game verification on the released zip installed through the Olympus repository: a battle FATE end to end, a collect FATE through hand-in, a boss FATE respecting the join threshold, a death with the return prompt, the companion summoned and put in the chosen stance at the first settled moment, and a Stop from the overlay and from `/moirai stop`.

## Out of scope

- The Yo-kai module and any zone-rotating module.
- Bonus (Twist of Fate) detection, continuation chains, aetheryte projection, and the versioned JSON data layer.
- Combat backends other than RotationSolver Reborn.
- Official Dalamud repository submission.

## Testing

- Core: the existing suite passes after the removal (70 tests); the companion upkeep adds eighteen more (88), written before the code.
- Plugin shell: clean Debug and Release builds; the packaged zip inspected for both assemblies and the stamped manifest.
- Workflows: YAML parsed locally; the manifest-merge command exercised against a copy of the live Olympus manifest; the changelog extraction and the tag/version check run locally against the committed files.
