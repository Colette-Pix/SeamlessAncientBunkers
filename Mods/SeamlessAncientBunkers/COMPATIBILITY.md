# 0.5.2 BunkBeds compatibility

BUNK-BED-COMPATIBILITY.md documents the optional adapter for the bed-warning replacement bundled in Vanilla Gravship Expanded - Chapter 1. It counts the connected colony while preserving bunk sleeping slots and medical exclusions.

# 0.5.1 trade, hauling and bed corrections

Current corrections and acceptance results are in TRADE-HAUL-BED-UPDATE.md. Trade completes immediately through native transactions; departures gather and load automatically after one confirmation. Manual and automatic connected hauling and assigned-bed preference are corrected. Bed-shortage warnings count the connected colony. Historical staging descriptions below are superseded. This release used isolated native-game fixtures, not a new certification of every installed mod.
# 0.5.0 connected-map expansion

Current behavior and tested scope are in CONNECTED-SERVICES-REPORT.md and CONNECTED-TRANSIT-REPORT.md. This release adds living passenger transport, slave and controlled Biotech mech travel, wardening/childcare, trained hauling/rescue, pen placement, special needs, physical consumer/trade/departure supply, filtered bill/output integration and route/queue recovery. Dubs remote toilet/bathing adapters are implemented; they were not individually engine-tested in this release. Physical rooms and adjacency remain local, and arbitrary mod jobs/groups and Multiplayer are not covered.

The entries below are historical compatibility evidence. Their old exclusions of these newly implemented features are superseded by the current reports. The older full-mod-list observations were not repeated as certification of this release; current tests used isolated fixtures. Personal saves and active mod settings were not changed for 0.5.0.

# 0.4.2 utility compatibility

Power conduits and optional Dubs Bad Hygiene pipes touching both ends of an opened ancient hatch now bridge their utility networks. Fixtures use real connected storage and sewage disposal, with native quality and availability rules. Local topology is preserved; links are reconstructed after loading. No Dubs assembly is bundled or required when Dubs is absent. This supersedes the older plumbing limitations recorded below. Automatic pawn traffic can be disabled without cutting utilities.

# 0.4.0 current-list check

Rechecked 104 active entries on 2026-09-27. Processor Framework had moved below Rabbie again; restored it directly before Rabbie with a fresh timestamped ModsConfig backup. No required dependency/order issue remained in the reviewed installed metadata. Colony saves were not edited.

The complete list loaded in an isolated copied-save observation. Two undrafted colonists returned automatically; the third lacked an available surface bed. See TEST-REPORT.md for scope and warnings. This does not certify all 104 mods for every action.

Dubs Bad Hygiene thirst now has optional remote drinking support, exercised in both directions using the native drink job. Local water still takes priority. Toilets, bathing and plumbing remain local. Caravan/ritual participants gather before native groups start; unsupported mod-specific group creation is not covered. Blast-door claiming is restricted to revealed doors in cleared ancient bunkers. Keyboard level selection is map viewing only and does not bypass exploration.

# Active-mod compatibility review — 0.3.0

Reviewed the user's current **103 active entries**, including Core and five DLC, against installed package metadata. All declared required packages were present. This was a dependency/order review of the complete list and a focused runtime review of likely AI, storage and job conflicts; it was **not a full 103-entry gameplay certification**. The user subsequently requested the load-order correction; it has now been applied with a timestamped backup. Colony saves were not changed.

## Existing load-order issue

**Fixed: [SYR] Processor Framework now loads directly before Rabbie The Moonrabbit race.** Rabbie's installed `About/About.xml` explicitly lists `syrchalis.processor.framework` as a dependency, and the previous active list placed Rabbie earlier. This existing dependency-order problem was separate from Seamless Ancient Bunkers. The original configuration was backed up beside ModsConfig.xml before editing. No missing dependency was found. Ordinary metadata cannot identify every undocumented conflict.

## Suspect mods and findings

