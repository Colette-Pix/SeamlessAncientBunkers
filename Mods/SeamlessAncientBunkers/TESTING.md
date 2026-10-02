# 0.5.0 connected-map acceptance

The connected reports name the exact passing logs and limitations. TransitProbe has `-sab-transit-tests` (carried-patient checkpoint/fresh-process continuation, slave, queue, mech), `-sab-transit-behavior` (trained hauling, emergency access, groups/exit/assault/departure) and `-sab-transit-custody` (prisoner release/bed transfer, pen, mental target, alternate entrance). ServicesProbe has `-sab-services-tests` (component checkpoint/repair/refuel/bill output/baby), `-sab-services-extended-tests` (filters/fairness/DLC/trade/needs) and `-sab-services-trade-reload-tests` (partial trade request checkpoint/continuation).

Use separate disposable save-data folders with an opened-hatch Autostart fixture, matching DLC, Harmony and uniquely named test mods. Copy production Defs AND Patches alongside the assembly in the isolated fixture mod. Start hidden helper processes with `-batchmode`, the test flag, explicit `-savedatafolder` and `-logFile`. Never use a personal colony: these fixtures destroy and replace occupants/items. A checkpoint run requires a new process loading its saved checkpoint as Autostart. Success requires the suite's ACCEPTANCE COMPLETE marker and no FAIL marker; a normal process exit alone is insufficient.

Current production metadata/package audit: **91 checks**, including the new passenger drivers and release assembly version. It does not simulate gameplay. Historical procedures and results follow.

# Hunting, husbandry and friendly pursuit regression

Build `Tests/HusbandryProbe/Source/HusbandryProbe.csproj` in Release with `GameDir` set to the game directory. Install its About and Assemblies folders as a separate local test mod. Use a disposable opened-hatch Autostart fixture and isolated save-data folder with Harmony, Core, fixture DLCs, Vanilla Expanded Framework, Vanilla Psycasts Expanded, production and `colet.sabhusbandryprobe`. Launch with `-batchmode -sab-husbandry-tests`, the isolated `-savedatafolder` and `-logFile` arguments. Never run this destructive fixture in a personal colony: it removes existing pawns and clears the entrance surroundings.

The probe checks actual travel and completed milking, shearing and hunting in both directions, then actual crossing and enemy damage for Attack-trained dogs and VPE skeletons in both directions. It also checks untrained-animal exclusion and forbidden entrance enforcement, including the VPE mental-state exception to native pawn forbidden checks. Success requires `[SAB HUSBANDRY] ACCEPTANCE COMPLETE` and no FAIL marker. Remove the separately installed test mod after the process exits. This does not certify every animal race, a full personal mod list, or multi-hop combat save/reload.

# Cross-map burial and surgery

Build `Tests/LogisticsProbe/Source/SABLogisticsTests.csproj` in Release. Copy its About and Assemblies folders to a separate local test mod. Use an isolated save-data directory with an opened-hatch disposable `Autostart.rws`, Harmony, Core, the fixture's DLC, production and `colet.sablogisticstests`. This fixture destroys existing pawns/items and creates its own patients, corpses and graves; never run it in a personal colony.

Launch with `-batchmode -sab-logistics-tests`, an absolute `-savedatafolder` and a separate `-logFile` argument. The first process saves `SAB-LogisticsTransit` while carrying a corpse and exits with `RELOAD REQUIRED`. Copy that checkpoint to the isolated `Autostart.rws` and launch a fresh process with the same flags. Require the reload assertion, actual burial and native operation completion in both directions, and `[SAB LOGISTICS] ACCEPTANCE COMPLETE`. Any FAIL marker is a failed run, regardless of process exit code. Remove the installed test mod afterward. See LOGISTICS-FIX.md for results and limitations.

# 0.4.2 utility regression

## Cross-level manual orders regression

Build `Tests/OrderProbe/Source/SABOrderProbe.csproj` and install its About/Assemblies in a separate test mod. Run with `-sab-order-tests`, an isolated save-data folder, Harmony, Core, the fixture's DLC, production and the probe. Start from a disposable opened-hatch `Autostart.rws`. The fixture changes storage filters, creates a pawn and objects, and saves `SAB-OrderTransit`; never use a personal colony. It checks native menu options and physically completes remote hauling, drafted movement, pickup and repair, along with selection retention. The hidden process skips only menu text measurement; it does not test rendered menu layout or synthesize keyboard/mouse events.

