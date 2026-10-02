using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
namespace SABTransitTests
{
 public class SavedBedProbe:GameComponent
 {
  bool done;
  public SavedBedProbe(Game game){}
  public override void GameComponentUpdate(){GameComponentTick();}
  public override void GameComponentTick()
  {
   if(done||!GenCommandLine.CommandLineArgPassed("sab-saved-bed-tests")||Current.ProgramState!=ProgramState.Playing)return;done=true;
   try
   {
    Log.Message("[SAB SAVED BEDS] version="+typeof(Manager).Assembly.GetName().Version+" enabled="+BunkerMod.Settings.enabled+" days="+GenDate.DaysPassed+" alert="+new Alert_NeedColonistBeds().GetReport().active);
    foreach(var map in Find.Maps)
    {
     Alert_NeedColonistBeds.AvailableColonistBeds(map,false,out int single,out int dbl,out int crib);
     Log.Message("[SAB SAVED BEDS] map="+map.uniqueID+" home="+map.IsPlayerHome+" linked="+string.Join(",",LinkedResources.Maps(map).Select(m=>m.uniqueID))+" single="+single+" double="+dbl+" crib="+crib);
     foreach(var p in map.mapPawns.FreeColonistsSpawned)Log.Message("[SAB SAVED BEDS] pawn="+p+" race="+p.def+" rest="+(p.needs?.rest!=null)+" slave="+p.IsSlave+" owned="+p.ownership?.OwnedBed+" partners="+string.Join(",",LovePartnerRelationUtility.ExistingLovePartners(p,false).Select(r=>r.otherPawn.ToString())));
     foreach(var b in map.listerThings.AllThings.OfType<Building_Bed>())Log.Message("[SAB SAVED BEDS] bed="+b+" faction="+b.Faction+" listed="+map.listerBuildings.allBuildingsColonist.Contains(b)+" colonist="+b.ForColonists+" medical="+b.Medical+" human="+b.def.building.bed_humanlike+" slots="+b.SleepingSlotsCount+" baby="+b.ForHumanBabies);
     foreach(var portal in map.listerThings.ThingsInGroup(ThingRequestGroup.MapPortal).OfType<MapPortal>())Log.Message("[SAB SAVED BEDS] portal="+portal+" other="+Portal.Other(portal)+" fog="+portal.Fogged()+" enterable="+portal.IsEnterable(out _));
    }
    var patches=Harmony.GetPatchInfo(AccessTools.Method(typeof(Alert_NeedColonistBeds),nameof(Alert_NeedColonistBeds.AvailableColonistBeds)));Log.Message("[SAB SAVED BEDS] patches="+string.Join(",",patches.Owners));
    var surface=Find.Maps.First(m=>m.IsPlayerHome);var linked=LinkedResources.Maps(surface);
    var replacement=AccessTools.Method(AccessTools.TypeByName("BunkBeds.Alert_NeedColonistBeds_AvailableColonistBeds_Patch"),"Prefix");
    if(replacement==null)throw new Exception("Expected installed BunkBeds replacement");
    var harmony=new Harmony("colet.seamlessancientbunkers");
    harmony.Unpatch(replacement,HarmonyPatchType.Transpiler,harmony.Id);
    foreach(var pawn in linked.Where(m=>m!=surface).SelectMany(m=>m.mapPawns.FreeColonistsSpawned).ToList())
    {pawn.jobs.StopAll();pawn.DeSpawn();GenSpawn.Spawn(pawn,CellFinder.StandableCellNear(surface.Center,surface,20),surface);}
    if(!new Alert_NeedColonistBeds().GetReport().active)throw new Exception("Baseline did not reproduce surface warning");
    Log.Message("[SAB SAVED BEDS] PASS reproduces false warning with all three colonists upstairs and assigned bed downstairs");
    harmony.Patch(replacement,transpiler:new HarmonyMethod(typeof(LinkedBedAvailability),nameof(LinkedBedAvailability.Transpiler)));
    if(new Alert_NeedColonistBeds().GetReport().active)throw new Exception("Connected BunkBeds warning remains active");
    foreach(var map in linked){Alert_NeedColonistBeds.AvailableColonistBeds(map,false,out int s,out int d,out _);if(s!=6||d!=0)throw new Exception("Expected six spare connected single beds, got "+s+","+d);}
    Log.Message("[SAB SAVED BEDS] PASS fixed warning clears and both floors report six spare beds");
    foreach(var bed in linked.SelectMany(m=>m.listerThings.AllThings.OfType<Building_Bed>()).Where(b=>b.Faction==Faction.OfPlayer&&b.ForColonists&&!b.Medical).ToList())bed.Medical=true;
    if(!new Alert_NeedColonistBeds().GetReport().active)throw new Exception("Real shortage was hidden");
    Log.Message("[SAB SAVED BEDS] PASS genuine shortage remains visible when ordinary beds become medical");
    Log.Message("[SAB SAVED BEDS] COMPLETE");
   }catch(Exception e){Log.Error("[SAB SAVED BEDS] FAIL "+e);}Application.Quit();
  }
 }
}
