using System;
using System.Linq;
using RimWorld;
using HarmonyLib;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SABTransitTests
{
 public class CustodyProbe:GameComponent
 {
  int stage,started,logged;AncientHatch hatch;Pawn worker,prisoner,animal,target;Building_Bed bed;Thing marker;IntVec3 center;
  public CustodyProbe(Game game){}
  void Check(bool ok,string message){if(!ok)throw new Exception(message);Log.Message("[SAB CUSTODY] PASS "+message);}
  IntVec3 Cell(Map m)=>CellFinder.StandableCellNear(m==hatch.Map?hatch.Position:hatch.exit.Position,m,6);
  Thing Spawn(string name,Map map,IntVec3 cell){var d=ThingDef.Named(name);var t=ThingMaker.MakeThing(d,d.MadeFromStuff?ThingDefOf.WoodLog:null);if(d.CanHaveFaction)t.SetFaction(Faction.OfPlayer);return GenSpawn.Spawn(t,cell,map);}
  Pawn Make(string kind,Faction faction,Map map,IntVec3 cell)
  {Pawn p;do{p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named(kind),faction,forceGenerateNewPawn:true,canGeneratePawnRelations:false));}while(kind=="Colonist"&&(p.WorkTypeIsDisabled(WorkTypeDefOf.Warden)||p.WorkTypeIsDisabled(WorkTypeDefOf.Handling)||p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling)));GenSpawn.Spawn(p,cell,map);return p;}
  void Work(WorkTypeDef def){worker.jobs.StopAll();foreach(var d in DefDatabase<WorkTypeDef>.AllDefs)worker.workSettings.SetPriority(d,d==def?1:0);Manager.Current.cooldowns.Clear();started=Manager.Now;}
  public override void GameComponentTick()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-transit-custody")||stage==99||Current.ProgramState!=ProgramState.Playing)return;
   try
   {
    Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
    if(stage==0)
    {
     hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);
     Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.work=true;BunkerMod.Settings.scanInterval=120;
     foreach(var m in new[]{hatch.Map,hatch.PocketMap})foreach(var p in m.mapPawns.AllPawnsSpawned.ToList())p.Destroy();Manager.Current.EnableClearedBunkers();
     var map=hatch.PocketMap;center=GenRadial.RadialCellsAround(hatch.exit.Position,18,true).First(c=>c.DistanceTo(hatch.exit.Position)>8&&CellRect.CenteredOn(c,3).Cells.All(z=>z.InBounds(map)&&z.Standable(map)&&z.GetEdifice(map)==null&&!z.Fogged(map)));
     foreach(var c in CellRect.CenteredOn(center,3).EdgeCells)Spawn(c==center+IntVec3.West*3?"Door":"Wall",map,c);
     bed=(Building_Bed)Spawn("Bed",map,center);bed.ForPrisoners=true;map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
     prisoner=Make("Pirate",Faction.OfAncientsHostile,map,center);prisoner.guest.SetGuestStatus(Faction.OfPlayer,GuestStatus.Prisoner);prisoner.guest.SetExclusiveInteraction(PrisonerInteractionModeDefOf.Release);
     worker=Make("Colonist",Faction.OfPlayer,hatch.Map,Cell(hatch.Map));worker.workSettings.EnableAndInitialize();Work(WorkTypeDefOf.Warden);
     Check(prisoner.guest.PrisonerIsSecure,"bunker prisoner starts secure and scheduled for release");stage=1;
    }
    foreach(var p in new[]{worker,prisoner,animal})if(p!=null&&!p.Dead){if(p.needs.food!=null)p.needs.food.CurLevelPercentage=1;if(p.needs.rest!=null)p.needs.rest.CurLevelPercentage=1;if(p.needs.mood!=null)p.needs.mood.CurLevelPercentage=1;}
    if(stage==1&&prisoner.guest.Released)
    {
     Check(prisoner.Map==hatch.Map,"prisoner carried upstairs and native release state applied");prisoner.Destroy();prisoner=null;bed.Destroy();
     marker=Spawn("PenMarker",hatch.PocketMap,center);hatch.PocketMap.animalPenManager.RebuildAllPens();
     Check(marker.TryGetComp<CompAnimalPenMarker>().PenState.Enclosed,"destination pen is enclosed");
     worker.jobs.StopAll();if(worker.Spawned)worker.DeSpawn();GenSpawn.Spawn(worker,Cell(hatch.Map),hatch.Map);
     animal=Make("Cow",Faction.OfPlayer,hatch.Map,Cell(hatch.Map));Work(WorkTypeDefOf.Handling);stage=2;
     Check(worker.workSettings.WorkGiversInOrderNormal.Any(g=>g.GetType()==typeof(WorkGiver_TakeToPen)),"handler has native pen work giver");
     Check(WorkGiver_InteractAnimal.CanInteractWithAnimal(worker,animal,out var why,false,true,true),"source animal interaction: "+why);
     var route=Graph.Reachable(worker).First(r=>r.map==hatch.PocketMap);
     using(new RemotePawnScope(worker,route.map,route.landing))using(new RemotePawnScope(animal,route.map,route.landing))
     {var chosen=AnimalPenUtility.GetPenAnimalShouldBeTakenTo(worker,animal,out why)??AnimalPenUtility.GetCurrentPenOf(animal,false);Check(chosen!=null,"native remote pen choice: "+why);}
    }
    if(stage==2&&animal.Map==hatch.PocketMap&&AnimalPenUtility.GetCurrentPenOf(animal,false)?.parent==marker&&!animal.roping.IsRoped)
        {
     Check(!animal.roping.IsRoped,"animal transferred and released into accepted remote pen");
     worker.jobs.StopAll();var intent=Broker.For(worker);if(intent!=null)Broker.Cancel(intent);
     target=Make("Colonist",Faction.OfPlayer,worker.Map,Cell(worker.Map));
     Check(worker.mindState.mentalStateHandler.TryStartMentalState(DefDatabase<MentalStateDef>.GetNamed("MurderousRage"),forced:true,forceWake:true),"native murderous rage starts with real target");
     ((MentalState_MurderousRage)worker.MentalState).target=target;target.drafter.Drafted=true;target.DeSpawn();GenSpawn.Spawn(target,Cell(hatch.Map),hatch.Map);
     Check(((MentalState_MurderousRage)worker.MentalState).IsTargetStillValidAndReachable(),"rage target remains valid after changing floors");stage=3;started=Manager.Now;
    }
    if(stage==3&&worker.Map==hatch.Map&&target.Map==hatch.Map)
    {
     Check(worker.MentalState is MentalState_MurderousRage rage&&rage.target==target,"rage pursuit crosses with native target identity preserved");
     worker.MentalState.RecoverFromState();worker.jobs.StopAll();
     IntVec3 Spot(Map map)=>GenRadial.RadialCellsAround(map==hatch.Map?hatch.Position:hatch.exit.Position,25,true).First(c=>c.DistanceTo(map==hatch.Map?hatch.Position:hatch.exit.Position)>8&&CellRect.CenteredOn(c,2).Cells.All(z=>z.InBounds(map)&&z.Standable(map)&&z.GetEdifice(map)==null&&!z.Fogged(map)));
     var alternate=(AncientHatch)Spawn("AncientHatch",hatch.Map,Spot(hatch.Map));alternate.GetComp<CompHackable>().HackNow();AccessTools.Field(typeof(MapPortal),"pocketMap").SetValue(alternate,hatch.PocketMap);
     var field=AccessTools.Field(typeof(PocketMapUtility),"currentlyGeneratingPortal");var previous=field.GetValue(null);field.SetValue(null,alternate);try{GenSpawn.Spawn(ThingMaker.MakeThing(hatch.exit.def),Spot(hatch.PocketMap),hatch.PocketMap);}finally{field.SetValue(null,previous);}
     Manager.Current.enabledPortals.Add(alternate);hatch.SetForbidden(false,false);alternate.SetForbidden(false,false);alternate.exit.SetForbidden(false,false);Check(Graph.Reachable(worker,false).Count(r=>r.map==hatch.PocketMap)>=2,"graph retains distinct entrances to one destination map; original="+Portal.BlockReason(worker,hatch,false,out _)+" alternate="+Portal.BlockReason(worker,alternate,false,out _));
     var original=Graph.Reachable(worker,false).First(r=>r.map==hatch.PocketMap&&r.portals[0]==hatch);var order=Broker.Begin(worker,original,Purpose.Manual,forced:true);hatch.SetForbidden(true,false);
     Check(Broker.Replan(Broker.For(worker))&&Broker.For(worker).portal==alternate,"blocked route replans through alternate open entrance");JobMaker.ReturnToPool(order);worker.jobs.TryTakeOrderedJob(JobMaker.MakeJob(Manager.TravelDef,alternate));stage=4;started=Manager.Now;
    }
    if(stage==4&&worker.Map==hatch.PocketMap&&Broker.For(worker)==null)
    {
     Check(true,"alternate entrance journey physically completes");hatch.SetForbidden(false,false);
     bed=(Building_Bed)Spawn("Bed",hatch.PocketMap,center);bed.ForPrisoners=true;
     prisoner=Make("Pirate",Faction.OfAncientsHostile,hatch.PocketMap,center);prisoner.guest.SetGuestStatus(Faction.OfPlayer,GuestStatus.Prisoner);
     var room=GenRadial.RadialCellsAround(hatch.Position,35,true).First(c=>c.DistanceTo(hatch.Position)>12&&CellRect.CenteredOn(c,3).Cells.All(z=>z.InBounds(hatch.Map)&&z.Standable(hatch.Map)&&z.GetEdifice(hatch.Map)==null&&!z.Fogged(hatch.Map)));
     foreach(var c in CellRect.CenteredOn(room,3).EdgeCells)Spawn(c==room+IntVec3.West*3?"Door":"Wall",hatch.Map,c);
     bed=(Building_Bed)Spawn("Bed",hatch.Map,room);bed.ForPrisoners=true;hatch.Map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();hatch.PocketMap.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
     Check(prisoner.ownership.ClaimBedIfNonMedical(bed),"prisoner assigned to bed on another floor");Work(WorkTypeDefOf.Warden);stage=5;
    }
    if(stage==5&&prisoner.Map==bed.Map&&prisoner.CurrentBed()==bed)
    {
     Check(prisoner.IsPrisonerOfColony&&prisoner.ownership.OwnedBed==bed,"warden physically transfers prisoner into assigned remote bed with custody intact");stage=99;Log.Message("[SAB CUSTODY] ACCEPTANCE COMPLETE");Application.Quit();
    }
    if(Manager.Now>=logged){logged=Manager.Now+1200;Log.Message("[SAB CUSTODY] stage="+stage+" worker="+worker?.CurJob+" intent="+Broker.For(worker)?.purpose+" prisoner="+prisoner?.Map+" animal="+animal?.Map);}
    if(Manager.Now-started>18000)throw new Exception("Timeout stage="+stage);
   }catch(Exception e){stage=99;Log.Error("[SAB CUSTODY] FAIL "+e);Application.Quit();}
  }
 }
}
