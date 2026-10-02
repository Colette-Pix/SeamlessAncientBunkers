# Connected-level management

Management screens and worker-availability warnings share a connected-colony roster. The roster follows loaded, revealed, enterable, reciprocal bunker links in both directions and through intermediate levels. Breaking a connection removes the other level; disabling the mod restores local queries. Separate colonies are not combined by tile, faction, or level number.

## Interfaces

- Work and Schedule include colonists on connected levels, with the original age and work restrictions.
- Animals and Mechs include the appropriate connected-level pawns and retain their native controls and eligibility filters.
- Misc. Robots includes active and docked robots associated with connected levels. Its tab's shutdown-all action includes connected chargers. Individual robot and charger identities remain intact.
- Standard building assignment candidate queries are expanded centrally through `CompAssignableToPawn`, including ordinary beds, animal beds, meditation spots, graves and deathrest caskets. Compatible mod subclasses using the same query contract also receive the expansion. Species, slave-bed, bloodfeeder and other assignment restrictions remain native.
- Thrones, the Assign tab, animal-master choices and bill-worker dropdowns already query across maps in vanilla. Their existing global choices and eligibility rules are preserved.
- Work-table rows refresh while open when roster membership changes. Clicking a pawn retains the native jump-to-pawn behavior.
- Allowed-area controls edit the currently viewed level for remote pawns, including animals, mechs and active/docked Misc. Robots. Each level keeps its own area restriction. A stale disconnected row cannot write an area belonging to another map.

## Availability checks

Connected rosters are used for hunting assignment and weapon warnings, taming skill warnings, growing skill/assignment warnings, construction skill/assignment readouts, production bill skill and mechanitor warnings, surgery skill warnings, doctor/miner/warden shortage alerts, prisoner warden checks, and the construction/growing mech-capability helper. Original skill, assignment, health, capacity, ideology, weapon, work-type and mech-overseer predicates remain in place.

Bed and crib availability counts combine both the population and beds across the connected group. Partner pairing also recognizes connected levels, avoiding counting one couple as two unrelated sleepers solely because they are on different floors.

## Scope of the shared roster

Only management/availability queries use this roster. The underlying `MapPawns` collections, map coordinates, reservations, pathfinding, targeting and actual job eligibility remain physical. This update does not add new automatic job scanners, general mech travel, cross-level control-signal range, baby/prisoner carrying, or remote corpse transport. It does not bypass physical requirements for loading transporters, medical care, pens, temperature, combat, or local utility access. Those systems cannot safely be made cross-level by returning extra pawns from a global map getter.

The audit covered the installed vanilla/DLC assembly, standard assignment subclasses and the installed Misc. Robots UI. Unknown mods that build completely custom menus without those contracts are not automatically rewritten.

## Regression probe

`Tests/ResourceProbe/Source/RosterProbe.cs` runs with `-sab-roster-tests` in a disposable save-data folder. It checks the actual patched tab getters, assignment APIs, warnings and work-table cache, including local-list isolation and disconnect/disable behavior. Optional Misc. Robots checks use the installed mod's creation and docking APIs. Never enable the probe in a personal game: it intentionally moves fixture pawns, changes priorities, creates buildings/animals/mechs, and assigns ownership.

Validated on RimWorld 1.6.4871 rev591 with all installed DLC and Misc. Robots: **90 engine checks passed**, with evidence in `Tests/Evidence/connected-rosters.log`. The production Release build has no warnings or errors and the existing API/package audit passes all 70 checks. The historical fixture logs four stale think-node identifiers while loading; no patch, UI or probe exceptions occurred in the successful run. No personal save or active-mod configuration was changed.

The production bill regression also passed **22 checks**, including a real remote mechanitor and genuine missing-mechanitor warnings, in `Tests/Evidence/connected-bill-warnings.log`. The temporary standalone test mod was removed after validation.
