using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using UnityEngine;
using SeamlessAncientBunkers;
using ProcessorFramework;
using AIRobot;
namespace SABRuntimeTests
{
 public partial class RuntimeTests
 {
  private X2_AIRobot robot;private Thing processor;private Pawn attacker;private int workStarted;private string robotName;private bool enemySaved;
  private void ExposeExpansion(){Scribe_References.Look(ref robot,"expRobot");Scribe_References.Look(ref processor,"expProcessor");Scribe_References.Look(ref attacker,"expAttacker");Scribe_Values.Look(ref workStarted,"expWorkStarted");Scribe_Values.Look(ref robotName,"expRobotName");Scribe_Values.Look(ref enemySaved,"expEnemySaved");}
  private void ExpansionTick()
  {
   if(Manager.Now>deadline)throw new Exception("Expansion timeout stage="+stage+" pawn="+pawn.CurJob+" map="+pawn.Map+" robot="+robot?.CurJob+" robotMap="+robot?.Map+" enemy="+attacker?.CurJob);
   if(Manager.Now%3000==0)Report("Expansion stage="+stage+" pawn="+pawn.CurJob+" robot="+robot?.CurJob);
   switch(stage)
   {
    case 300:
     Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.debug=true;BunkerMod.Settings.scanInterval=120;BunkerMod.Settings.minStay=120;Manager.Current.Toggle(hatch);
     foreach(var p in Find.Maps.SelectMany(m=>m.mapPawns.FreeColonistsSpawned).ToList()){p.drafter.Drafted=true;p.jobs.StopAll();}
     do{pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false));}while(pawn.GetDisabledWorkTypes().Count>0);
     GenSpawn.Spawn(pawn,surface.Center+new IntVec3(8,0,3),surface);ResetPawn(DefDatabase<WorkTypeDef>.GetNamed("QuarryMining"));
     Clear(bunker,hatch.exit.Position+new IntVec3(-8,0,-7),5);testTarget=Spawn("QRY_MiniQuarry",bunker,hatch.exit.Position+new IntVec3(-8,0,-7));Pass("Quarry remote job fixture",301);break;
    case 301:
     if(pawn.Map==bunker&&pawn.CurJobDef?.defName=="QRY_MineQuarry")
     {Report("PASS automatic remote Quarry custom job starts");workStarted=Manager.Now;Pass("Quarry executes original driver",302);}break;
    case 302:
     if(Manager.Now-workStarted>=400)
     {if(pawn.CurJobDef?.defName!="QRY_MineQuarry")throw new Exception("Quarry work stopped unexpectedly");Report("PASS Quarry original driver works after arrival");ResetPawn();testTarget.Destroy();Travel(surface);Pass("Processor return",303);}break;
    case 303:
     if(pawn.Map==surface)
     {
      ResetPawn(WorkTypeDefOf.Hauling);processor=Spawn("SABTestProcessor",bunker,hatch.exit.Position+new IntVec3(5,0,5));processor.TryGetComp<CompProcessor>().EnableAllProcesses();var input=Spawn("Steel",surface,surface.Center+new IntVec3(6,0,5));input.stackCount=5;Pass("Processor remote ingredient supply and fill fixture",304);
     }break;
    case 304:
     if(processor.TryGetComp<CompProcessor>().activeProcesses.Count>0)
     {Report("PASS Processor remote ingredient supply followed by automatic custom fill");ResetPawn();Travel(surface);Pass("Processor empty return",305);}break;
    case 305:
     if(pawn.Map==surface)
     {ResetPawn(WorkTypeDefOf.Hauling);foreach(var process in processor.TryGetComp<CompProcessor>().activeProcesses)process.ActiveProcessTicks=60000;Pass("Processor empty fixture",306);}break;
    case 306:
     if(bunker.listerThings.ThingsOfDef(ThingDefOf.ComponentIndustrial).Any(t=>t.Position.DistanceTo(processor.Position)<6))
     {
      Report("PASS automatic remote Processor empty produces output");ResetPawn();pawn.drafter.Drafted=true;processor.Destroy();
      var station=(X2_Building_AIRobotRechargeStation)Spawn("AIRobot_RechargeStation_Cleaner",surface,surface.Center+new IntVec3(-6,0,-5));robot=X2_Building_AIRobotCreator.CreateRobot("AIRobot_Cleaner",station.Position+IntVec3.East,surface,Faction.OfPlayer);station.robot=robot;robot.rechargeStation=station;robot.needs.rest.CurLevel=1;robotName=robot.Name.ToString();
      foreach(var c in GenRadial.RadialCellsAround(hatch.exit.Position+new IntVec3(5,0,-5),3,true)){bunker.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);bunker.areaManager.Home[c]=true;}
      for(int n=0;n<5;n++){var cell=hatch.exit.Position+new IntVec3(4+n,0,-5);FilthMaker.TryMakeFilth(cell,bunker,ThingDefOf.Filth_Dirt);foreach(var filth in cell.GetThingList(bunker).OfType<Filth>())AccessTools.Field(typeof(Filth),"growTick").SetValue(filth,Manager.Now-1000);}
      Pass("Misc Robots remote cleaning fixture",307);
     }break;
    case 307:
     if(robot.Map==bunker&&robot.CurJobDef==JobDefOf.Clean)
     {if(robot.Name.ToString()!=robotName||robot.rechargeStation.Map!=surface)throw new Exception("Robot identity/station changed");Report("PASS Misc Robots cleaner crosses and starts ordinary cleaning; name/station preserved");robot.needs.rest.CurLevel=0.30f;robot.jobs.EndCurrentJob(JobCondition.InterruptForced);Pass("robot return to remote charger",308);}break;
    case 308:
     if(robot.Map==surface)
     {Report("PASS robot returns through hatch for low battery");Pass("robot native recharge job",309);}break;
    case 309:
     if(robot.CurJobDef?.defName=="AIRobot_GoRecharge"||!robot.Spawned)
     {
      Report("PASS robot resumes native recharge behavior");robot.rechargeStation.SpawnRobotAfterRecharge=false;robot.needs.rest.CurLevel=1;
      var station=(X2_Building_AIRobotRechargeStation)Spawn("AIRobot_RechargeStation_Hauler",surface,surface.Center+new IntVec3(-6,0,6));robot=X2_Building_AIRobotCreator.CreateRobot("AIRobot_Hauler",station.Position+IntVec3.East,surface,Faction.OfPlayer);station.robot=robot;robot.rechargeStation=station;robot.needs.rest.CurLevel=1;
      foreach(var map in new[]{surface,bunker})foreach(var group in map.haulDestinationManager.AllGroupsListInPriorityOrder)group.Settings.filter.SetDisallowAll();
      var zone=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,bunker.zoneManager);bunker.zoneManager.RegisterZone(zone);zone.AddCell(hatch.exit.Position+new IntVec3(7,0,-2));zone.settings.filter.SetDisallowAll();zone.settings.filter.SetAllow(ThingDefOf.Silver,true);zone.settings.Priority=StoragePriority.Critical;
      var silver=Spawn("Silver",surface,surface.Center+new IntVec3(6,0,6));silver.stackCount=17;Pass("Misc Robots hauler fixture",312);
     }break;
    case 312:
     if(bunker.listerThings.ThingsOfDef(ThingDefOf.Silver).Sum(t=>t.stackCount)==17)
     {
      if(surface.listerThings.ThingsOfDef(ThingDefOf.Silver).Any()||robot.carryTracker.CarriedThing!=null)throw new Exception("Robot cargo not conserved");Report("PASS Misc Robots hauler carries and places exactly 17 silver across maps");
      EnemyFixture();
     }break;
    case 313:
     Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;foreach(var colonist in Find.Maps.SelectMany(m=>m.mapPawns.FreeColonistsSpawned)){colonist.drafter.Drafted=true;colonist.jobs.StopAll();}EnemyFixture();break;
    case 310:
     if(!enemySaved&&attacker.Map==surface&&attacker.CurJobDef?.defName=="SAB_Pursue")
     {enemySaved=true;LongEventHandler.QueueLongEvent(()=>{GameDataSaveLoader.SaveGame("SAB-EnemyTransit");GameDataSaveLoader.LoadGame("SAB-EnemyTransit");Report("PASS enemy pursuit save/reload");},null,false,null);break;}
     if(attacker.Map==bunker)
     {if(attacker.GetLord()?.Map!=bunker)throw new Exception("Enemy retained wrong-map lord");if(PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.Count(p=>p.ThingID==attacker.ThingID)!=1)throw new Exception("Enemy duplicate");Report("PASS assaulting enemy follows colonist into forbidden, traffic-disabled bunker; destination lord and identity valid");workStarted=Manager.Now;Pass("enemy attacks after entry",311);}break;
    case 311:
     if(attacker.mindState.enemyTarget is Pawn target&&target.Map==bunker)
     {Report("PASS enemy reacquires bunker pawn as combat target");attacker.GetLord()?.Notify_PawnLost(attacker,PawnLostCondition.ExitedMap);attacker.Destroy();hatch.TrySetForbidden(false);hatch.exit.TrySetForbidden(false);Manager.Current.Toggle(hatch);Report("EXTENSION JOBS ROBOTS ENEMIES COMPLETE");if(GenCommandLine.CommandLineArgPassed("sab-pursuit-door-test")){Report("ENEMY DOOR AND SAVE TEST COMPLETE");done=true;Application.Quit();return;}Pass("gravship fixture",320);}break;
    default:GravshipTick();break;
   }
  }
  private void EnemyFixture(){
      foreach(var resident in surface.mapPawns.AllPawnsSpawned.Where(p=>p.Faction==Faction.OfPlayer).ToList()){resident.DeSpawn();GenSpawn.Spawn(resident,hatch.exit.Position+new IntVec3(4,0,4),bunker);} var pirate=Find.FactionManager.AllFactions.First(f=>f.HostileTo(Faction.OfPlayer)&&f.def.humanlikeFaction);attacker=PawnGenerator.GeneratePawn(PawnKindDefOf.Pirate,pirate);GenSpawn.Spawn(attacker,surface.Center+new IntVec3(10,0,0),surface);LordMaker.MakeNewLord(pirate,new LordJob_AssaultColony(pirate,canKidnap:false,canTimeoutOrFlee:false,canSteal:false),surface,new[]{attacker});
      foreach(var cell in CellRect.CenteredOn(hatch.Position,3).EdgeCells)
      {var wall=Spawn(cell==hatch.Position+new IntVec3(3,0,0)?"Door":"Wall",surface,cell,ThingDefOf.Steel);if(wall is Building_Door)wall.HitPoints=10;}
      Manager.Current.enabledPortals.Clear();hatch.TrySetForbidden(true);hatch.exit.TrySetForbidden(true);Pass("enemy pursuit through a closed door ignores colony traffic controls",310);
  }
  private void GravshipTick()
  {
   switch(stage)
   {
    case 320:
     {
      ResetPawn();pawn.drafter.Drafted=false;
      var center=surface.Center+new IntVec3(25,0,20);Clear(surface,center,10);
      foreach(var cell in CellRect.CenteredOn(center,6))surface.terrainGrid.SetFoundation(cell,DefDatabase<TerrainDef>.GetNamed("Substructure"));
      var engine=(Building_GravEngine)Spawn("GravEngine",surface,center);Spawn("PilotConsole",surface,center+new IntVec3(3,0,3));
      engine.launchInfo=new LaunchInfo{pilot=pawn,quality=1};engine.ForceSubstructureDirty();
      if(engine.ValidSubstructure.Count<20)throw new Exception("Missing valid ship substructure");
      if(GravshipSupport.BlockReason(engine)==null)throw new Exception("Occupied bunker did not block unanchored departure");
      Find.World.GetComponent<WorldComponent_GravshipController>().InitiateTakeoff(engine,surface.Tile);
      if(WorldComponent_GravshipController.CutsceneInProgress)throw new Exception("Unsafe takeoff was allowed");
      Report("PASS unanchored launch is rejected while colony pawns remain in bunker");
      Spawn("GravAnchor",surface,surface.Center+new IntVec3(-15,0,15));if(GravshipSupport.BlockReason(engine)!=null)throw new Exception("Anchor did not permit departure");
      pawn.DeSpawn();GenSpawn.Spawn(pawn,center+new IntVec3(2,0,0),surface);ResetPawn();
      var cargo=ThingMaker.MakeThing(ThingDefOf.Steel);cargo.stackCount=25;pawn.inventory.innerContainer.TryAdd(cargo);Travel(bunker);
      var ship=GravshipUtility.GenerateGravship(engine);
      if(pawn.Spawned||!ship.Pawns.Contains(pawn)||Broker.For(pawn)!=null||pawn.CurJobDef==Manager.TravelDef)throw new Exception("Departure left a stale bunker trip or failed to board pawn");
      if(!hatch.Spawned||!Find.Maps.Contains(bunker))throw new Exception("Anchored launch lost bunker");
      var neighbors=new System.Collections.Generic.List<PlanetTile>();Find.WorldGrid.GetTileNeighbors(surface.Tile,neighbors);GravshipUtility.TravelTo(ship,surface.Tile,neighbors.First());
      Report("PASS actual gravship extraction/world departure cancels active bunker trip and keeps onboard pawn/inventory");
      stage=321;deadline=Manager.Now+18000;
      LongEventHandler.QueueLongEvent(()=>{GameDataSaveLoader.SaveGame("SAB-GravshipFlight");Report("GRAVSHIP FLIGHT SAVED: restart this isolated save to verify landing");done=true;Application.Quit();},null,false,null);
     }break;
    case 321:
     {
      var ship=Current.Game.Gravship;if(ship==null||!ship.Pawns.Contains(pawn))throw new Exception("In-flight save lost gravship pawn");
      if(pawn.inventory.innerContainer.TotalStackCountOfDef(ThingDefOf.Steel)!=25)throw new Exception("In-flight inventory mismatch");
      Report("PASS gravship in-flight save/reload preserves pawn and 25 steel");
      var landing=surface.Center+new IntVec3(45,0,20);Clear(surface,landing,9);GravshipPlacementUtility.PlaceGravshipInMap(ship,landing,surface,out var spawned);
      Find.WorldObjects.Remove(ship);Current.Game.Gravship=null;
      if(!pawn.Spawned||pawn.Map!=surface||PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.Count(p=>p.ThingID==pawn.ThingID)!=1)throw new Exception("Landing pawn identity invalid");
      CameraJumper.TryJump(pawn);Report("PASS actual gravship landing restores one pawn without stale route");ResetPawn();Travel(bunker);Pass("post-landing bunker route",322);
     }break;
    case 322:
     if(pawn.Map==bunker){Report("PASS normal bunker transit still works after gravship launch/save/load/landing");Manager.Current.Cleanup();Report("EXTENSION ACCEPTANCE COMPLETE");done=true;Application.Quit();}break;
   }
  }
 }
}




