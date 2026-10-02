## 0.4.2 utility connections

Built against RimWorld 1.6.4871 rev591 with zero compiler warnings/errors; the existing API/package audit passes all 70 checks. Real-engine evidence: `Tests/Evidence/utilities-042.log` (31 passing assertions, ACCEPTANCE COMPLETE) and `Tests/Evidence/utilities-no-dubs-042.log` (3 passing assertions, ACCEPTANCE COMPLETE).

Verified separate map-local topology, electricity consumption across the hatch with a single battery debit per tick, water draw and contamination preservation, sewage delivery with conserved volume, native toilet eligibility, temporary network-list restoration, save/reload, operation with automatic traffic disabled, power/water/sewage disconnection after cutting lines, fixture unavailability after cutting plumbing, hidden-line reconnection, and reverse water flow. The no-Dubs run verifies successful optional integration startup and bunker-to-surface electricity with a single battery debit. No personal colony save or configuration was changed; the already-running personal game was left running.

Tests use a disposable historical bunker fixture with the obsolete test component removed from its copy. The passing logs retain pre-existing think-node/ideology migration and material-thread warnings. Earlier setup attempts failed because the restricted process could not access Workshop, and because a broader historical fixture required missing third-party mods; neither is represented as a passing run. The suite exercises native utility methods and fixture eligibility in the real engine, not a full pawn toilet animation, multi-hatch stress test, or long-term colony performance run. Pump/heating equipment participates in the shared component lists but is not separately acceptance-tested here.

## 0.4.1 door-defense fix

Build: zero errors or warnings. Runtime evidence: Tests/Evidence/engine72-doorsecurity.log, 13 passing assertions and ACCEPTANCE COMPLETE. Verified native hacked-door ownership bypass, real EnemyPursuit.Transfer registration, closed-door physical blocking, colonist and original-defender access, unclaimed-door protection, save/reload retention, restored access after recruitment, and actual native pathfinding damage to a closed blocking door in an enclosed corridor. Legacy fixture retains its previously documented missing old test-component warning. No original personal save was modified. This uses the user-approved incoming-enemy fallback and does not assert complete hidden-encounter clearance. Applies to future crossings, not unidentifiable prior arrivals.
# 0.4.0 verification

Built against RimWorld 1.6.4871 rev591 with zero compiler warnings/errors. The final metadata/package audit passes 70 checks.

Completed isolated real-engine checks:

- engine55-doors.log: live defenders block clearance and claiming; corpses do not; cleared bunkers enable automatically; normal Claim changes blast-door ownership; manual traffic shutoff survives reload; ordinary medical recovery beds are accepted.
- engine67-inward-needs.log: injured pawn without an owned bed can select a surface bed; native scheduled drug and clothing decisions route in both directions; local drug jobs remain local; ritual and caravan candidate validation includes remote pawns and restores their actual map; meal pickup, transit save/reload, surface eating and native caravan creation after saved gathering pass.
- engine65-levels-learning.log: two bunkers receive levels in entry order; generated-but-unentered bunkers are excluded; up/down wrapping, actual displayed-map switching, key defaults and save/reload numbering pass. Learning Helper definitions load without new definition errors. Physical keyboard input and every dialog focus state were not separately automated.
- engine66-launch-gather.log: native boarding request finds a bunker route; launch pilot assignments and the deferred ritual survive reload; the native launch ritual starts only after the pilot physically reaches the surface.
- engine69-inward-thirst.log: Dubs thirst uses its native job to reach surface water, then enters the bunker and drinks there when no local source remains.

The complete 104-entry personal mod configuration was loaded with a separate copied colony save. After undrafting only in that copy, two bunker colonists automatically returned to their assigned surface beds/work. The remaining quest colonist had no valid available surface bed; the colony's two regular beds were already assigned and its other bed was for prisoners. This is not an entrance or enemy-clearance failure. No test saved over the personal colony. The original test snapshot is retained privately; no colony save is distributed.

