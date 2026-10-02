using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 public static class ExtraNeeds
 {
  public static readonly string[] Nodes={"RimWorld.JobGiver_TakeDrugsForDrugPolicy","RimWorld.JobGiver_SatisfyChemicalNeed","RimWorld.JobGiver_SatifyChemicalDependency","RimWorld.JobGiver_MoveDrugsToInventory","RimWorld.JobGiver_OptimizeApparel","DubsBadHygiene.JobGiver_DrinkWater",
   "RimWorld.JobGiver_GetHemogen","RimWorld.JobGiver_GetDeathrest","RimWorld.JobGiver_Meditate","RimWorld.JobGiver_Learn",
   "DubsBadHygiene.JobGiver_UseToilet","DubsBadHygiene.JobGiver_HaveWash"};
  public static Purpose Kind(ThinkNode_JobGiver node)=>node is JobGiver_OptimizeApparel?Purpose.Apparel:node.GetType().Namespace=="DubsBadHygiene"?Purpose.Thirst:Purpose.Drug;
  public static Job Query(ThinkNode_JobGiver node,Pawn pawn)=>
   (Job)AccessTools.Method(node.GetType(),"TryGiveJob").Invoke(node,new object[]{pawn});
  public static Job Plan(ThinkNode_JobGiver node,Pawn pawn,Job local,int apparelTick)
  {
   if(RemotePawnScope.Active||Manager.Current==null||Broker.For(pawn)!=null||!pawn.RaceProps.Humanlike)return null;
   bool deathrest=node is JobGiver_GetDeathrest,meditation=node.GetType()==typeof(JobGiver_Meditate);
   bool sanitation=node.GetType().Namespace=="DubsBadHygiene";
   bool assigned=local?.targetA.Thing is Building lb&&lb.GetAssignedPawn()==pawn;
   if(local!=null&&!(deathrest&&!assigned)&&!(meditation&&!assigned)&&!(sanitation&&!local.targetA.HasThing))return null;
   var purpose=Kind(node);
   if(purpose==Purpose.Apparel&&apparelTick>Manager.Now)return null;
   if(!Manager.Current.CanScan(pawn,purpose,purpose==Purpose.Thirst||deathrest||node is JobGiver_GetHemogen))return null;
   Route best=null;Thing target=null;IntVec3 cell=IntVec3.Invalid;float bestCost=float.MaxValue;
   foreach(var route in Graph.Reachable(pawn).Where(r=>!Broker.Blacklisted(pawn,r.map)))
   using(new RemotePawnScope(pawn,route.map,route.landing))
   using(new ProbeAudit(route.map,false))
   {
    int savedTick=pawn.mindState.nextApparelOptimizeTick;
    Job candidate=null;
    try
    {
     if(purpose==Purpose.Apparel)pawn.mindState.nextApparelOptimizeTick=Manager.Now;
     candidate=Query(node,pawn);
     if(candidate==null)continue;
     if((deathrest||sanitation)&&!candidate.targetA.HasThing)continue;
     if(node is JobGiver_Learn&&!candidate.targetA.HasThing)continue;
     if(meditation&&local!=null&&(!(candidate.targetA.Thing is Building meditationBuilding)||meditationBuilding.GetAssignedPawn()!=pawn))continue;
     if(deathrest&&local?.targetA.Thing is Building_Bed&&(!(candidate.targetA.Thing is Building_Bed restBed)||restBed.GetAssignedPawn()!=pawn))continue;
     if(purpose==Purpose.Apparel&&candidate.def!=JobDefOf.Wear)continue;
     var thing=candidate.targetA.Thing;var pos=candidate.targetA.Cell;
     if(thing!=null&&(thing.Map!=route.map||!thing.Spawned||thing.IsForbidden(pawn)||thing.IsBurning()))continue;
     if(!pos.IsValid||!pos.InBounds(route.map)||pos.Fogged(route.map)||!Portal.Allowed(pawn,route.map,pos)||!pawn.CanReach(candidate.targetA,PathEndMode.Touch,Danger.None)||Broker.Claimed(pawn,route.map,thing,pos))continue;
     float cost=route.cost+route.landing.DistanceTo(pos);
     if(cost<bestCost){best=route;target=thing;cell=pos;bestCost=cost;}
    }
    finally{pawn.mindState.nextApparelOptimizeTick=savedTick;if(candidate!=null)JobMaker.ReturnToPool(candidate);}
   }
   if(best==null)return null;
   var travel=Broker.Begin(pawn,best,purpose,target,cell);
   if(travel!=null)Broker.For(pawn).needNode=node.GetType().FullName;
   return travel;
  }
  public static Job Arrive(Intent intent)
  {
   var type=AccessTools.TypeByName(intent.needNode);
   if(type==null||!Nodes.Any(n=>AccessTools.TypeByName(n)?.IsAssignableFrom(type)==true))return null;
   if(intent.purpose==Purpose.Apparel)intent.pawn.mindState.nextApparelOptimizeTick=Manager.Now;
   return Query((ThinkNode_JobGiver)Activator.CreateInstance(type),intent.pawn);
  }
 }
 [HarmonyPatch]
 public static class ExtraNeedPatch
 {
  public static IEnumerable<MethodBase> TargetMethods()=>ExtraNeeds.Nodes.Select(AccessTools.TypeByName).Where(t=>t!=null).Select(t=>AccessTools.Method(t,"TryGiveJob")).Where(m=>m!=null).Distinct();
  public static void Prefix(Pawn pawn,out int __state){__state=pawn.mindState.nextApparelOptimizeTick;}
  public static void Postfix(ThinkNode_JobGiver __instance,Pawn pawn,int __state,ref Job __result)
  {
   try{var job=ExtraNeeds.Plan(__instance,pawn,__result,__state);if(job!=null){if(__result!=null)JobMaker.ReturnToPool(__result);__result=job;}}
   catch(Exception e){Log.ErrorOnce("[SAB] Extra need route: "+e,1870060);}
  }
 }
}
