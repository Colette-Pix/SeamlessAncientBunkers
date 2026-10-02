using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 public class DeparturePiece:IExposable
 {
  public Thing thing;public int count,request;
  public void ExposeData(){Scribe_References.Look(ref thing,"thing");Scribe_Values.Look(ref count,"count");Scribe_Values.Look(ref request,"request");}
 }
 public class DepartureRow:IExposable
 {
  public List<DeparturePiece> pieces=new List<DeparturePiece>();
  public void ExposeData(){Scribe_Collections.Look(ref pieces,"pieces",LookMode.Deep);}
 }
 public class PendingDeparture:IExposable
 {
  public Map map;public List<Thing> transporters=new List<Thing>();public List<DepartureRow> rows=new List<DepartureRow>();
  public bool caravan;public PlanetTile start,destination;public IntVec3 meeting;public int expires;
  public void ExposeData()
  {Scribe_References.Look(ref map,"map");Scribe_Collections.Look(ref transporters,"transporters",LookMode.Reference);Scribe_Collections.Look(ref rows,"rows",LookMode.Deep);Scribe_Values.Look(ref caravan,"caravan");Scribe_Values.Look(ref start,"start");Scribe_Values.Look(ref destination,"destination");Scribe_Values.Look(ref meeting,"meeting");Scribe_Values.Look(ref expires,"expires");}
  public IEnumerable<DeparturePiece> Pieces=>rows.SelectMany(r=>r.pieces);
  public static PendingDeparture Create(Map map,List<TransferableOneWay> selections)
  {
   var plan=new PendingDeparture{map=map,expires=Manager.Now+60000};
   foreach(var selected in selections.Where(r=>r.CountToTransfer>0))
   {
    var row=new DepartureRow();int left=selected.CountToTransfer;
    foreach(var thing in selected.things.Where(t=>t!=null&&!t.Destroyed).OrderBy(t=>t.MapHeld==map?0:1))
    {int count=Math.Min(left,thing.stackCount);if(count<=0)break;row.pieces.Add(new DeparturePiece{thing=thing,count=count});left-=count;}
    if(left!=0)throw new InvalidOperationException("Selected departure supplies changed.");plan.rows.Add(row);
   }
   return plan;
  }
 }
 // Projection is limited to synchronous native dialog validation. Nothing is
 // spawned, reserved, transferred or allowed to tick while these raw fields differ.
 public sealed class DepartureQuery:IDisposable
 {
  static readonly AccessTools.FieldRef<Thing,sbyte> MapIndex=AccessTools.FieldRefAccess<Thing,sbyte>("mapIndexOrState");
  static readonly AccessTools.FieldRef<Thing,IntVec3> Position=AccessTools.FieldRefAccess<Thing,IntVec3>("positionInt");
  readonly List<(Thing thing,sbyte map,IntVec3 cell)> old=new List<(Thing,sbyte,IntVec3)>();
  public readonly bool remote;
  public static Route Arrival(Thing item,Map map)
  {
   if(item?.Spawned!=true)return null;
   if(item is Pawn pawn&&Portal.Eligible(pawn,true,true))
   {var direct=Graph.Reachable(pawn,false).FirstOrDefault(r=>r.map==map);if(direct!=null)return direct;}
   foreach(var worker in Find.Maps.SelectMany(m=>m.mapPawns.FreeColonistsSpawned).Where(p=>Portal.Eligible(p)&&Hauling.CanHaul(p)))
   {
    var source=worker.Map==item.Map?new Route{map=worker.Map,landing=worker.Position}:Graph.Reachable(worker).FirstOrDefault(r=>r.map==item.Map);
    if(source==null)continue;
    using(new RemotePawnScope(worker,source.map,source.landing))
    {
     if(!worker.CanReserveAndReach(item,PathEndMode.ClosestTouch,Danger.Some))continue;
     var route=Graph.Reachable(worker).FirstOrDefault(r=>r.map==map);if(route!=null)return route;
    }
   }
   return null;
  }
  public DepartureQuery(Map map,IEnumerable<Thing> things)
  {
   var projected=new List<(Thing thing,Route route)>();
   foreach(var item in things.Distinct().Where(t=>t!=null&&t.MapHeld!=map))
   {var route=Arrival(item,map);if(route==null)throw new InvalidOperationException("No available route for "+item.LabelShort+" to the departure floor.");projected.Add((item,route));}
   remote=projected.Count>0;
   try{foreach(var pair in projected){old.Add((pair.thing,MapIndex(pair.thing),Position(pair.thing)));MapIndex(pair.thing)=(sbyte)Find.Maps.IndexOf(map);Position(pair.thing)=pair.route.landing;}}
   catch{Dispose();throw;}
  }
  public void Dispose(){for(int n=old.Count-1;n>=0;n--){MapIndex(old[n].thing)=old[n].map;Position(old[n].thing)=old[n].cell;}old.Clear();}
 }
 public static class Departures
 {
  public static bool Executing;
  public static DepartureStaging Current=>Verse.Current.Game.GetComponent<DepartureStaging>();
  public static IEnumerable<Thing> Selected(List<TransferableOneWay> rows)=>rows.Where(r=>r.CountToTransfer>0).SelectMany(r=>r.things);
  public static void Queue(PendingDeparture plan)
  {
   foreach(var old in Current.pending.Where(p=>plan.caravan&&p.caravan&&p.map==plan.map||p.transporters.Intersect(plan.transporters).Any()).ToList())Cancel(old,false);
   Current.pending.Add(plan);
   Messages.Message("Departure confirmed. Selected passengers and supplies will gather through the entrances, then loading will continue automatically.",MessageTypeDefOf.NeutralEvent,false);
  }
  public static bool Protect(Thing thing)=>Verse.Current.Game!=null&&Current?.pending?.Any(p=>p.Pieces.Any(q=>q.thing==thing||q.request>0&&ServiceDeliveries.Current.Receipts(q.request).Any(r=>r.item==thing)))==true;
  public static void Cancel(PendingDeparture plan,bool notify)
  {
   Current.pending.Remove(plan);
   foreach(var piece in plan.Pieces)
   {
    if(piece.request>0)ServiceDeliveries.Current.Release(piece.request);
    if(piece.thing is Pawn pawn&&Broker.For(pawn)?.purpose==Purpose.Gathering)Broker.Cancel(Broker.For(pawn));
   }
   if(notify)Messages.Message("Departure gathering cancelled: a selected passenger, supply or route became unavailable.",MessageTypeDefOf.RejectInput,false);
  }
  public static void CancelAll(){foreach(var plan in Current.pending.ToList())Cancel(plan,false);}
  public static void Tick()
  {
   foreach(var plan in Current.pending.ToList())
   try
   {
    if(!BunkerMod.Settings.enabled||plan.map==null||!Find.Maps.Contains(plan.map)||Manager.Now>plan.expires||plan.transporters.Any(t=>t?.Spawned!=true||t.Map!=plan.map)){Cancel(plan,true);continue;}
    bool waiting=false,failed=false;var restored=new List<TransferableOneWay>();
    var marker=plan.transporters.FirstOrDefault()??plan.map.listerThings.AllThings.OfType<MapPortal>().FirstOrDefault(p=>Portal.Other(p)!=null);
    if(marker==null){Cancel(plan,true);continue;}
    foreach(var row in plan.rows)
    {
     var transferable=new TransferableOneWay();int count=0;
     foreach(var piece in row.pieces)
     {
      count+=piece.count;
      if(piece.request>0)
      {
       if(ServiceDeliveries.Current.IsPending(piece.request)){waiting=true;continue;}
       var receipts=ServiceDeliveries.Current.Receipts(piece.request);
       if(!ServiceDeliveries.Current.IsComplete(piece.request)||receipts.Sum(r=>r.count)<piece.count||receipts.Any(r=>r.item==null||r.item.Destroyed||r.item.MapHeld!=plan.map)){failed=true;break;}
       foreach(var receipt in receipts){if(!receipt.item.Spawned)waiting=true;if(!transferable.things.Contains(receipt.item))transferable.things.Add(receipt.item);}
       continue;
      }
      var thing=piece.thing;
      if(thing==null||thing.Destroyed||thing.stackCount<piece.count){failed=true;break;}
      if(thing.MapHeld==plan.map){transferable.things.Add(thing);continue;}
      waiting=true;
      if(thing is Pawn passenger)
      {
       if(passenger.Dead||passenger.Drafted||passenger.InMentalState){failed=true;break;}
       if(!passenger.Spawned||Broker.For(passenger)!=null)continue;
       var route=Gatherings.RouteTo(passenger,plan.map);
       if(route!=null){var job=Broker.Begin(passenger,route,Purpose.Gathering,forced:true);if(job!=null)passenger.jobs.TryTakeOrderedJob(job);continue;}
       bool planned=false;
       foreach(var carrier in Find.Maps.SelectMany(m=>m.mapPawns.FreeColonistsSpawned).Where(p=>Portal.Eligible(p)&&Hauling.CanHaul(p)&&Broker.For(p)==null))
       {
        var native=JobMaker.MakeJob(JobDefOf.Wait);native.expiryInterval=1;var job=PawnTransit.Plan(carrier,passenger,marker,native,true);
        if(job!=null){carrier.jobs.TryTakeOrderedJob(job);planned=true;break;}JobMaker.ReturnToPool(native);
       }
       if(!planned&&DepartureQuery.Arrival(passenger,plan.map)==null)failed=true;
      }
      else
      {
       if(DepartureQuery.Arrival(thing,plan.map)==null){failed=true;break;}
       piece.request=ServiceDeliveries.Current.Add(thing,piece.count,destination:plan.map,cell:marker.Position);
      }
     }
     if(failed)break;
     if(!waiting){if(transferable.MaxCount<count){failed=true;break;}transferable.AdjustTo(count);}restored.Add(transferable);
    }
    if(failed){Cancel(plan,true);continue;}if(waiting)continue;
    bool accepted;
    try
    {
     Executing=true;
     if(plan.caravan)
     {
      var dialog=new Dialog_FormCaravan(plan.map,designatedMeetingPoint:plan.meeting);dialog.transferables=restored;
      AccessTools.Field(typeof(Dialog_FormCaravan),"startingTile").SetValue(dialog,plan.start);AccessTools.Field(typeof(Dialog_FormCaravan),"destinationTile").SetValue(dialog,plan.destination);
      accepted=(bool)AccessTools.Method(typeof(Dialog_FormCaravan),"TryFormAndSendCaravan").Invoke(dialog,null);
     }
     else
     {
      var dialog=new Dialog_LoadTransporters(plan.map,plan.transporters.Select(t=>t.TryGetComp<CompTransporter>()).ToList());
      AccessTools.Field(typeof(Dialog_LoadTransporters),"transferables").SetValue(dialog,restored);
      accepted=(bool)AccessTools.Method(typeof(Dialog_LoadTransporters),"TryAccept").Invoke(dialog,null);
     }
    }
    finally{Executing=false;}
    Cancel(plan,!accepted);
   }
   catch(Exception e){Cancel(plan,true);Log.ErrorOnce("[SAB] Departure gathering cancelled: "+e,1870072);}
  }
 }
 [HarmonyPatch(typeof(Thing),nameof(Thing.CanStackWith))]
 public static class PreserveDepartureSelection
 {public static bool Prefix(Thing __instance,Thing other,ref bool __result){if(__instance==other||!Departures.Protect(__instance)&&!Departures.Protect(other))return true;__result=false;return false;}}
 [HarmonyPatch(typeof(CompTransporter),nameof(CompTransporter.CompGetGizmosExtra))]
 public static class PendingDepartureControls
 {
  public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result,CompTransporter __instance)
  {
   foreach(var gizmo in __result)yield return gizmo;
   if(Departures.Current.pending.Any(p=>p.transporters.Contains(__instance.parent)))yield return new Command_Action{defaultLabel="Cancel loading",defaultDesc="Cancel the confirmed loading order and its connected-floor gathering.",action=()=>{foreach(var plan in Departures.Current.pending.Where(p=>p.transporters.Contains(__instance.parent)).ToList())Departures.Cancel(plan,false);}};
  }
 }
 [HarmonyPatch(typeof(CompTransporter),nameof(CompTransporter.CompInspectStringExtra))]
 public static class PendingDepartureStatus
 {
  public static void Postfix(CompTransporter __instance,ref string __result)
  {if(Departures.Current?.pending.Any(p=>p.transporters.Contains(__instance.parent))==true)__result=(__result.NullOrEmpty()?"":__result+"\n")+"Loading: gathering selected cargo and passengers from connected floors.";}
 }
}