The test fixtures retain a removed legacy test GameComponent and emit its known load warning, plus stale think-node notices. The full personal list emits existing translation/content warnings. The completed runs contain no observed SAB travel/patch exception. This is not a claim of zero warnings or comprehensive certification of every active mod. Earlier animal, Quarry/Processor, robot, hostile-pursuit and gravship-flight/landing tests below remain historical evidence. The final revision adds a destination-picker guard before gravship fuel spending and refreshes level ownership after moved hatches; these guards are build/API checked, not a repeated full launch cutscene test.

Development fixture failures were investigated: a drug prohibited by the test pawn's normal rules was replaced with a scheduled medical drug; a naked fixture pawn was correctly blocked by cold surface danger and was dressed; local natural water initially prevented the intentional remote-thirst case and was removed only in the test map. Safety and native policy checks were retained.

# 0.3.2 animal support verification

Release build: zero warnings/errors; 53 API/package checks passed. The isolated real-game run `Tests/Evidence/engine49-animals.log` completed all animal assertions: tamed eligibility, nearby local food preference, automatic bunker feeding, transit save/reload with unique identity, assigned animal sleeping spot use, trained following of a drafted master with assignment preserved, follow-toggle rejection, forbidden-hatch and allowed-area rejection, tamed wandering, traffic-toggle rejection, and wild wandering. The test uses a Labrador retriever; every animal species and modded animal AI are not separately certified. Fieldwork-follow uses the native setting and job tag but was not independently exercised in the engine fixture.

This run loaded Harmony, Core, Biotech, Odyssey, this mod and a source-controlled disposable animal harness. The initial legacy fixture contains a removed older test component, generating an expected missing-component load warning, and stale think-node notices after adding Biotech. These are fixture migration messages, not production failures. The first development run stopped because the generated dog had not been trained in obedience; the corrected fixture trains it through the native API. No animal acceptance failure occurred in the completed engine49 run. Your active game and personal saves were not used for these tests.

The final assembly adds UI wording and null/animal work-menu guards after that gameplay run; the final build/API audit covers those changes. Earlier colonist, robot, enemy and gravship results below remain historical evidence, not a new full regression of every mod combination.

# 0.3.1 child support verification

Allows free child colonists through the shared colony eligibility gate, including automatic needs/work routes and manual orders. Babies remain excluded. Work probing and hauling explicitly check RimWorld's age-based work restrictions as well as existing work priorities and incapabilities. No cross-map learning search is added.

Release build: zero warnings/errors. All 52 API/package checks pass. Child gameplay has not been run in an engine fixture or in Femageddon; the engine results below are historical 0.3.0 evidence, not a child gameplay test.

# 0.3.0 acceptance report

The 0.3.0 build passes **52 API/package checks** and compiles with zero warnings/errors. `engine45-full-030.log` repeats the original ten acceptance scenarios, then tests Quarry mining, Processor input staging/fill/empty, Misc. Robots cleaning and charging return, a hauler robot delivering exactly 17 silver, hostile pursuit, and gravship departure. All stages through saving the gravship in flight pass. This run uses Harmony, all five DLC, Vanilla Expanded Framework, Quarry, Processor Framework, Misc. Robots and Vanilla Gravship Expanded Chapters 1 and 2.

The 20-colonist/6,000-tick regression records one total crossing, maximum one per pawn. It measures 678 work-planner calls, 53.57 ms total, maximum 1.91 ms; 200 graph queries take 2 ms.

`engine46-final-landing.log` completes the final-build flight save/reload, landing, unique-pawn and 25-steel checks, then confirms successful bunker transit after landing. It ends in EXTENSION ACCEPTANCE COMPLETE. `engine47-enemy-door.log` additionally verifies a hostile raider reaching the hatch through an enclosed room's closed door, saving/reloading the pursuit job, entering a forbidden/traffic-disabled bunker, acquiring a pawn there as a combat target, and holding a destination-map lord with one pawn identity. It ends in ENEMY DOOR AND SAVE TEST COMPLETE.

The final 0.3.0 runs retain four translation errors from the loaded mod combination and the direct-launch Steam notice. No SAB exception or failed acceptance assertion appears in these completed runs. Manhunter and vanilla sapper/breacher states are supported by the same implementation but were not each given an independent end-to-end combat fixture.

