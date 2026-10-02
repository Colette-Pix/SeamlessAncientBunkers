using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 public class DepartureStaging:GameComponent
 {
  public List<int> requests=new List<int>();
  public List<PendingDeparture> pending=new List<PendingDeparture>();
  public DepartureStaging(Game game){}
  public override void ExposeData(){Scribe_Collections.Look(ref requests,"sabDepartureRequests",LookMode.Value);Scribe_Collections.Look(ref pending,"sabPendingDepartures",LookMode.Deep);if(Scribe.mode==LoadSaveMode.PostLoadInit){requests=requests??new List<int>();pending=pending??new List<PendingDeparture>();}}
  public override void GameComponentTick()
  {
   if(Manager.Now%120!=0)return;
   Departures.Tick();
   foreach(var id in requests.ToList())
    if(!ServiceDeliveries.Current.IsPending(id)){ServiceDeliveries.Current.Release(id);requests.Remove(id);}
  }
 }
 public static class DepartureCargo
 {
  public static IEnumerable<Map> Maps(Map destination)
  {
   var seen=new HashSet<Map>();
   foreach(var p in Find.Maps.SelectMany(m=>m.mapPawns.FreeColonistsSpawned).Where(p=>Portal.Eligible(p)&&Hauling.CanHaul(p)))
   {
    var maps=Graph.Reachable(p).Select(r=>r.map).Concat(new[]{p.Map}).ToList();
    if(!maps.Contains(destination))continue;
    foreach(var m in maps)if(m!=destination&&seen.Add(m))yield return m;
   }
  }
  public static void Add(List<TransferableOneWay> rows,Thing item)
  {
   if(rows.Any(t=>t.things.Contains(item)))return;
   var row=TransferableUtility.TransferableMatching(item,rows,TransferAsOneMode.PodsOrCaravanPacking);
   if(row==null){row=new TransferableOneWay();rows.Add(row);}row.things.Add(item);
  }
  public static bool Stage(List<TransferableOneWay> rows,Map destination,IntVec3 cell,Thing passengerDestination=null)
  {
   bool remote=false;
   foreach(var row in rows.Where(r=>r.CountToTransfer>0))
   {
    int remaining=row.CountToTransfer;
    foreach(var item in row.things.Where(t=>t!=null&&!t.Destroyed).OrderBy(t=>t.MapHeld==destination?0:1))
    {
     int count=Math.Min(remaining,item.stackCount);if(count<=0)break;remaining-=count;
     if(item.MapHeld==destination)continue;
     remote=true;
     if(item is Pawn passenger)
     {
      if(Broker.For(passenger)!=null)continue;
      var route=Gatherings.RouteTo(passenger,destination);
      if(route!=null){var travel=Broker.Begin(passenger,route,Purpose.Manual,forced:true);if(travel!=null)passenger.jobs.TryTakeOrderedJob(travel);continue;}
      var receiver=passengerDestination??destination.mapPawns.FreeColonistsSpawned.FirstOrDefault(p=>!p.Downed);
      if(receiver==null)continue;
      foreach(var carrier in Find.Maps.SelectMany(m=>m.mapPawns.FreeColonistsSpawned).Where(p=>Portal.Eligible(p)&&Hauling.CanHaul(p)&&Broker.For(p)==null))
      {
       var native=JobMaker.MakeJob(JobDefOf.Wait);native.expiryInterval=1;
       var carry=PawnTransit.Plan(carrier,passenger,receiver,native,true);
       if(carry!=null){carrier.jobs.TryTakeOrderedJob(carry);break;}JobMaker.ReturnToPool(native);
      }
     }
     else
     {
      var id=ServiceDeliveries.Current.Add(item,count,destination:destination,cell:cell);
      var pending=Current.Game.GetComponent<DepartureStaging>().requests;if(!pending.Contains(id))pending.Add(id);
     }
    }
   }
   return remote;
  }
 }
 [HarmonyPatch(typeof(Dialog_FormCaravan),"AddItemsToTransferables")]
 public static class ConnectedCaravanCargo
 {
  public static void Postfix(Map ___map,List<TransferableOneWay> ___transferables)
  {
   if(!BunkerMod.Settings.enabled||Manager.Current==null)return;
   foreach(var map in DepartureCargo.Maps(___map))
    foreach(var item in CaravanFormingUtility.AllReachableColonyItems(map).ToList())DepartureCargo.Add(___transferables,item);
  }
 }
 [HarmonyPatch(typeof(Dialog_LoadTransporters),"AddItemsToTransferables")]
 public static class ConnectedTransporterCargo
 {
  public static void Postfix(Map ___map,List<CompTransporter> ___transporters,List<TransferableOneWay> ___transferables)
  {
   if(!BunkerMod.Settings.enabled||Manager.Current==null)return;
   foreach(var map in DepartureCargo.Maps(___map))
    foreach(var item in TransporterUtility.AllSendableItems(___transporters,map).ToList())DepartureCargo.Add(___transferables,item);
  }
 }
 [HarmonyPatch(typeof(Dialog_LoadTransporters),"AddPawnsToTransferables")]
 public static class ConnectedTransporterPassengers
 {
  public static void Postfix(Map ___map,List<CompTransporter> ___transporters,List<TransferableOneWay> ___transferables)
  {
   if(!BunkerMod.Settings.enabled||Manager.Current==null)return;
   foreach(var map in DepartureCargo.Maps(___map))
    foreach(var pawn in TransporterUtility.AllSendablePawns(___transporters,map).ToList())DepartureCargo.Add(___transferables,pawn);
  }
 }
 [HarmonyPatch(typeof(Dialog_LoadTransporters),"TryAccept")]
 public static class StageTransporterCargo
 {
  public static bool Prefix(Dialog_LoadTransporters __instance,Map ___map,List<CompTransporter> ___transporters,List<TransferableOneWay> ___transferables,ref bool __result)
  {
   if(Departures.Executing||!BunkerMod.Settings.enabled||Manager.Current==null||___transporters.Count==0)return true;
   try
   {
    var plan=PendingDeparture.Create(___map,___transferables);
    if(plan.Pieces.All(p=>p.thing.MapHeld==___map))
    {
     // A newly accepted local selection supersedes an earlier remote loading plan.
     if(!Departures.Current.pending.Any(p=>p.transporters.Any(t=>___transporters.Any(c=>c.parent==t))))return true;
     try{Departures.Executing=true;__result=(bool)AccessTools.Method(typeof(Dialog_LoadTransporters),"TryAccept").Invoke(__instance,null);}
     finally{Departures.Executing=false;}
     if(__result)foreach(var old in Departures.Current.pending.Where(p=>p.transporters.Any(t=>___transporters.Any(c=>c.parent==t))).ToList())Departures.Cancel(old,false);
     return false;
    }
    using(new DepartureQuery(___map,plan.Pieces.Select(p=>p.thing)))
     if(!(bool)AccessTools.Method(typeof(Dialog_LoadTransporters),"CheckForErrors").Invoke(__instance,new object[]{TransferableUtility.GetPawnsFromTransferables(___transferables)})){__result=false;return false;}
    plan.transporters=___transporters.Select(t=>(Thing)t.parent).ToList();Departures.Queue(plan);__result=true;return false;
   }
   catch(Exception e){Messages.Message(e.GetBaseException().Message,MessageTypeDefOf.RejectInput,false);__result=false;return false;}
  }
 }
}
