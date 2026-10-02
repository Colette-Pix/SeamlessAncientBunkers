using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
    public static class MechTransit
    {
        [ThreadStatic] static bool finding;
        public static Building_MechCharger FindCharger(Pawn mech, Pawn carrier, bool forced)
        {
            if (finding || Manager.Current == null || !BunkerMod.Settings.enabled || !mech.Spawned || !carrier.Spawned) return null;
            try
            {
                finding = true;
                foreach (var route in Graph.Reachable(carrier, !forced))
                using (new RemotePawnScope(carrier, route.map, route.landing))
                using (mech == carrier ? null : new RemotePawnScope(mech, route.map, route.landing))
                {
                    var charger = JobGiver_GetEnergy_Charger.GetClosestCharger(mech, carrier, forced);
                    if (charger != null && !Broker.Claimed(carrier, route.map, charger, IntVec3.Invalid)) return charger;
                }
                return null;
            }
            finally { finding = false; }
        }
        public static Job Charge(Pawn p, JobGiver_GetEnergy_Charger node)
        {
            if (RemotePawnScope.Active || !TransitPolicy.Mech(p) || Broker.For(p) != null || node.GetPriority(p) <= 0 || !Manager.Current.CanScan(p, Purpose.MechCharge, true)) return null;
            var charger = FindCharger(p, p, false);
            var route = charger == null ? null : Graph.Reachable(p).FirstOrDefault(r => r.map == charger.Map);
            return route == null ? null : Broker.Begin(p, route, Purpose.MechCharge, charger);
        }
        public static Job Arrive(Intent i)
        {
            if (!(i.target is Building_MechCharger charger) || !charger.Spawned || charger.Map != i.pawn.Map ||
                !charger.CanPawnChargeCurrently(i.pawn) || charger.IsForbidden(i.pawn) || !i.pawn.CanReserveAndReach(charger,PathEndMode.InteractionCell,Danger.Some)) return null;
            var job = JobMaker.MakeJob(JobDefOf.MechCharge, charger); job.overrideFacing = Rot4.South; return job;
        }
    }
    [HarmonyPatch(typeof(JobGiver_GetEnergy_Charger), "TryGiveJob")]
    public static class RemoteMechCharging
    {
        public static void Postfix(JobGiver_GetEnergy_Charger __instance, Pawn pawn, ref Job __result)
        { if (__result == null && Manager.Current != null) __result = MechTransit.Charge(pawn, __instance); }
    }
    [HarmonyPatch(typeof(JobGiver_GetEnergy_Charger), nameof(JobGiver_GetEnergy_Charger.GetClosestCharger))]
    public static class RemoteDisabledMechCharger
    {
        public static void Postfix(Pawn mech, Pawn carrier, bool forced, ref Building_MechCharger __result)
        { if (__result == null && mech != carrier) __result = MechTransit.FindCharger(mech, carrier, forced); }
    }
    [HarmonyPatch(typeof(MechanitorUtility), nameof(MechanitorUtility.InMechanitorCommandRange))]
    public static class ConnectedMechCommandRange
    {
        public static void Postfix(Pawn mech, LocalTargetInfo target, ref bool __result)
        {
            if (__result || Manager.Current == null || !BunkerMod.Settings.enabled || !target.IsValid) return;
            var overseer = mech.GetOverseer(); var map = target.Thing?.MapHeld ?? mech.MapHeld;
            if (overseer?.Spawned != true || map == null || map == overseer.Map || !target.Cell.InBounds(map)) return;
            foreach (var route in Graph.Reachable(overseer, false).Where(r => r.map == map))
            {
                float distance = 0; var cell = overseer.Position;
                foreach (var portal in route.portals) { distance += cell.DistanceTo(portal.Position) + 8; cell = Portal.Other(portal).Position; }
                if (distance + cell.DistanceTo(target.Cell) < 24.9f) { __result = true; return; }
            }
        }
    }
}
