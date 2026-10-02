using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 public static class PrisonerTransit
 {
  public static JobDef ReleaseDef=>DefDatabase<JobDef>.GetNamed("SAB_ReleaseAfterTransit");
  public static Job Bed(Pawn p,ThinkResult local)
  {
   if(RemotePawnScope.Active||Manager.Current==null||!BunkerMod.Settings.work||p.workSettings==null)return null;
   var givers=p.workSettings.WorkGiversInOrderNormal;var giver=givers.FirstOrDefault(g=>g is WorkGiver_Warden_TakeToBed);
   if(giver==null||!p.workSettings.WorkIsActive(giver.def.workType)||giver.MissingRequiredCapacity(p)!=null)return null;
   if(local.IsValid&&(local.Job.workGiverDef==null||givers.FindIndex(g=>g.def==local.Job.workGiverDef)<=givers.IndexOf(giver)))return null;
   if(!Manager.Current.CanScan(p,Purpose.FetchPassenger,false,"prisonerBed"))return null;
   var sources=new List<Route>{new Route{map=p.Map,landing=p.Position}};sources.AddRange(Graph.Reachable(p));
   foreach(var source in sources)
   foreach(var prisoner in source.map.mapPawns.PrisonersOfColonySpawned)
   {
    if(prisoner.InMentalState||Manager.Current.Claimed(prisoner))continue;
    Building_Bed bed;
    using(new RemotePawnScope(p,source.map,source.landing))
    {
     if(!(bool)AccessTools.Method(typeof(WorkGiver_Warden),"ShouldTakeCareOfPrisoner").Invoke(giver,new object[]{p,prisoner,false}))continue;
     bed=prisoner.ownership.OwnedBed;
     if(bed?.Map==source.map||bed==null)
     {
      if(!prisoner.Downed||prisoner.InBed()||!HealthAIUtility.ShouldSeekMedicalRest(prisoner))continue;
      bed=PawnTransit.FindBed(p,prisoner,true);
     }
    }
    if(bed==null||bed.Map==source.map)continue;
    var native=JobMaker.MakeJob(prisoner.Downed?JobDefOf.TakeWoundedPrisonerToBed:JobDefOf.EscortPrisonerToBed,prisoner,bed);native.count=1;native.workGiverDef=giver.def;
    var trip=PawnTransit.Plan(p,prisoner,bed,native);if(trip!=null)return trip;JobMaker.ReturnToPool(native);
   }
   return null;
  }
  public static Job Plan(Pawn p,ThinkResult local)
  {
   if(RemotePawnScope.Active||Manager.Current==null||!BunkerMod.Settings.work||p.workSettings==null)return null;
   var givers=p.workSettings.WorkGiversInOrderNormal;var giver=givers.FirstOrDefault(g=>g is WorkGiver_Warden_ReleasePrisoner);
   if(giver==null||!p.workSettings.WorkIsActive(giver.def.workType)||giver.MissingRequiredCapacity(p)!=null)return null;
   if(local.IsValid&&(local.Job.workGiverDef==null||givers.FindIndex(g=>g.def==local.Job.workGiverDef)<=givers.IndexOf(giver)))return null;
   if(!Manager.Current.CanScan(p,Purpose.FetchPassenger))return null;
   var routes=Graph.Reachable(p);var sources=new List<Route>{new Route{map=p.Map,landing=p.Position}};sources.AddRange(routes);
   foreach(var source in sources.Where(r=>!r.map.CanEverExit))
   foreach(var prisoner in source.map.mapPawns.PrisonersOfColonySpawned)
   {
    if(prisoner.Downed||prisoner.InMentalState||prisoner.guest.Released||!prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Release)||Manager.Current.Claimed(prisoner))continue;
    using(new RemotePawnScope(p,source.map,source.landing))
     if(!(bool)AccessTools.Method(typeof(WorkGiver_Warden),"ShouldTakeCareOfPrisoner").Invoke(giver,new object[]{p,prisoner,false}))continue;
    foreach(var destination in sources.Where(r=>r.map.CanEverExit))
    {
     var marker=destination.map.listerThings.AllThings.OfType<AncientHatch>().FirstOrDefault(h=>Portal.Other(h)!=null);
     if(marker==null)continue;
     var native=JobMaker.MakeJob(ReleaseDef,prisoner);native.workGiverDef=giver.def;
     var travel=PawnTransit.Plan(p,prisoner,marker,native);if(travel!=null)return travel;JobMaker.ReturnToPool(native);
    }
   }
   return null;
  }
  public static Job Arrive(Pawn carrier,Pawn prisoner,Job job)
  {
   if(!carrier.Map.CanEverExit||prisoner.Downed||prisoner.InMentalState||!prisoner.IsPrisonerOfColony||
    !prisoner.guest.IsInteractionEnabled(PrisonerInteractionModeDefOf.Release))return null;
   // The native release-cell query needs a spawned prisoner. Drop safely first;
   // the dedicated continuation preserves source-prison eligibility already checked.
   if(!carrier.carryTracker.TryDropCarriedThing(carrier.Position,ThingPlaceMode.Near,out _))return null;
   if(!RCellFinder.TryFindPrisonerReleaseCell(prisoner,carrier,out var cell))return null;
   job.targetB=cell;job.count=1;return job;
  }
 }
 public class JobDriver_ReleaseAfterTransit:JobDriver
 {
  Pawn Prisoner=>job.targetA.Pawn;
  public override bool TryMakePreToilReservations(bool errorOnFailed)=>pawn.Reserve(job.targetA,job,1,-1,null,errorOnFailed);
  protected override IEnumerable<Toil> MakeNewToils()
  {
   this.FailOn(()=>Prisoner==null||Prisoner.Dead||Prisoner.Downed||Prisoner.InMentalState||!Prisoner.IsPrisonerOfColony||
    Prisoner.guest.IsInteractionDisabled(PrisonerInteractionModeDefOf.Release)||!pawn.Map.CanEverExit);
   yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.ClosestTouch);
   yield return Toils_Haul.StartCarryThing(TargetIndex.A);
   var carry=Toils_Haul.CarryHauledThingToCell(TargetIndex.B);yield return carry;
   yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.B,carry,false);
   yield return Toils_General.Do(()=>
   {
    var released=Prisoner;GenGuest.PrisonerRelease(released);
    if(!PawnBanishUtility.WouldBeLeftToDie(released,released.Map.Tile))GenGuest.AddHealthyPrisonerReleasedThoughts(released);
    QuestUtility.SendQuestTargetSignals(released.questTags,"Released",released.Named("SUBJECT"));
   });
  }
 }
}