The gravship test exercises actual ship extraction, world-object travel, save/reload in a fresh process, and native landing placement. It verifies cancellation of an onboard pawn's obsolete bunker route and preservation of that pawn's identity and 25 steel in inventory, then checks ordinary bunker travel after landing. It also verifies rejection of unanchored takeoff while player-faction pawns remain underground. This is not a pilot-menu/cutscene visual test. Early attempts to reload in the same process while Unity was rebuilding the world display caused native crashes in the isolated test process; the final harness saves and quits, then loads that flight save in a fresh process.

The sections below retain the original requirement matrix and historical 0.2.0 evidence. New adapters supersede the earlier Quarry/Processor/Misc. Robots exclusions. Removed-mod loading was separately tested on 0.2.0; 0.3.0 cleanup additionally disables the feature before cancellation and clears the new hostile-pursuit job.

Tested with the installed Windows RimWorld **1.6.4871 rev591** engine, .NET Framework 4.7.2 mod assembly, and Harmony. Tests ran in a separate `-savedatafolder=BunkerRuntime`, with generated fixtures and a separate active-mod configuration. No personal colony save was loaded or edited. This is single-player evidence, not a Multiplayer certification.

## Original definition of done

| Requirement | Implementation and actual-engine result |
| --- | --- |
| 1. Higher-priority remote assigned work | PASS: remote research beats available lower-priority local cleaning; fixture first verifies that vanilla can select the local cleaning job. |
| 2. Come upstairs to eat, return to work | PASS: researcher crosses for a meal and subsequently resumes underground research. |
| 3. Owned bunker bed | PASS: owned-bed travel and sleep tested in both directions. |
| 4. Doctor crosses without duplicate patient claims | PASS: two doctors compete for one remote patient; one soft claim wins; vanilla tending starts after transit. |
| 5. Stockpile hauling | PASS: 25 steel picked up, carried through the entrance and placed; exact conservation asserted: source 0, destination 25, carry tracker empty. |
| 6. No unopened/hostile automatic entry | PASS: unopened hatch produces no route and no map; spawned hostile threat excludes the route. |
| 7. Destroyed/forbidden entrance cancels | PASS: forbid during travel and destroy a test entrance during travel; pending intent clears without transfer. |
| 8. Save/load transit without loss or duplication | PASS: legacy 0.1 walking intent loads and completes ten crossings with inventory intact; separate save/reload during the entrance wait while carrying 25 steel completes delivery with one pawn identity. Transfer itself is synchronous, with no intermediate tick to save. |
| 9. Twenty colonists avoid oscillation | PASS: 20 colonists, two maps, research contention, 6,000 ticks; Common Sense + Allow Tool run recorded one total transfer and at most one per pawn. This is a bounded fixture, not a long-running colony benchmark. |
| 10. Disabled feature preserves ordinary jobs | PASS: cleanup removes custom jobs/intents; disabled observation confirms ordinary research continues. |

## Additional coverage

- Multi-hop travel between two generated bunkers through the surface.
- Allowed-area denial and drafted-pawn exclusion.
- Remote medical-bed routing and emergency firefighting.
- Ingredient supply followed by completed vanilla cooking; construction supply followed by a completed wall.
- Building recreation fallback.
- The same command action used by **Prioritize via entrance** starts a forced ordinary research job. The headless test invokes the action; mouse interaction and menu appearance were not visually tested.
- Temporary remote map/position state restores after an injected exception.
- Cleanup followed by a new process with the production mod absent: cleaned save loads, pawn identities remain unique, no custom jobs remain, and a new save reloads successfully (3 maps, 26 colonists including fixtures). The first load reports missing removed game-component types; the follow-up save/reload succeeds. See removal evidence.

## Engine runs and performance

`engine35-compat.log` is the complete acceptance suite with Harmony, Core, Odyssey, HugsLib, Allow Tool and Common Sense. All acceptance stages completed. Common Sense was installed locally but was **not enabled in the user's personal active list**; this was an additional compatibility combination.

