using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SABServicesProbe
{
 public class AutomaticHaulProbe:GameComponent
 {
  int stage,start,nextLog;bool fetched;AncientHatch hatch;Pawn worker;Thing item;Map destination;IntVec3 storeCell;readonly List<Thing> blockers=new List<Thing>();
  public AutomaticHaulProbe(Game game){}
  bool Active=>GenCommandLine.CommandLineArgPassed("sab-services-automatic-haul-tests");
  void Check(bool ok,string label){if(!ok)throw new Exception(label);Log.Message("[SAB AUTO HAUL] PASS "+label);}
  IntVec3 Center(Map map)=>(map==hatch.Map?hatch.Position:hatch.exit.Position)+new IntVec3(4,0,4);
  IntVec3 Empty(Map map)=>GenRadial.RadialCellsAround(Center(map),22,true).First(c=>c.InBounds(map)&&c.Standable(map)&&c.GetEdifice(map)==null&&c.GetFirstItem(map)==null&&c.GetZone(map)==null);
  Thing Spawn(ThingDef def,Map map,int count=1,IntVec3? at=null)
  {var t=ThingMaker.MakeThing(def);t.stackCount=count;GenSpawn.Spawn(t,at??Empty(map),map);t.SetForbidden(false,false);return t;}
  Zone_Stockpile Zone(Map map,StoragePriority priority,int count,bool block)
  {
   var zone=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,map.zoneManager);map.zoneManager.RegisterZone(zone);
   zone.GetStoreSettings().filter.SetDisallowAll();zone.GetStoreSettings().filter.SetAllow(ThingDef.Named("Meat_Megaspider"),true);zone.GetStoreSettings().Priority=priority;
   for(int n=0;n<count;n++){var c=Empty(map);zone.AddCell(c);if(block)blockers.Add(Spawn(ThingDefOf.Steel,map,75,c));}
   return zone;
  }
  void Reset(Map pawnMap,Map sourceMap,Map storageMap)
  {
   worker.jobs.StopAll();var old=Broker.For(worker);if(old!=null)Broker.Cancel(old);if(worker.carryTracker.CarriedThing!=null)worker.carryTracker.TryDropCarriedThing(worker.Position,ThingPlaceMode.Near,out _);
   foreach(var map in new[]{hatch.Map,hatch.PocketMap})foreach(var zone in map.zoneManager.AllZones.OfType<Zone_Stockpile>().ToList())zone.Delete();
   foreach(var b in blockers)if(!b.Destroyed)b.Destroy();blockers.Clear();if(item?.Destroyed==false)item.Destroy();
   worker.DeSpawn();GenSpawn.Spawn(worker,Center(pawnMap),pawnMap);Manager.Current.cooldowns.Clear();((IDictionary)AccessTools.Field(typeof(Manager),"nextScan").GetValue(Manager.Current)).Clear();
   item=Spawn(ThingDef.Named("Meat_Megaspider"),sourceMap,17);sourceMap.areaManager.Home[item.Position]=false;
   Zone(storageMap,StoragePriority.Critical,12,true);var available=Zone(storageMap,StoragePriority.Important,stage==3?12:1,stage==3);storeCell=available.Cells[0];destination=storageMap;
   if(stage==3){var blocked=storeCell.GetFirstItem(storageMap);blockers.Remove(blocked);blocked.Destroy();Check(available.Cells.Count>BunkerMod.Settings.maxCandidates,"selected storage group has one free cell beyond a single scan budget");}
   start=Manager.Now;fetched=false;
   Check(sourceMap.listerHaulables.ThingsPotentiallyNeedingHauling().Contains(item),"insect meat remains in native haulable list without local storage or source home area stage "+stage);
   if(stage==1)
   {
    var route=Graph.Reachable(worker).First(r=>r.map==storageMap);int intents=Manager.Current.intents.Count;
    using(new RemotePawnScope(worker,route.map,route.landing))
    {
     Check(Hauling.FindStorage(worker,item,storageMap,StoragePriority.Unstored,out var c,out var priority,true)&&c==storeCell&&priority==StoragePriority.Important,"manual exhaustive search reaches free group beyond full preferred group and tiny budget");
     Check(Hauling.FindStorage(worker,item,storageMap,StoragePriority.Unstored,out c,out priority)&&c==storeCell,"automatic per-group budget does not starve later eligible storage");
    }
    Check(Manager.Current.intents.Count==intents&&worker.Map==pawnMap,"storage queries create no intent and restore the pawn map");
    item.SetForbidden(true,false);Check(Hauling.StartHaul(worker,item)==null,"automatic planner honors forbidden insect meat");item.SetForbidden(false,false);
   }
   // Let the ordinary JobGiver_Work tree choose and complete the task.
   Log.Message("[SAB AUTO HAUL] setup stage="+stage+" worker="+pawnMap.uniqueID+" source="+sourceMap.uniqueID+" destination="+storageMap.uniqueID+" cell="+storeCell);
  }
  public override void GameComponentUpdate()
  {if(!Active||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;}
  public override void GameComponentTick()
  {
   if(!Active||stage==99||LongEventHandler.AnyEventNowOrWaiting)return;
   try
   {
    if(stage==0)
    {
     hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.work=true;BunkerMod.Settings.haul=true;BunkerMod.Settings.maxCandidates=8;BunkerMod.Settings.scanInterval=60;BunkerMod.Settings.minStay=0;Manager.Current.Toggle(hatch);
     foreach(var map in new[]{hatch.Map,hatch.PocketMap})
     {
      foreach(var p in map.mapPawns.AllPawnsSpawned.ToList())p.Destroy();
      foreach(var group in map.haulDestinationManager.AllGroups)group.Settings.filter.SetDisallowAll();
      foreach(var c in GenRadial.RadialCellsAround(Center(map),27,true).Where(c=>c.InBounds(map)))
      {map.roofGrid.SetRoof(c,null);foreach(var t in c.GetThingList(map).ToList())if(!(t is MapPortal))t.Destroy();map.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);map.areaManager.Home[c]=false;}map.fogGrid.ClearAllFog();
     }
     worker=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,forceNoBackstory:true));GenSpawn.Spawn(worker,Center(hatch.Map),hatch.Map);worker.workSettings.EnableAndInitialize();foreach(var type in DefDatabase<WorkTypeDef>.AllDefs)worker.workSettings.SetPriority(type,type==WorkTypeDefOf.Hauling?1:0);
     for(int hour=0;hour<24;hour++)worker.timetable.SetAssignment(hour,TimeAssignmentDefOf.Work);
     Check(Hauling.CanHaul(worker),"fixture pawn can perform automatic hauling");stage=1;Reset(hatch.Map,hatch.Map,hatch.PocketMap);return;
    }
    if(worker.needs.food!=null)worker.needs.food.CurLevelPercentage=1;if(worker.needs.rest!=null)worker.needs.rest.CurLevelPercentage=1;if(worker.needs.joy!=null)worker.needs.joy.CurLevelPercentage=1;if(worker.needs.mood!=null)worker.needs.mood.CurLevelPercentage=1;
    if(Hauling.IsFetch(Broker.For(worker)))fetched=true;
    if(Manager.Now>=nextLog)
    {
     nextLog=Manager.Now+1800;Log.Message("[SAB AUTO HAUL] progress stage="+stage+" ticks="+(Manager.Now-start)+" job="+worker.CurJob+" intent="+Broker.For(worker)?.needNode+" worker="+worker.Map?.uniqueID+" item="+item.MapHeld?.uniqueID+" eligible="+Portal.Eligible(worker)+" canHaul="+Hauling.CanHaul(worker)+" timetable="+worker.timetable.CurrentAssignment);
     Log.Message("[SAB AUTO HAUL] cooldowns="+string.Join(";",Manager.Current.cooldowns.Select(c=>c.pawn+":"+c.until+":"+c.failedMap?.uniqueID+":"+c.failures))+" scans="+string.Join(";",((Dictionary<string,int>)AccessTools.Field(typeof(Manager),"nextScan").GetValue(Manager.Current)).Select(c=>c.Key+"="+c.Value))+" now="+Manager.Now);
     foreach(var route in Graph.Reachable(worker))
     using(new RemotePawnScope(worker,route.map,route.landing))
     {
      if(route.map!=item.MapHeld)continue;
      var stock=ServicesInventory.Items(route.map).OrderBy(t=>t.PositionHeld.DistanceToSquared(route.landing)).ToList();
      var store=Hauling.Storage(worker,item,true,out var cell,out bool local);
      Log.Message("[SAB AUTO HAUL] source="+route.map.uniqueID+" landing="+route.landing+" nativeUsable="+HaulAIUtility.PawnCanAutomaticallyHaul(worker,item,false)+" free="+ConsumerSupply.Free(worker,item)+" storage="+store?.map.uniqueID+" local="+local+" cell="+cell+" index="+stock.IndexOf(item)+" count="+stock.Count+" cursors="+string.Join(";",((Dictionary<string,int>)AccessTools.Field(typeof(FairScan),"cursors").GetValue(null)).Where(c=>c.Key.Contains("hauling")).Select(c=>c.Key+"="+c.Value)));
     }
    }
    if(item.Spawned&&item.Map==destination&&item.Position==storeCell&&worker.carryTracker.CarriedThing==null)
    {
     Check(item.stackCount==17&&Find.Maps.Sum(m=>m.listerThings.ThingsOfDef(item.def).Sum(t=>t.stackCount))==17,"native automatic hauling physically stores exact insect meat quantity stage "+stage);
     if(stage==2||stage==4)Check(fetched,"remote-source automatic haul uses an empty pickup trip stage "+stage);
     if(stage==1){stage=2;Reset(hatch.Map,hatch.PocketMap,hatch.Map);return;}
     if(stage==2){stage=3;Reset(hatch.PocketMap,hatch.PocketMap,hatch.Map);return;}
     if(stage==3){stage=4;Reset(hatch.PocketMap,hatch.Map,hatch.PocketMap);return;}
     if(stage==4){stage=5;Reset(hatch.Map,hatch.PocketMap,hatch.PocketMap);return;}
     if(stage==5){stage=6;Reset(hatch.Map,hatch.Map,hatch.Map);return;}
     Log.Message("[SAB AUTO HAUL] ACCEPTANCE COMPLETE");stage=99;Application.Quit();return;
    }
    if(Manager.Now-start>18000)throw new Exception("Timeout stage "+stage+" job="+worker.CurJob+" intent="+Broker.For(worker)?.needNode);
   }
   catch(Exception e){Log.Error("[SAB AUTO HAUL] FAIL "+e);stage=99;Application.Quit();}
  }
 }
}
