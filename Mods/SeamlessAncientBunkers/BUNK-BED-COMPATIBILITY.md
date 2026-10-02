# Connected bed warnings with BunkBeds — 0.5.2

Vanilla Gravship Expanded - Chapter 1 bundles a BunkBeds assembly that replaces `Alert_NeedColonistBeds.AvailableColonistBeds` with a Harmony prefix and skips the vanilla calculation entirely. The 0.5.1 connected-colony transpiler only extended the skipped vanilla method, so the replacement continued counting beds and colonists on individual floors.

The optional adapter now extends the replacement's building roster, colonist roster and partner-map comparison with the same connected-colony queries. BunkBeds still performs its own calculation, including independent bunk sleeping slots, medical exclusions, babies and partner rules. No BunkBeds assembly is distributed, and the adapter is inactive when its replacement type is absent.

Diagnosis used a disposable copy of `Femageddom.rws` saved on 2026-09-27, with the user's gameplay mods. The original save/configuration was not edited. The initial diagnostic found two colonists upstairs and one downstairs, with floor-dependent counts despite a valid connection. The original warning reappears when the third colonist returns upstairs because the local surface count has only two ordinary beds. The connected colony has nine eligible ordinary single beds for three colonists.

Runtime regression source: `Tests/TransitProbe/Source/SavedBedProbe.cs`. The test disables only this optional adapter to reproduce the old warning, restores it to verify matching connected counts and warning suppression, then changes ordinary beds to medical in memory to check that a genuine shortage is still reported. No modified test state is saved.

Acceptance: `BunkerBedInvestigation/fixed.log` reproduces the pre-fix warning, verifies six spare connected beds and no false warning, and preserves a genuine medical-only shortage. `BunkerConnectedTransit/BedAlert/0.5.2.log` passes all 16 vanilla-only assertions. The release build has zero warnings/errors and all 94 staged API/package checks pass.

