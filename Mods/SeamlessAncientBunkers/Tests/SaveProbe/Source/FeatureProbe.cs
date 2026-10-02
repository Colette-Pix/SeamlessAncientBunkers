using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
namespace SABSaveProbe
{
 [StaticConstructorOnStartup]
 public static class FeatureTrace
 {
  static FeatureTrace(){if(GenCommandLine.CommandLineArgPassed("sab-feature-tests"))new Harmony("colet.sab.featuretrace").Patch(AccessTools.Method(typeof(Broker),nameof(Broker.Cancel)),prefix:new HarmonyMethod(typeof(FeatureTrace),nameof(Cancel)));}
  public static void Cancel(Intent intent,bool failure){if(intent?.purpose==Purpose.Dining)Log.Message("[SAB FEATURE TRACE] Cancel failure="+failure+" job="+intent.pawn.CurJob+" eligible="+Portal.Eligible(intent.pawn,true)+" reason="+Portal.BlockReason(intent.pawn,intent.portal,true,out _)+" stack="+Environment.StackTrace);}
 }
 public class FeatureProbe:GameComponent
 {
  int stage,started;Pawn pawn;AncientHatch hatch;Thing food;
  public FeatureProbe(Game g){}
  public override void GameComponentTick(){if(GenCommandLine.CommandLineArgPassed("sab-feature-tests")&&stage==1&&Manager.Now-started<300&&Manager.Now%10==0)Log.Message("[SAB FEATURE TRACE] job="+pawn.CurJob+" cargo="+pawn.carryTracker.CarriedThing+" intent="+Broker.For(pawn)?.purpose+" map="+pawn.Map);}
  public override void ExposeData(){Scribe_Values.Look(ref stage,"featureStage");Scribe_Values.Look(ref started,"featureStart");Scribe_References.Look(ref pawn,"featurePawn");Scribe_References.Look(ref hatch,"featureHatch");Scribe_References.Look(ref food,"featureFood");}
  void Check(bool yes,string text){if(!yes)throw new Exception(text);Log.Message("[SAB FEATURE TEST] PASS "+text);}
  void Reset(){Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.debug=true;Manager.Current.EnableClearedBunkers();}
  Thing Spawn(string def,Map map,IntVec3 pos,ThingDef stuff=null){var t=ThingMaker.MakeThing(ThingDef.Named(def),stuff);if(t.def.CanHaveFaction)t.SetFaction(Faction.OfPlayer);return GenSpawn.Spawn(t,pos,map);}
  void Forget(Job j){if(j!=null)JobMaker.ReturnToPool(j);var i=Broker.For(pawn);if(i!=null)Broker.Cancel(i);Reset();}
  public override void GameComponentUpdate()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-feature-tests")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||stage==9)return;
   Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);
   try
   {
    if(stage==0)
    {
     hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);Reset();
     pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false));GenSpawn.Spawn(pawn,hatch.exit.Position+IntVec3.East*4,hatch.PocketMap);
     pawn.jobs.StopAll();pawn.needs.food.CurLevelPercentage=0.7f;pawn.needs.rest.CurLevelPercentage=0.4f;
     var bed=(Building_Bed)Spawn("Bed",hatch.Map,hatch.Position+IntVec3.East*6,ThingDefOf.WoodLog);
     var injury=pawn.health.AddHediff(HediffDefOf.Cut,pawn.RaceProps.body.corePart);injury.Severity=12;
     var ground=JobMaker.MakeJob(JobDefOf.LayDown,pawn.Position);var rest=NeedProbe.Plan(pawn,Purpose.Rest,ground);
     Check(rest?.def==Manager.TravelDef&&Broker.For(pawn)?.target==bed,"injured pawn without owned bed routes to ordinary surface bed");Forget(rest);JobMaker.ReturnToPool(ground);pawn.health.RemoveHediff(injury);
     foreach(var trait in pawn.story.traits.allTraits.ToList())pawn.story.traits.RemoveTrait(trait);
     var beer=Spawn("Penoxycyline",hatch.Map,hatch.Position+IntVec3.East*3);var policy=pawn.drugs.CurrentPolicy;
     for(int n=0;n<policy.Count;n++){policy[n].allowScheduled=policy[n].drug==beer.def;policy[n].daysFrequency=0.01f;policy[n].onlyIfMoodBelow=1f;policy[n].onlyIfJoyBelow=1f;}
     Log.Message("[SAB FEATURE TEST] drug eligible="+pawn.drugs.ShouldTryToTakeScheduledNow(beer.def)+" route="+Portal.BlockReason(pawn,hatch.exit,true,out _));
     var drugs=ExtraNeeds.Query(new JobGiver_TakeDrugsForDrugPolicy(),pawn);
     Check(drugs?.def==Manager.TravelDef&&Broker.For(pawn)?.target==beer,"native scheduled drug policy selects remote scheduled drug");Forget(drugs);
     var localBeer=Spawn("Penoxycyline",pawn.Map,pawn.Position+IntVec3.East);drugs=ExtraNeeds.Query(new JobGiver_TakeDrugsForDrugPolicy(),pawn);Check(drugs!=null&&drugs.def!=Manager.TravelDef&&Broker.For(pawn)==null,"available local drug keeps native job");Forget(drugs);localBeer.Destroy();beer.Destroy();
     pawn.apparel.DestroyAll();var apparel=Spawn("Apparel_Parka",hatch.Map,hatch.Position+IntVec3.East*4,ThingDefOf.Cloth);
     var zone=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,hatch.Map.zoneManager);hatch.Map.zoneManager.RegisterZone(zone);zone.AddCell(apparel.Position);zone.GetStoreSettings().filter.SetAllowAll(null);pawn.outfits.CurrentApparelPolicy.filter.SetAllowAll(null);pawn.mindState.nextApparelOptimizeTick=0;
     var wear=ExtraNeeds.Query(new JobGiver_OptimizeApparel(),pawn);Check(wear?.def==Manager.TravelDef&&Broker.For(pawn)?.target==apparel,"native outfit optimizer selects stored remote clothing");Forget(wear);apparel.DeSpawn();pawn.apparel.Wear((Apparel)apparel);
     pawn.DeSpawn();GenSpawn.Spawn(pawn,hatch.Position+IntVec3.East*4,hatch.Map);
     var inwardDrug=Spawn("Penoxycyline",hatch.PocketMap,hatch.exit.Position+IntVec3.East*3);var inward=ExtraNeeds.Query(new JobGiver_TakeDrugsForDrugPolicy(),pawn);Check(inward?.def==Manager.TravelDef&&Broker.For(pawn)?.finalMap==hatch.PocketMap,"drug policy also routes from surface INTO bunker");Forget(inward);inwardDrug.Destroy();
     var pants=Spawn("Apparel_Pants",hatch.PocketMap,hatch.exit.Position+IntVec3.East*4,ThingDefOf.Cloth);var innerZone=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,hatch.PocketMap.zoneManager);hatch.PocketMap.zoneManager.RegisterZone(innerZone);innerZone.AddCell(pants.Position);innerZone.GetStoreSettings().filter.SetAllowAll(null);pawn.mindState.nextApparelOptimizeTick=0;
     inward=ExtraNeeds.Query(new JobGiver_OptimizeApparel(),pawn);Check(inward?.def==Manager.TravelDef&&Broker.For(pawn)?.target==pants,"outfit policy also routes from surface INTO bunker");Forget(inward);pants.Destroy();pawn.DeSpawn();GenSpawn.Spawn(pawn,hatch.exit.Position+IntVec3.East*4,hatch.PocketMap);
     Check(Dialog_FormCaravan.AllSendablePawns(hatch.Map,false).Contains(pawn),"native caravan candidate list includes bunker pawn");
     Check(RitualCandidates.Pawns(hatch.Map.mapPawns).Contains(pawn),"ritual candidate list includes bunker colonist");
     var assignments=Dialog_BeginRitual.CreateRitualRoleAssignments(null,new TargetInfo(hatch.Position,hatch.Map),hatch.Map,null,null,null,null);
     Check(assignments.AllCandidatePawns.Contains(pawn),"native ritual assignment builder includes remote pawn after validation");
     BunkerLevels.Entered(hatch.PocketMap);Check(BunkerLevels.Number(hatch.Map)==0&&BunkerLevels.Number(hatch.PocketMap)==-1,"surface and first entered bunker receive levels zero and minus one");
     Check(BunkerLevels.Next(hatch.Map,false)==hatch.PocketMap&&BunkerLevels.Next(hatch.PocketMap,false)==hatch.Map&&BunkerLevels.Next(hatch.Map,true)==hatch.PocketMap,"level navigation wraps in both directions");
     var original=pawn.Map;using(var query=new CaravanQuery(hatch.Map,new List<Pawn>{pawn})){Check(pawn.Map==hatch.Map,"caravan validation uses arrival map");}Check(pawn.Map==original,"caravan validation restores real pawn map");
     var chair=Spawn("DiningChair",hatch.Map,hatch.Position+IntVec3.South*5,ThingDefOf.WoodLog);Spawn("Table1x2c",hatch.Map,chair.Position+IntVec3.East,ThingDefOf.WoodLog);
     Check(Dining.Seat(pawn,pawn.Position,9999,out _)==false,"bunker has no usable dining seat");
     food=Spawn("MealSimple",pawn.Map,pawn.Position+IntVec3.North);food.stackCount=3;var ingest=JobMaker.MakeJob(JobDefOf.Ingest,food);ingest.count=1;
     var dine=Dining.Plan(pawn,ingest);Check(dine?.def.defName=="SAB_Collect"&&Broker.For(pawn)?.purpose==Purpose.Dining,"meal routes to remote table with pickup");JobMaker.ReturnToPool(ingest);
     pawn.jobs.TryTakeOrderedJob(dine);started=Manager.Now;stage=1;Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
    }
    else if(stage==1)
    {
     if(pawn.carryTracker.CarriedThing!=null&&Broker.For(pawn)?.purpose==Purpose.Dining)
     {stage=2;LongEventHandler.QueueLongEvent(()=>{GameDataSaveLoader.SaveGame("SAB-FeatureTransit");GameDataSaveLoader.LoadGame("SAB-FeatureTransit");},null,false,null);}
     else if(Manager.Now-started>5000)throw new Exception("dining pickup timed out: "+pawn.CurJob);
    }
    else if(stage==2)
    {
     Check(Broker.For(pawn)?.purpose==Purpose.Dining&&pawn.carryTracker.CarriedThing!=null,"dining cargo and intent survive reload");Check(BunkerLevels.Number(hatch.PocketMap)==-1,"bunker level assignment survives reload");stage=3;Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
    }
    else if(stage==3)
    {
     if(pawn.Map==hatch.Map&&pawn.CurJobDef==JobDefOf.Ingest){Check(true,"pawn physically crosses hatch and begins eating on surface");stage=4;started=Manager.Now;}
     else if(Manager.Now-started>10000)throw new Exception("dining travel timed out: "+pawn.CurJob+" map="+pawn.Map);
    }
    else if(stage==4&&Manager.Now-started>500)
    {
     pawn.jobs.StopAll();Reset();pawn.DeSpawn();GenSpawn.Spawn(pawn,hatch.exit.Position+IntVec3.East*4,hatch.PocketMap);
     Gatherings.Queue(new Gathering{caravan=true,map=hatch.Map,pawns=new List<Pawn>{pawn},meeting=hatch.Position+IntVec3.East*2,exit=new IntVec3(0,0,hatch.Position.z),startTile=hatch.Map.Tile,destinationTile=hatch.Map.Tile});
     stage=5;started=Manager.Now;LongEventHandler.QueueLongEvent(()=>{GameDataSaveLoader.SaveGame("SAB-Gathering");GameDataSaveLoader.LoadGame("SAB-Gathering");},null,false,null);
    }
    else if(stage==5){Check(Manager.Current.gatherings.Any(g=>g.caravan&&g.pawns.Contains(pawn)),"pending caravan gathering survives reload");stage=6;Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;}
    else if(stage==6)
    {
     if(pawn.Map==hatch.Map&&pawn.GetLord()?.LordJob is LordJob_FormAndSendCaravan){Check(true,"caravan creates native lord only after physical arrival");stage=9;Log.Message("[SAB FEATURE TEST] FEATURE ACCEPTANCE COMPLETE");Application.Quit();}
     else if(Manager.Now-started>10000)throw new Exception("caravan gathering timed out: "+pawn.CurJob+" map="+pawn.Map+" lord="+pawn.GetLord()?.LordJob);
    }
   }catch(Exception e){stage=9;Log.Error("[SAB FEATURE TEST] FAIL "+e);Application.Quit();}
  }
 }
}




