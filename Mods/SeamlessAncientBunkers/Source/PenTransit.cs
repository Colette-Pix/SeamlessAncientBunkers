using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 [HarmonyPatch(typeof(AnimalPenUtility),nameof(AnimalPenUtility.NeedsToBeManagedByRope))]
 public static class BunkerPenEligibility
 {
  public static void Postfix(Pawn pawn,ref bool __result)
  {
   if(!__result&&BunkerMod.Settings.enabled&&pawn?.Spawned==true&&pawn.Faction==Faction.OfPlayer&&
    pawn.Roamer&&AnimalPenUtility.IsRopeManagedAnimalDef(pawn.def)&&pawn.Map.IsPocketMap&&
    BunkerLevels.Surface(pawn.Map)?.IsPlayerHome==true)__result=true;
  }
 }
 public static class PenTransit
 {
  public static bool Valid(Pawn carrier,Pawn animal,Thing destination)
  {
   var pen=destination?.TryGetComp<CompAnimalPenMarker>();
   return pen!=null&&pen.PenState.Enclosed&&pen.AcceptsToPen(animal)&&
    (bool)AccessTools.Method(typeof(AnimalPenUtility),"CanUseAndReach").Invoke(null,new object[]{animal,pen,false,carrier});
  }
  public static Job Plan(Pawn p,ThinkResult local)
  {
   if(RemotePawnScope.Active||!BunkerMod.Settings.work||p.workSettings==null||Broker.For(p)!=null)return null;
   var givers=p.workSettings.WorkGiversInOrderNormal;
   var giver=givers.FirstOrDefault(g=>g.GetType()==typeof(WorkGiver_TakeToPen));
   if(giver==null||p.workSettings.GetPriority(giver.def.workType)<=0||giver.MissingRequiredCapacity(p)!=null)return null;
   if(local.IsValid&&(local.Job.workGiverDef==null||givers.FindIndex(g=>g.def==local.Job.workGiverDef)<=givers.IndexOf(giver)))return null;
   if(!Manager.Current.CanScan(p,Purpose.Passenger))return null;
   var maps=new List<Route>{new Route{map=p.Map,landing=p.Position}};maps.AddRange(Graph.Reachable(p));
   foreach(var source in maps)
   foreach(var animal in FairScan.Window(source.map.mapPawns.SpawnedColonyAnimals,BunkerMod.Settings.maxCandidates,FairScan.Key(p,"unPenned",source.map)))
   {
    if(!AnimalPenUtility.NeedsToBeManagedByRope(animal)||animal.roping.IsRoped||(animal.InMentalState&&animal.MentalStateDef!=MentalStateDefOf.Roaming)||animal.Downed||
     animal.Map.designationManager.DesignationOn(animal,DesignationDefOf.ReleaseAnimalToWild)!=null||
     AnimalPenUtility.GetCurrentPenOf(animal,false)!=null||Manager.Current.Claimed(animal))continue;
    using(new RemotePawnScope(p,source.map,source.landing))
     if(!WorkGiver_InteractAnimal.CanInteractWithAnimal(p,animal,out _,false,true,true,true)||!p.CanReserveAndReach(animal,PathEndMode.Touch,Danger.None))continue;
    foreach(var destination in maps.Where(r=>r.map!=source.map))
    {
     CompAnimalPenMarker pen;
     using(new RemotePawnScope(p,destination.map,destination.landing))
     using(new RemotePawnScope(animal,destination.map,destination.landing))
      pen=AnimalPenUtility.GetPenAnimalShouldBeTakenTo(p,animal,out _)??AnimalPenUtility.GetCurrentPenOf(animal,false);
     if(pen==null)continue;
     var native=JobMaker.MakeJob(JobDefOf.RopeToPen,animal,IntVec3.Invalid,pen.parent);native.workGiverDef=giver.def;
     var travel=PawnTransit.Plan(p,animal,pen.parent,native);
     if(travel!=null)return travel;JobMaker.ReturnToPool(native);
    }
   }
   return null;
  }
 }
}
