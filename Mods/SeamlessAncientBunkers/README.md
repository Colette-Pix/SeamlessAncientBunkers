# Seamless Ancient Bunkers — 0.5.2

Connect cleared Odyssey bunkers to your colony for work, needs, care, physical deliveries and supported group activities. Requires RimWorld 1.6, Odyssey and Harmony. This is a single-player mod.

## Install and start

1. Enable the mod after Harmony and Odyssey. Restart RimWorld after replacing its assembly. Existing saves and already generated bunkers are supported.
2. Open and explore a bunker normally. Automatic traffic turns on after clearance; a deliberate manual shutoff is saved.
3. Keep both entrances allowed and provide suitable destinations. Undraft pawns for autonomous work. Use normal work priorities, policies, assignments, stockpile priorities and allowed areas on each floor.
4. Use the entrance's **Traffic status** command to inspect blocked routes. Turning off automatic traffic does not disconnect utilities.

Manual hauling searches all eligible connected storage, including later groups behind a full high-priority stockpile. Automatic haulers can travel empty to remote ordinary stock and then carry it to eligible storage. Remote storage generally needs higher priority than available local storage to attract ordinary automatic hauling. Consumer supply, explicit bill output and selected departure deliveries have their own target requests. Workers can fetch supported missing supplies themselves; surgery supply uses Doctor work and replacement components use Construction work.

## Controls

- `=` moves up a level and `-` moves down, wrapping through the connected stack. Rebind in Keyboard configuration. Surface is 0; entered bunkers are numbered in first-entry order. No unopened map is explored by these controls.
- Keep a colonist selected while switching floors, then right-click normally. Supported work, pickup, hauling and drafted **Go here** orders travel through entrances. Accepted orders show a green confirmation circle and an entrance-to-target line. Shift-queued supported orders retain their destination maps across travel and saves.
- Entrances also offer direct travel, remote work orders, traffic controls and pending gathering cancellation. Manual orders may use traffic-disabled connections but still respect forbidden access and valid landings.
- Settings control work, needs, hauling, scan frequency and candidate budgets.

## Connected behavior

**People and care.** Adults, children, colony slaves and supported player-controlled quest pawns can travel within their native restrictions. Rescuers carry patients to suitable beds across floors. Supported prisoner bed transfers and releases, baby carrying, disabled-mech transport and native container passenger jobs use saved physical carrying. Wardens and childcare workers can seek remote jobs, including feeding and lessons. Babies and downed pawns do not travel independently.

**Work.** Audited native work givers cover construction/maintenance, production, research, mining, growing, cleaning, hunting, husbandry, wardening, childcare and selected DLC tasks. Work-giver order remains authoritative. Within the same giver, native priority and total route distance can make nearby remote work beat distant local work, with a preference for staying local. Rotating candidate scans prevent a rejected prefix from permanently hiding later jobs.

**Supplies and stock.** Construction, bills, fuel, medicine, repair components, childcare food and supported specialized consumers can request physical supplies across entrances. Corpses support ordinary hauling, butchering and assigned burial. Eligible haul-source containers and explicitly requested gene-bank packs can be fetched. Supported bill counts apply native item filters across connected stock; explicit remote include/output stockpiles are retained. Real products are carried to the selected output destination. Stored packs are not automatically shuffled between gene banks.

**Needs.** Supported routes include food, beds, medical rest, recreation, clothing, drugs, dining, hemogen, deathrest, meditation, learning and optional Dubs hygiene. Ordinary sleep prefers a usable assigned bed across floors. Medical-rest decisions preserve native medical bed selection. The colonist-bed shortage warning counts beds and colonists across connected floors, including with the BunkBeds replacement bundled in Vanilla Gravship Expanded; medical beds still do not count as ordinary sleeping beds. Assigned deathrest caskets are recognized. Native policies and destination jobs are checked again after arrival.

**Animals and mechs.** Tamed animals can seek food/beds and follow masters. Trained animals can haul and rescue. Handlers can move unpenned livestock into an accepted enclosed pen on another floor. Penned animals do not wander out via idle entrance travel. Controlled Biotech mechs can work and use compatible chargers; cross-floor command range includes route distance and bandwidth remains native. Misc. Robots preserve their charger assignments. Attack-trained animals and optional VPE enthralled skeletons retain supported hostile pursuit.

