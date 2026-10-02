using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
namespace SABTransitTests
{
 public class DepartureProbe:GameComponent
 {
  int stage,started,logged;bool saved;AncientHatch hatch;Pawn worker;Thing stock,pod;
  public DepartureProbe(Game game){}
  public override void ExposeData(){Scribe_Values.Look(ref stage,"departureStage");Scribe_Values.Look(ref saved,"departureSaved");Scribe_References.Look(ref hatch,"departureHatch");Scribe_References.Look(ref worker,"departureWorker");Scribe_References.Look(ref stock,"departureStock");Scribe_References.Look(ref pod,"departurePod");}
  void Check(bool value,string text){if(!value)throw new Exception(text);Log.Message("[SAB DEPARTURE] PASS "+text);}
  IntVec3 Cell(Map map)=>CellFinder.StandableCellNear(map==hatch.Map?hatch.Position:hatch.exit.Position,map,8);
  Thing Spawn(string def,Map map,int count=1){var t=ThingMaker.MakeThing(ThingDef.Named(def));if(t.def.CanHaveFaction)t.SetFaction(Faction.OfPlayer);t.stackCount=count;return GenSpawn.Spawn(t,Cell(map),map);}
  TransferableOneWay Row(Thing thing,int count){var row=new TransferableOneWay();row.things.Add(thing);row.AdjustTo(count);return row;}
  bool AcceptPod(Thing item,int count)
  {var dialog=new Dialog_LoadTransporters(hatch.Map,new List<CompTransporter>{pod.TryGetComp<CompTransporter>()});AccessTools.Field(typeof(Dialog_LoadTransporters),"transferables").SetValue(dialog,new List<TransferableOneWay>{Row(item,count)});return (bool)AccessTools.Method(typeof(Dialog_LoadTransporters),"TryAccept").Invoke(dialog,null);}
  public override void GameComponentTick()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-departure-tests")||stage==99||Current.ProgramState!=ProgramState.Playing)return;
   try
   {
    Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
    if(stage==0)
    {
     hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.haul=true;BunkerMod.Settings.work=true;BunkerMod.Settings.scanInterval=120;
     foreach(var map in new[]{hatch.Map,hatch.PocketMap})foreach(var p in map.mapPawns.AllPawnsSpawned.ToList())p.Destroy();Manager.Current.EnableClearedBunkers();
     do{worker=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false));}while(worker.WorkTypeIsDisabled(WorkTypeDefOf.Hauling));GenSpawn.Spawn(worker,Cell(hatch.Map),hatch.Map);worker.workSettings.EnableAndInitialize();foreach(var d in DefDatabase<WorkTypeDef>.AllDefs)worker.workSettings.SetPriority(d,d==WorkTypeDefOf.Hauling?1:0);
     pod=Spawn("TransportPod",hatch.Map);stock=Spawn("Silver",hatch.PocketMap,8);
     Check(AcceptPod(stock,5),"first transporter confirmation accepted without a reopen step");Check(Departures.Current.pending.Count==1&&stock.Map==hatch.PocketMap,"confirmed order queues physical gathering without moving stock during validation");
     stage=1;saved=true;GameDataSaveLoader.SaveGame("DeparturePending");Log.Message("[SAB DEPARTURE] RELOAD REQUIRED");Application.Quit();return;
    }
    if(started==0){started=Manager.Now;Check(saved&&Departures.Current.pending.Count==1,"pending departure and exact selection restored in fresh process");}
    if(worker.Spawned){worker.needs.food.CurLevelPercentage=1;worker.needs.rest.CurLevelPercentage=1;worker.needs.mood.CurLevelPercentage=1;}
    var transporter=pod.TryGetComp<CompTransporter>();
    if(stage==1&&transporter.innerContainer.TotalStackCountOfDef(ThingDefOf.Silver)==5)
    {
     Check(stock.Spawned&&stock.Map==hatch.PocketMap&&stock.stackCount==3,"one confirmation automatically delivers and natively loads exact five, leaving three unsold");Check(Departures.Current.pending.Count==0,"completed loading releases pending selection");
     transporter.CancelLoad(hatch.Map);Check(AcceptPod(stock,2),"second connected loading accepted");Departures.CancelAll();Check(Departures.Current.pending.Count==0,"cancel clears confirmed departure and pending requests");
     Check(AcceptPod(stock,2),"replacement fixture begins with a remote loading plan");var local=Spawn("Cloth",hatch.Map,1);Check(AcceptPod(local,1)&&Departures.Current.pending.Count==0,"accepted local replacement cancels obsolete remote plan");transporter.CancelLoad(hatch.Map);local.Destroy();
     var heavy=Spawn("Steel",hatch.PocketMap,10000);heavy.stackCount=10000;Check(!AcceptPod(heavy,10000)&&Departures.Current.pending.Count==0,"native mass limit rejects overweight remote loading before gathering");heavy.Destroy();
     worker.jobs.StopAll();Manager.Current.cooldowns.Clear();stock=Spawn("Gold",hatch.PocketMap,17);
     var caravan=new Dialog_FormCaravan(hatch.Map,designatedMeetingPoint:worker.Position);caravan.transferables=new List<TransferableOneWay>{Row(worker,1),Row(stock,13)};
     AccessTools.Field(typeof(Dialog_FormCaravan),"startingTile").SetValue(caravan,hatch.Map.Tile);AccessTools.Field(typeof(Dialog_FormCaravan),"destinationTile").SetValue(caravan,hatch.Map.Tile);
     Check((bool)AccessTools.Method(typeof(Dialog_FormCaravan),"TryFormAndSendCaravan").Invoke(caravan,null),"first caravan confirmation accepted with remote cargo");Check(Departures.Current.pending.Count==1,"caravan selection saved for automatic loading");stage=2;started=Manager.Now;
    }
    if(stage==2&&worker.inventory.innerContainer.TotalStackCountOfDef(ThingDefOf.Gold)==13)
    {
     Check(Departures.Current.pending.Count==0,"native caravan packing resumed automatically");Check(stock.Spawned&&stock.Map==hatch.PocketMap&&stock.stackCount==4,"caravan physically packs selected thirteen and preserves four remaining");stage=99;Log.Message("[SAB DEPARTURE] ACCEPTANCE COMPLETE");Application.Quit();
    }
    if(Manager.Now>=logged){logged=Manager.Now+1200;Log.Message("[SAB DEPARTURE] stage="+stage+" job="+worker.CurJob+" pending="+Departures.Current.pending.Count+" cargo="+stock.MapHeld+" lord="+worker.GetLord()?.LordJob);}
    if(Manager.Now-started>24000)throw new Exception("Timeout stage="+stage);
   }
   catch(Exception e){stage=99;Log.Error("[SAB DEPARTURE] FAIL "+e);Application.Quit();}
  }
 }
}
