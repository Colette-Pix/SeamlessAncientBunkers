using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 public static class Hauling
 {
  public static bool Capable(Pawn p)=>p!=null&&!p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)&&!p.IsWorkTypeDisabledByAge(WorkTypeDefOf.Hauling,out _)&&p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation);
  public static bool CanHaul(Pawn p)=>p.workSettings?.GetPriority(WorkTypeDefOf.Hauling)>0&&Capable(p);
  const string Fetch="SAB.HaulFetch";
  public static bool IsFetch(Intent i)=>i?.needNode==Fetch;
  public static bool FetchEnabled(Intent i)=>i?.pawn!=null&&BunkerMod.Settings.haul&&CanHaul(i.pawn)&&
   i.target?.Destroyed==false&&ServicesInventory.Holder(i.target)?.Map==i.finalMap;
  static bool ItemUsable(Pawn p,Thing item,bool forced)=>item!=null&&item.def.EverHaulable&&!(item is Pawn)&&
   !(item is Genepack&&!item.Spawned)&&p.carryTracker.MaxStackSpaceEver(item.def)>0&&ConsumerSupply.Free(p,item)&&
   (!item.Spawned||HaulAIUtility.PawnCanAutomaticallyHaul(p,item,forced));
  // Called while the pawn is physically or query-projected onto the item's map.
  // Native local storage (including containers) retains equal-priority preference.
  public static Route Storage(Pawn p,Thing item,bool forced,out IntVec3 cell,out bool localAvailable,List<Route> routes=null,bool exhaustive=false)
  {
   cell=IntVec3.Invalid;localAvailable=false;if(item?.MapHeld!=p.Map)return null;
   var priority=StoreUtility.CurrentStoragePriorityOf(item,forced);
   if(StoreUtility.TryFindBestBetterStorageFor(item,p,p.Map,priority,p.Faction,out var localCell,out var local))
   {localAvailable=true;cell=localCell.IsValid?localCell:(local as Thing)?.Position??IntVec3.Invalid;priority=local.GetStoreSettings().Priority;}
   Route best=null;
   foreach(var route in routes??Graph.Reachable(p,!forced))
   using(new RemotePawnScope(p,route.map,route.landing))
    if(FindStorage(p,item,route.map,priority,out var candidate,out var found,forced||exhaustive))
    {best=route;cell=candidate;priority=found;}
   return best;
  }
  public static Job StartHaul(Pawn p,Thing item,bool forced=false)
  {
   if(!BunkerMod.Settings.enabled||!(forced?Capable(p):BunkerMod.Settings.haul&&CanHaul(p))||
    item?.Destroyed!=false||p.carryTracker.CarriedThing!=null)return null;
   if(item.MapHeld!=p.Map)
   {
    Route source=null;
    foreach(var candidate in Graph.Reachable(p,!forced).Where(r=>r.map==item.MapHeld))
    using(new RemotePawnScope(p,candidate.map,candidate.landing))
    {
     if(!ItemUsable(p,item,forced))continue;
     var destination=Storage(p,item,forced,out _,out bool local,exhaustive:true);
     if(destination!=null||local){source=candidate;break;}
    }
    if(source==null)return null;
    var fetch=Broker.Begin(p,source,Purpose.Work,item,forced:forced);
    if(fetch!=null)Broker.For(p).needNode=Fetch;
    return fetch;
   }
   if(!ItemUsable(p,item,forced))return null;
   // Once a candidate is selected, revalidate all storage cells. A second bounded
   // window could skip the very cell found by the planning query and lose the job.
   var route=Storage(p,item,forced,out var cell,out bool localAvailable,exhaustive:true);
   if(route==null)return localAvailable?HaulAIUtility.HaulToStorageJob(p,item,forced):null;
   return Broker.Begin(p,route,Purpose.Haul,item,cell,forced:forced,count:Math.Min(item.stackCount,p.carryTracker.MaxStackSpaceEver(item.def)));
  }
  public static Job Arrive(Intent i)=>StartHaul(i.pawn,i.target,i.forced);
  public static bool FindStorage(Pawn p,Thing item,Map map,StoragePriority above,out IntVec3 cell,out StoragePriority priority,bool exhaustive=false)
  {
   cell=IntVec3.Invalid;priority=above;
   var groups=map.haulDestinationManager.AllGroupsListInPriorityOrder.Where(group=>group.Settings.Priority>above&&
    group.parent.HaulDestinationEnabled&&group.Settings.AllowedToAccept(item)&&group.parent.Accepts(item)&&
    (!(group.parent is Thing owner)||owner.Faction==p.Faction)).ToList();
   int perGroup=Math.Max(1,BunkerMod.Settings.maxCandidates/Math.Max(1,groups.Count));
   foreach(var group in groups)
   {
    var cells=group.CellsList.OrderBy(c=>c.DistanceToSquared(p.Position));
    foreach(var c in exhaustive?cells:FairScan.Window(cells,perGroup,FairScan.Key(p,"storage:"+group.parent.GetHashCode(),map)))
    {
     // Passing a remote source item to vanilla's carrier search uses the wrong source coordinates.
     if(!Portal.Allowed(p,map,c)||c.Fogged(map)||Broker.Claimed(p,map,null,c)||!StoreUtility.IsGoodStoreCell(c,map,item,null,p.Faction)||!p.CanReserveAndReach(c,PathEndMode.ClosestTouch,Danger.None)) continue;
     cell=c;priority=group.Settings.Priority;return true;
    }
   }
   return false;
  }
  public static Job Plan(Pawn p,ThinkResult local)
  {
   if(!BunkerMod.Settings.haul||!CanHaul(p)||p.carryTracker.CarriedThing!=null||!Manager.Current.CanScan(p,Purpose.Haul)) return null;
   if(local.IsValid)
   {
    var ordered=p.workSettings.WorkGiversInOrderNormal;
    int localRank=ordered.FindIndex(g=>g.def==local.Job.workGiverDef),haulRank=ordered.FindIndex(g=>g is WorkGiver_HaulGeneral);
    if(localRank<0||haulRank<0||localRank<haulRank)return null;
   }
   var requested=ServiceDeliveries.Plan(p);if(requested!=null)return requested;
   var routes=Graph.Reachable(p).Where(r=>!Broker.Blacklisted(p,r.map)).ToList();
   var sources=new List<Route>{new Route{map=p.Map,landing=p.Position}};sources.AddRange(routes);
   Thing bestItem=null;float bestCost=float.MaxValue;
   float localCost=local.IsValid&&local.Job.targetA.IsValid&&local.Job.targetB.IsValid?
    p.Position.DistanceTo(local.Job.targetA.Cell)+local.Job.targetA.Cell.DistanceTo(local.Job.targetB.Cell):float.MaxValue;
   int quota=Math.Max(1,BunkerMod.Settings.maxCandidates/sources.Count);
   foreach(var source in sources)
   using(new RemotePawnScope(p,source.map,source.landing))
   {
    var onward=source.portals.Count==0?routes:Graph.Reachable(p).Where(r=>!Broker.Blacklisted(p,r.map)).ToList();
    // Reserve part of the bounded query for nearby stock so a newly dropped item
    // does not wait behind a whole map of distant rocks. The remainder advances
    // in stable identity order, and wandering cannot reset its stream.
    var stock=ServicesInventory.Items(source.map).ToList();
    var nearby=stock.OrderBy(t=>t.PositionHeld.DistanceToSquared(source.landing)).Take(quota/2).ToList();
    foreach(var item in nearby.Concat(FairScan.Window(stock.Where(t=>!nearby.Contains(t)).OrderBy(t=>t.thingIDNumber),quota-nearby.Count,
     FairScan.Key(p,"hauling:"+(source.portals.LastOrDefault()?.thingIDNumber??-1),source.map))))
    {
     if(Manager.Current.Claimed(item)||!ItemUsable(p,item,false))continue;
     var destination=Storage(p,item,false,out var cell,out bool localAvailable,onward);
     if(destination==null&&(!localAvailable||source.portals.Count==0))continue;
     float cost=source.cost+source.landing.DistanceTo(item.PositionHeld)+
      (destination==null?item.PositionHeld.DistanceTo(cell):destination.cost+destination.landing.DistanceTo(cell));
     if(cost<bestCost&&(!local.IsValid||cost+12<localCost)){bestItem=item;bestCost=cost;}
    }
   }
   if(bestItem!=null)return StartHaul(p,bestItem);
   return Supply(p,routes);
  }
  private static Job Supply(Pawn p,List<Route> routes)=>ConsumerSupply.ForHauler(p,routes);
  public static Job Delivery(Intent i)
  {
   if(ConsumerSupply.IsSupply(i))return ConsumerSupply.Delivery(i);
   var p=i.pawn;var item=p.carryTracker.CarriedThing;
   if(item==null)return null;
   var cell=i.cell;
   bool storage=cell.IsValid&&cell.GetSlotGroup(p.Map)?.Settings.AllowedToAccept(item)==true&&cell.GetSlotGroup(p.Map).parent.Accepts(item);
   if(!cell.IsValid||!Portal.Allowed(p,p.Map,cell)||!StoreUtility.IsGoodStoreCell(cell,p.Map,item,p,p.Faction)||
      (i.purpose==Purpose.Haul&&!storage))
   {
    if(!FindStorage(p,item,p.Map,StoragePriority.Unstored,out cell,out _)) {p.carryTracker.TryDropCarriedThing(p.Position,ThingPlaceMode.Near,out _);return null;}
    storage=true;
   }
   var job=JobMaker.MakeJob(JobDefOf.HaulToCell,item,cell);
   job.count=item.stackCount;job.haulMode=storage?HaulMode.ToCellStorage:HaulMode.ToCellNonStorage;
   return job;
  }
 }
 public class JobDriver_Collect:JobDriver
 {
  public override bool TryMakePreToilReservations(bool errorOnFailed)=>pawn.inventory?.Contains(job.targetA.Thing)==true||
   (!job.targetA.Thing.Spawned&&ServicesInventory.Holder(job.targetA.Thing) is Thing holder?pawn.Reserve(holder,job,1,-1,null,errorOnFailed):pawn.Reserve(job.targetA,job,1,job.count,null,errorOnFailed));
  protected override IEnumerable<Toil> MakeNewToils()
  {
   this.FailOn(()=>Broker.For(pawn)==null||!BunkerMod.Settings.enabled||(ConsumerSupply.IsSupply(Broker.For(pawn))?!ConsumerSupply.Enabled(Broker.For(pawn)):LinkedLogistics.IsLogistics(Broker.For(pawn))?!LinkedLogistics.Enabled(Broker.For(pawn)):
    Broker.For(pawn)?.purpose==Purpose.AnimalCargo?!AnimalTransit.CanHaul(pawn):(Broker.For(pawn)?.purpose!=Purpose.Dining&&!(Broker.For(pawn)?.forced==true?Hauling.Capable(pawn):Hauling.CanHaul(pawn)))));
   if(pawn.inventory?.Contains(job.targetA.Thing)==true)yield return Toils_Misc.TakeItemFromInventoryToCarrier(pawn,TargetIndex.A);
   else if(!job.targetA.Thing.Spawned&&ServicesInventory.Holder(job.targetA.Thing) is Thing holder)
   {
    job.targetB=holder;
    this.FailOnDespawnedNullOrForbidden(TargetIndex.B);
    yield return Toils_Goto.GotoThing(TargetIndex.B,PathEndMode.ClosestTouch);
    var extract=ToilMaker.MakeToil("SAB_ExtractStoredSupply");extract.defaultCompleteMode=ToilCompleteMode.Instant;
    extract.initAction=()=>{var item=job.targetA.Thing;if(ServicesInventory.Holder(item)!=holder||item.holdingOwner==null||
     item.holdingOwner.TryTransferToContainer(item,pawn.carryTracker.innerContainer,Math.Min(job.count,item.stackCount))<=0)EndJobWith(JobCondition.Incompletable);};
    yield return extract;
   }
   else
   {
    this.FailOn(()=>job.targetA.Thing.DestroyedOrNull()||job.targetA.Thing!=pawn.carryTracker.CarriedThing&&
     (!job.targetA.Thing.Spawned||job.targetA.Thing.IsForbidden(pawn)));
    yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.ClosestTouch);
    yield return Toils_Haul.StartCarryThing(TargetIndex.A);
   }
   var done=ToilMaker.MakeToil("SAB_Collected");done.defaultCompleteMode=ToilCompleteMode.Instant;
   done.initAction=()=>{var intent=Broker.For(pawn);if(intent!=null){intent.cargo=pawn.carryTracker.CarriedThing;intent.carrying=true;ServiceDeliveries.Collected(intent);}};
   yield return done;
  }
 }
}


