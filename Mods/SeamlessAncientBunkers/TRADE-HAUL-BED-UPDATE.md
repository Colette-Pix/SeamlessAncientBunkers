# Trade, departures, hauling and assigned beds — 0.5.1

This update corrects the extra cross-floor trade/departure steps introduced in 0.5.0, fixes missed storage and remote pickup, and makes ordinary sleep prefer an assigned bed. This report supersedes the two-step trade/departure descriptions in the historical connected-map reports.

## Behavior

- Ground trade uses the normal immediate native transaction after one confirmation. Remote merchandise and silver do not need a preliminary haul. Native prices, payment checks, exact splitting, ownership and purchased-item placement remain authoritative. A sold remote pawn completes native sale effects on its source floor, then joins the trader on the trader's floor. Obsolete 0.5.0 saved trade-staging requests are retired without altering unrelated deliveries.
- Caravan, pod and shuttle selections require one confirmation. Native validation runs first. Exact selected quantities and passengers are saved while physical gathering proceeds; native packing/loading starts automatically afterward. Cancel at an entrance or the transporter. Changed accepted selections supersede earlier plans. Native capacity and final eligibility are checked again before loading begins. Already delivered goods remain real items when gathering is cancelled.
- Manual hauling uses a complete search of eligible connected storage. Previously, a full high-priority stockpile could exhaust the global cell budget and hide a later usable stockpile. Automatic scans now allocate a share to each eligible group and rotate candidates within groups. Nearby stock gets a bounded share while a stable rotating remainder prevents distant candidates from starving; selected jobs exhaustively revalidate storage to avoid losing a previously found cell. Filters, storage priority, reservations, allowed areas and reachability still apply.
- Automatic ordinary hauling can travel empty to a source on another map, recheck the source/storage, collect it and carry it to suitable storage. Manual orders share the same pickup/delivery planner.
- Ordinary sleep prefers a valid reachable assigned bed across floors. Medical-rest decisions retain appropriate native medical-bed selection. An invalid or inaccessible assigned bed does not trap the pawn without a fallback.

- The existing bed-shortage patch counts sleeping beds and colonists across connected levels, including partners split across floors. A dedicated regression verifies both directions and actual alert suppression. Medical beds remain excluded; true shortages, disconnected floors and disabled connectivity retain native shortage behavior.

## Validation

Tests use disposable saves and separately named fixture mods. Personal saves/configuration are unchanged. Runtime test sources are in Tests/TransitProbe and Tests/ServicesProbe.

- `BunkerConnectedServices/immediate-trade1.log`: native immediate remote sale, exact unsold remainder and payment; insufficient-funds rejection leaves merchandise, silver and selected animal unchanged; successful purchase paid by remote silver; bank-held genepack sale; remote animal sale keeps identity, native faction/lord and source-floor possession drops. No hauling requests or transit intents are created by these trades, including with hauling disabled.
- `BunkerConnectedTransit/Departure/save.log` and `Departure/reload2.log`: first confirmation saved; fresh process restores the pending exact selection; physical pod loading of five items leaves three at source; cancellation clears pending work; native overweight rejection; caravan automatically packs thirteen items and leaves four without reopening the dialog.
- `BunkerConnectedTransit/Haul/menu2.log` reproduces the reported insect-meat no-storage menu rejection with a blocked high-priority group preceding a free group. `Haul/four-directions.log` confirms native right-click acceptance and exact physical delivery after the fix, including remote pickup and both travel directions.

- `BunkerConnectedServices/automatic-haul4.log`: six native automatic-hauling scenarios, exact insect-meat quantities, empty remote pickup, both travel directions, native local baseline, no source Home area, forbidden item exclusion, and a one-free-cell storage group beyond a single scan budget.
- `BunkerConnectedServices/beds2.log`: assigned bed beats local and closer remote alternatives in a three-floor colony; throttle/failure history cannot lose ownership; allowed-area, unreachable and unspawned fallback; actual sleep restores rest and retains assignment; native medical bed wins when ill without changing ordinary assignment.
- `BunkerConnectedTransit/BedAlert/check2.log`: connected bed counts in both directions, actual alert clear, partners share double-bed demand across floors, medical exclusion, genuine shortage, native unrelated double-bed rules, disabled/disconnected controls.

## Integrated release

The production build completed with zero warnings and zero errors; all 94 API/package checks passed. Its SHA256 is `9BA7A4B3005C6A1E4FC9145244BB3CE00853790669B4EC35A0FF7556FAB476D1`, identical to the final services test assembly.

`BunkerConnectedTransit/Departure/final.log` passes fresh-process loading, exact counts, cancellation, accepted local selection replacing the remote plan, native mass rejection, and automatic caravan packing. `BunkerConnectedTransit/BedAlert/final.log` passes all 16 bed-warning checks on this exact production assembly. Earlier failing logs are diagnostic history, not acceptance.


The final native right-click hauling rerun (`BunkerConnectedTransit/Haul/final.log`) passes all four source/pawn/storage arrangements with exact 17-item insect-meat deliveries. The staged package also passes all 94 checks and contains one original mod DLL only.
