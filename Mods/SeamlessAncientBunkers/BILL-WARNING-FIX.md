# Cross-level bill skill warning

The worktable Add bill action previously checked only its map's free colonists before displaying `RecipeRequiresSkills`. An empty bunker therefore displayed the warning even when a qualified colonist upstairs could travel down and perform the bill.

`Source/BillSkillWarning.cs` uses the shared roster in `Source/LinkedPawnRoster.cs` to check loaded maps connected by revealed, enterable, reciprocal bunker portals. It retains the original recipe skill predicate. The later connected-roster update also covers the mechanitor warning and other availability checks; see `CONNECTED-ROSTERS.md`. Global pawn lists, bill creation and work assignment remain unchanged. Disabling the mod restores the local query; disconnected colonies are excluded.

Validation on RimWorld 1.6.4871 rev591:

- Release build: no warnings or errors.
- Existing API/package audit: 70 checks passed.
- Isolated engine probe: 16 assertions passed using the actual Add bill menu callbacks. Covered an empty bunker with skilled colonists upstairs, local skilled colonists, insufficient skills across all linked maps, global disable, disconnected portals, and skilled colonists downstairs with an empty surface. Every case still added its bill and produced the expected dialog state. Global pawn lists remained local.

Engine evidence: `Tests/Evidence/bill-skill-warning.log`. The copied historical fixture emitted four stale think-node-key messages during loading; no patch or probe exceptions occurred. No personal save was edited.

To rerun, build `Tests/ResourceProbe/Source/ResourceProbe.csproj` with an explicit `GameDir`, stage its About and Assemblies folders as an isolated local test mod, and enable it after the production mod in a separate save-data folder. Use a copied `Autostart.rws` fixture containing an opened linked bunker and at least one colonist, with developer mode and run-in-background enabled. Start with `-sab-bill-warning-tests`, `-savedatafolder=...` and `-logFile ...`. Steam access and graphics initialization are required. The probe modifies only its disposable running fixture, exits automatically, and must log `[SAB BILL WARNING] ACCEPTANCE COMPLETE` with no FAIL marker. Remove the staged test mod after the run.
