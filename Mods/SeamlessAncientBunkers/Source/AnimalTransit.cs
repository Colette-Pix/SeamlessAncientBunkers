using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 public static class AnimalTransit
 {
  public static bool CanHaul(Pawn p)=>AnimalSupport.Supported(p)&&!p.InMentalState&&!p.Downed&&
   p.training?.HasLearned(DefDatabase<TrainableDef>.GetNamed("Haul"))==true;
  public static Job Haul(Pawn p)
  {
   if(RemotePawnScope.Active||Manager.Current==null||!BunkerMod.Settings.haul||!CanHaul(p)||!Manager.Current.CanScan(p,Purpose.AnimalCargo))return null;
   var routes=Graph.Reachable(p);
   foreach(var item in FairScan.Window(p.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver),BunkerMod.Settings.maxCandidates,FairScan.Key(p,"animalCargo",p.Map)))
   {
    if(Manager.Current.Claimed(item)||item.IsForbidden(p)||!HaulAIUtility.PawnCanAutomaticallyHaulFast_NewTemp(p,item,false)||p.carryTracker.MaxStackSpaceEver(item.def)<=0)continue;
    var priority=StoreUtility.CurrentStoragePriorityOf(item);
    Hauling.FindStorage(p,item,p.Map,priority,out _,out var best);
    Route chosen=null;IntVec3 cell=IntVec3.Invalid;
    foreach(var route in routes)
    using(new RemotePawnScope(p,route.map,route.landing))
     if(Hauling.FindStorage(p,item,route.map,best,out var next,out var nextPriority)){chosen=route;cell=next;best=nextPriority;}
    if(chosen!=null)return Broker.Begin(p,chosen,Purpose.AnimalCargo,item,cell,count:Math.Min(item.stackCount,p.carryTracker.MaxStackSpaceEver(item.def)));
   }
   return null;
  }
 }
 [HarmonyPatch(typeof(JobGiver_Haul),"TryGiveJob")]
 public static class LinkedAnimalHaul
 {public static void Postfix(Pawn pawn,ref Job __result){if(__result==null)__result=AnimalTransit.Haul(pawn);}}
 [HarmonyPatch(typeof(JobGiver_RescueNearby),"TryGiveJob")]
 public static class LinkedAnimalRescue
 {public static void Postfix(Pawn pawn,ref Job __result){if(__result==null&&pawn.RaceProps.Animal)__result=PawnTransit.Rescue(pawn,ThinkResult.NoJob);}}
}
