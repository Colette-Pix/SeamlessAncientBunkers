using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SeamlessAncientBunkers
{
    public static class GroupTransit
    {
        public static Job Follow(Pawn pawn, Pawn leader)
        {
            if(leader?.Spawned!=true||pawn?.Spawned!=true||pawn.Map==leader.Map||RemotePawnScope.Active||Broker.For(pawn)!=null||
                Manager.Current==null||pawn.CurJob?.playerForced==true||pawn.Downed||pawn.InMentalState)return null;
            var destinationLord=leader.GetLord();
            if(pawn.GetLord()!=null&&(destinationLord==null||destinationLord.Map!=leader.Map))return null;
            using(new TransitPolicy(pawn,pawn.GetLord()!=null?TravelPermission.Group:TravelPermission.None))
            {
                if(!Manager.Current.CanScan(pawn,Purpose.GroupTravel))return null;
                var route=Graph.Reachable(pawn).FirstOrDefault(r=>r.map==leader.Map);if(route==null)return null;
                var job=Broker.Begin(pawn,route,Purpose.GroupTravel,leader);
                if(job!=null){var i=Broker.For(pawn);i.destinationLord=destinationLord;i.needNode="SAB.Follow";}
                return job;
            }
        }
        public static Job Social(Pawn pawn,ThinkTreeDutyHook hook)
        {
            if(RemotePawnScope.Active||Manager.Current==null||Broker.For(pawn)!=null||pawn.GetLord()!=null||!pawn.IsFreeNonSlaveColonist||
                !Manager.Current.CanScan(pawn,Purpose.GroupTravel,false,hook.ToString()))return null;
            foreach(var route in Graph.Reachable(pawn))
            foreach(var lord in route.map.lordManager.lords.ToList())
            {
                if(!(lord.LordJob is LordJob_VoluntarilyJoinable joinable)||lord.LordJob is LordJob_Ritual)continue;
                bool canJoin;
                using(new RemotePawnScope(pawn,route.map,route.landing))
                using(new ProbeAudit(route.map,false))
                    canJoin=lord.CurLordToil.VoluntaryJoinDutyHookFor(pawn)==hook&&joinable.VoluntaryJoinPriorityFor(pawn)>0;
                if(!canJoin)continue;
                var job=Broker.Begin(pawn,route,Purpose.GroupTravel);
                if(job!=null){var i=Broker.For(pawn);i.destinationLord=lord;i.needNode="SAB.Social";}
                return job;
            }
            return null;
        }
        public static void BeforeTransfer(Pawn pawn)
        {
            var i=Broker.For(pawn);
            if(i?.purpose==Purpose.GroupTravel&&pawn.GetLord()!=null&&pawn.GetLord()!=i.destinationLord)
                pawn.GetLord().Notify_PawnLost(pawn,PawnLostCondition.ExitedMap);
        }
        public static Job Arrive(Intent i)
        {
            var lord=i.destinationLord;
            if(lord!=null)
            {
                if(lord.Map!=i.pawn.Map||!lord.Map.lordManager.lords.Contains(lord))return null;
                if(lord.LordJob is LordJob_VoluntarilyJoinable joinable&&joinable.VoluntaryJoinPriorityFor(i.pawn)<=0)return null;
                if(i.pawn.GetLord()!=lord){i.pawn.GetLord()?.Notify_PawnLost(i.pawn,PawnLostCondition.LeftVoluntarily);lord.AddPawn(i.pawn);}
                var wait=JobMaker.MakeJob(JobDefOf.Wait);wait.expiryInterval=30;return wait;
            }
            if(i.target is Pawn leader&&leader.Spawned&&leader.Map==i.pawn.Map)
            {var job=JobMaker.MakeJob(JobDefOf.FollowClose,leader);job.expiryInterval=140;job.checkOverrideOnExpire=true;job.followRadius=5;return job;}
            return null;
        }
    }
    [HarmonyPatch(typeof(JobGiver_AIFollowPawn),"TryGiveJob")]
    public static class LinkedFollowLeader
    {
        public static void Postfix(JobGiver_AIFollowPawn __instance,Pawn pawn,ref Job __result)
        {
            if(__result!=null||RemotePawnScope.Active||Manager.Current==null)return;
            var leader=AccessTools.Method(__instance.GetType(),"GetFollowee").Invoke(__instance,new object[]{pawn}) as Pawn;
            __result=GroupTransit.Follow(pawn,leader);
        }
    }
    [HarmonyPatch(typeof(ThinkNode_JoinVoluntarilyJoinableLord),nameof(ThinkNode_JoinVoluntarilyJoinableLord.TryIssueJobPackage))]
    public static class LinkedSocialGathering
    {
        public static void Postfix(ThinkNode_JoinVoluntarilyJoinableLord __instance,Pawn pawn,ref ThinkResult __result)
        {if(!__result.IsValid){var job=GroupTransit.Social(pawn,__instance.dutyHook);if(job!=null)__result=new ThinkResult(job,__instance);}}
    }
    public static class EmergencyTransit
    {
        public static Job Flee(Pawn pawn,Job local)
        {
            if(local==null||RemotePawnScope.Active||Manager.Current==null||Broker.For(pawn)!=null||pawn.Faction!=Faction.OfPlayer||pawn.Drafted||pawn.Downed)return null;
            using(new TransitPolicy(pawn,TravelPermission.Emergency|(pawn.MentalStateDef==MentalStateDefOf.PanicFlee?TravelPermission.Mental:TravelPermission.None)))
            {
                if(!Manager.Current.CanScan(pawn,Purpose.Evacuate,true))return null;
                var route=Graph.Reachable(pawn).FirstOrDefault(r=>Portal.Safe(r.map)&&pawn.Position.DistanceTo(r.portals[0].Position)<=18&&r.landing.GetDangerFor(pawn,r.map)==Danger.None);
                return route==null?null:Broker.Begin(pawn,route,Purpose.Evacuate,cell:route.landing);
            }
        }
    }
    [HarmonyPatch]
    public static class LinkedFleeToSafety
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(var name in new[]{"RimWorld.JobGiver_FleeDanger","RimWorld.JobGiver_FleeFire","RimWorld.JobGiver_FleeImmediateThreat","RimWorld.JobGiver_AnimalFlee"})
            {var t=AccessTools.TypeByName(name);var m=t==null?null:AccessTools.DeclaredMethod(t,"TryGiveJob");if(m!=null)yield return m;}
        }
        public static void Postfix(Pawn pawn,ref Job __result)
        {var travel=EmergencyTransit.Flee(pawn,__result);if(travel!=null){JobMaker.ReturnToPool(__result);__result=travel;}}
    }
}
