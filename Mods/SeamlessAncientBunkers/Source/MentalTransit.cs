using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 public static class MentalTransit
 {
  public static bool Wandering(Pawn p)=>p?.MentalStateDef?.defName=="Wander_Sad"||p?.MentalStateDef?.defName=="Wander_Psychotic"||p?.MentalStateDef?.defName=="Wander_Psychotic_Short";
  public static bool Valid(Intent i)=>i.purpose!=Purpose.MentalWander||Wandering(i.pawn);
  public static Job Wander(Pawn p,Job local)
  {
   if(RemotePawnScope.Active||Manager.Current==null||!Wandering(p)||local?.def!=JobDefOf.GotoWander||Broker.For(p)!=null)return null;
   using(new TransitPolicy(p,TravelPermission.Mental))
   {
    if(!Manager.Current.CanScan(p,Purpose.MentalWander)||!Rand.Chance(0.15f))return null;
    var route=Graph.Reachable(p).FirstOrDefault(r=>r.portals.Count==1&&p.Position.DistanceTo(r.portals[0].Position)<=10&&!Broker.Blacklisted(p,r.map));
    return route==null?null:Broker.Begin(p,route,Purpose.MentalWander);
   }
  }
 }
 [HarmonyPatch(typeof(JobGiver_Wander),"TryGiveJob")]
 public static class ConnectedMentalWander
 {
  public static void Postfix(Pawn pawn,ref Job __result)
  {var travel=MentalTransit.Wander(pawn,__result);if(travel!=null){JobMaker.ReturnToPool(__result);__result=travel;}}
 }
 // Preserve the native rage target when it walks to another connected floor.
 // This does not grant unrestricted travel to unrelated mental states.
 [HarmonyPatch(typeof(MentalState_MurderousRage),nameof(MentalState_MurderousRage.IsTargetStillValidAndReachable))]
 public static class LinkedRageTarget
 {
  public static bool Prefix(MentalState_MurderousRage __instance,ref bool __result)
  {
   var pawn=__instance.pawn;var target=__instance.target;
   if(RemotePawnScope.Active||!BunkerMod.Settings.enabled||pawn?.Spawned!=true||target?.Spawned!=true||target.Map==pawn.Map)return true;
   __result=false;
   var roots=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).ToList();
   var visited=new HashSet<Map>{pawn.Map};var pending=new Queue<Route>();pending.Enqueue(new Route{map=pawn.Map,landing=pawn.Position});
   while(pending.Count>0)
   {
    var at=pending.Dequeue();
    foreach(var root in roots)
    {
     MapPortal link=root.Map==at.map?root:root.exit;var other=Portal.Other(link);
     if(link?.Map!=at.map||other==null||visited.Contains(other.Map))continue;
     using(new RemotePawnScope(pawn,at.map,at.landing))if(!EnemyPursuit.CanCross(pawn,link))continue;
     if(!EnemyPursuit.Landing(pawn,other,out var landing))continue;
     visited.Add(other.Map);
     if(other.Map==target.Map)
     {using(new RemotePawnScope(pawn,other.Map,landing))__result=pawn.CanReach(target,PathEndMode.Touch,Danger.Deadly,true);return false;}
     pending.Enqueue(new Route{map=other.Map,landing=landing});
    }
   }
   return false;
  }
 }
 [HarmonyPatch(typeof(JobGiver_MurderousRage),"TryGiveJob")]
 public static class LinkedRageJob
 {
  public static void Postfix(Pawn pawn,ref Job __result)
  {
   if(RemotePawnScope.Active||__result?.targetA.Thing?.Map==pawn.Map)return;
   var job=EnemyPursuit.Plan(pawn,__result);
   if(__result!=null)JobMaker.ReturnToPool(__result);
   __result=job;
  }
 }
}
