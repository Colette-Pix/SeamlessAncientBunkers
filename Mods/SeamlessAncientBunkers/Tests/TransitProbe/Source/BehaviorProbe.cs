using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SABTransitTests
{
 public class BehaviorProbe:GameComponent
 {
  int stage,started,logged;AncientHatch hatch;Pawn worker,dog,guest,organizer;Thing cargo;Lord party;IntVec3 stockCell;
  public BehaviorProbe(Game game){}
  void Check(bool ok,string message){if(!ok)throw new Exception(message);Log.Message("[SAB BEHAVIOR] PASS "+message);}
  IntVec3 Cell(Map map)=>CellFinder.StandableCellNear(map==hatch.Map?hatch.Position:hatch.exit.Position,map,8);
  Pawn Make(string kind,Faction faction,Map map){var p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named(kind),faction,forceGenerateNewPawn:true,canGeneratePawnRelations:false));GenSpawn.Spawn(p,Cell(map),map);return p;}
  void Move(Pawn p,Map map,IntVec3? cell=null){p.jobs.StopAll();if(p.Spawned)p.DeSpawn();GenSpawn.Spawn(p,cell??Cell(map),map);}
  void Next(int n){stage=n;started=Manager.Now;Manager.Current.cooldowns.Clear();}
  void ResetWorker(){worker.GetLord()?.Notify_PawnLost(worker,PawnLostCondition.LeftVoluntarily);worker.jobs.StopAll();var i=Broker.For(worker);if(i!=null)Broker.Cancel(i);Manager.Current.cooldowns.Clear();}
  void EmergencyChecks()
  {
   ResetWorker();Move(worker,hatch.Map);var enemy=Make("Pirate",Faction.OfAncientsHostile,hatch.Map);
   Move(enemy,hatch.Map,CellFinder.StandableCellNear(hatch.Position+IntVec3.East*65,hatch.Map,8));
   Check(!Graph.Reachable(worker).Any(r=>r.map==hatch.PocketMap),"ordinary work avoids hostile source map");
   using(new TransitPolicy(worker,TravelPermission.Emergency))
   {
    Check(Graph.Reachable(worker).Any(r=>r.map==hatch.PocketMap),"distant raid permits a safe emergency route");
    hatch.SetForbidden(true,false);Check(!Graph.Reachable(worker).Any(r=>r.map==hatch.PocketMap),"emergency route honors forbidden entrance");hatch.SetForbidden(false,false);
    Move(enemy,hatch.Map,CellFinder.StandableCellNear(hatch.Position,hatch.Map,3));
    Check(!Graph.Reachable(worker).Any(r=>r.map==hatch.PocketMap),"enemy at entrance blocks emergency route");
   }
   enemy.Destroy();
  }
  public override void GameComponentTick()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-transit-behavior")||stage==99||Current.ProgramState!=ProgramState.Playing)return;
   try
   {
    Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
    if(stage==0)
    {
     hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);
     Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.haul=true;BunkerMod.Settings.work=true;BunkerMod.Settings.scanInterval=120;
     foreach(var m in new[]{hatch.Map,hatch.PocketMap})foreach(var p in m.mapPawns.AllPawnsSpawned.ToList())p.Destroy();
     Manager.Current.EnableClearedBunkers();worker=Make("Colonist",Faction.OfPlayer,hatch.Map);worker.workSettings.EnableAndInitialize();foreach(var d in DefDatabase<WorkTypeDef>.AllDefsListForReading)worker.workSettings.SetPriority(d,0);
     dog=Make("LabradorRetriever",Faction.OfPlayer,hatch.Map);dog.training.Train(TrainableDefOf.Obedience,worker,true);dog.training.Train(DefDatabase<TrainableDef>.GetNamed("Haul"),worker,true);
     cargo=ThingMaker.MakeThing(ThingDefOf.Silver);cargo.stackCount=30;GenSpawn.Spawn(cargo,Cell(hatch.Map),hatch.Map);
     stockCell=GenRadial.RadialCellsAround(hatch.exit.Position,12,true).First(c=>c.InBounds(hatch.PocketMap)&&c.Standable(hatch.PocketMap)&&c.GetEdifice(hatch.PocketMap)==null&&c.GetZone(hatch.PocketMap)==null&&!c.Fogged(hatch.PocketMap));var zone=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,hatch.PocketMap.zoneManager);hatch.PocketMap.zoneManager.RegisterZone(zone);zone.AddCell(stockCell);zone.settings.filter.SetDisallowAll();zone.settings.filter.SetAllow(ThingDefOf.Silver,true);zone.settings.Priority=StoragePriority.Critical;
     Check(AnimalTransit.CanHaul(dog),"animal haul training recognized");Check(Graph.Reachable(dog).Any(r=>r.map==hatch.PocketMap),"trained animal has open route");
     var haul=AnimalTransit.Haul(dog);Check(haul!=null&&dog.jobs.TryTakeOrderedJob(haul),"trained animal starts remote stockpile hauling");Next(1);
    }
    foreach(var p in new[]{worker,dog,organizer})if(p!=null&&!p.Dead){if(p.needs.food!=null)p.needs.food.CurLevelPercentage=1;if(p.needs.rest!=null)p.needs.rest.CurLevelPercentage=1;if(p.needs.mood!=null)p.needs.mood.CurLevelPercentage=1;}
    if(stage==1&&cargo.Spawned&&cargo.Map==hatch.PocketMap&&cargo.Position==stockCell)
    {
     Check(cargo.stackCount==30,"animal delivered exact stack into remote storage");dog.Destroy();dog=null;EmergencyChecks();
     organizer=Make("Colonist",Faction.OfPlayer,hatch.PocketMap);party=LordMaker.MakeNewLord(Faction.OfPlayer,new LordJob_Joinable_Party(Cell(hatch.PocketMap),organizer,DefDatabase<GatheringDef>.GetNamed("Party")),hatch.PocketMap,new[]{organizer});
     worker.needs.joy.CurLevelPercentage=0.1f;for(int h=0;h<24;h++)worker.timetable.SetAssignment(h,TimeAssignmentDefOf.Joy);
     var join=GroupTransit.Social(worker,party.CurLordToil.VoluntaryJoinDutyHookFor(worker));if(join!=null)Check(worker.jobs.TryTakeOrderedJob(join),"remote native party invitation produces journey");Next(2);
    }
    if(stage==2&&worker.Map!=hatch.PocketMap&&Broker.For(worker)==null){var invitation=GroupTransit.Social(worker,party.CurLordToil.VoluntaryJoinDutyHookFor(worker));if(invitation!=null)Check(worker.jobs.TryTakeOrderedJob(invitation),"remote native party invitation produces journey");}
    if(stage==2&&worker.Map==hatch.PocketMap&&worker.GetLord()==party)
    {
     Check(true,"participant physically joins destination party lord");ResetWorker();organizer.Destroy();organizer=null;Move(worker,hatch.Map);
     guest=Make("Pirate",Faction.OfAncientsHostile,hatch.PocketMap);var loot=ThingMaker.MakeThing(ThingDefOf.Silver);loot.stackCount=17;guest.carryTracker.TryStartCarry(loot);
     var exit=ExtraNeeds.Query(new JobGiver_ExitMapBest(),guest);Check(exit?.def.defName=="SAB_ExitConnected","native pocket-map exit uses connected continuation");guest.jobs.TryTakeOrderedJob(exit);Next(3);
    }
    if(stage==3&&guest.Map==hatch.Map)
    {
     Check(guest.carryTracker.CarriedThing?.stackCount==17,"exit preserves carried loot across entrance");Check(guest.GetLord()?.LordJob is LordJob_ExitMapBest,"exit duty continues toward real world-map edge");guest.Destroy();guest=null;
     var raider=Make("Pirate",Faction.OfAncientsHostile,hatch.Map);LordMaker.MakeNewLord(raider.Faction,new LordJob_AssaultColony(raider.Faction),hatch.Map,new[]{raider});
     var structure=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.WoodLog);structure.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(structure,GenRadial.RadialCellsAround(hatch.exit.Position,12,true).First(c=>c.InBounds(hatch.PocketMap)&&c.Standable(hatch.PocketMap)&&c.GetEdifice(hatch.PocketMap)==null),hatch.PocketMap);Check(EnemyPursuit.Target(raider,structure),"assault can target remote colony structure");guest=raider;Next(31);
    }
    if(stage==31&&guest.Map==hatch.PocketMap)
    {
     Check(guest.GetLord()?.LordJob is LordJob_AssaultColony,"raider reaches building-only floor with assault duty preserved");guest.Destroy();guest=null;
     var item=ThingMaker.MakeThing(ThingDefOf.Gold);item.stackCount=13;GenSpawn.Spawn(item,Cell(hatch.PocketMap),hatch.PocketMap);hatch.PocketMap.areaManager.Home[item.Position]=true;
     var rows=new List<TransferableOneWay>();DepartureCargo.Add(rows,item);rows[0].AdjustTo(13);worker.workSettings.SetPriority(WorkTypeDefOf.Hauling,1);
     Check(DepartureCargo.Stage(rows,hatch.Map,worker.Position),"departure selection stages remote cargo before loading");cargo=item;Next(4);
    }
    if(stage==4&&cargo.Spawned&&cargo.Map==hatch.Map)
    {Check(cargo.stackCount==13,"departure cargo arrives physically with exact count");Check(!RemotePawnScope.Active,"behavior probes leave no remote context");stage=99;Log.Message("[SAB BEHAVIOR] ACCEPTANCE COMPLETE");Application.Quit();}
    if(Manager.Now>=logged){logged=Manager.Now+1200;Log.Message("[SAB BEHAVIOR] stage="+stage+" worker="+worker?.CurJob+" dog="+dog?.CurJob+" guest="+guest?.CurJob);}
    if(Manager.Now-started>18000)throw new Exception("Timeout stage="+stage);
   }
   catch(Exception e){stage=99;Log.Error("[SAB BEHAVIOR] FAIL "+e);Application.Quit();}
  }
 }
}