**Groups and danger.** Eligible colonists can join native parties/marriages. Supported follow-leader journeys rejoin an actual destination group. Emergency work/rescue and fleeing may cross despite distant threats, provided enemies are clear of both endpoints and the landing is safe. Assaulting raiders can seek colony pawns or structures; native exit journeys preserve carried loot/passengers. Murderous rage retains its target across reachable entrances, and selected wandering mental states can use nearby safe entrances. Arbitrary guest, quest and modded group AI is not globally rewritten.

**Routes and saves.** Discovery only uses opened, loaded entrance pairs. It does not generate maps. Distinct arrivals are retained for maps with multiple entrances, and blocked journeys can replan. Passenger, cargo, queued-order and delivery state survives saves. Claims and native reservations prevent competing collection. Failed carrying routes drop passengers safely.

## Trading and departure

Ground traders list reachable remote goods. Confirmation completes the normal native sale and payment immediately, including remote silver. No staging or reopening is required. Orbital trading combines each floor's own powered beacon coverage; purchases arrive on the native trade map.

Confirm caravan, pod or shuttle loading once. Eligible remote supplies and passengers gather physically and native packing/loading then continues automatically. The saved plan retains exact quantities and destination, and native mass/loading/eligibility checks still run. Pending gathering can be cancelled at an entrance or the transporter. Cancelling can leave already gathered goods on the destination floor; it does not start the cancelled departure.

Caravan/ritual gathering and gravship boarding remain supported. Unanchored takeoff checks connected bunker occupants, including contained colony pawns, prisoners and quest lodgers. Bring them up or use a grav anchor.

## Power, plumbing and doors

Connect a conduit to the edge of the opened hatch's 3x3 footprint and another to its ladder below. Do the same with Dubs Bad Hygiene plumbing. Lines inside the footprint count; diagonal-only contact does not. Normal and hidden lines work. Both ends need a line.

Connected networks share real generation, batteries, water and sewage services with native capacity/quality rules. Removing a line or sealing the connection disconnects utilities; switching off pawn traffic does not. Dubs is optional.

Revealed ancient blast doors become claimable after clearance. Claiming retains hacking locks. Enemies arriving through supported hostile pursuit cannot open closed ancient blast doors and must break through; native defenders retain ordinary access. Open or held-open doors remain passable.

## Limits and compatibility

Physical maps remain separate: rooms, temperature, gas, fire propagation, ranged combat, pen nutrition and building adjacency remain local. Research facilities and gene assembly need their native local setup. Floor painting has no dedicated missing-dye delivery request, although local dye and ordinary hauled supply work. No multiplayer or unopened-map travel is claimed.

Optional adapters explicitly support Quarry, Processor Framework, Misc. Robots, Dubs Bad Hygiene and VPE skeleton pursuit; Adaptive Storage acceptance checks are respected. These mods are not bundled. Arbitrary custom jobs, needs, containers and group frameworks need their own adapters. A modified native work giver can still change behavior.

[TRADE-HAUL-BED-UPDATE.md](TRADE-HAUL-BED-UPDATE.md) records the 0.5.1 corrections. [CONNECTED-SERVICES-REPORT.md](CONNECTED-SERVICES-REPORT.md) and [CONNECTED-TRANSIT-REPORT.md](CONNECTED-TRANSIT-REPORT.md) map the work to the audit and distinguish completed engine tests from source-supported or untested combinations. [COMPATIBILITY.md](COMPATIBILITY.md) and [TEST-REPORT.md](TEST-REPORT.md) retain historical evidence; older limitations there are superseded only where the current reports say so. Representative tests are not certification of every DLC/mod, multi-hop and interruption combination.

## Removal and development

Before removing the mod, use **Prepare this save for mod removal**, save, quit and disable it. This clears custom transit jobs, requests and queued orders. Keep normal backups when changing mods.

Build with .NET SDK 8+ and installed game/Harmony assemblies:

```
dotnet build Source/SeamlessAncientBunkers.csproj -c Release -p:GameDir="C:/path/to/RimWorld" -p:HarmonyPath="C:/path/to/0Harmony.dll"
```

Output is `1.6/Assemblies/SeamlessAncientBunkers.dll`, targeting .NET Framework 4.7.2. Game/Harmony assemblies are not redistributed. Management roster integration is documented in [CONNECTED-ROSTERS.md](CONNECTED-ROSTERS.md).

Tests under `Tests` deliberately alter disposable colonies. Never enable the test fixtures in a personal save. See [TESTING.md](TESTING.md) for isolated launch instructions and the connected reports for passing logs.


