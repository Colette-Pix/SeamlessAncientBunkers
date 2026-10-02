using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;
namespace SABSaveProbe
{
 public class ThirstProbe:GameComponent
 {
  int stage,start;Pawn pawn;AncientHatch hatch;
  public ThirstProbe(Game g){}
  void Check(bool value,string text){if(!value)throw new Exception(text);Log.Message("[SAB THIRST TEST] PASS "+text);}
  public override void GameComponentUpdate()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-thirst-tests")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||stage==9)return;
   Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);
   try
   {
    if(stage==0)
    {
     hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);
     Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;Manager.Current.EnableClearedBunkers();
     pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false));GenSpawn.Spawn(pawn,hatch.exit.Position+IntVec3.East*4,hatch.PocketMap);
     var need=pawn.needs.AllNeeds.FirstOrDefault(n=>n.def.defName=="DBHThirst");Check(need!=null,"Dubs thirst enabled in isolated test settings");need.CurLevel=0.05f;
     var water=ThingMaker.MakeThing(ThingDef.Named("DBH_WaterBottle"));water.stackCount=5;GenSpawn.Spawn(water,hatch.Position+IntVec3.East*3,hatch.Map);
     var node=(ThinkNode_JobGiver)Activator.CreateInstance(AccessTools.TypeByName("DubsBadHygiene.JobGiver_DrinkWater"));
     var job=ExtraNeeds.Query(node,pawn);Check(job?.def==Manager.TravelDef&&Broker.For(pawn)?.purpose==Purpose.Thirst,"native Dubs drink search routes to surface water");
     pawn.jobs.TryTakeOrderedJob(job);stage=1;start=Manager.Now;Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
    }
    else
    {
     var need=pawn.needs.AllNeeds.First(n=>n.def.defName=="DBHThirst");
     if(stage==1&&pawn.Map==hatch.Map&&need.CurLevel>0.5f)
     {
      Check(true,"pawn reaches surface and satisfies thirst with native drink job");pawn.jobs.StopAll();pawn.inventory.innerContainer.ClearAndDestroyContents();Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;Manager.Current.EnableClearedBunkers();
      foreach(var t in hatch.Map.listerThings.AllThings.Where(t=>t.def.defName=="DBH_WaterBottle").ToList())t.Destroy();
      foreach(var c in hatch.Map.AllCells)if(c.GetTerrain(hatch.Map).IsWater)hatch.Map.terrainGrid.SetTerrain(c,TerrainDefOf.Soil);
      var water=ThingMaker.MakeThing(ThingDef.Named("DBH_WaterBottle"));water.stackCount=5;GenSpawn.Spawn(water,hatch.exit.Position+IntVec3.East*3,hatch.PocketMap);need.CurLevel=0.05f;
      var node=(ThinkNode_JobGiver)Activator.CreateInstance(AccessTools.TypeByName("DubsBadHygiene.JobGiver_DrinkWater"));var job=ExtraNeeds.Query(node,pawn);
      Log.Message("[SAB THIRST TEST] inward query="+job+" intent="+Broker.For(pawn)?.purpose+" carry="+pawn.carryTracker.CarriedThing+" route="+Portal.BlockReason(pawn,hatch,true,out _));
      Check(job?.def==Manager.TravelDef&&Broker.For(pawn)?.finalMap==hatch.PocketMap,"native Dubs drink search also routes INTO bunker");pawn.jobs.TryTakeOrderedJob(job);stage=3;start=Manager.Now;
     }
     else if(stage==3&&pawn.Map==hatch.PocketMap&&need.CurLevel>0.5f){Check(true,"pawn enters bunker and satisfies thirst there");stage=9;Log.Message("[SAB THIRST TEST] THIRST ACCEPTANCE COMPLETE");Application.Quit();}
     else if(Manager.Now-start>6000)throw new Exception("thirst travel timed out: "+pawn.CurJob+" map="+pawn.Map+" need="+need.CurLevel);
    }
   }catch(Exception e){stage=9;Log.Error("[SAB THIRST TEST] FAIL "+e);Application.Quit();}
  }
 }
}