That run measured 200 graph queries in 2 ms and 655 work-planner calls in 49.53 ms total, with a 1.17 ms maximum. Calls include throttled early exits. These numbers describe this fixture and machine only, not overall game performance.

`engine38-user-mods.log` repeats the complete suite on the final production build with selected mods from the user's active list and all five DLC. It additionally passes actual Adaptive Storage/sbz crate delivery, full-crate rejection, and a Dubs bladder job interrupting and resuming a saved travel intent. It finishes with USER MOD-LIST COMPATIBILITY RUN COMPLETE. The same 20-colonist fixture records one total transfer, maximum one per pawn; 586 work-planner calls total 43.62 ms, maximum 1.27 ms. See COMPATIBILITY.md for exact scope and logged warnings.

`engine36-removal.log` and `removal-results.txt` record removal testing. Engine launch logs also contain a Steam API initialization notice because the tests were launched directly, outside Steam.

The final API/package audit passes **40 checks**: Harmony targets and argument names, compiled types, portal pairing, carry-retention job flags, declared dependencies, no map-generation calls, and no redistributed game/Harmony assemblies. These checks supplement gameplay tests; they do not replace them.

## Scope and deliberate choices

All five development phases are implemented. File organization is flatter than the proposed design, and the build references the actual installed game assemblies instead of a reference NuGet package; no game assemblies are redistributed. Speculative jobs are reduced to saved intent data and recreated after arrival. The graph is reconstructed from live portals, with bounded scans and per-purpose throttling rather than long-lived remote-job caches.

Supported scanner coverage is listed in README.md. The runtime suite exercises representative research, cleaning, doctoring, firefighting, hauling, cooking and construction flows; it does not individually certify every scanner, policy, custom race, equipment loadout, or third-party job patch. Animal/mech/slave autonomy and carrying living pawns are outside scope. Arbitrary mod work givers require dedicated adapters. Plumbing, electrical networks, rooms, counters and incidents stay map-local.

See COMPATIBILITY.md for the active-mod review, targeted fixes, and additional installed-mod test results. Test harness source and selected evidence accompany the release. Harnesses are destructive fixture tools and must never be enabled in a personal save.


# Floor-sleep cooldown fix (2026-09-27)

Colonist rest planning now bypasses the post-trip minimum stay when vanilla selects sleeping on the ground. Existing local beds remain preferred, and route safety, failed-destination avoidance and scan throttling still apply. This addresses a code path that could strand a tired pawn beside the bunker exit during the stay cooldown; the reported personal-save occurrence has not been reproduced.

Production assembly and runtime harness compile with zero warnings/errors. The owned-bed engine scenario now starts with an active post-trip cooldown, verifies that ordinary work remains blocked, and requires immediate bed travel followed by vanilla sleep. This updated engine scenario has not been executed; compilation alone is not gameplay verification.
# Cross-level manual orders — 2026-09-27

Production build completed with zero warnings/errors; all 70 API/package checks passed. The isolated OrderProbe recorded 18 gameplay assertions in `BunkerOrders/orders6.log`, then 9 reload assertions in `BunkerOrdersReload/reload.log`. Both processes reported `[SAB ORDER TEST] ACCEPTANCE COMPLETE`.

Verified ordinary prioritize-hauling menu detection of a connected-map stockpile and actual cargo delivery; retained selection through level navigation; native drafted Go here through an entrance to the exact destination while remaining drafted; selection retention during physical transfer; native remote inventory pickup; remote prioritized repair; and fresh-process restoration and completion of the saved drafted order. Checks used disposable fixture saves with Harmony, Core, all official DLC, production and the temporary probe. No personal save or running colony was changed. The temporary probe was removed afterward.

Hidden-process testing bypassed only `FloatMenuOption.SetSizeMode` text measurement because Unity has no GUI skin outside OnGUI. Native menu generation, actions, jobs, routing, physical travel and serialization ran normally. Rendered UI, synthesized key/mouse input, Shift-queued cross-map orders, arbitrary third-party menu providers and every possible job type were not exhaustively tested.

Tested production DLL SHA-256: `7AFE405E79EF52AF16428506A8ECCF1FEE314D13476840D30FFE2525D8300A14`.
