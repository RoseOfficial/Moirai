# Moirai — Dawntrail Gemstone Module

**Date:** 2026-09-25
**Status:** Approved design, pre-implementation
**Scope:** A farm module that ranks every Dawntrail zone's Shared FATE to its maximum, buys and learns every collectible the Dawntrail gemstone traders sell, then works toward the Ty'aitya Whistle and farms on. Follows the core design of 2026-08-28; the module slots in beside the Yo-kai module (§8 there) and reuses its patterns: a pure module that composes a zone rotation, a shell-side purchaser that runs per frame outside the busy guard, and ids everywhere.

## Overview

FATEs in Dawntrail zones pay Bicolor Gemstones and advance that zone's Shared FATE rank. The rank gates what the zone's gemstone trader sells, and rank 4 in all six zones opens the two city traders. Buying everything takes about 13,240 gemstones against a hold of 1,500, so the run has to shop as it goes. The mode is one switch: the run ranks the zones, shops whenever the gemstones near the cap, learns what it buys, turns surplus gemstones into vouchers when nothing is left to buy, buys the voucher mount at 500 Turali vouchers, and then keeps farming until stopped.

## 1. Facts this design rests on

Verified on xivapi v2 (game version 7.56x1, EXDSchema column names) and ffxiv.consolegameswiki.com on 2026-09-25, unless marked otherwise. The implementation plan re-verifies every id before it goes into the data table.

