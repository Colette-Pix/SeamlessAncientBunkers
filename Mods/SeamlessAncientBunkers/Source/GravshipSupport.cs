using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
namespace SeamlessAncientBunkers
{
 public static class GravshipSupport
 {
  public static string BlockReason(Building_GravEngine engine)
  {
   if(engine?.Map==null||engine.Map.listerThings.AnyThingWithDef(ThingDefOf.GravAnchor))return null;
   return DescendantOccupied(engine.Map,0)?"Colony pawns, prisoners or guests are still in a linked bunker, including those inside containers. Bring them upstairs or build a grav anchor before departing.":null;
  }
  private static bool DescendantOccupied(Map map,int depth)
  {
   var visited=new HashSet<Map>{map};var pending=new Queue<Map>();pending.Enqueue(map);
   while(pending.Count>0)
   foreach(var hatch in pending.Dequeue().listerThings.AllThings.OfType<AncientHatch>())
   {
    var child=hatch.PocketMap;if(child==null||!visited.Add(child))continue;
    var pawns=new List<Pawn>();ThingOwnerUtility.GetAllThingsRecursively(child,ThingRequest.ForGroup(ThingRequestGroup.Pawn),pawns);
    if(pawns.Any(p=>!p.Dead&&(p.Faction==Faction.OfPlayer||p.IsPrisonerOfColony||p.HostFaction==Faction.OfPlayer||p.IsQuestLodger())))return true;
    pending.Enqueue(child);
   }
   return false;
  }  public static void BeforeDeparture(Building_GravEngine engine)
  {
   var manager=Manager.Current;if(manager==null)return;
   foreach(var group in manager.gatherings.Where(g=>g.map==engine.Map).ToList())Gatherings.Cancel(group,false);
   var deck=engine.ValidSubstructure;
   foreach(var intent in manager.intents.ToList())
   {
    bool aboard=intent.pawn?.Map==engine.Map&&deck.Contains(intent.pawn.Position);
    bool movingPortal=intent.route?.Any(p=>p?.Map==engine.Map&&deck.Contains(p.Position))==true;
    if(!aboard&&!movingPortal)continue;
    Broker.Cancel(intent);
    if(intent.pawn?.CurJobDef==Manager.TravelDef||intent.pawn?.CurJobDef?.defName=="SAB_Collect"||intent.pawn?.CurJobDef?.defName=="SAB_CollectPassenger")intent.pawn.jobs.EndCurrentJob(JobCondition.InterruptForced,false);
   }
  }
 }
 [HarmonyPatch(typeof(CompPilotConsole),nameof(CompPilotConsole.StartChoosingDestination_NewTemp))]
 public static class BunkerDestinationCheck
 {
  // Reject before the destination picker spends fuel; selection/gathering remains available.
  static bool Prefix(CompPilotConsole __instance,bool launching)
  {
   if(!launching)return true;
   var reason=GravshipSupport.BlockReason(__instance.engine);
   if(reason==null)return true;
   Messages.Message(reason,MessageTypeDefOf.RejectInput,false);return false;
  }
 }
 [HarmonyPatch(typeof(WorldComponent_GravshipController),nameof(WorldComponent_GravshipController.InitiateTakeoff))]
 public static class BunkerTakeoffCheck
 {
  static bool Prefix(Building_GravEngine engine){var reason=GravshipSupport.BlockReason(engine);if(reason==null)return true;Messages.Message(reason,MessageTypeDefOf.RejectInput,false);return false;}
 }
 [HarmonyPatch(typeof(GravshipUtility),nameof(GravshipUtility.GenerateGravship))]
 public static class BunkerDepartureCleanup
 {
  static void Prefix(Building_GravEngine engine)=>GravshipSupport.BeforeDeparture(engine);
 }
 [HarmonyPatch(typeof(GravshipPlacementUtility),nameof(GravshipPlacementUtility.PlaceGravshipInMap))]
 public static class BunkerArrivalMap
 {
  static void Postfix(Gravship gravship,Map map)
  {
   foreach(var hatch in gravship.Things.OfType<AncientHatch>())if(hatch.Spawned&&hatch.PocketMap?.Parent is PocketMapParent parent)parent.sourceMap=map;
  }
 }
}

