using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;
namespace SABTransitTests
{
 [StaticConstructorOnStartup]
 public static class HaulMenuLayout
 {
  static HaulMenuLayout(){if(GenCommandLine.CommandLineArgPassed("sab-haul-order-tests"))new Harmony("colet.sabhaulmenu.fixture").Patch(AccessTools.Method(typeof(FloatMenuOption),nameof(FloatMenuOption.SetSizeMode)),prefix:new HarmonyMethod(typeof(HaulMenuLayout),nameof(Prefix)));}
  public static bool Prefix()=>Event.current!=null;
 }
 public class HaulOrderProbe:GameComponent
 {
  int stage,started,logged;AncientHatch hatch;Pawn worker;Thing item;Map destination;IntVec3 cell;
  public HaulOrderProbe(Game game){}
  void Check(bool ok,string message){if(!ok)throw new Exception(message);Log.Message("[SAB HAUL ORDER] PASS "+message);}
  IntVec3 Cell(Map map)=>CellFinder.StandableCellNear(map==hatch.Map?hatch.Position:hatch.exit.Position,map,8);
  void Order(Map workerMap,Map source,Map storage)
  {
   worker.jobs.StopAll();var active=Broker.For(worker);if(active!=null)Broker.Cancel(active);if(worker.Map!=workerMap){worker.DeSpawn();GenSpawn.Spawn(worker,Cell(workerMap),workerMap);}Manager.Current.cooldowns.Clear();
   foreach(var map in new[]{hatch.Map,hatch.PocketMap})foreach(var group in map.haulDestinationManager.AllGroupsListInPriorityOrder)group.Settings.filter.SetDisallowAll();
   if(stage==1)
   {
    BunkerMod.Settings.maxCandidates=8;
    var full=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,storage.zoneManager);storage.zoneManager.RegisterZone(full);full.GetStoreSettings().filter.SetDisallowAll();full.GetStoreSettings().filter.SetAllow(ThingDef.Named("Meat_Megaspider"),true);full.GetStoreSettings().Priority=StoragePriority.Critical;
    foreach(var c in GenRadial.RadialCellsAround(Cell(storage),20,true).Where(c=>c.InBounds(storage)&&c.Standable(storage)&&!c.Fogged(storage)&&c.GetEdifice(storage)==null&&c.GetFirstItem(storage)==null&&c.GetZone(storage)==null).Take(12).ToList()){full.AddCell(c);var block=ThingMaker.MakeThing(ThingDefOf.Gold);GenSpawn.Spawn(block,c,storage);}
   }
   cell=GenRadial.RadialCellsAround(Cell(storage),15,true).First(c=>c.InBounds(storage)&&c.Standable(storage)&&!c.Fogged(storage)&&c.GetEdifice(storage)==null&&c.GetFirstItem(storage)==null&&c.GetZone(storage)==null);
   var zone=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,storage.zoneManager);storage.zoneManager.RegisterZone(zone);zone.AddCell(cell);zone.GetStoreSettings().filter.SetDisallowAll();zone.GetStoreSettings().filter.SetAllow(ThingDef.Named("Meat_Megaspider"),true);zone.GetStoreSettings().Priority=StoragePriority.Important;
   item=ThingMaker.MakeThing(ThingDef.Named("Meat_Megaspider"));item.stackCount=17;GenSpawn.Spawn(item,Cell(source),source);source.areaManager.Home[item.Position]=false;destination=storage;
   Current.Game.CurrentMap=workerMap;Find.Selector.ClearSelection();Find.Selector.Select(worker);BunkerLevels.Entered(hatch.PocketMap);if(source!=workerMap)BunkerLevels.Switch(source==hatch.Map);Check(Find.CurrentMap==source,"view is on clicked item floor");
   var options=FloatMenuMakerMap.GetOptions(new List<Pawn>{worker},item.DrawPos,out _);Log.Message("[SAB HAUL ORDER] menu "+string.Join(" | ",options.Select(o=>o.Label+(o.Disabled?" DISABLED":""))));
   Check(!RemotePawnScope.Active&&worker.Map==workerMap&&Broker.For(worker)==null,"native menu preserves real pawn map and creates no intent");
   var haul=options.FirstOrDefault(o=>!o.Disabled&&o.Label.IndexOf("Prioritize hauling",StringComparison.OrdinalIgnoreCase)>=0);Check(haul!=null,"native manual haul option offered stage "+stage);haul.action();Check(Broker.For(worker)!=null,"manual order creates physical journey stage "+stage);started=Manager.Now;
  }
  public override void GameComponentTick()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-haul-order-tests")||stage==99||Current.ProgramState!=ProgramState.Playing)return;
   try
   {
    Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
    if(stage==0)
    {
     hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.haul=false;BunkerMod.Settings.work=false;
     foreach(var map in new[]{hatch.Map,hatch.PocketMap})foreach(var p in map.mapPawns.AllPawnsSpawned.ToList())p.Destroy();Manager.Current.EnableClearedBunkers();
     do{worker=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false));}while(worker.WorkTypeIsDisabled(WorkTypeDefOf.Hauling));GenSpawn.Spawn(worker,Cell(hatch.Map),hatch.Map);worker.workSettings.EnableAndInitialize();foreach(var d in DefDatabase<WorkTypeDef>.AllDefs)worker.workSettings.SetPriority(d,d==WorkTypeDefOf.Hauling?1:0);
     stage=1;Order(hatch.Map,hatch.Map,hatch.PocketMap);
    }
    worker.needs.food.CurLevelPercentage=1;worker.needs.rest.CurLevelPercentage=1;
    if(item.Spawned&&item.Map==destination&&item.Position==cell&&worker.carryTracker.CarriedThing==null)
    {
     Check(item.stackCount==17,"manual haul physically delivers exact stack stage "+stage);item.Destroy();
     if(stage==1){stage=2;Order(hatch.Map,hatch.PocketMap,hatch.Map);}
     else if(stage==2){stage=3;Order(hatch.PocketMap,hatch.Map,hatch.PocketMap);}
     else if(stage==3){stage=4;Order(hatch.PocketMap,hatch.PocketMap,hatch.Map);}
     else{stage=99;Log.Message("[SAB HAUL ORDER] ACCEPTANCE COMPLETE");Application.Quit();}
    }
    if(Manager.Now>=logged){logged=Manager.Now+1200;Log.Message("[SAB HAUL ORDER] stage="+stage+" job="+worker.CurJob+" intent="+Broker.For(worker)?.purpose);}
    if(Manager.Now-started>14000)throw new Exception("Timeout stage="+stage);
   }
   catch(Exception e){stage=99;Log.Error("[SAB HAUL ORDER] FAIL "+e);Application.Quit();}
  }
 }
}
