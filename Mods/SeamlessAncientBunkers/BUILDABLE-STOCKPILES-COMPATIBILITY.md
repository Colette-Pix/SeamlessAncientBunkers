# Buildable Ancient Stockpiles compatibility — 2026-09-27

Result: compatible with the existing Seamless Ancient Bunkers 0.5.2 assembly. No production code change or replacement Workshop upload was required.

Installed dependency: Kanae's Buildable Ancient Stockpiles, Workshop 3530842173, package kanae.BuildableAncientStockpiles. Its only gameplay patch adds 114 steel and the Misc designation category to the native AncientHatch. It introduces no replacement entrance class or routing system.

## Engine verification

RimWorld 1.6.4871, Harmony, installed DLC, the actual Buildable Ancient Stockpiles mod, production SAB 0.5.2 and a separate temporary test mod. A disposable copied fixture was used; no personal save or active mod configuration was changed.

BunkerBuildable/run3.log: 21 PASS assertions, ACCEPTANCE COMPLETE, no test FAIL. The test created two new player-owned AncientHatch objects using the patched definition and generated distinct pocket maps. It unlocked and cleared them in test setup, then verified:

- Buildable mod active and the actual steel/category patch present.
- Both hatch/exit pairs recognized and automatic traffic activated after clearance.
- Both bunkers present in level navigation.
- Drafted orders physically walk A -> surface -> B and B -> surface -> A, reaching the requested cell while remaining drafted.
- Non-forced storage searches find the other bunker through two entrances.
- Non-forced hauling jobs physically carry seven silver to storage in each direction, with observed surface travel and exact delivered quantity.
- A forbidden destination hatch blocks automatic travel.
- Disabling its automatic traffic blocks automatic routes while retaining manual routes.

Scope: the test directly starts the non-forced hauling jobs after checking storage selection; it does not assert spontaneous work-giver scheduling. Hatches are spawned as completed player buildings, not built by a pawn from a blueprint. Hacking, exploration and clearance are performed by fixture setup. It does not certify every mod combination, nested bunker layout or save/reload during a multi-hop trip.

Earlier run2 stopped at clearance because generated hostile structures remained in the fixture. Removing all generated threats corrected setup without changing production code. run.log was an unsuccessful sandbox launch without Steam/Harmony access. These runs are not acceptance evidence. The final log retains native fixture/mod warnings, including Buildable Ancient Stockpiles' native impassable-building configuration warning.

## Release verification

The tested local DLL and staged 0.5.2 release DLL have the same SHA256:
BD7A42ABBD3A4CB88524F1DD7FE094BDE899EDC0EE60B6B57A560B7431A9EF90

A read-only Steam query returned Workshop item 3808896476, public visibility, and the current Version 0.5.2 description. The earlier successful 0.5.2 publication marker records the same DLL hash. No new upload was made because compatibility already exists.

## Use

Open/unlock and clear both stockpiles. Keep their hatches and ladders allowed, safe, reachable and within the pawn's allowed areas. Leave automatic traffic enabled for autonomous work and hauling. The route between sibling stockpiles runs through their shared surface; they are not directly connected underground. Normal storage filters and priorities apply.

## Reproduction

Source: Mods/SeamlessAncientBunkers/Tests/BuildableProbe.
Build Source/BuildableProbe.csproj in Release. Install the About folder and built assembly as a separate temporary local mod, after production SAB and Buildable Ancient Stockpiles. Use an isolated save-data folder with a disposable Autostart fixture, matching DLC and Harmony. Launch with -batchmode -sab-buildable-tests, explicit -savedatafolder and -logFile arguments. The test deliberately destroys fixture occupants and structures. Never enable it in a personal save.

The temporary installed test mod was moved out of Mods into BunkerBuildable/InstalledProbe after completion.
