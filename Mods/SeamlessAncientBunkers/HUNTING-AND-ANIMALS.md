# Hunting, husbandry and friendly pursuit — 2026-09-27

## Findings and corrections

- Hunting was absent from the audited cross-map work-giver list. Added `WorkGiver_HunterHunt`; normal work priorities, weapon checks, hunt designations and arrival revalidation apply.
- Milking and shearing already used the remote work broker. Verified actual completed jobs in both directions; no changes to their work givers were necessary.
- Pursuit previously admitted only hostile raiders and vanilla manhunters. Added player-owned Attack-trained animals and VPE's `VPE_SummonedSkeleton` in its `VPE_Manhunter` state. No VPE assembly dependency was added.
- Friendly pursuit uses opened, enabled hatch connections, allowing combat despite hostile threats. Forbidden entrances, allowed areas, healthy/mobile pawn requirements, existing duties, carrying and explicit orders remain respected. Attack-trained animals seek enemies while idle without needing their master or release toggle. At the destination they can acquire a reachable hostile pawn and fight.
- Friendly targets exclude downed pawns, prisoners and friendlies. Existing hostile pursuit behavior is retained. Ordinary needs and work jobs are not replaced with combat travel.
- VPE's mental state bypasses native pawn-specific forbidden checks. The new path also checks the player-faction forbidden flag explicitly; the first gameplay run caught this and the second verified the correction.

## Verification

Production and isolated test assemblies built successfully with zero warnings/errors. The API/package audit passed 71 checks. The final isolated engine run, with installed VPE and Vanilla Expanded Framework, passed 28 assertions and ended with `[SAB HUSBANDRY] ACCEPTANCE COMPLETE` with no test failures.

The engine test physically completed hunting, milking and shearing in both directions. Attack-trained Labradors and VPE skeletons crossed from surface to bunker and bunker to surface and damaged an enemy after arrival. Untrained animals were excluded and forbidden entrances blocked both fighter types.

Evidence: `BunkerHusbandry/run2.log` under the game directory. Reproduction is documented in TESTING.md. The temporary installed test mod was removed after the process exited; its source remains under Tests/HusbandryProbe. No personal colony or personal mod configuration was changed. The test log contains existing fixture/mod startup warnings; this is not certification of every race, the full personal mod list, or multi-hop combat save/reload.

The updated production DLL is installed in this mod's `1.6/Assemblies` directory. Restart RimWorld to load it. No Workshop publication was performed.
