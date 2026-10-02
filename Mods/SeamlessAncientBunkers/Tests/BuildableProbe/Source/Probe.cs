using System;
using System.Linq;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SABBuildableProbe
{
 public class Probe : GameComponent
 {
  int stage, started; AncientHatch a,b; Pawn pawn; Thing cargo; IntVec3 dest;
  bool sawSurface;
  public Probe(Game g) {}
  void Check(bool ok,string text) { if(!ok)throw new Exception(text);Log.Message("[SAB BUILDABLE] PASS "+text); }
  void Clean(Map map)
  {
   foreach(var p in map.mapPawns.AllPawnsSpawned.ToList())p.Destroy();
   foreach(var t in map.listerThings.AllThings.Where(t=>!(t is MapPortal)&&t.HostileTo(Faction.OfPlayer)).ToList())if(!t.Destroyed)t.Destroy();
   foreach(var group in map.haulDestinationManager.AllGroupsListInPriorityOrder.ToList())group.Settings.filter.SetDisallowAll();
   map.fogGrid.ClearAllFog();
  }
  void OpenArea(Map map,IntVec3 center,int radius)
  {
   foreach(var c in GenRadial.RadialCellsAround(center,radius,true).Where(c=>c.InBounds(map)))
   {
    foreach(var t in c.GetThingList(map).ToList())
     if(!(t is MapPortal)&&!(t is Pawn)&& (t.def.category==ThingCategory.Building||t.def.category==ThingCategory.Item||t.def.category==ThingCategory.Plant))t.Destroy();
    map.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);
    map.roofGrid.SetRoof(c,null);
   }
  }
  void Move(Map map)
  {
   dest=CellFinder.StandableCellNear((map==a.PocketMap?a:b).exit.Position+IntVec3.East*4,map,3);
   Check(Graph.Reachable(pawn,false).Any(r=>r.map==map&&r.portals.Count==2),"two-entrance manual route to "+map.uniqueID);
   pawn.drafter.Drafted=true;
   Check(LinkedOrders.Issue(pawn,JobMaker.MakeJob(JobDefOf.Goto,dest),map,workCell:dest,queue:false),"drafted move accepted");
   started=Manager.Now;sawSurface=false;
  }
  void Haul(Map map)
  {
   pawn.drafter.Drafted=false;pawn.jobs.StopAll();Manager.Current.intents.Clear();Manager.Current.cooldowns.Clear();
   foreach(var m in Find.Maps)foreach(var g in m.haulDestinationManager.AllGroupsListInPriorityOrder.ToList())g.Settings.filter.SetDisallowAll();
   dest=CellFinder.StandableCellNear((map==a.PocketMap?a:b).exit.Position+IntVec3.East*4,map,3);
   var zone=dest.GetZone(map) as Zone_Stockpile;
   if(zone==null){zone=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,map.zoneManager);map.zoneManager.RegisterZone(zone);zone.AddCell(dest);}
   zone.GetStoreSettings().filter.SetAllow(ThingDefOf.Silver,true);zone.GetStoreSettings().Priority=StoragePriority.Critical;
   cargo=ThingMaker.MakeThing(ThingDefOf.Silver);cargo.stackCount=7;GenSpawn.Spawn(cargo,pawn.Position,pawn.Map);cargo.SetForbidden(false,false);
   var route=Hauling.Storage(pawn,cargo,false,out var cell,out _);
   Check(route!=null&&route.map==map&&route.portals.Count==2,"automatic storage search finds other built stockpile through surface");
   var job=Hauling.StartHaul(pawn,cargo);
   Check(job!=null,"non-forced hauling job accepted");
   pawn.jobs.StartJob(job,JobCondition.InterruptForced);started=Manager.Now;sawSurface=false;
  }
  public override void GameComponentUpdate()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-buildable-tests")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||stage==99)return;
   Application.runInBackground=true;
   foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);
   Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
   try
   {
    if(stage==0)
    {
     Check(ModsConfig.IsActive("kanae.buildableancientstockpiles"),"actual Buildable Ancient Stockpiles mod active");
     var def=ThingDef.Named("AncientHatch");
     Check(def.designationCategory?.defName=="Misc"&&def.costList.Any(c=>c.thingDef==ThingDefOf.Steel&&c.count==114),"buildable hatch patch applied");
     Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.haul=true;
     var surface=Find.Maps.First(m=>!m.IsPocketMap);
     foreach(var m in Find.Maps.ToList())Clean(m);
     OpenArea(surface,surface.Center,22);
     stage=1;
     LongEventHandler.QueueLongEvent(()=>{
      a=(AncientHatch)ThingMaker.MakeThing(def);a.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(a,surface.Center+IntVec3.West*8,surface);
      b=(AncientHatch)ThingMaker.MakeThing(def);b.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(b,surface.Center+IntVec3.East*8,surface);
      a.GetComp<CompHackable>().HackNow();a.GetOtherMap();b.GetComp<CompHackable>().HackNow();b.GetOtherMap();
     },null,false,null);
     return;
    }
    if(stage==1)
    {
     Check(Portal.Other(a)!=null&&Portal.Other(b)!=null&&a.PocketMap!=b.PocketMap,"two new player-owned hatches create distinct recognized stockpiles");
     foreach(var h in new[]{a,b}){Clean(h.PocketMap);OpenArea(h.PocketMap,h.exit.Position,14);h.SetForbidden(false,false);h.exit.SetForbidden(false,false);}
     pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false));
     foreach(var t in pawn.story.traits.allTraits.ToList())pawn.story.traits.RemoveTrait(t);
     GenSpawn.Spawn(pawn,CellFinder.StandableCellNear(a.exit.Position,a.PocketMap,5),a.PocketMap);
     pawn.workSettings.EnableAndInitialize();pawn.workSettings.SetPriority(WorkTypeDefOf.Hauling,1);
     Check(Hauling.CanHaul(pawn),"fixture pawn can haul");
     a.OnEntered(pawn);b.OnEntered(pawn);Manager.Current.EnableClearedBunkers();
     Log.Message("[SAB BUILDABLE] Clearance a="+Portal.Cleared(a)+" b="+Portal.Cleared(b)+" fog="+a.Fogged()+"/"+b.Fogged()+" enter="+a.IsEnterable(out var why)+"/"+b.IsEnterable(out var whyB)+" reason="+why+"/"+whyB);
     Check(Manager.Current.Enabled(a)&&Manager.Current.Enabled(b),"both built stockpiles automatically activate after clearance");
     Check(BunkerLevels.Stack(a.Map).Contains(a.PocketMap)&&BunkerLevels.Stack(a.Map).Contains(b.PocketMap),"both stockpiles available in level navigation");
     Move(b.PocketMap);stage=2;return;
    }
    pawn.needs.food.CurLevelPercentage=1;pawn.needs.rest.CurLevelPercentage=1;
    if(pawn.Map==a.Map)sawSurface=true;
    if(stage==2&&pawn.Map==b.PocketMap&&pawn.Position==dest)
    {Check(sawSurface&&pawn.Drafted,"drafted movement A to surface to B completes");Move(a.PocketMap);stage=3;}
    else if(stage==3&&pawn.Map==a.PocketMap&&pawn.Position==dest)
    {Check(sawSurface&&pawn.Drafted,"drafted movement B to surface to A completes");Haul(b.PocketMap);stage=4;}
    else if(stage==4&&cargo.Spawned&&cargo.Map==b.PocketMap&&cargo.Position==dest&&pawn.carryTracker.CarriedThing==null)
    {Check(sawSurface&&cargo.stackCount==7,"seven silver physically hauled A to surface to B");Haul(a.PocketMap);stage=5;}
    else if(stage==5&&cargo.Spawned&&cargo.Map==a.PocketMap&&cargo.Position==dest&&pawn.carryTracker.CarriedThing==null)
    {
     Check(sawSurface&&cargo.stackCount==7,"seven silver physically hauled B to surface to A");
     b.SetForbidden(true,false);Check(!Graph.Reachable(pawn).Any(r=>r.map==b.PocketMap),"forbidden destination hatch blocks automatic route");
     b.SetForbidden(false,false);Manager.Current.Toggle(b);Check(!Graph.Reachable(pawn).Any(r=>r.map==b.PocketMap),"disabled destination traffic blocks automatic route");
     Check(Graph.Reachable(pawn,false).Any(r=>r.map==b.PocketMap),"manual route remains available with automatic traffic disabled");
     Log.Message("[SAB BUILDABLE] ACCEPTANCE COMPLETE");stage=99;Application.Quit();
    }
    if(stage!=99&&Manager.Now-started>16000)throw new Exception("Timed out stage="+stage+" map="+pawn.Map+" job="+pawn.CurJob+" intent="+Broker.For(pawn)?.purpose);
   }catch(Exception e){stage=99;Log.Error("[SAB BUILDABLE] FAIL "+e);Application.Quit();}
  }
 }
}