After the first process reports `[SAB ORDER TEST] ACCEPTANCE COMPLETE`, copy its `SAB-OrderTransit.rws` to a second isolated folder as `Autostart.rws` and repeat the launch. Require the pending-move reload assertion and another acceptance-complete marker. Remove the temporary installed probe afterward. Current production API/package audit remains 70 checks.

Build `Tests/UtilityProbe/Source/UtilityProbe.csproj` in Release with `GameDir` set to the installed game directory. Copy the probe's About and Assemblies folders into a separate local test mod. Use only an isolated save-data folder and a disposable opened-hatch fixture, with Harmony, Core, Odyssey, Dubs Bad Hygiene, production and the probe active. Additional DLC should match the fixture. Enable developer mode and name the copied fixture Autostart.rws. Run the game with `-sab-utility-tests`, the isolated `-savedatafolder=...`, and `-logFile "absolute/path/to/log"` (Unity's log option uses a space, not an equals sign). Workshop mods require a process able to access Steam.

The test deliberately removes nearby buildings and existing utility infrastructure, places test devices, checks power and native Dubs methods/fixture availability, saves/reloads SAB-Utilities, cuts the entrance connections and reconnects with hidden lines. Never use it in a personal colony. Expected marker: `[SAB UTILITY TEST] ACCEPTANCE COMPLETE`, with 31 PASS lines.

Rebuild the probe with `-p:DefineConstants=POWER_ONLY`, disable Dubs in the isolated configuration and rerun from the disposable original fixture. Expected marker: `[SAB POWER ONLY] ACCEPTANCE COMPLETE`, with 3 PASS lines covering optional startup and electricity from bunker to surface. Remove the temporary local probe mod afterward. No probe belongs in a normal active mod list.

# Reproducing 0.3.0 verification

See TEST-REPORT.md for results and COMPATIBILITY.md for the corrected load order and installed-mod findings. All fixtures are disposable and must use a separate save-data folder. Never enable the test harness in a personal colony.

## Build/API check

Build the production mod as described in README.md, then run:

```
dotnet run --project Tests/ApiCheck -- "C:/path/to/RimWorld" "C:/path/to/RimWorld/Mods/SeamlessAncientBunkers"
```

Expected result: 52 passing API/package checks. This audit does not simulate gameplay.

## Runtime test mod

Copy Tests/RuntimeHarness to RimWorld/Mods/SABRuntimeTests and build its Source/SABRuntimeTests.csproj in Release. The 0.3 harness targets .NET Framework 4.8 because the installed Processor Framework DLL requires it; the production mod still targets 4.7.2. The harness references installed Quarry, Processor Framework and Misc. Robots DLLs from their standard Workshop paths. GameDir and HarmonyPath may need adjustment for a nonstandard installation. No third-party DLL is included in this release.

The harness deliberately clears terrain/inventory, creates pawns and buildings, changes needs/work, opens bunkers, destroys fixtures and saves checkpoints. Its active list must contain Harmony, Core, Odyssey, the production mod, Quarry, Processor Framework, Misc. Robots and the harness. The recorded final combination also includes Royalty, Ideology, Biotech, Anomaly, Vanilla Expanded Framework and both Vanilla Gravship Expanded chapters.

Prepare an isolated Config/ModsConfig.xml and preferences with developer mode and run-in-background enabled. Launch with:

```
RimWorldWin64.exe -sab-tests -sab-all-tests -savedatafolder=IsolatedSABTests -logFile=IsolatedSABTests/run.log
```

Create a disposable colony or use the developer quick-test start. The harness begins when a live colony map exists. It first runs ten crossings and walking save/load, then the original acceptance cases, carried-stack entrance-wait reload, 20-colonist stability, disabled behavior, and the new custom work/robot/enemy/gravship fixtures. The original evidence reused a legacy SAB-Transit save to also check 0.1 migration. Such fixture saves are not redistributed.

The first process ends after writing SAB-GravshipFlight and reporting GRAVSHIP FLIGHT SAVED. Restart the isolated game with -sab-tests -sab-extension-tests and load SAB-GravshipFlight. This verifies the in-flight save in a fresh engine process, performs native landing placement and checks subsequent bunker travel. Expected final marker: EXTENSION ACCEPTANCE COMPLETE. A FAIL marker is a failure even when the game exits with code zero.

The targeted -sab-pursuit-door-test flag can start from the isolated SAB-AcceptanceStart checkpoint. It encloses the hatch behind a closed door, creates an assaulting raider, saves/reloads during pursuit, and verifies bunker entry and combat targeting. Its final marker is ENEMY DOOR AND SAVE TEST COMPLETE.

