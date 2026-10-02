using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SABResourceProbe
{
 public class ResourceProbe : GameComponent
 {
  int stage;
  AncientHatch first, second;
  Pawn worker;
  IntVec3 buildCell;
  int deadline;
  bool crossedWithCargo;
  public ResourceProbe(Game game) { }
  public override void ExposeData()
  {
   Scribe_Values.Look(ref stage,"resourceStage");
   Scribe_References.Look(ref first,"resourceFirst");
   Scribe_References.Look(ref second,"resourceSecond");
  }
  void Check(bool value,string message)
  {
   if(!value) throw new Exception(message);
   Log.Message("[SAB RESOURCE TEST] PASS "+message);
  }
  IntVec3 Cell(Map map)
  {
   return map.AllCells.First(c=>c.DistanceTo(map.Center)<20 && c.Standable(map)&&!c.Fogged(map)&&c.GetEdifice(map)==null&&c.GetZone(map)==null&&c.GetThingList(map).Count==0);
  }
  Thing Stock(Map map,int count)
  {
   var cell=Cell(map);
   var zone=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,map.zoneManager);
   map.zoneManager.RegisterZone(zone);zone.AddCell(cell);
   var steel=ThingMaker.MakeThing(ThingDefOf.Steel);steel.stackCount=count;
   GenSpawn.Spawn(steel,cell,map);map.resourceCounter.UpdateResourceCounts();return steel;
  }
  void Counts(int total)
  {
   foreach(var map in new[]{first.Map,first.PocketMap,second.PocketMap})
   {
    Check(map.resourceCounter.GetCount(ThingDefOf.Steel)==total,"exact linked steel total on map "+map.uniqueID);
    Check(map.resourceCounter.AllCountedAmounts[ThingDefOf.Steel]==total,"dictionary agrees on map "+map.uniqueID);
   }
  }
  public override void GameComponentUpdate()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-resource-tests")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||stage==3)return;
   Application.runInBackground=true;
   foreach(var window in Find.WindowStack.Windows.ToList())if(window.forcePause)Find.WindowStack.TryRemove(window,false);
   Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
   try
   {
    if(stage==0)
    {
     BunkerMod.Settings.enabled=true;
     var roots=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).Where(h=>Portal.Other(h)!=null).ToList();
     first=roots[0];second=roots.First(h=>h!=first&&h.Map==first.Map);
     foreach(var map in Find.Maps)
     {
      map.fogGrid.ClearAllFog();
      foreach(var steel in map.listerThings.ThingsOfDef(ThingDefOf.Steel).ToList())steel.Destroy();
      map.resourceCounter.UpdateResourceCounts();
     }
     Stock(first.Map,37);Stock(second.PocketMap,13);
     Check(LinkedResources.Maps(first.PocketMap).Count==3,"two-hop sibling bunker connection includes three unique maps");
     Counts(50);
     Check(LinkedResources.LocalCount(first.PocketMap,ThingDefOf.Steel)==0,"bunker physical stock remains empty");
     Check(first.PocketMap.listerThings.ThingsOfDef(ThingDefOf.Steel).Count==0,"global thing list remains local");
     var amounts=first.PocketMap.resourceCounter.AllCountedAmounts;amounts[ThingDefOf.Steel]=999;
     Counts(50);
     first.Map.resourceCounter.UpdateResourceCounts();Counts(50);
     var group=ThingRequestGroup.HaulableEver;
     int expected=new[]{first.Map,first.PocketMap,second.PocketMap}.Sum(m=>LinkedResources.Amounts(new System.Collections.Generic.List<Map>{m}).Where(p=>group.Includes(p.Key)).Sum(p=>p.Value));
     Check(first.PocketMap.resourceCounter.GetCountIn(group)==expected,"request-group count is aggregated once");
     var cat=ThingDefOf.Steel.thingCategories.First();
     BunkerMod.Settings.enabled=false;
     int categoryExpected=new[]{first.Map,first.PocketMap,second.PocketMap}.Sum(m=>m.resourceCounter.GetCountIn(cat));
     Check(first.PocketMap.resourceCounter.GetCount(ThingDefOf.Steel)==0,"global disable restores local count");
     BunkerMod.Settings.enabled=true;
     Check(first.PocketMap.resourceCounter.GetCountIn(cat)==categoryExpected,"category recursion does not double count");
     Current.Game.CurrentMap=first.PocketMap;
     var build=new Designator_Build(ThingDefOf.Wall);
     build.ProcessInput(new Event());
     Check(Find.DesignatorManager.SelectedDesignator==build,"real build menu selects designator with remote-only materials");
     var menu=Find.WindowStack.Windows.OfType<FloatMenu>().Last();
     var options=AccessTools.Field(typeof(FloatMenu),"options").GetValue(menu) as System.Collections.Generic.List<FloatMenuOption>;
     var steelOption=options.FirstOrDefault(o=>o.tutorTag=="SelectStuff-Wall-Steel");
     Check(steelOption!=null,"real material menu includes remote stockpiled steel");steelOption.action();
     Find.WindowStack.TryRemove(menu,false);
     var target=Cell(first.PocketMap);first.PocketMap.terrainGrid.SetTerrain(target,TerrainDefOf.Concrete);
     Check(build.CanDesignateCell(target).Accepted,"normal bunker placement accepts valid floor");
     build.DesignateSingleCell(target);
     Check(target.GetThingList(first.PocketMap).OfType<Blueprint_Build>().Any(),"real designator creates bunker blueprint");
     var water=Cell(first.PocketMap);first.PocketMap.terrainGrid.SetTerrain(water,TerrainDef.Named("WaterDeep")); Check(!build.CanDesignateCell(water).Accepted,"unsupported terrain still rejected");
     Check(!build.CanDesignateCell(new IntVec3(-1,0,-1)).Accepted,"out-of-bounds placement still rejected");
     var exit=second.exit;var pos=exit.Position;var destination=exit.Map;exit.DeSpawn();
     Check(first.PocketMap.resourceCounter.GetCount(ThingDefOf.Steel)==37,"removed portal immediately splits linked totals");
     Check(destination.resourceCounter.GetCount(ThingDefOf.Steel)==13,"disconnected same-location bunker keeps own stock");
     Check(LinkedResources.Maps(destination).Count==1,"level metadata cannot reconnect an orphan map");
     GenSpawn.Spawn(exit,pos,destination);Counts(50);
     var entrance=exit.entrance;exit.entrance=null;
     Check(first.Map.resourceCounter.GetCount(ThingDefOf.Steel)==37,"nonreciprocal portal reference excluded");
     exit.entrance=entrance;
     stage=1;
     LongEventHandler.QueueLongEvent(()=>{GameDataSaveLoader.SaveGame("SAB-Resources");GameDataSaveLoader.LoadGame("SAB-Resources");},null,false,null);
    }
    else if(stage==1)
    {
     Counts(50);
     Check(LinkedResources.LocalCount(first.Map,ThingDefOf.Steel)==37&&LinkedResources.LocalCount(second.PocketMap,ThingDefOf.Steel)==13,"physical stock conserved across save/reload");
     // Disposable end-to-end fixture: leave portals intact but remove competing work/threats.
     foreach(var map in Find.Maps)
     foreach(var thing in map.listerThings.AllThings.ToList())
     {
      if(thing is MapPortal)continue;
      if(thing is Pawn || thing.def.category==ThingCategory.Building || thing.def.category==ThingCategory.Item || thing.def.IsBlueprint || thing.def.IsFrame)thing.Destroy();
     }
     foreach(var map in new[]{first.Map,first.PocketMap})
     {
      var portal=map==first.Map?(MapPortal)first:first.exit;
      foreach(var c in GenRadial.RadialCellsAround(portal.Position,12,true).Where(c=>c.InBounds(map)))map.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);
      foreach(var zone in map.zoneManager.AllZones.ToList())zone.Delete();
     }
     do {worker=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);}while(worker.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)||worker.WorkTypeIsDisabled(WorkTypeDefOf.Construction));
     GenSpawn.Spawn(worker,first.Position+IntVec3.East*3,first.Map);
     worker.skills.GetSkill(SkillDefOf.Construction).Level=20;
     foreach(var work in DefDatabase<WorkTypeDef>.AllDefsListForReading)if(!worker.WorkTypeIsDisabled(work))worker.workSettings.SetPriority(work,work==WorkTypeDefOf.Hauling||work==WorkTypeDefOf.Construction?1:0);
     var stockCell=first.Position+IntVec3.East*4;
     var zone2=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,first.Map.zoneManager);first.Map.zoneManager.RegisterZone(zone2);zone2.AddCell(stockCell);
     var steel=ThingMaker.MakeThing(ThingDefOf.Steel);steel.stackCount=5;GenSpawn.Spawn(steel,stockCell,first.Map);
     buildCell=first.exit.Position+IntVec3.East*5;
     GenConstruct.PlaceBlueprintForBuild(ThingDefOf.Wall,buildCell,first.PocketMap,Rot4.North,Faction.OfPlayer,ThingDefOf.Steel);
     Manager.Current.intents.Clear();Manager.Current.cooldowns.Clear();Manager.Current.enabledPortals.Clear();Manager.Current.enabledPortals.Add(first);
     BunkerMod.Settings.haul=true;BunkerMod.Settings.work=true;BunkerMod.Settings.scanInterval=120;BunkerMod.Settings.minStay=300;
     foreach(var map in Find.Maps)map.resourceCounter.UpdateResourceCounts();
     Check(first.PocketMap.resourceCounter.GetCount(ThingDefOf.Steel)==5,"construction sees five remote steel before delivery");
     Check(Graph.Reachable(worker).Any(r=>r.map==first.PocketMap),"builder has a safe real portal route");
     deadline=Manager.Now+20000;stage=2;
    }
    else
    {
     worker.needs.food.CurLevel=1;worker.needs.rest.CurLevel=1;
     if(worker.Map==first.PocketMap&&worker.carryTracker.CarriedThing?.def==ThingDefOf.Steel)crossedWithCargo=true;
     var completed=buildCell.GetEdifice(first.PocketMap);
     if(completed?.def==ThingDefOf.Wall)
     {
      Check(crossedWithCargo,"pawn physically carried steel through the bunker portal");
      Check(completed.Stuff==ThingDefOf.Steel,"vanilla construction finished the steel wall");
      Check(Find.Maps.Sum(m=>m.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t=>t.stackCount))==0&&worker.carryTracker.CarriedThing==null,"construction consumed exactly the five delivered steel");
      stage=3;Log.Message("[SAB RESOURCE TEST] ACCEPTANCE COMPLETE");Application.Quit();
     }
     else if(Manager.Now>deadline)throw new Exception("Construction timed out: "+worker.CurJob+" map="+worker.Map+" intent="+Broker.For(worker)?.purpose);
     Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
    }
   }
   catch(Exception e){stage=3;Log.Error("[SAB RESOURCE TEST] FAIL "+e);Application.Quit();}
  }
 }
}