| Mod or group | Finding and practical effect |
| --- | --- |
| Adaptive Storage Framework + [sbz] Gravship Storage | Fixed a real integration gap: remote storage now checks the storage parent's `Accepts` result as well as filter rules, including on delivery. An actual sbz gravship crate accepted a cross-map delivery; a full crate with a one-slot limit was correctly rejected. |
| Dubs Bad Hygiene | Fixed suspended-travel resumption so ordinary jobs tagged `SatisfyingNeeds` can run first. Runtime test completed a local bladder/toilet job while retaining the route, then resumed travel. Toilets, drinking water and plumbing must be available on each inhabited map; this mod does not plan remote hygiene trips or connect plumbing networks. |
| Performance Optimizer | Its installed code caches pawn/thing forbidden checks briefly during toil checks. Transit now also asks the uncached faction overload before crossing. The complete forbidden-entrance test passed with this mod loaded. Its own beauty/need update intervals can still delay map-change feedback. |
| Allow Tool + HugsLib | Complete ordinary-work/needs/hauling suite passed while loaded, both in the selected-user-mod run and alongside Common Sense. Allow Tool's custom work givers are not automatically added to the remote whitelist. |
| Simple sidearms | Loaded throughout the selected-mod suite without a discovered transit conflict. Inventory and carried-stack preservation passed; specific sidearm retrieval/loadout policies were not separately exercised. |
| Humanoid Alien Races | Framework loaded throughout the selected-mod suite. This does not certify every Kurin, Rabbie, Mincho or other custom-race need/trait. Eligible adult and child free colonists use the supported ordinary jobs; race-specific job givers remain local unless explicitly supported. |
| Snap Out! | Loaded throughout the selected-mod suite without a discovered conflict. Its custom interaction job is outside automatic remote scanning. Mentally unstable pawns themselves are excluded from automatic transit. |
| Quarry; [SYR] Processor Framework | Loaded throughout the selected-mod suite. Version 0.3.0 explicitly supports Quarry mining and Processor fill/empty work, plus Processor input staging. Actual Quarry work and completed Processor input/output were verified across maps. |
| Misc. Robots; robot retexture | Misc. Robots loaded during the selected-mod suite. Version 0.3.0 supports Misc. Robots cleaners and haulers, preserving charger assignment and returning across maps for charging. Cleaning, low-battery return and native recharge, and delivery of exactly 17 silver by a hauling bot passed engine tests. The retexture adds no routing code. |
| Vanilla Expanded Framework | Loaded throughout the selected-mod suite. Framework coexistence does not certify every expansion-specific scanner or map lifecycle. |
| Vanilla Gravship Expanded Chapters 1 and 2 | Both installed chapters were loaded in the 0.3.0 regression. Anchored extraction/world departure, in-flight save/reload, landing and restored bunker routing are tested through actual engine APIs. Unsafe unanchored departure with bunker occupants is rejected. Pilot-menu interaction, animated cutscenes and every chapter-specific launch mode are not certified. |
| Gene, race, apparel, furniture, biome, UI and content mods | No direct conflict was established from package review. They were not individually behavior-tested; mods that alter a supported vanilla job giver or add needs can still affect the probe. A failing remote scanner is skipped for that session with one diagnostic. |

## Historical 0.2.0 selected-mod engine run

For the current build, `engine45-full-030.log`, `engine46-final-landing.log` and `engine47-enemy-door.log` contain the completed 0.3.0 regression and targeted tests. The active test combination was Harmony, Core and all five DLC, Vanilla Expanded Framework, Quarry, Processor Framework, Misc. Robots and Vanilla Gravship Expanded Chapters 1 and 2. It passed original colony acceptance, the new custom work and robot scenarios, enemy pursuit including a closed door and transit reload, and gravship extraction/world travel/save/reload/landing. The earlier storage/hygiene compatibility evidence below remains evidence of those fixes on 0.2.0, not a new full-list certification of every combination.

`Tests/Evidence/engine38-user-mods.log` records the full suite plus the specific storage and hygiene tests, ending in **USER MOD-LIST COMPATIBILITY RUN COMPLETE**. The configuration included all five DLC and these installed mods in their existing relative order:

Harmony; Adaptive Storage Framework; Vanilla Expanded Framework; HugsLib; Allow Tool; Dubs Bad Hygiene; Humanoid Alien Races; Quarry; Simple sidearms; Snap Out!; Misc. Robots; [SYR] Processor Framework; Performance Optimizer; [sbz] Gravship Storage.

The test used temporary local copies because the isolated engine could not discover Workshop items through Steam. The copies contained the installed 1.6 content. The user's saved Performance Optimizer settings were copied into the isolated configuration; this is not a claim that every personal mod preference was reproduced.

All ten core acceptance scenarios passed in this combination. The 20-colonist, 6,000-tick fixture recorded one transfer total, at most one per pawn. The measured work planner made 586 calls, 43.62 ms total, maximum 1.27 ms; 200 graph queries took 2 ms.

The log is retained without hiding warnings: it includes Steam initialization and four English-translation errors, a test horseshoes fixture's default-material notice, and a generated pawn-relation reference warning during later fixture checkpoint saves. These were not observed travel exceptions; this is not a zero-warning claim. The walking/carrying reload assertions and final test completion passed. Long-term save stability with the complete personal list remains unverified.

Common Sense was installed but **not active** in the user's list. It was tested separately alongside Allow Tool; there is no need to remove it from the personal active list because it is not there.

## Suggested setup

The Processor Framework/Rabbie ordering is already corrected. Enable Seamless Ancient Bunkers after Harmony and Odyssey (placing it after the work/storage frameworks makes the list easier to inspect), and enable automatic traffic only on explored, safe hatches. Provide hygiene facilities on each map. Quarry/Processor workers and supported Misc. Robots can now travel automatically; other unlisted custom work givers remain outside the whitelist. No broad mod removal is justified by the conflicts found here.


## Animal routing in 0.3.2

Tamed animal food/bed/follow behavior and idle tamed/wild wandering now use safe opened routes. Normal master assignment and follow flags remain authoritative. Roped animals and dryads remain under native movement control. Pens are still map-local, and this does not add cross-map animal hauling or support arbitrary custom animal think trees. The isolated Labrador acceptance run is recorded in TEST-REPORT.md; it is not a certification of every race or livestock species.



