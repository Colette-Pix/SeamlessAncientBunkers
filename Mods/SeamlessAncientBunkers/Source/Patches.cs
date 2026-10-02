using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    public static class FoodPatch
    {
        public static void Postfix(JobGiver_GetFood __instance, Pawn pawn, ref Job __result)
        {
            if(__instance.GetPriority(pawn)<=0)return;
            try { var travel=NeedProbe.Plan(pawn,Purpose.Food,__result,foodNode:__instance)??Dining.Plan(pawn,__result); if(travel!=null){if(__result!=null)JobMaker.ReturnToPool(__result);__result=travel;} }
            catch (Exception e) { Log.ErrorOnce("[Seamless Ancient Bunkers] Food planning skipped: " + e, 1870021); }
        }
    }
    [HarmonyPatch(typeof(JobGiver_GetRest), "TryGiveJob")]
    public static class RestPatch
    {
        public static void Postfix(JobGiver_GetRest __instance, Pawn pawn, ref Job __result)
        {
            // Only redirect a genuine vanilla sleep decision; preserve medical/rest restrictions.
            if (__result?.def != JobDefOf.LayDown) return;
            try
            {
                var travel = NeedProbe.Plan(pawn,Purpose.Rest,__result);
                if (travel != null) {JobMaker.ReturnToPool(__result);__result = travel;}
            }
            catch (Exception e) { Log.ErrorOnce("[Seamless Ancient Bunkers] Rest planning skipped: " + e, 1870022); }
        }
    }
    [HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
    public static class WorkPatch
    {
        public static void Postfix(JobGiver_Work __instance, Pawn pawn, ref ThinkResult __result)
        {
            if(RemotePawnScope.Active||Broker.For(pawn)!=null) return;
            try
            {
                var passenger = PawnTransit.TryWrap(pawn,__result.Job);
                if(passenger!=null) { __result=new ThinkResult(passenger,__instance);return; }
                Job travel;
                using(new TransitPolicy(pawn,__instance.emergency?TravelPermission.Emergency:TravelPermission.None))
                    travel = PawnTransit.Rescue(pawn,__result) ?? (!__instance.emergency?(PrisonerTransit.Bed(pawn,__result)??PrisonerTransit.Plan(pawn,__result)??PenTransit.Plan(pawn,__result)):null) ?? WorkProbe.Plan(pawn,__result,__instance.emergency);
                if(travel==null&&!__instance.emergency) travel=Hauling.Plan(pawn,__result);
                if (travel != null) {if(__result.Job!=null)JobMaker.ReturnToPool(__result.Job);__result = new ThinkResult(travel, __instance);}
            }
            catch (Exception e) { Log.ErrorOnce("[Seamless Ancient Bunkers] Work planning skipped: " + e, 1870023); }
        }
    }

    [HarmonyPatch(typeof(MapPortal), nameof(MapPortal.GetGizmos))]
    public static class GizmoPatch
    {
        public static void Postfix(MapPortal __instance, ref IEnumerable<Gizmo> __result)
        {
            if (Portal.Root(__instance) != null) __result = Add(__result, __instance);
        }
        private static IEnumerable<Gizmo> Add(IEnumerable<Gizmo> original, MapPortal portal)
        {
            foreach (var gizmo in original) yield return gizmo;
            if (Manager.Current == null) yield break;
            foreach(var extra in Orders.Gizmos(portal)) yield return extra;
            yield return new Command_Toggle
            {
                defaultLabel = "Automatic colony traffic",
                defaultDesc = "Allow colony work, needs, hauling and supplies through this opened hatch, plus animal food, beds, following and nearby wandering. Turns on automatically after clearance. Switching it off manually is remembered. Both directions share this setting.",
                isActive = () => Manager.Current.Enabled(portal),
                toggleAction = () => Manager.Current.Toggle(portal)
            };
            yield return new Command_Action
            {
                defaultLabel = "Send pawn through...",
                defaultDesc = "Choose an eligible colony pawn on this map. Works with automatic traffic disabled; the same danger and access checks apply. Open the bunker normally first.",
                action = () =>
                {
                    var choices = new List<FloatMenuOption>();
                    foreach (var pawn in portal.Map.mapPawns.AllPawnsSpawned.Where(p=>Portal.Eligible(p)&&p.Faction==Faction.OfPlayer).ToList())
                    {
                        var selectedPawn = pawn;
                        if (!Portal.CanTravel(pawn, portal, false, out _)) continue;
                        choices.Add(new FloatMenuOption(pawn.LabelShortCap, () =>
                        {
                            var job = Manager.Current.Plan(selectedPawn, portal, Purpose.Manual, null);
                            if (job != null) selectedPawn.jobs.TryTakeOrderedJob(job);
                        }));
                    }
                    if (choices.Count == 0) choices.Add(new FloatMenuOption("No eligible colony pawns or safe opened link", null));
                    Find.WindowStack.Add(new FloatMenu(choices));
                }
            };
        }
    }

    // Uses the vanilla portal APIs, with deterministic, safe landing beside impassable hatches.
    public class JobDriver_BunkerTravel : JobDriver_EnterPortal
    {
        private int nextFullValidation;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !TravelStillValid());
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(90).FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch)
                .WithProgressBarToilDelay(TargetIndex.A);
            var cross = ToilMaker.MakeToil("SAB_Cross");
            cross.defaultCompleteMode = ToilCompleteMode.Instant;
            cross.initAction = () =>
            {
                var portal = MapPortal;
                var other = Portal.Other(portal);
                if (!TravelStillValid(true) || !Portal.Landing(pawn, other, out IntVec3 cell))
                {
                    EndJobWith(JobCondition.Incompletable);
                    return;
                }
                var destination = other.Map;
                // DeSpawn ends this job. Capture all required values before calling it.
                Manager.Current.Cool(pawn);
                bool selected=Find.Selector.IsSelected(pawn);
                bool drafted=pawn.Drafted;
                QueuedTransitOrders.PreserveNativeQueue(pawn);
                GroupTransit.BeforeTransfer(pawn);
                pawn.DeSpawnOrDeselect();
                RobotSupport.SpawnTransferred(pawn, cell, destination);
                portal.OnEntered(pawn);
                if(drafted&&pawn.drafter!=null)pawn.drafter.Drafted=true;
                if(selected&&!Find.Selector.IsSelected(pawn))Find.Selector.SelectedObjects.Add(pawn);
                if (pawn.inventory != null) pawn.inventory.UnloadEverything = !destination.IsPocketMap;
                pawn.mindState.priorityWork.ClearPrioritizedWorkAndJobQueue();
                // Normal AI now re-runs food/rest/work selection on the actual destination map.
            };
            yield return cross;
        }
        private bool TravelStillValid(bool force = false)
        {
            var manager = Manager.Current;
            var intent = manager?.intents.FirstOrDefault(i => i.pawn == pawn && i.portal == MapPortal);
            if (intent == null || Manager.Now >= intent.expires) return false;
            if (!Broker.Enabled(intent)) return false;
            if (!AnimalSupport.ValidIntent(intent)||!MentalTransit.Valid(intent)) return false;
            // The final vanilla toil may already have moved the pawn before completion.
            if (pawn.Map == intent.destination) return true;
            if (!Portal.Eligible(pawn,Broker.Transport(intent),intent.forced||intent.purpose==Purpose.Manual) || MapPortal?.Spawned != true || Portal.Other(MapPortal) == null) return false;
            if (!intent.forced&&intent.purpose != Purpose.Manual && (!BunkerMod.Settings.enabled || !manager.Enabled(MapPortal))) return false;
            if(Broker.Transport(intent)&&pawn.carryTracker.CarriedThing==null) return false;
            if(intent.giver!=null&&!WorkProbe.Allowed(pawn,intent.giver.Worker)) return false;
            if(intent.target!=null&&!Broker.Transport(intent)&&(intent.target.Destroyed||(!intent.target.Spawned&&
                !(ConsumerSupply.IsFetch(intent)&&ServicesInventory.Holder(intent.target)?.Map==intent.finalMap)))) return false;
            if (!force && Manager.Now < nextFullValidation) return true;
            nextFullValidation = Manager.Now + 60;
            return Portal.CanTravel(pawn, MapPortal, !intent.forced&&intent.purpose != Purpose.Manual, out Map destination) && destination == intent.destination;
        }
    }
}

