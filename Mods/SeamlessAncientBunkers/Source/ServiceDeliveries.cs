using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 public class ServiceReceipt:IExposable
 {
  public int requestId,count;public Thing item;public ThingDef def,stuff;public Map map;public IntVec3 cell;
  public bool hasQuality,tainted;public QualityCategory quality;public int hitPoints;
  public void ExposeData()
  {Scribe_Values.Look(ref requestId,"requestId");Scribe_Values.Look(ref count,"count");Scribe_References.Look(ref item,"item");Scribe_Defs.Look(ref def,"def");Scribe_Defs.Look(ref stuff,"stuff");Scribe_References.Look(ref map,"map");Scribe_Values.Look(ref cell,"cell");Scribe_Values.Look(ref hasQuality,"hasQuality");Scribe_Values.Look(ref quality,"quality");Scribe_Values.Look(ref tainted,"tainted");Scribe_Values.Look(ref hitPoints,"hitPoints");}
 }
 public class ServiceDelivery:IExposable
 {
  public int id,count;public Thing item;public List<Thing> remainingSources=new List<Thing>();public Pawn trader;public Bill_Production bill;public Map explicitMap;public IntVec3 cell=IntVec3.Invalid;
  public Map Destination=>explicitMap??trader?.Map??ConnectedBills.GroupMap(bill?.GetSlotGroup());
  public void ExposeData(){Scribe_Values.Look(ref id,"id");Scribe_Values.Look(ref count,"count");Scribe_References.Look(ref item,"item");Scribe_Collections.Look(ref remainingSources,"remainingSources",LookMode.Reference);Scribe_References.Look(ref trader,"trader");Scribe_References.Look(ref bill,"bill");Scribe_References.Look(ref explicitMap,"destination");Scribe_Values.Look(ref cell,"cell",IntVec3.Invalid);if(Scribe.mode==LoadSaveMode.PostLoadInit)remainingSources=remainingSources??new List<Thing>();}
 }
 // Saved explicit output/delivery requests. No ownership/payment
 // changes occur here. Disconnecting cancels the trip, leaving every item in the world.
 public class ServiceDeliveries:GameComponent
 {
  public static ServiceDeliveries Current=>Verse.Current.Game.GetComponent<ServiceDeliveries>();
  public List<ServiceDelivery> requests=new List<ServiceDelivery>();List<int> completed=new List<int>();List<ServiceReceipt> receipts=new List<ServiceReceipt>();int nextId;
  public ServiceDeliveries(Game game){}
  public override void LoadedGame()
  {
   // Older saves may contain the retired trade-staging workflow. Trade now uses the
   // ordinary single native transaction; abandon only those obsolete requests.
   foreach(var r in requests.Where(r=>r.trader!=null).ToList())
   {
    if(Manager.Current!=null)foreach(var i in Manager.Current.intents.Where(i=>IsSupply(i)&&Request(i)==r).ToList())Broker.Cancel(i);
    Release(r.id);
   }
  }
  public override void ExposeData(){Scribe_Collections.Look(ref requests,"sabServiceDeliveries",LookMode.Deep);Scribe_Collections.Look(ref completed,"sabServiceDeliveriesCompleted",LookMode.Value);Scribe_Collections.Look(ref receipts,"sabServiceReceipts",LookMode.Deep);Scribe_Values.Look(ref nextId,"sabServiceDeliveryId");if(Scribe.mode==LoadSaveMode.PostLoadInit){requests=requests??new List<ServiceDelivery>();completed=completed??new List<int>();receipts=receipts??new List<ServiceReceipt>();}}
  public bool IsComplete(int id)=>completed.Contains(id);
  public bool IsPending(int id)=>requests.Any(r=>r.id==id);
  public void Cancel(int id)=>requests.RemoveAll(r=>r.id==id);
  public List<ServiceReceipt> Receipts(int id)=>receipts.Where(r=>r.requestId==id).ToList();
  public void Release(int id){Cancel(id);completed.Remove(id);receipts.RemoveAll(r=>r.requestId==id);}
  public void Clear(){requests.Clear();completed.Clear();receipts.Clear();}
  void Complete(ServiceDelivery r){requests.Remove(r);if(r.explicitMap!=null&&!completed.Contains(r.id))completed.Add(r.id);}
  public int Add(Thing item,int count,Pawn trader=null,Bill_Production bill=null,Map destination=null,IntVec3? cell=null)
  {
   var existing=requests.FirstOrDefault(r=>r.item==item&&r.trader==trader&&r.bill==bill&&r.explicitMap==destination&&r.cell==(cell??IntVec3.Invalid));
   if(existing!=null){existing.count=Math.Max(existing.count,count);return existing.id;}
   var request=new ServiceDelivery{id=++nextId,item=item,count=count,trader=trader,bill=bill,explicitMap=destination,cell=cell??IntVec3.Invalid};requests.Add(request);return request.id;
  }
  const string Fetch="SAB.DispatchFetch:",Deliver="SAB.Dispatch:";
  public static bool IsFetch(Intent i)=>i?.needNode?.StartsWith(Fetch)==true;
  public static bool IsSupply(Intent i)=>IsFetch(i)||i?.needNode?.StartsWith(Deliver)==true;
  static ServiceDelivery Request(Intent i)=>Current.requests.FirstOrDefault(r=>i.needNode==(IsFetch(i)?Fetch:Deliver)+r.id);
  public static bool Preserve(Thing item)=>item!=null&&Verse.Current.Game!=null&&Current?.requests?.Any(r=>r.item==item||r.remainingSources?.Contains(item)==true)==true;
  public static void Collected(Intent i)
  {
   if(!IsSupply(i)||IsFetch(i))return;
   var r=Request(i);var cargo=i.pawn.carryTracker.CarriedThing;
   if(r==null||cargo==null||r.item==cargo)return;
   // Pickup can split a stack. Follow the selected physical portion through need
   // interruptions and saves; keep the source only for an outstanding capacity-limited remainder.
   if(r.count>cargo.stackCount&&r.item?.Destroyed==false&&!r.remainingSources.Contains(r.item))r.remainingSources.Add(r.item);
   r.item=cargo;
  }
  void Delivered(ServiceDelivery r,int count)
  {
   r.count-=count;
   if(r.count<=0){Complete(r);return;}
   r.item=r.remainingSources.FirstOrDefault(t=>t!=null&&!t.Destroyed);
   r.remainingSources.Remove(r.item);
  }
  public static bool Enabled(Intent i)=>BunkerMod.Settings.haul&&Hauling.CanHaul(i.pawn)&&Request(i)?.Destination!=null;
  public static Job Plan(Pawn p)
  {
   if(!BunkerMod.Settings.haul||!Hauling.CanHaul(p)||p.carryTracker.CarriedThing!=null)return null;
   var routes=Graph.Reachable(p);
   foreach(var r in Current.requests.ToList())
   {
    if(r.item==null||r.item.Destroyed||r.count<=0||r.Destination==null||r.trader?.Dead==true){Current.requests.Remove(r);continue;}
    if(r.item.MapHeld==r.Destination&&ServicesInventory.Holder(r.item)!=null)
    {int count=Math.Min(r.count,r.item.stackCount);if(r.explicitMap!=null)Current.Record(r,r.item,count,r.item.PositionHeld);Current.Delivered(r,count);continue;}
    if(ServicesInventory.Holder(r.item)==null||Manager.Current.Claimed(r.item))continue;
    if(r.item is Pawn passenger&&r.trader!=null)
    {var carry=PawnTransit.Plan(p,passenger,r.trader,JobMaker.MakeJob(JobDefOf.Wait),true);if(carry!=null)return carry;continue;}
    var source=r.item.MapHeld==p.Map?new Route{map=p.Map,landing=p.Position}:routes.FirstOrDefault(x=>x.map==r.item.MapHeld&&ConsumerSupply.Accessible(p,x,r.item));
    if(source==null)continue;
    using(new RemotePawnScope(p,source.map,source.landing))if(!ConsumerSupply.Free(p,r.item))continue;
    if(source.map==p.Map)return BeginDelivery(p,r);
    var job=Broker.Begin(p,source,Purpose.Work,r.item,count:Math.Min(r.count,r.item.stackCount));
    if(job!=null)Broker.For(p).needNode=Fetch+r.id;
    return job;
   }
   return null;
  }
  static bool Cell(Pawn p,ServiceDelivery r,Thing cargo,out IntVec3 cell)
  {
   cell=IntVec3.Invalid;
   if(r.Destination!=p.Map)return false;
   IEnumerable<IntVec3> cells;
   if(r.bill!=null)
   {
    var group=r.bill.GetSlotGroup();if(!ConnectedBills.Valid(r.bill,group)||!group.Settings.AllowedToAccept(cargo))return false;
    cells=group.CellsList;
   }
   else if(r.explicitMap!=null)
   {
    if(!r.cell.IsValid||!r.cell.InBounds(p.Map))return false;
    cells=GenRadial.RadialCellsAround(r.cell,6,true);
   }
   else
   {
    if(r.trader?.Spawned!=true||r.trader.HostileTo(p))return false;
    cells=GenRadial.RadialCellsAround(r.trader.Position,8,true)
     .Concat(p.Map.areaManager.Home.ActiveCells.OrderBy(c=>c.DistanceToSquared(r.trader.Position)))
     .Concat(p.Map.haulDestinationManager.AllGroups.SelectMany(g=>g.CellsList));
   }
   foreach(var c in cells)
    if(c.InBounds(p.Map)&&c.Standable(p.Map)&&!c.Fogged(p.Map)&&Portal.Allowed(p,p.Map,c)&&!Broker.Claimed(p,p.Map,null,c)&&
     (r.trader==null||(p.Map.areaManager.Home[c]||c.GetSlotGroup(p.Map)?.Settings.AllowedToAccept(cargo)==true)&&
      p.Map.reachability.CanReach(r.trader.Position,c,PathEndMode.Touch,TraverseMode.PassDoors,Danger.Some))&&
     StoreUtility.IsGoodStoreCell(c,p.Map,cargo,p,p.Faction)&&p.CanReserveAndReach(c,PathEndMode.OnCell,Danger.None))
    {cell=c;return true;}
   return false;
  }
  static Job BeginDelivery(Pawn p,ServiceDelivery r)
  {
   if(!ConsumerSupply.Free(p,r.item))return null;
   Route route=null;IntVec3 cell=IntVec3.Invalid;
   foreach(var candidate in Graph.Reachable(p).Where(x=>x.map==r.Destination))
    using(new RemotePawnScope(p,candidate.map,candidate.landing))if(Cell(p,r,r.item,out cell)){route=candidate;break;}
   if(route==null)return null;
   var job=Broker.Begin(p,route,Purpose.Supply,r.item,cell,count:Math.Min(r.count,Math.Min(r.item.stackCount,p.carryTracker.MaxStackSpaceEver(r.item.def))));
   if(job!=null)Broker.For(p).needNode=Deliver+r.id;return job;
  }
  public static Job Arrive(Intent i)
  {var r=Request(i);return r==null?null:BeginDelivery(i.pawn,r);}
  public static Job Delivery(Intent i)
  {
   Collected(i);
   var r=Request(i);var cargo=i.pawn.carryTracker.CarriedThing;
   if(r==null||cargo==null||!Cell(i.pawn,r,cargo,out var cell))return null;
   int delivered=Math.Min(r.count,cargo.stackCount);
   if(r.explicitMap!=null)Current.Record(r,cargo,delivered,cell);
   Current.Delivered(r,delivered);
   var job=JobMaker.MakeJob(JobDefOf.HaulToCell,cargo,cell);job.count=cargo.stackCount;
   job.haulMode=r.bill!=null?HaulMode.ToCellStorage:HaulMode.ToCellNonStorage;return job;
  }
  void Record(ServiceDelivery r,Thing cargo,int count,IntVec3 cell)
  {
   var inner=cargo.GetInnerIfMinified();var qualityComp=inner.TryGetComp<CompQuality>();
   receipts.Add(new ServiceReceipt{requestId=r.id,count=count,item=cargo,def=cargo.def,stuff=inner.Stuff,map=r.Destination,cell=cell,
    hasQuality=qualityComp!=null,quality=qualityComp?.Quality??QualityCategory.Normal,tainted=(inner as Apparel)?.WornByCorpse==true,hitPoints=inner.HitPoints});
  }
 }
 [HarmonyPatch(typeof(Manager),nameof(Manager.Cleanup))]
 public static class ClearServiceDeliveries
 {public static void Postfix()=>ServiceDeliveries.Current?.Clear();}
 [HarmonyPatch(typeof(Thing),nameof(Thing.TryAbsorbStack))]
 public static class PreserveRequestedStock
 {public static bool Prefix(Thing other,ref bool __result){if(!ServiceDeliveries.Preserve(other))return true;__result=false;return false;}}
 [HarmonyPatch(typeof(Thing),nameof(Thing.CanStackWith))]
 public static class RequestedStockPlacement
 {
  public static bool Prefix(Thing __instance,Thing other,ref bool __result)
  {if(__instance==other||!ServiceDeliveries.Preserve(__instance)&&!ServiceDeliveries.Preserve(other))return true;__result=false;return false;}
 }
}
