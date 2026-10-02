using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
namespace SABSaveProbe
{
 public class LaunchGatherProbe:GameComponent
 {
  int stage,start;Pawn pawn;AncientHatch hatch;Building_GravEngine engine;Thing console;
  public LaunchGatherProbe(Game g){}
  public override void ExposeData(){Scribe_Values.Look(ref stage,"launchGatherStage");Scribe_Values.Look(ref start,"launchGatherStart");Scribe_References.Look(ref pawn,"launchGatherPawn");Scribe_References.Look(ref hatch,"launchGatherHatch");Scribe_References.Look(ref engine,"launchGatherEngine");Scribe_References.Look(ref console,"launchGatherConsole");}
  void Check(bool value,string text){if(!value)throw new Exception(text);Log.Message("[SAB LAUNCH GATHER TEST] PASS "+text);}
  Thing Spawn(string def,IntVec3 pos){var t=ThingMaker.MakeThing(ThingDef.Named(def));t.SetFaction(Faction.OfPlayer);return GenSpawn.Spawn(t,pos,hatch.Map);}
  public override void GameComponentUpdate()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-launch-gather-tests")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||stage==3)return;
   Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);
   try
   {
    if(stage==0)
    {
     hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;Manager.Current.EnableClearedBunkers();
     pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false));GenSpawn.Spawn(pawn,hatch.exit.Position+IntVec3.East*4,hatch.PocketMap);
     var center=hatch.Map.Center+new IntVec3(25,0,20);foreach(var c in CellRect.CenteredOn(center,7)){foreach(var t in c.GetThingList(hatch.Map).ToList())if(!(t is Pawn)&&!(t is MapPortal))t.Destroy();hatch.Map.terrainGrid.SetTerrain(c,TerrainDefOf.Soil);hatch.Map.terrainGrid.SetFoundation(c,DefDatabase<TerrainDef>.GetNamed("Substructure"));}hatch.Map.fogGrid.ClearAllFog();
     engine=(Building_GravEngine)Spawn("GravEngine",center);console=Spawn("PilotConsole",center+new IntVec3(3,0,3));engine.ForceSubstructureDirty();Check(engine.ValidSubstructure.Count>20,"real gravship deck created");
     engine.pawnsToBoard=new HashSet<Pawn>{pawn};var boarding=ExtraNeeds.Query(new JobGiver_BoardOrLeaveGravship(),pawn);Check(boarding?.def==Manager.TravelDef&&Broker.For(pawn)?.purpose==Purpose.Boarding,"native boarding request routes bunker pawn upstairs");if(boarding!=null)JobMaker.ReturnToPool(boarding);Broker.Cancel(Broker.For(pawn));engine.pawnsToBoard=null;
     var ritual=Find.IdeoManager.IdeosListForReading.SelectMany(i=>i.PreceptsListForReading).OfType<Precept_GravshipLaunch>().First();
     var assignments=Dialog_BeginRitual.CreateRitualRoleAssignments(ritual,console,hatch.Map,null,new List<Pawn>{pawn},new Dictionary<string,Pawn>{{"pilot",pawn}},pawn);
     Check(assignments.Participants.Contains(pawn),"bunker pilot participates in native launch assignments");
     ritual.behavior.TryExecuteOn(console,pawn,ritual,null,assignments,true);
     Check(Manager.Current.gatherings.Any(g=>g.ritual==ritual&&g.pawns.Contains(pawn))&&pawn.GetLord()==null,"launch defers native ritual until bunker pilot arrives");
     stage=1;start=Manager.Now;LongEventHandler.QueueLongEvent(()=>{GameDataSaveLoader.SaveGame("SAB-LaunchGather");GameDataSaveLoader.LoadGame("SAB-LaunchGather");},null,false,null);
    }
    else if(stage==1){Check(Manager.Current.gatherings.Any(g=>g.assignments.Participants.Contains(pawn)&&g.ritual!=null),"launch ritual and selected pilot survive reload");stage=2;Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;}
    else if(pawn.Map==hatch.Map&&pawn.GetLord()?.LordJob is LordJob_Ritual){Check(true,"native launch ritual starts after pilot physically reaches surface");stage=3;Log.Message("[SAB LAUNCH GATHER TEST] LAUNCH GATHER ACCEPTANCE COMPLETE");Application.Quit();}
    else if(Manager.Now-start>6000)throw new Exception("launch gather timed out: "+pawn.CurJob+" map="+pawn.Map+" groups="+Manager.Current.gatherings.Count);
   }catch(Exception e){stage=3;Log.Error("[SAB LAUNCH GATHER TEST] FAIL "+e);Application.Quit();}
  }
 }
}