- **Currency.** Dawntrail pays the same Bicolor Gemstone as Endwalker, item 26807 (ItemUICategory 100 Currency, StackSize 1500). The hold is 1,500. It lives in `CurrencyManager`'s item bucket rather than a bag; `CurrencyManager.GetItemCount(26807)` reads it. Whether `InventoryManager.GetInventoryItemCount` also sees it is not verified.
- **Zones.** TerritoryType 1187 Urqopacha, 1188 Kozama'uka, 1189 Yak T'el, 1190 Shaaloani, 1191 Heritage Found, 1192 Living Memory (ExVersion 5). Cities: 1185 Tuliyollal, 1186 Solution Nine.
- **Ranks.** Sheet `FateProgressUI` rows 12–17: `ReqFatesToRank2/3/4` = 6/20/40, increments, so 6, 26 and 66 FATEs in total for ranks 2, 3 and 4 (the rank-4 achievements count to 66). Rank 2 adds the zone's riding map, rank 3 a furnishing, rank 4 the orchestrion rolls, minions, barding and cards, plus the achievement "Free Market Friend: <zone>" (Achievement 3559–3564 in zone order).
- **Reading ranks.** The client keeps them only in `AgentFateProgress` (`Tabs[3].Zones[6]`: `TerritoryTypeId`, `CurrentRank`, `MaxRank`, `FateProgress`, `NeededFates`), present in the bundled ClientStructs. Observed in the reference tools, not verified here: the agent fills only once the Shared FATE window (addon `FateProgress`, main command 84) has opened, and callback −1 closes it; `MaxRank` has been reported unreliable, so the rank ceiling (4) comes from the data table, not the agent.
- **Traders.** Every trader's only handler is a CustomTalk (721619 zone traders, 721620 city traders and Gadfrid). The shops sit in sheet `FateShop`, keyed by NPC id: three `SpecialShop` rows per zone trader, nested supersets picked by the game's script by rank. Rank-4 items carry `SpecialShop.Item[].AchievementUnlock` = 3559–3564; some carry `Item[].Quest`. The city traders' "rank 4 in all six zones" gate lives only in their script (wiki); nothing in the sheets encodes it. Every Dawntrail gemstone shop (SpecialShop 1770736–1770755) charges item 26807.
- **Distances.** From the `MapMarker` sheet (aetheryte markers, maps 857–862, size factor 100, no offset), every zone trader stands 17–74 y from an attuned-able aetheryte in its zone (Appendix T). A trip is a teleport and a short walk.
- **Edelina** (ENpcResident 1036913, Luxury Trader, Mor Dhona at map 22.1, 4.9; no `Level` placement row) has a DefaultTalk, a GilShop (263043) and SpecialShop 1770501, so interacting opens a menu. That shop sells the Ty'aitya Whistle (item 43590, Mount row 342) for 500 Turali Bicolor Gemstone Vouchers (43961).
- **Unlock checks.** `UIState.IsItemActionUnlocked(Item*)` answers "already learned" for every item kind in the catalog (minion, mount, barding, orchestrion roll, Triple Triad card, framer's kit, fashion accessory, unlock-link items such as riding maps); `QuestManager.IsQuestComplete` answers the quest gates. Both are in the bundled ClientStructs.
- **Shop window.** The exchange opens as `ShopExchangeCurrency`; a purchase is callback (0, row, quantity) followed by a confirmation (`ShopExchangeCurrencyDialog` or `SelectYesno`), and callback −1 closes it (the reference SND script, facts only). The row order is not sheet order and locked rows may be hidden, which is why rows are found by item id (§7).

## 2. Stages

The module derives the stage from the snapshot on every call and stores none, so a restart, a death, or a purchase made by hand never leaves it out of step.

1. **Ranking** — any of the six zones below rank 4.
2. **Collecting** — all six at rank 4 and a catalog collectible is not learned whose gate quest (if any) is complete and that is not unavailable this session (§7). Affordability does not count: a collectible waiting on gemstones keeps the stage.
3. **Mount** — every buyable collectible learned, the whistle wanted ("Buy the Ty'aitya Whistle", on by default) and not learned.
4. **Farming on** — everything done. The run never stops on its own here: it farms the six zones and turns gemstones into Turali vouchers near the cap.

Until the first rank read lands, the only thing the module asks for is that read (§4).

A collectible whose gate quest is not complete, or that the game's shop does not list (§7), never holds a stage back: it is shown on the overlay as a note, and it counts again the moment its quest is done or a new session starts.

## 3. Zones

Ranking farms the unmaxed zones only, as a zone rotation (G1–G5 of the core catalog): the current zone when it is unmaxed, otherwise the first unmaxed one in map order (1187 → 1192). A zone is left when a read shows it at rank 4, or after the quiet period with no eligible FATE (G2, 120 s). A zone that cannot be reached is skipped (G4); every one failing in a row stops the run with `ZonesUnreachable` (G5), which also covers a character without Dawntrail. The unmaxed set shrinks as reads come in, and the rotation is rebuilt when it changes, the same way the Yo-kai module rebuilds its rotation for a new yokai.

The other three stages rotate all six zones.

## 4. Shared FATE ranks

**Reader (shell).** `SharedFateReader` performs one read per request: it opens the Shared FATE window through main command 84, waits for the agent's zones to carry the six Dawntrail territories, copies rank, progress and needed for each, and closes the window with callback −1. One attempt is bounded at 10 s and ends either with the six zones or with a failure. The window is on screen for a second or two.

**Read policy (planner).** The module asks for a read (`ReadRanks` → intent `ReadSharedFate`) only between FATEs, in a settled moment:

- at the start of the run;
- while ranking, when the completions it attributed to a zone since the last read reach what that zone still needed for its next rank, and after each completion from then on until a read shows the rank-up;
- on every 10th completion in an unmaxed zone since the last read, as a check against credit the attribution missed;
- never once all six zones read rank 4.

Completions come from the Director: `ModuleContext` gains the session ledger's completed count, and the module attributes each increase to the territory the snapshot shows at that tick.

**Failures.** A failed read is asked again after 60 s; three failures in a row stop the run with the new reason `RanksUnreadable`, with a chat line pointing at `/moirai debug`.

## 5. Snapshot and data

`WorldSnapshot` gains, all by id:

- `SharedFate`: per territory the read rank, progress and needed, the read's stamp, and the reader's consecutive failure count; null until the first read.
- Item counts for the gemstones (26807, read through `CurrencyManager`), the two vouchers (35833 Endwalker, 43961 Turali), and every catalog item, added to the existing `ItemCounts`.
- `LearnedItems`: the catalog items this character has learned, from `UIState.IsItemActionUnlocked`, refreshed about once a second (some forty items).
- `QuestsDone`: the catalog's gate quests that are complete.
- `ShopReady`: the shopper can take a trip ("Buy from the traders" on); `FailedTraders`: traders whose trip failed this session; `UnavailableItems`: items a trader's window did not list this session.

`ModuleContext` gains `Completed` (the ledger's count, §4) and `BetweenFates` (the Director is selecting a FATE, no payout is pending, and the player is not mounted, in combat or casting).

**Catalog.** A static table in the shell, `Data/GemstoneData.cs`, holds the ten traders (six zone traders, Kajeel Ja, Beryl, Gadfrid, Edelina) and what they sell: NPC id, territory, the aetheryte a trip teleports to, the NPC's position, and per item its id, cost, currency, kind (collectible, voucher, mount) and gate (zone rank, all six at rank 4, gate quest). Appendix T lists it. It reaches the core as `GemstoneConfig` inside `RunSettings` (a new optional parameter, as the Yo-kai config did), so a recording replays with the catalog it ran with.

## 6. Shopping

**When.** A trip is asked for only between FATEs (`BetweenFates`) and only while `ShopReady` holds:

- the gemstones reach the spend threshold (default 1,400, a setting); or
- in the Mount stage, the Turali vouchers reach 500 (the trip is to Edelina).

The trip is held while a payout is pending (D6), since it is a zone change. The cap alone triggers shopping: the run keeps farming in every stage, so buying at the threshold rather than the moment something becomes affordable loses nothing, and trips stay rare (one per ninety-odd FATEs at 16 gemstones a FATE).

**Learning.** A catalog collectible that sits in the bags unlearned (bought by the shopper, or by hand) is used in the next settled moment, whatever the stage, through a `LearnItem` directive; asked for 10 s of settled moments without taking, it is given up on for the session and noted (the E8 pattern).

**What.** An item is buyable when it is not learned, not in the bags, its gates are met, it is not unavailable this session, its trader has not failed, and the gemstones cover it. The trip goes to the trader with the largest total of affordable buyable items; ties go to the trader in the current zone, then to catalog order. At that trader the list is every buyable item, riding maps first (they speed up the farming in their zone), then catalog order, while the gemstones still cover the next.

**Vouchers.** When no trader has anything buyable and the gemstones are at the threshold, the trip is for vouchers: Turali vouchers from a city trader once all six zones are rank 4 (the one in the current zone, else Kajeel Ja), Endwalker vouchers from Gadfrid in Old Sharlayan before that. Before the Mount stage the exchange brings the gemstones down below 500, keeping enough for the unlocks still to come; from the Mount stage on it exchanges every full 100.

**The mount.** In the Mount stage, at 500 Turali vouchers, the trip is to Edelina for the whistle, which is then learned. With the whistle switch off the Mount stage is skipped.

**Directive.** `GoShopping(trader, items with quantities)` becomes the intent `ShopAtTrader`, idempotent while the shopper runs, like `BuyMinion` → `AcquireMinion`. After the trip the module sees the new counts and decides again, so a trip that leaves the gemstones above the threshold is followed by the next one.

## 7. The shopper

`GemstoneShopper` runs per frame from the plugin while a trip is active, outside the planner's busy guard, as the minion purchaser does.

1. **Travel.** Teleport (Telepo) to the trader's aetheryte from the table unless already in that territory within 150 y of the trader; then walk with vnavmesh, sprinting when the game allows. The walk aims at the table position until the NPC is in the object table, then at the NPC's own position. Edelina has no placement row: her table position comes from her map coordinates, resolved to the mesh floor.
2. **Talk.** Interact; TextAdvance carries any talk. Edelina's menu entry is picked by the index in the settings, pinned from a debug report with her menu open; a menu anywhere else is unexpected and fails the trip with the debug hint.
3. **Buy.** In `ShopExchangeCurrency` (or `ShopExchangeItem` at Edelina), each wanted item's row is found by item id among the window's values, never by position or text. Callback (0, row, quantity), confirm, and wait until the count moves. An item the window does not list is added to `UnavailableItems` and skipped; that does not fail the trip.
4. **Finish.** Close the window (callback −1), use each bought collectible to learn it, and confirm it is learned.

Each step is bounded at 20 s; the walk fails when it has not got closer for 10 s. A failed trip prints a chat line naming the trader and the step, adds the trader to `FailedTraders` for the session, and hands the character back to the planner. A pause, a stop, or a duty (D11) resets a trip under way, as it does the purchaser and the repairer.

The window's value layout (where each row's item id and cost sit) is pinned while writing the plan from public sources; if it cannot be, the first release reads rows from a debug report, which already dumps every open window's values.

## 8. Failures and stop reasons

New `StopReason` values:

- `RanksUnreadable` — three rank reads in a row failed (§4).
- `GemstonesCapped` — the gemstones are at the full 1,500, shopping is on, and no working trader has anything to sell for them (every relevant trader failed, or the voucher source did).

With "Buy from the traders" off there are no trips; the cap is noted on the overlay and the run farms on. Everything else is the core's: death (D1, D9), duties (D11), pause (§11 of the core design), stuck (D10), and unreachable zones (G4, G5). A zone trader who is still closed for the character's story progress fails the trip at the talk step and is skipped for the session.

## 9. Settings, overlay, debug report

**Gemstones tab.** The mode switch (off by default; turning it on turns the Yo-kai mode off and the reverse); the spend threshold (default 1,400); "Buy from the traders" (on); "Buy the Ty'aitya Whistle" (on); Edelina's menu index. Below them, the six zones' ranks and progress from the last read, and the catalog with each item marked learned, buyable, waiting on a rank, needing a quest, or unavailable, with the gemstones still needed for everything.

**Overlay.** The stage and zone ("Ranking — Kozama'uka rank 3, 12/40"), gemstones and vouchers, the next purchase, and the notes (needs a quest, unavailable, a trader failed). During a trip, the shopper's status.

**Debug report.** The last rank read with its stamp and failures, the catalog state, the shopper's step, and the values of an open `FateProgress`, `ShopExchangeCurrency`, `ShopExchangeItem`, `SelectString` or `SelectIconString` window.

**Mode choice.** `DirectorFactory.CreateModule` takes the gemstone module when its config is enabled, before the Yo-kai, rotation and single-zone modules.

## 10. Fixes folded in

- **Dawntrail aetheryte positions.** On xivapi the Dawntrail aetherytes' `Level` links (200–216 link to 10566055 and up) point at rows the `Level` sheet does not have, and `AetheryteProjection` positions aetherytes from exactly that link. If the local data agrees, the snapshot carries no aetherytes in Dawntrail, and the teleport cost model (A12), teleport legs (C17) and ladder rung 4 are inert in the zones this mode farms. The plan checks the local sheet first; if confirmed, positions fall back to the `MapMarker` row for the aetheryte (data type 3, key the aetheryte id, inside the map's marker range), converted by the map's size factor and offsets, with the height taken from the mesh floor there.
- **Purchases between FATEs.** The Director acts on a buy directive (`GoShopping`, and the Yo-kai `BuyMinion`) only between FATEs. Until now `BuyMinion` was taken whenever the module asked, which drops a FATE under way.

