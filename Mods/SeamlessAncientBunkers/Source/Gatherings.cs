using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 public class SavedSelection:IExposable
 {
  public List<Thing> things=new List<Thing>();public int count;
  public void ExposeData(){Scribe_Collections.Look(ref things,"things",LookMode.Reference);Scribe_Values.Look(ref count,"count");}
  public TransferableOneWay Restore(){var t=new TransferableOneWay();t.things.AddRange((things??new List<Thing>()).Where(x=>x!=null&&!x.Destroyed));t.AdjustTo(Math.Min(count,t.MaxCount));return t;}
 }
 public class Gathering:IExposable
 {
  public Map map;public List<Pawn> pawns=new List<Pawn>(),downed=new List<Pawn>();public int expires;
  public List<SavedSelection> selections=new List<SavedSelection>();public IntVec3 meeting,exit;
  public PlanetTile startTile,destinationTile;public bool caravan,forced,animals,mechs,visitors;
  public TargetInfo target;public Pawn organizer;public Precept_Ritual ritual;public RitualObligation obligation;public RitualRoleAssignments assignments;
  public void ExposeData()
  {
   Scribe_References.Look(ref map,"map");Scribe_Collections.Look(ref pawns,"pawns",LookMode.Reference);Scribe_Collections.Look(ref downed,"downed",LookMode.Reference);Scribe_Values.Look(ref expires,"expires");
   Scribe_Collections.Look(ref selections,"selections",LookMode.Deep);Scribe_Values.Look(ref meeting,"meeting");Scribe_Values.Look(ref exit,"exit");Scribe_Values.Look(ref startTile,"startTile");Scribe_Values.Look(ref destinationTile,"destinationTile");Scribe_Values.Look(ref caravan,"caravan");Scribe_Values.Look(ref forced,"forced");
   Scribe_TargetInfo.Look(ref target,true,"target");Scribe_References.Look(ref organizer,"organizer");Scribe_References.Look(ref ritual,"ritual");Scribe_References.Look(ref obligation,"obligation");Scribe_Deep.Look(ref assignments,"assignments");
   Scribe_Values.Look(ref animals,"animals");Scribe_Values.Look(ref mechs,"mechs");Scribe_Values.Look(ref visitors,"visitors");
  }
 }
 public static class Gatherings
 {
  [ThreadStatic] public static bool Executing;
  public const string Waiting="waiting for linked-map participants.";
  public static Route RouteTo(Pawn p,Map map)=>p?.Spawned==true?Graph.Reachable(p,false).FirstOrDefault(r=>r.map==map):null;
  public static IEnumerable<Pawn> Candidates(Map map)=>Find.Maps.Where(m=>m!=map).SelectMany(m=>m.mapPawns.AllPawnsSpawned).Where(p=>p.Faction==Faction.OfPlayer&&Portal.Eligible(p)&&RouteTo(p,map)!=null);
  public static void Queue(Gathering g)
  {
   foreach(var old in Manager.Current.gatherings.Where(x=>x.pawns.Intersect(g.pawns).Any()).ToList())Cancel(old,false);
   g.expires=Manager.Now+30000;Manager.Current.gatherings.Add(g);
   Messages.Message("Selected pawns are gathering through the bunker entrances before "+(g.caravan?"caravan loading":"the ritual or launch")+" begins.",MessageTypeDefOf.NeutralEvent,false);
  }
  public static void Cancel(Gathering g,bool notify)
  {
   Manager.Current.gatherings.Remove(g);
   foreach(var p in g.pawns.Where(p=>p!=null))
   {
    var intent=Broker.For(p);if(intent?.purpose==Purpose.Gathering)Broker.Cancel(intent);
    if(p.Spawned&&(p.CurJob?.reportStringOverride==Waiting||p.CurJobDef==Manager.TravelDef))p.jobs.EndCurrentJob(JobCondition.InterruptForced);
   }
   if(notify)Messages.Message("Bunker gathering cancelled: a participant or route became unavailable. Select the group again when the route is ready.",MessageTypeDefOf.RejectInput,false);
  }
  public static void Tick()
  {
   foreach(var g in Manager.Current.gatherings.ToList())
   {
    try
    {
    if(!BunkerMod.Settings.enabled||g.map==null||!Find.Maps.Contains(g.map)||g.pawns.Any(p=>p==null||!p.Spawned||p.Dead||p.Downed||p.Drafted||p.InMentalState)||Manager.Now>g.expires){Cancel(g,true);continue;}
    if(g.pawns.All(p=>p.Map==g.map))
    {
     Manager.Current.gatherings.Remove(g);
     foreach(var p in g.pawns){var i=Broker.For(p);if(i?.purpose==Purpose.Gathering)Broker.Cancel(i);if(p.CurJob?.reportStringOverride==Waiting)p.jobs.EndCurrentJob(JobCondition.InterruptForced,false);}
     try
     {
      Executing=true;
      if(g.caravan)
      {
       var items=g.selections.Select(s=>s.Restore()).ToList();
       if(items.Where((t,n)=>t.CountToTransfer<g.selections[n].count).Any()){Messages.Message("Caravan gathering cancelled because selected supplies changed.",MessageTypeDefOf.RejectInput,false);continue;}
       CaravanFormingUtility.StartFormingCaravan(g.pawns,g.downed,Faction.OfPlayer,items,g.meeting,g.exit,g.startTile,g.destinationTile);
      }
      else if(g.ritual!=null&&g.target.IsValid&&g.target.Map==g.map)
      {
       if(g.ritual.behavior is RitualBehaviorWorker_GravshipLaunch ship){ship.boardColonyAnimals=g.animals;ship.boardColonyMechs=g.mechs;ship.forceVisitorsToLeave=g.visitors;}
       g.ritual.behavior.TryExecuteOn(g.target,g.organizer,g.ritual,g.obligation,g.assignments,g.forced);
      }
     }
     finally{Executing=false;}
     continue;
    }
    foreach(var p in g.pawns)
    {
     if(p.Map==g.map)
     {
      if(p.CurJob?.reportStringOverride!=Waiting){var wait=JobMaker.MakeJob(JobDefOf.Wait);wait.expiryInterval=120;wait.reportStringOverride=Waiting;p.jobs.TryTakeOrderedJob(wait);}
      continue;
     }
     if(Broker.For(p)?.purpose==Purpose.Gathering)continue;
     var route=RouteTo(p,g.map);if(route==null){Cancel(g,true);break;}
     var travel=Broker.Begin(p,route,Purpose.Gathering,forced:true);if(travel!=null)p.jobs.TryTakeOrderedJob(travel);
    }
    }
    catch(Exception e){Cancel(g,true);Log.ErrorOnce("[SAB] Gathering cancelled: "+e,1870061);}
   }
  }
 }
 // The caravan dialog's native validation reads each selected pawn from its arrival point.
 // The mutation that creates the caravan is deferred until all pawns really arrive.
 public sealed class CaravanQuery:IDisposable
 {
  [ThreadStatic] public static CaravanQuery Current;
  public readonly Map map;public bool remote;public readonly PendingDeparture plan;private DepartureQuery scope;private readonly CaravanQuery previous;
  public CaravanQuery(Map map,List<Pawn> pawns,PendingDeparture plan=null)
  {
   this.map=map;this.plan=plan;previous=Current;Current=this;
   try{scope=new DepartureQuery(map,plan==null?pawns.Cast<Thing>():plan.Pieces.Select(p=>p.thing));remote=scope.remote;}
   catch{Dispose();throw;}
  }
  public void Dispose(){scope?.Dispose();Current=previous;}
 }
 [HarmonyPatch(typeof(Dialog_FormCaravan),nameof(Dialog_FormCaravan.AllSendablePawns))]
 public static class CaravanCandidates
 {
  [ThreadStatic] static bool nested;
  public static void Postfix(Map map,bool reform,ref List<Pawn> __result)
  {
   if(nested||reform||!BunkerMod.Settings.enabled||Manager.Current==null)return;
   try{nested=true;var valid=new HashSet<Pawn>(Gatherings.Candidates(map));foreach(var m in Find.Maps.Where(m=>m!=map))__result.AddRange(Dialog_FormCaravan.AllSendablePawns(m,false).Where(valid.Contains));__result=__result.Distinct().ToList();}finally{nested=false;}
  }
 }
 [HarmonyPatch(typeof(Dialog_FormCaravan),"TryFormAndSendCaravan")]
 public static class CaravanValidation
 {
  public static bool Prefix(Map ___map,List<TransferableOneWay> ___transferables,ref bool __result,out CaravanQuery __state)
  {
   __state=null;if(Departures.Executing||!BunkerMod.Settings.enabled)return true;
   var pawns=TransferableUtility.GetPawnsFromTransferables(___transferables);
   try{var plan=PendingDeparture.Create(___map,___transferables);if(plan.Pieces.All(p=>p.thing.MapHeld==___map))return true;__state=new CaravanQuery(___map,pawns,plan);return true;}catch(Exception e){Messages.Message(e.Message,MessageTypeDefOf.RejectInput,false);__result=false;return false;}
  }
  public static void Finalizer(CaravanQuery __state){__state?.Dispose();}
  public static void Postfix(Map ___map,bool __result,CaravanQuery __state)
  {
   if(__result&&__state==null&&!Departures.Executing&&BunkerMod.Settings.enabled)
    foreach(var old in Departures.Current.pending.Where(p=>p.caravan&&p.map==___map).ToList())Departures.Cancel(old,false);
  }
 }
 [HarmonyPatch(typeof(CaravanFormingUtility),nameof(CaravanFormingUtility.StartFormingCaravan))]
 public static class CaravanGather
 {
  public static bool Prefix(List<Pawn> pawns,List<Pawn> downedPawns,List<TransferableOneWay> transferables,IntVec3 meetingPoint,IntVec3 exitSpot,PlanetTile startingTile,PlanetTile destinationTile)
  {
   var query=CaravanQuery.Current;if(Gatherings.Executing||query?.remote!=true)return true;
   if(query.plan!=null){var plan=query.plan;plan.caravan=true;plan.meeting=meetingPoint;plan.start=startingTile;plan.destination=destinationTile;Departures.Queue(plan);return false;}
   Gatherings.Queue(new Gathering{caravan=true,map=query.map,pawns=pawns.ToList(),downed=downedPawns.ToList(),meeting=meetingPoint,exit=exitSpot,startTile=startingTile,destinationTile=destinationTile,selections=transferables.Where(t=>t.CountToTransfer>0).Select(t=>new SavedSelection{things=t.things.ToList(),count=t.CountToTransfer}).ToList()});return false;
  }
 }
 [HarmonyPatch]
 public static class RitualCandidates
 {
  public static IEnumerable<MethodBase> TargetMethods(){yield return AccessTools.Method(typeof(Dialog_BeginRitual),nameof(Dialog_BeginRitual.CreateRitualRoleAssignments));yield return AccessTools.Method(typeof(RitualBehaviorWorker),nameof(RitualBehaviorWorker.CanStartRitualNow));}
  public static List<Pawn> Pawns(MapPawns mp)
  {
   var result=new List<Pawn>(mp.FreeColonistsAndPrisonersSpawned);
   var map=(Map)AccessTools.Field(typeof(MapPawns),"map").GetValue(mp);
   if(BunkerMod.Settings.enabled&&Manager.Current!=null)result.AddRange(Gatherings.Candidates(map).Where(p=>p.IsFreeNonSlaveColonist));
   return result.Distinct().ToList();
  }
  public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
  {
   var getter=AccessTools.PropertyGetter(typeof(MapPawns),nameof(MapPawns.FreeColonistsAndPrisonersSpawned));
   foreach(var instruction in code){if(instruction.Calls(getter)){instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(RitualCandidates),nameof(Pawns));}yield return instruction;}
  }
 }
 [HarmonyPatch]
 public static class RitualQueries
 {
  public static IEnumerable<MethodBase> TargetMethods()=>typeof(RitualRole).Assembly.GetTypes().Where(t=>typeof(RitualRole).IsAssignableFrom(t)||typeof(RitualBehaviorWorker).IsAssignableFrom(t)||t==typeof(RitualRoleAssignments))
   .SelectMany(t=>t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly))
   .Where(m=>!m.IsAbstract&&(m.Name=="PawnNotAssignableReason"||m.Name=="AppliesToPawn"||m.Name=="PawnCanFillRole")&&m.GetParameters().Any(p=>p.ParameterType==typeof(TargetInfo))).Cast<MethodBase>();
  public static void Prefix(object[] __args,out RemotePawnScope __state)
  {
   __state=null;if(!BunkerMod.Settings.enabled||Manager.Current==null)return;
   var p=__args.OfType<Pawn>().FirstOrDefault();var target=__args.OfType<TargetInfo>().FirstOrDefault();
   if(p?.Spawned==true&&target.Map!=null&&p.Map!=target.Map){var route=Gatherings.RouteTo(p,target.Map);if(route!=null)__state=new RemotePawnScope(p,route.map,route.landing);}
  }
  public static void Finalizer(RemotePawnScope __state){__state?.Dispose();}
 }
 [HarmonyPatch]
 public static class RitualGather
 {
  public static IEnumerable<MethodBase> TargetMethods()=>typeof(RitualBehaviorWorker).Assembly.GetTypes().Where(t=>typeof(RitualBehaviorWorker).IsAssignableFrom(t)).Select(t=>t.GetMethod("TryExecuteOn",BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)).Where(m=>m!=null&&!m.IsAbstract).Cast<MethodBase>();
  public static bool Prefix(RitualBehaviorWorker __instance,TargetInfo target,Pawn organizer,Precept_Ritual ritual,RitualObligation obligation,RitualRoleAssignments assignments,bool playerForced)
  {
   if(Gatherings.Executing||!BunkerMod.Settings.enabled||Manager.Current==null||ritual==null)return true;
   var pawns=assignments.Participants.ToList();var ship=__instance as RitualBehaviorWorker_GravshipLaunch;
   if(ship?.boardColonyAnimals==true)pawns.AddRange(Gatherings.Candidates(target.Map).Where(p=>p.IsColonyAnimal));
   if(!pawns.Any(p=>p.Map!=target.Map))return true;
   if(pawns.Any(p=>p.Map!=target.Map&&Gatherings.RouteTo(p,target.Map)==null)){Messages.Message("A selected participant has no safe route through the bunker entrances.",MessageTypeDefOf.RejectInput,false);return false;}
   Gatherings.Queue(new Gathering{map=target.Map,pawns=pawns.Distinct().ToList(),target=target,organizer=organizer,ritual=ritual,obligation=obligation,assignments=assignments,forced=playerForced,animals=ship?.boardColonyAnimals==true,mechs=ship?.boardColonyMechs==true,visitors=ship?.forceVisitorsToLeave==true});return false;
  }
 }
 [HarmonyPatch(typeof(JobGiver_BoardOrLeaveGravship),"TryGiveJob")]
 public static class BoardingRoute
 {
  public static void Postfix(Pawn pawn,ref Job __result)
  {
   if(__result!=null||RemotePawnScope.Active||Manager.Current==null||Broker.For(pawn)!=null||!Manager.Current.CanScan(pawn,Purpose.Boarding,true))return;
   foreach(var r in Graph.Reachable(pawn,false))
   {
    var engine=r.map.listerThings.ThingsOfDef(ThingDefOf.GravEngine).OfType<Building_GravEngine>().FirstOrDefault(e=>e.pawnsToBoard?.Contains(pawn)==true);
    if(engine==null)continue;
    __result=Broker.Begin(pawn,r,Purpose.Boarding,engine,forced:true);return;
   }
  }
 }
}