The historical -sab-modlist-tests extension additionally requires Adaptive Storage Framework, [sbz] Gravship Storage and Dubs Bad Hygiene. It was used for the 0.2 storage/hygiene acceptance evidence.

Do not use -nographics: the real game requires texture initialization. The final flight test uses the normal rendered engine, not batch mode. Early same-process reload attempts while the world renderer was regenerating caused native Unity crashes in the isolated test process; the final two-process workflow avoids that test-runner race. The pilot-menu UI and cinematic are not asserted by the launch API test.

## Removal probe

Tests/RemovalProbe is a separate source-only mod with no production assembly reference. Copy it to Mods/SABRemovalProbe and build Source/RemovalProbe.csproj. In an isolated game, use the production cleanup command, save and exit. Disable the production mod and main harness, enable the removal probe, launch with -sab-removal-test and load the cleaned save. It checks for absent production assembly, no SAB jobs, unique pawn IDs and a second successful save/reload. The included removal evidence is from 0.2.0; 0.3.0 additionally clears hostile-pursuit jobs and disables traffic before cleanup.

## Limits

The suite proves the named fixture outcomes; it is not certification of all 103 personal active entries, every race/need, custom enemy AI, every gravship expansion scenario, or Multiplayer. Final evidence retains engine/mod warnings. No personal colony save was used. Only the explicitly requested load-order correction changed the user's configuration, with a backup first.

## Animal routing (0.3.2)

Copy Tests/AnimalHarness to Mods/SABAnimalTests and build Source/SABAnimalTests.csproj. Use Harmony, Core, Biotech, Odyssey, the production mod and that harness in a separate save-data folder. Start from a disposable colony with a generated/opened ancient hatch and safe exit; the harness clears nearby terrain and removes food to create its scenarios. Launch with -sab-animal-tests and the isolated -savedatafolder argument. It saves/reloads SAB-AnimalTransit and exits with ANIMAL ACCEPTANCE COMPLETE after the food, bed, follow, access and wandering assertions. Never enable this harness in a personal save. The source contains destructive fixture setup intentionally limited to the isolated test game. Current API audit expectation: 53 checks.

## Cleared bunker activation (0.3.3)

Copy Tests/SaveProbe to Mods/SABSaveProbe and build Source/SABSaveProbe.csproj. With Harmony, Core, Odyssey, production and the probe enabled, use an isolated generated/open bunker fixture and launch -sab-clearance-tests. It adds a hostile defender, confirms activation is blocked, kills the defender and confirms its corpse does not block activation, then verifies saved manual shutoff and the global disable setting. Expected marker: CLEARANCE ACCEPTANCE COMPLETE. This is a destructive fixture and must never run on a personal save.

The separate -sab-save-probe mode observes a duplicated existing colony under its mod list, logs route blockers, undrafts bunker colonists only in that in-memory copy, then observes automatic activation and return. It does not save or overwrite the input. Personal save copies and their logs are not distributed with the mod.

## 0.4.0 additional acceptance modes

Use the same isolated SaveProbe harness, never a personal save. Start from the disposable opened-hatch fixture and pass one of: -sab-feature-tests (drugs/outfits in both directions, injury bed fallback, dining transit save/reload and caravan gathering); -sab-level-tests (two generated bunkers, entry numbering, wrapping and save/reload); -sab-launch-gather-tests (boarding and deferred native launch ritual with saved assignments); -sab-thirst-tests (Dubs installed with thirst enabled, drinking trips in both directions). Each mode deliberately changes fixture objects and exits with its named ACCEPTANCE COMPLETE marker. The final API audit expects 70 checks. The source-only test project requires the installed game libraries and Harmony; no third-party binaries are distributed.

The 0.4.0 personal-save observation used a duplicate and never wrote the original. That input and its logs stay outside the release. Workshop publication tooling is private and excluded from both the release and Workshop content.

## 0.4.1 door-defense regression

The SaveProbe flag `-sab-door-tests` requires an isolated save with an opened bunker. It creates an incoming hostile and blast door, checks original defender/colonist access and ownership independence, saves/reloads, then builds an enclosed test corridor and orders the raider through its closed blast door. Success requires actual door damage from native pathfinding without the door opening. Expected marker: `[SAB DOOR TEST] ACCEPTANCE COMPLETE`. This destructive fixture must never run on a personal save.

## 0.4.4 order feedback
Use the OrderProbe workflow above. Expect 36 first-process and 24 fresh-process assertions, covering native green FeedbackGoto creation, selection-overlay line endpoints, deselection, cancellation, arrival, and reload. Current API audit: 71 checks. See ORDER-FEEDBACK-FIX.md for evidence and visual-testing limits.