## 11. Testing

Planner tests in `tests/Moirai.Tests/GemstoneModuleTests.cs`, one per catalog item below, written before the code; the Director test for S15 goes in `DirectorTests`. The shell parts (reader, shopper, snapshot reads, aetheryte fallback) are thin and checked in-game on the release, like every executor.

## Appendix S — Gemstone catalog

- S1. The stage is derived from the snapshot each call (Ranking, Collecting, Mount, Farming on) and nothing but the first rank read is asked for before one lands. Farming on never ends the run.
- S2. Ranking farms the unmaxed zones as a rotation: the current zone when unmaxed, else the first unmaxed in map order; a zone read at rank 4 is left for the next unmaxed one; quiet moves on (G2); unreachable is skipped (G4); every zone failing stops with `ZonesUnreachable` (G5).
- S3. Collecting, Mount and Farming on rotate all six zones.
- S4. Rank reads are asked at the start; when a zone's completions since the last read reach its remaining need, and after each completion from then until the rank-up is read; on every 10th completion in an unmaxed zone; never once all six read rank 4; only between FATEs in a settled moment.
- S5. A failed read is asked again after 60 s; three in a row stop with `RanksUnreadable`.
- S6. A trip is asked for only between FATEs and while shopping is ready, when the gemstones reach the threshold, and never below it (the Mount-stage voucher trip excepted, S9).
- S7. Buyable means not learned, not in the bags, gates met (zone rank, all six at rank 4 for the city traders, quest done), not unavailable, trader not failed, and affordable. The trader is the one with the largest affordable total; ties go to the current zone's trader, then catalog order. The list is riding maps first, then catalog order, while the gemstones cover the next item.
- S8. With nothing buyable anywhere, the trip is for vouchers: Turali from a city trader once all six are rank 4 (the current zone's, else Kajeel Ja), Endwalker from Gadfrid before that; the quantity brings the gemstones below 500 before the Mount stage and below 100 from it on.
- S9. In the Mount stage, 500 Turali vouchers send the trip to Edelina for the whistle; with the whistle switch off the stage is skipped.
- S10. A quest-gated item is noted and never holds a stage back; completing the quest makes it buyable.
- S11. An item a trader's window did not list is unavailable for the session: excluded from buyable, noted, never holding a stage back, never planned again.
- S12. A trader whose trip failed is never planned again this session; its items fall to the other rules (S8, S13).
- S13. The gemstones at 1,500 with shopping on and no working trader to spend them at stop with `GemstonesCapped`; with shopping off the cap is only noted.
- S14. A collectible in the bags that is not learned is asked to be learned in settled moments, whatever the stage, and given up on after 10 s of asking.
- S15. The Director takes a buy directive only between FATEs; asked mid-FATE it carries on with the FATE and the module asks again later. This covers the Yo-kai `BuyMinion` as well.
- S16. A trip is held while a payout is pending (D6).

## Appendix T — Traders and items

To be re-verified against the sheets when the data table is written.

**Zone traders** (ENpcResident = ENpcBase id; positions from `Level` type 8; aetheryte distances from `MapMarker`):

| Zone | Trader | NPC | Position | Aetheryte | Walk |
|---|---|---|---|---|---|
| 1187 Urqopacha | Tepli | 1048628 | 301.50, -172.28, -484.55 | 200 Wachunpelo | ~74 y |
| 1188 Kozama'uka | Kunuhali | 1048778 | -200.34, 6.30, -522.77 | 202 Ok'hanu | ~53 y |
| 1189 Yak T'el | Rral Wuruq | 1048933 | -381.56, 23.54, -436.93 | 205 Iq Br'aax | ~17 y |
| 1190 Shaaloani | Mitepe | 1049283 | 357.75, -1.49, 469.42 | 207 Hhusatahwi | ~22 y |
| 1191 Heritage Found | Toashana | 1049438 | -258.37, 30.00, -594.20 | 211 The Outskirts | ~38 y |
| 1192 Living Memory | Clerk PX-0029 | 1049528 | 30.32, 53.20, 802.79 | 213 Leynode Mnemo | ~31 y |

**Other traders:** Kajeel Ja 1048383 (1185 Tuliyollal, -23.90, -10.00, 105.34; aetheryte 216 Tuliyollal); Beryl 1049082 (1186 Solution Nine, -198.78, 0.90, -4.33); Gadfrid 1037055 (962 Old Sharlayan, 78.37, 5.14, -36.78; shop 1770470); Edelina 1036913 (156 Mor Dhona, map 22.1, 4.9; shop 1770501). The aetherytes for Beryl, Gadfrid and Edelina are pinned in the plan.

**Collectibles** (cost in gemstones; rank-4 items gated by Achievement 3559–3564; riding maps at rank 2):

| Trader | Items |
|---|---|
| Tepli | 43607 Urqopacha Riding Map 90; 44312 orchestrion roll 450; 45009 orchestrion roll 450; 43571 Alpaca Cria (minion) 700 |
| Kunuhali | 43608 riding map 90; 44313 roll 450; 45010 roll 450 |
| Rral Wuruq | 43609 riding map 90; 44314 roll 450; 45011 roll 450; 43574 Ilyikty'i (minion) 700 |
| Mitepe | 43610 riding map 90; 44315 roll 450; 45012 roll 450; 43601 Turali Barding 450; 44479 Ttokrrone card 300 (quest 70495) |
| Toashana | 43611 riding map 90; 44316 roll 450; 45013 roll 450; 43873 Otis card 300 (quest 70478) |
| Clerk PX-0029 | 43612 riding map 90; 44317 roll 450; 45014 roll 450 (quest 70494); 43874 Cahciua card 300 (quest 70494); 44480 Mica the Magical Mu card 300 (quest 70495) |
| Kajeel Ja (all six rank 4) | 44310 roll 450; 45008 roll 450; 44293 Crimson Dawn Framer's Kit 600; 44294 Golden Dawn Framer's Kit 600 |
| Beryl (all six rank 4) | 44311 roll 450; 43960 Dark Solution Framer's Kit 600; 44292 Bright Solution Framer's Kit 600; 44256 Neon Parasol (fashion accessory) 500 |

Totals: 8,990 at the zone traders (maps 540, rank-4 items 8,450), 4,250 at the city traders, 13,240 in all.

**Vouchers and the mount:** 43961 Turali Bicolor Gemstone Voucher, 100 gemstones at Kajeel Ja and Beryl; 35833 Bicolor Gemstone Voucher, 100 gemstones at Gadfrid (cost to be verified); 43590 Ty'aitya Whistle, 500 × 43961 at Edelina.

**Out of the catalog:** furnishings (rank 3 in every zone, the Kozama'uka rank-4 flag, the city furnishings), materials, and Ut'ohmu Siderite are not collectibles and are never bought.

## Open facts to pin in game

1. The agent fills only after the Shared FATE window opens, and callback −1 closes it (§4).
2. `ShopExchangeCurrency`'s value layout: where each row's item id and cost sit (§7).
3. Whether locked rows are hidden or listed greyed (either way rows are matched by item id; a listed-but-locked row would fail the count check and be marked unavailable).
4. Edelina's menu index for the voucher exchange, and her floor position.
5. The Dawntrail aetheryte `Level` links in the local data (§10).
6. Gemstones through `CurrencyManager` against `InventoryManager` (§5), cross-checked in the debug report.
