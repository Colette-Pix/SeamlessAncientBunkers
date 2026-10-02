using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
    public static class PawnTransit
    {
        public static bool IsPassenger(Intent i) => i != null && (i.purpose == Purpose.FetchPassenger || i.purpose == Purpose.Passenger);
        public static bool Describe(Job job, out Pawn passenger, out Thing destination)
        {
            passenger = null; destination = null;
            if (job?.def?.driverClass == null) return false;
            var driver = job.def.driverClass;
            if (typeof(JobDriver_TakeToBed).IsAssignableFrom(driver) || driver == typeof(JobDriver_HaulMechToCharger) ||
                driver.Name == "JobDriver_CarryToCryptosleepCasket" || driver.Name == "JobDriver_CarryToTransporter")
            { passenger = job.targetA.Thing as Pawn; destination = job.targetB.Thing; }
            else if (typeof(JobDriver_CarryToBuilding).IsAssignableFrom(driver) || driver.Name == "JobDriver_CarryToEntityHolder")
            { passenger = job.targetB.Thing as Pawn; destination = job.targetA.Thing; }
            return passenger != null && destination != null;
        }
        public static Job TryWrap(Pawn carrier, Job job)
        {
            if (!Describe(job, out var passenger, out var destination) || !passenger.Spawned ||
                (carrier.Map == passenger.Map && passenger.Map == destination.MapHeld)) return null;
            return Plan(carrier, passenger, destination, job, job.playerForced);
        }
        public static bool DestinationUsable(Pawn carrier, Pawn passenger, Thing destination, Job job)
        {
            if (destination?.Spawned != true || passenger == null || passenger.Dead || destination.IsForbidden(carrier) ||
                !Portal.Allowed(carrier, destination.Map, destination.Position) ||
                Manager.Current.intents.Any(i => i.pawn != carrier && i.deliveryTarget == destination && IsPassenger(i))) return false;
            if (!carrier.CanReserveAndReach(destination, PathEndMode.Touch, Danger.Some)) return false;
            if(job.def==PrisonerTransit.ReleaseDef)return destination.Map.CanEverExit;
            if (job.def==JobDefOf.RopeToPen) return PenTransit.Valid(carrier,passenger,destination);
            if (destination is Building_Bed bed)
                return RestUtility.IsValidBedFor(bed, passenger, carrier, false, false, false,
                    job.def.makeTargetPrisoner ? GuestStatus.Prisoner : passenger.GuestStatus);
            if (destination is Building_MechCharger charger) return charger.CanPawnChargeCurrently(passenger);
            if (destination is Building_Enterable enterable) return enterable.CanAcceptPawn(passenger);
            if (destination is Pawn mother && job.def==JobDefOf.BreastfeedCarryToMom)
                return ChildcareUtility.CanHaulBabyToDownedMomToBreastfeedNow(carrier,mother,passenger,job.playerForced,out _);
            if (destination is Pawn trader && job.def==JobDefOf.Wait)
                return trader.trader?.CanTradeNow==true&&!trader.HostileTo(carrier);
            return destination.Faction == Faction.OfPlayer;
        }
        public static Job Plan(Pawn carrier, Pawn passenger, Thing destination, Job nativeJob, bool forced = false)
        {
            if (Manager.Current == null || !BunkerMod.Settings.enabled || RemotePawnScope.Active || carrier == passenger ||
                passenger?.Spawned != true || passenger.Dead || destination?.Spawned != true || nativeJob == null ||
                carrier.carryTracker?.CarriedThing != null || passenger.IsForbidden(carrier) || Manager.Current.Claimed(passenger)) return null;
            // Native arrest resistance must happen before moving a standing hostile pawn.
            if (!passenger.Downed && nativeJob.def == JobDefOf.Capture) return null;
            var permission = TransitPolicy.For(carrier) | (nativeJob.def == JobDefOf.Rescue ? TravelPermission.Emergency : TravelPermission.None);
            using (new TransitPolicy(carrier, permission))
            {
                if (!Portal.Eligible(carrier, false, forced)) return null;
                var routes = Graph.Reachable(carrier, !forced);
                Route source = passenger.Map == carrier.Map ? new Route { map = carrier.Map, landing = carrier.Position } : routes.FirstOrDefault(r =>
                {
                    if(r.map!=passenger.Map)return false;
                    using(new RemotePawnScope(carrier,r.map,r.landing))return carrier.CanReserveAndReach(passenger,PathEndMode.ClosestTouch,Danger.Some);
                });
                if (source == null) return null;
                Route destinationRoute;
                using (new RemotePawnScope(carrier, source.map, source.landing))
                {
                    if (!carrier.CanReserveAndReach(passenger, PathEndMode.ClosestTouch, Danger.Some)) return null;
                    var destinations=new List<Route>();
                    if(destination.Map==carrier.Map)destinations.Add(new Route{map=carrier.Map,landing=carrier.Position});
                    else destinations.AddRange(Graph.Reachable(carrier,!forced).Where(r=>r.map==destination.Map));
                    destinationRoute=destinations.FirstOrDefault(r=>
                    {
                        using(new RemotePawnScope(carrier,r.map,r.landing))
                        using(new RemotePawnScope(passenger,r.map,r.landing))return DestinationUsable(carrier,passenger,destination,nativeJob);
                    });
                    if (destinationRoute == null) return null;
                }
                if (source.map == carrier.Map && destination.Map == carrier.Map) return nativeJob;
                var route = source.map != carrier.Map ? source : destinationRoute;
                if (route == null) return null;
                var job = Broker.Begin(carrier, route, source.map != carrier.Map ? Purpose.FetchPassenger : Purpose.Passenger, passenger, forced: forced, count: 1);
                if (job != null)
                {
                    var intent = Broker.For(carrier); intent.deliveryTarget = destination; intent.orderedJob = nativeJob;
                    intent.permission = permission;
                }
                return job;
            }
        }
        public static Job Arrive(Intent i)
        {
            var p = i.pawn; var passenger = i.target as Pawn; var destination = i.deliveryTarget; var original = i.orderedJob;
            if (passenger == null || passenger.Dead || destination?.Spawned != true || original == null) return null;
            if (i.purpose == Purpose.FetchPassenger)
            {
                // Remove the old soft claim before creating the carrying leg.
                Manager.Current.intents.Remove(i);
                using (new TransitPolicy(p, i.permission)) return Plan(p, passenger, destination, original, i.forced);
            }
            if (p.carryTracker.CarriedThing != passenger || destination.Map != p.Map) return null;
            if(original.def!=JobDefOf.RopeToPen&&!DestinationUsable(p,passenger,destination,original))return null;
            if(original.def==PrisonerTransit.ReleaseDef){i.orderedJob=null;return PrisonerTransit.Arrive(p,passenger,original);}
            // Start the original driver from a real spawned target: its custody, guest,
            // bed and container side effects execute once through vanilla's normal path.
            if (!p.carryTracker.TryDropCarriedThing(p.Position, ThingPlaceMode.Near, out var dropped) || dropped != passenger) return null;
            i.orderedJob = null;
            if(original.def==JobDefOf.RopeToPen)
            {
                var pen=destination.TryGetComp<CompAnimalPenMarker>();
                if(!PenTransit.Valid(p,passenger,destination))return null;
                var rope=WorkGiver_TakeToPen.MakeJob(p,passenger,pen,false,RopingPriority.Closest,out _);
                JobMaker.ReturnToPool(original);return rope;
            }
            if (original.def.driverClass == typeof(JobDriver_HaulMechToCharger)) original.targetC = destination.InteractionCell;
            return original;
        }
        public static Building_Bed FindBed(Pawn carrier, Pawn passenger, bool prisoner = false, bool forced = false)
        {
            if (passenger?.Spawned != true || carrier?.Spawned != true) return null;
            var routes = new List<Route> { new Route { map = carrier.Map, landing = carrier.Position } };
            routes.AddRange(Graph.Reachable(carrier, !forced));
            foreach (var route in routes)
            using (new RemotePawnScope(carrier, route.map, route.landing))
            using (new RemotePawnScope(passenger, route.map, route.landing))
            {
                var bed = RestUtility.FindBedFor(passenger, carrier, false, false, prisoner ? GuestStatus.Prisoner : passenger.GuestStatus);
                if (bed != null && !Manager.Current.intents.Any(i => i.pawn != carrier && i.deliveryTarget == bed && IsPassenger(i))) return bed;
            }
            return null;
        }
        public static Job Rescue(Pawn p, ThinkResult local)
        {
            if (RemotePawnScope.Active || Broker.For(p) != null || !BunkerMod.Settings.work) return null;
            var rescueTraining=DefDatabase<TrainableDef>.GetNamedSilentFail("Rescue");
            bool animal = p.RaceProps.Animal && rescueTraining!=null && p.training?.HasLearned(rescueTraining) == true;
            var giver = DefDatabase<WorkGiverDef>.AllDefsListForReading.FirstOrDefault(d => d.Worker is WorkGiver_RescueDowned);
            if (!animal && (giver == null || p.workSettings == null || !p.workSettings.WorkIsActive(giver.workType) || p.WorkTagIsDisabled(giver.workTags))) return null;
            if (local.IsValid && local.Job.def != JobDefOf.Wait_Wander && local.Job.def != JobDefOf.GotoWander)
            {
                if (animal || local.Job.workGiverDef == null) return null;
                var ordered = p.workSettings.WorkGiversInOrderNormal;
                if (ordered.FindIndex(g => g.def == local.Job.workGiverDef) <= ordered.FindIndex(g => g.def == giver)) return null;
            }
            using (new TransitPolicy(p, TravelPermission.Emergency | (animal ? TravelPermission.AnimalWork : TravelPermission.None)))
            {
                if (!Manager.Current.CanScan(p, Purpose.FetchPassenger, true)) return null;
                var maps = new List<Route> { new Route { map = p.Map, landing = p.Position } }; maps.AddRange(Graph.Reachable(p));
                foreach (var route in maps)
                foreach (var patient in route.map.mapPawns.SpawnedDownedPawns.ToList())
                {
                    if (patient == p || patient.InBed() || Manager.Current.Claimed(patient)) continue;
                    Building_Bed bed;
                    using (new RemotePawnScope(p, route.map, route.landing))
                    {
                        if (!HealthAIUtility.CanRescueNow(p, patient)) continue;
                        bed = FindBed(p, patient);
                    }
                    if (bed == null || (patient.Map == p.Map && bed.Map == p.Map)) continue;
                    var native = JobMaker.MakeJob(JobDefOf.Rescue, patient, bed); native.count = 1; native.workGiverDef = animal ? null : giver;
                    var result = Plan(p, patient, bed, native); if (result != null) return result;
                    JobMaker.ReturnToPool(native);
                }
            }
            return null;
        }
    }

    public class JobDriver_CollectPassenger : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => job.targetA.Thing==null || (pawn.carryTracker.CarriedThing!=job.targetA.Thing && (!job.targetA.Thing.Spawned || job.targetA.Thing.IsForbidden(pawn))));
            this.FailOn(() => !PawnTransit.IsPassenger(Broker.For(pawn)) || !BunkerMod.Settings.enabled || (job.targetA.Thing as Pawn)?.Dead != false);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_General.Do(() => { var i = Broker.For(pawn); if (i != null) { i.carrying = true; i.cargo = pawn.carryTracker.CarriedThing; } });
        }
    }

    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    public static class PassengerOrders
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(List<FloatMenuOption> __result, FloatMenuContext context)
        {
            if (Manager.Current == null || !BunkerMod.Settings.enabled || !context.ClickedCell.InBounds(Find.CurrentMap)) return;
            foreach (var patient in context.ClickedCell.GetThingList(Find.CurrentMap).OfType<Pawn>().Where(p => p.Downed).ToList())
            foreach (var carrier in context.allSelectedPawns.Where(p => p.Faction == Faction.OfPlayer && !p.RaceProps.Animal))
            {
                if (carrier == patient || !Portal.Eligible(carrier, false, true)) continue;
                foreach (bool capture in new[] { false, true })
                {
                    if (capture && (!patient.RaceProps.Humanlike || patient.IsPrisonerOfColony || carrier.WorkTagIsDisabled(WorkTags.Violent))) continue;
                    Building_Bed bed;
                    using (new TransitPolicy(carrier, TravelPermission.Emergency)) bed = PawnTransit.FindBed(carrier, patient, capture, true);
                    if (bed == null || (bed.Map == patient.Map && carrier.Map == patient.Map)) continue;
                    var pawn = carrier; var takee = patient; var chosen = bed; bool makePrisoner = capture;
                    __result.Add(new FloatMenuOption((capture ? "Capture" : "Rescue") + " via entrance: " + patient.LabelShort + " (" + carrier.LabelShort + ")", () =>
                    {
                        var native = JobMaker.MakeJob(makePrisoner ? JobDefOf.Capture : JobDefOf.Rescue, takee, chosen); native.count = 1; native.playerForced = true;
                        var travel = PawnTransit.Plan(pawn, takee, chosen, native, true);
                        if (travel != null) pawn.jobs.TryTakeOrderedJob(travel);
                    }));
                }
            }
        }
    }
}
