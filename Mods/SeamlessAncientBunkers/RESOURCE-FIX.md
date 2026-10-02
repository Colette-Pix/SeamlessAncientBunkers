# Linked bunker resources and building selection

The installed RimWorld 1.6.4871 build menu (`Designator_Build.ProcessInput`) rejected stuff-based buildings when its current-map `ListerThings.ThingsOfDef` list was empty. ResourceCounter also reported only that map's stockpiles. There was no blanket bunker prohibition in the blueprint placement validation path.

`Source/LinkedResources.cs` fixes both issues:

- Traverse reciprocal, spawned ancient hatch/exit pairs on loaded maps, including multiple hops and sibling bunkers. Both ends must be revealed, enterable, and finished loading. Never infer connections from a world tile, map parent, stored Z-level, or colony ownership.
- Aggregate resource-count queries and dictionary snapshots using each map's original local backing counter. Preserve vanilla stockpile counting rules and refresh timing. Count each connected map once, without rewriting local counts or recursively querying aggregate getters.
- Extend only the building menu's material-presence check to include remote stockpiled materials. Preserve local loose-material behavior and all ordinary designator behavior, terrain, collision, research and placement checks.
- Keep thing lists, reservations, nutrition/need calculations and actual construction delivery local. The existing supply/hauling system transports materials through portals; counts do not teleport or consume remote stacks.
- Re-evaluate connectivity on queries, so portal removal, broken reciprocal references and unloading immediately split groups. Disabling the mod restores local counts. The per-hatch automatic-traffic toggle controls pawn jobs, not physical map-group membership; stock can remain visible while transport is paused or unsafe.

No door-defense source, save-format, shared version metadata or Steam publication changes are part of this fix. The installed assembly is built from the current source tree so the separate door-defense changes remain included.

## Verification

The source builds against the installed game and Harmony libraries without warnings. The existing API/package audit passes all 70 checks.

`Tests/ResourceProbe` is an opt-in destructive fixture, never a mod to enable for a personal colony. Build with an explicit `GameDir`, enable it after the production mod in an isolated save-data folder, and launch with `-sab-resource-tests`. Its starting disposable save must have two opened ancient hatches on one surface, each with a loaded bunker. The source checks three-map totals, raw-count conservation, dictionary isolation, category/group counts, global disable, the real material menu and blueprint creation, terrain/bounds rejection, disconnect/reconnect, broken pair references, and save/reload. It then clears fixture objects, creates a worker and five surface steel, and requires physical delivery and a completed steel wall in the bunker. Success is the log marker `[SAB RESOURCE TEST] ACCEPTANCE COMPLETE`; a FAIL marker is a failure even if the process exits successfully.

The tests use the real game engine with Harmony, Core and official expansions. They do not certify the entire personal third-party mod list. Personal saves and settings are not edited.
