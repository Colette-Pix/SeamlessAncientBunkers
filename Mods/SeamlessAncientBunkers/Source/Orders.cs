using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
namespace SeamlessAncientBunkers
{
 public class RemoteWorkOrder
 {
  public string label;public Action action;
  public RemoteWorkOrder(string label,Action action){this.label=label;this.action=action;}
 }
 public static class Orders
 {
  public static Pawn orderingPawn;
  public static IEnumerable<Gizmo> Gizmos(MapPortal portal)
  {
   if(Manager.Current.gatherings.Count>0||Departures.Current.pending.Count>0)
    yield return new Command_Action{defaultLabel="Cancel bunker gatherings",defaultDesc="Cancel pending departure, ritual and launch gatherings through bunker entrances.",action=()=>{foreach(var group in Manager.Current.gatherings.ToList())Gatherings.Cancel(group,false);Departures.CancelAll();}};
   yield return new Command_Action {defaultLabel="Travel to linked map...",defaultDesc="Choose a colony pawn and destination. Uses already opened links, including multiple hops.",action=()=>Choose(portal,p=>{
    var choices=Graph.Reachable(p,false).GroupBy(r=>r.map).Select(g=>g.OrderBy(r=>r.cost).First()).Select(r=>new FloatMenuOption(r.map.ToString()+" ("+r.portals.Count+" hops)",()=>{var job=Broker.Begin(p,r,Purpose.Manual,forced:true);if(job!=null)p.jobs.TryTakeOrderedJob(job);})).ToList();
    if(choices.Count==0)choices.Add(new FloatMenuOption("No safe opened route",null));Find.WindowStack.Add(new FloatMenu(choices));})};
   yield return new Command_Action {defaultLabel="Remote work order...",defaultDesc="Choose a colonist, switch to a connected map, then right-click a work target. Escape cancels this mode.",action=()=>Choose(portal,p=>{
    orderingPawn=p;Messages.Message("Switch to a linked map and right-click a target to prioritize work via entrances. Escape cancels.",MessageTypeDefOf.NeutralEvent,false);})};
   yield return new Command_Action {defaultLabel="Traffic status",defaultDesc="Show why pawns cannot use this entrance and their current routes.",action=()=>Find.WindowStack.Add(new Dialog_MessageBox(
    "This hatch: "+(Manager.Current.Enabled(portal)?"automatic traffic on":"automatic traffic off")+"\n"+
    string.Join("\n",portal.Map.mapPawns.AllPawnsSpawned.Where(p=>p.Faction==Faction.OfPlayer).Select(p=>p.LabelShort+": "+(Portal.BlockReason(p,portal,true,out _)??"Route available; waiting for a reason to travel.")))+"\n\n"+
    string.Join("\n",Manager.Current.intents.Select(i=>i.pawn?.LabelShort+": "+i.purpose+", hop "+(i.hop+1)+"/"+(i.route?.Count??1)))))};
  }
  private static void Choose(MapPortal portal,Action<Pawn> action)
  {
   var choices=portal.Map.mapPawns.AllPawnsSpawned.Where(p=>Portal.Eligible(p)&&p.Faction==Faction.OfPlayer).Select(p=>new FloatMenuOption(p.LabelShortCap,()=>action(p))).ToList();
   if(choices.Count==0)choices.Add(new FloatMenuOption("No eligible colony pawns",null));Find.WindowStack.Add(new FloatMenu(choices));
  }
  public static List<FloatMenuOption> WorkOptions(Pawn pawn,Map map,IntVec3 cell)
   =>WorkOrders(pawn,map,cell).Select(o=>new FloatMenuOption(o.label,o.action)).ToList();
  public static List<RemoteWorkOrder> WorkOrders(Pawn pawn,Map map,IntVec3 cell)
  {
   var choices=new List<RemoteWorkOrder>();
   if(pawn.workSettings==null||pawn.RaceProps.Animal)return choices;
   var route=Graph.Reachable(pawn,false).Where(r=>r.map==map).OrderBy(r=>r.cost).FirstOrDefault();
   if(route==null)return choices;
   using(new RemotePawnScope(pawn,map,route.landing))
   using(new ProbeAudit(map))
    foreach(var giver in pawn.workSettings.WorkGiversInOrderNormal.Concat(pawn.workSettings.WorkGiversInOrderEmergency).Distinct().Where(g=>WorkProbe.Allowed(pawn,g)))
    {
     var scanner=(WorkGiver_Scanner)giver;
     foreach(var thing in cell.GetThingList(map).Cast<Thing>().Concat(new Thing[]{null}))
     {
      if(thing==null&&!scanner.def.scanCells||thing!=null&&!scanner.def.scanThings)continue;
      var job=WorkProbe.At(pawn,giver.def,thing,cell,true,true);
      if(job==null)continue;
      JobMaker.ReturnToPool(job);
      var target=thing;var def=giver.def;
      choices.Add(new RemoteWorkOrder("Prioritize via entrance: "+def.label+" ("+pawn.LabelShort+")",()=>{
       var travel=Broker.Begin(pawn,route,Purpose.Work,target,cell,def,forced:true);
       if(travel!=null&&pawn.jobs.TryTakeOrderedJob(travel))OrderFeedback.Accepted(map,cell);
       orderingPawn=null;
      }));
     }
    }
   return choices;
  }
 }
 [HarmonyPatch(typeof(UIRoot_Play),nameof(UIRoot_Play.UIRootOnGUI))]
 public static class RemoteOrderInput
 {
  public static void Prefix()
  {
   if(Orders.orderingPawn==null)return;
   var e=Event.current;
   if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){Orders.orderingPawn=null;e.Use();return;}
   if(e.type!=EventType.MouseDown||e.button!=1)return;
   var map=Find.CurrentMap;var cell=UI.MouseCell();
   if(map==null||!cell.InBounds(map))return;
   try{
    var choices=Orders.WorkOptions(Orders.orderingPawn,map,cell);
    if(choices.Count==0)choices.Add(new FloatMenuOption("No supported reachable work for this colonist",null));
    Find.WindowStack.Add(new FloatMenu(choices));e.Use();
   }catch(Exception ex){Orders.orderingPawn=null;Log.Error("[SAB] Remote order: "+ex);}
  }
 }
}
