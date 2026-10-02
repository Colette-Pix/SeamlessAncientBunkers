using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
namespace SeamlessAncientBunkers
{
 public static class Dining
 {
  public static bool Seat(Pawn p,IntVec3 root,float radius,out IntVec3 cell)
  {
   cell=IntVec3.Invalid;
   foreach(var t in p.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial).Where(t=>t.def.building?.isSittable==true).OrderBy(t=>t.Position.DistanceToSquared(root)).Take(BunkerMod.Settings.maxCandidates))
   {
    if(t.Position.DistanceTo(root)>radius||t.Faction!=p.Faction||t.Fogged()||t.IsForbidden(p)||t.IsBurning()||!t.IsSociallyProper(p)||!p.CanReserveAndReach(t,PathEndMode.OnCell,Danger.None)||!Toils_Ingest.TryFindFreeSittingSpotOnThing(t,p,out var c)||!Portal.Allowed(p,p.Map,c))continue;
    if(!GenAdj.CardinalDirections.Any(d=>(c+d).InBounds(p.Map)&&(c+d).GetEdifice(p.Map)?.def.surfaceType==SurfaceType.Eat))continue;
    cell=c;return true;
   }
   return false;
  }
  public static Job Plan(Pawn p,Job ingest)
  {
   var food=ingest?.targetA.Thing;
   if(RemotePawnScope.Active||!p.RaceProps.Humanlike||ingest?.def!=JobDefOf.Ingest||food?.def.category!=ThingCategory.Item||food.def.IsDrug||food.def.ingestible.chairSearchRadius<=0||p.needs.food.CurCategory>=HungerCategory.UrgentlyHungry||Manager.Current==null||Broker.For(p)!=null)return null;
   if(!food.Spawned&&p.inventory?.Contains(food)!=true)return null;
   if(!Manager.Current.CanScan(p,Purpose.Dining))return null;
   if(Seat(p,food.Spawned?food.Position:p.Position,food.def.ingestible.chairSearchRadius,out _))return null;
   Route best=null;IntVec3 target=IntVec3.Invalid;float distance=float.MaxValue;
   foreach(var r in Graph.Reachable(p).Where(r=>!Broker.Blacklisted(p,r.map)))
    using(new RemotePawnScope(p,r.map,r.landing))
     if(Seat(p,r.landing,9999f,out var cell)&&!Broker.Claimed(p,r.map,null,cell))
     {float cost=r.cost+r.landing.DistanceTo(cell);if(cost<distance){best=r;target=cell;distance=cost;}}
   return best==null?null:Broker.Begin(p,best,Purpose.Dining,food,target,count:Math.Min(food.stackCount,Math.Max(1,ingest.count)));
  }
  public static Job Arrive(Intent i)
  {
   var food=i.pawn.carryTracker.CarriedThing;if(food==null)return null;
   var cell=i.cell;
   if(!Seat(i.pawn,cell,4f,out cell)){var fallback=JobMaker.MakeJob(JobDefOf.Ingest,food);fallback.count=Math.Min(food.stackCount,Math.Max(1,i.count));return fallback;}
   var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("SAB_Dine"),food,cell);job.count=Math.Max(1,i.count);return job;
  }
 }
 public class JobDriver_Dine:JobDriver
 {
  public override bool TryMakePreToilReservations(bool errorOnFailed)=>pawn.ReserveSittableOrSpot(job.targetB.Cell,job,errorOnFailed);
  protected override IEnumerable<Toil> MakeNewToils()
  {
   this.FailOn(()=>pawn.carryTracker.CarriedThing==null||!Portal.Allowed(pawn,pawn.Map,job.targetB.Cell));
   yield return Toils_Goto.GotoCell(TargetIndex.B,PathEndMode.OnCell);
   var eat=ToilMaker.MakeToil("SAB_EatAtTable");eat.defaultCompleteMode=ToilCompleteMode.Instant;
   eat.initAction=()=>{var ingest=JobMaker.MakeJob(JobDefOf.Ingest,pawn.carryTracker.CarriedThing);ingest.count=job.count;pawn.jobs.StartJob(ingest,JobCondition.Succeeded);};
   yield return eat;
  }
 }
}

