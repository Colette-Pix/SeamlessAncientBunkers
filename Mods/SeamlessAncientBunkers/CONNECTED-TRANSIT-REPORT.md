# Connected transport and behavior

Implementation and isolated engine validation on RimWorld 1.6.4871, 2026-09-27. This is the transport half of CONNECTED-MAP-WORK-SPLIT.md. The services half is documented in CONNECTED-SERVICES-REPORT.md. Personal saves and active mod configuration were not edited.

## Implemented

| Audit IDs | Behavior |
| --- | --- |
| 01 | Saved living-passenger journeys fetch the passenger, carry the same pawn through entrances, and resume native rescue/capture/bed/container/disabled-mech drivers at the destination. Destination custody, bed suitability and access are checked. Failed routes drop carried passengers safely. Downed rescue/capture menu choices can select remote beds. |
| 03 | Colony slaves may travel for their permitted work and needs. Native slave status, restrictions and assignments remain authoritative. |
| 05 | Controlled Biotech mechs can travel for work and to a native compatible charger. Handlers can transport disabled mechs. Mech repair/charger hauling scanners are supported. Cross-floor command distance uses the route, including entrance costs; bandwidth remains native. |
| 06 | Emergency work/rescue and native flee decisions can use an entrance despite a distant raid. Enemy proximity to either endpoint, fire/vacuum/danger at the landing, forbidden entrances and allowed areas still constrain the trip. |
| 12 | Caravan, pod and shuttle dialogs offer eligible connected-floor supplies. Selected remote supplies are physically staged before native loading; passengers travel or are carried. Close the dialog to allow staging, then reopen it and confirm the selection. Staging does not initiate departure. |
| 16 | Eligible colonists can accept native party/marriage gathering invitations across floors and join the real destination lord after arriving. Each native duty priority gets its own scan interval. Existing explicit ritual gathering remains supported. |
| 17 | Trained animals can haul to higher-priority remote storage and rescue suitable downed allies. Handlers can relocate unpenned livestock through entrances and finish native placement into an accepted enclosed pen. Native pen management recognizes connected home bunkers without changing global map-home status. Penned animals do not leave through idle portal wandering. |
| 18–19 | Supported follow-leader journeys rejoin a real destination group. Native pocket-map exit decisions retain carried loot/passengers and continue an exit duty on the next floor. Assaulting raiders can seek player structures on a floor without a pawn target. |
| 20 | Murderous rage retains its actual target across a reachable entrance. Sad/psychotic wandering can occasionally traverse a nearby safe entrance; own-room and unrelated mental states are not granted this behavior. Panic fleeing can use the scoped emergency policy. |
| 23 | Saved queued orders retain their destination maps. Crossing preserves pending forced orders. Blocked routes are replanned, with distinct entrances to one map retained so disconnected regions are not treated as interchangeable. |

Prisoner release uses native source-prison eligibility, carries the prisoner to a surface map, then applies native release state, thoughts and quest signals at a native release cell. Remote assigned prisoner beds and downed-prisoner beds use the passenger pipeline. Gravship departure checks include contained colony pawns, prisoners and quest lodgers, and traverse the full descendant graph rather than stopping at a fixed depth.

## Acceptance evidence

Fixture source: Tests/TransitProbe/Source. Runtime content was loaded through the uniquely named SABTransitFixture mod with isolated configurations under BunkerConnectedTransit. The production 0.5.0 assembly combines both halves. Final package and release verification are recorded below.

- `run7.log`: living patient pickup followed by a save while physically carried. Earlier iterations exposed a real carried-target validity bug: a carried pawn is legitimately unspawned. The pickup driver now explicitly accepts its own carried passenger.
- `reload4.log`: fresh-process identity/destination restoration; completed rescue in both directions; one patient identity; slave status after crossing; queued round trip on the correct maps; controlled Biotech mech physically reaches the remote powered charger and gains energy. Ends with `[SAB TRANSIT] ACCEPTANCE COMPLETE`.
- `Behavior/run6.log`: trained-animal exact-stack storage delivery; ordinary versus emergency danger policy; forbidden and enemy-near-entrance rejection; physical party join; exit carrying loot and preserving exit duty; a raider reaches a building-only floor with assault duty; exact-count departure cargo arrives physically. Ends with `[SAB BEHAVIOR] ACCEPTANCE COMPLETE`.
- `Custody/final2.log`: completed surface prisoner release; accepted enclosed-pen placement with native unroping; actual murderous-rage pursuit preserving target identity; two distinct entrances retained, original entrance blocked and real alternate journey completed; warden physically placed a prisoner into its assigned bed on another floor with custody intact. Ends with `[SAB CUSTODY] ACCEPTANCE COMPLETE`.

Development logs are retained, including failed fixture assumptions. For example, released prisoners retain native guest state while leaving; pen placement must wait for the native unroping step; freshly created fixture entrances need ordinary access checks. Passing planning assertions alone are not treated as completed journeys.

## Deliberate boundaries and untested combinations

- Physical maps, rooms, temperature/gas/fire propagation, line of sight, ranged attacks and adjacent-building effects remain local. No cross-floor shooting or shared-room simulation was added.
- This is representative engine testing, not every combination in the original audit. Three-floor queued work, every living-pawn container, every mech work mode, every ritual and guest lord, save/reload at every transition, and every modded think tree are not all separately certified.
- Arbitrary visitor, escort, quest and mod-defined group AI is not globally split or cloned. Follow travel requires a valid destination group; exit continuation supports the native decision to leave. Third-party calming/arrest AI requires its own supported adapter. Standing hostile arrest resistance is not bypassed by the passenger API.
- Pen filters/enclosure remain local. This supplies cross-floor placement for animals needing a pen; it does not introduce one global pasture-balancing simulation or merge fences across levels. Other handlers' ropes and animals marked for release to the wild are respected.
- Departure staging is a two-step operation requiring the dialog to be reopened. Changed or cancelled selections can leave real staged supplies on the departure floor; they do not commit an old departure. Native mass, passenger, shuttle and loading validation runs afterward.
- Mech command distance uses traversable route segments, not cross-map coordinates. It does not grant global command range or alter native bandwidth.
- Removal preparation clears saved transit orders, departure requests and custom transit jobs. It preserves unrelated native jobs. Removing a mod from a save remains a separate user action.

## Final integration

Both owners completed implementation and handed off their sources. The services half passed `final-services2.log`, `final-extended2.log` and the fresh-process partial-cargo continuation `trade-reload2.log`. The production 0.5.0 build has zero warnings/errors and passes 91 metadata/package checks. The exact production assembly also passed `release-core.log` (passenger reload/rescue, slave, queue and mech recharge) and `Behavior/release.log` (animal work, danger, social/exit/assault and physical departure cargo). The final services integration result and package hash are recorded in RELEASE-READY.txt in the staged release.

The exact production assembly passed the complete services regression in BunkerConnectedServices/release-extended.log, including real interrupted trade staging and native sale. Staged package verification passed all 91 checks. Production SHA256: D787A69C616C73B5436AA3999A9DB42D4DF97AECAB7C3C7049A88F7B7FE08573.
