using System;
using System.Linq;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;
namespace SABSaveProbe
{
 public class ClearanceProbe:GameComponent
 {
  int stage;AncientHatch hatch;
  public ClearanceProbe(Game g){}
  public override void ExposeData(){Scribe_Values.Look(ref stage,"clearanceStage");Scribe_References.Look(ref hatch,"clearanceHatch");}
  void Check(bool yes,string text){if(!yes)throw new Exception(text);Log.Message("[SAB CLEARANCE TEST] PASS "+text);}
  public override void GameComponentUpdate()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-clearance-tests")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||stage==2)return;
   Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);
   try
   {
    var manager=Manager.Current;
    if(stage==0)
    {
     hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);
     manager.Cleanup();BunkerMod.Settings.enabled=true;
     var enemy=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named("Megaspider"),Faction.OfInsects,forceGenerateNewPawn:true,canGeneratePawnRelations:false));
     GenSpawn.Spawn(enemy,hatch.exit.Position+IntVec3.East*3,hatch.PocketMap);
     var door=(Building)ThingMaker.MakeThing(ThingDef.Named("AncientBlastDoor"));door.SetFaction(Faction.OfAncients);GenSpawn.Spawn(door,hatch.exit.Position+IntVec3.East*8,hatch.PocketMap);
     Check(!door.ClaimableBy(Faction.OfPlayer),"blast door cannot be claimed while a defender lives");
     Check(!Portal.Cleared(hatch),"living defender prevents clearance");manager.EnableClearedBunkers();Check(!manager.Enabled(hatch),"occupied bunker stays disabled");
     enemy.Kill(null);Check(Portal.Cleared(hatch),"dead defender corpse does not prevent clearance");
     Check(door.ClaimableBy(Faction.OfPlayer),"cleared bunker blast door is claimable");
     new Designator_Claim().DesignateThing(door);Check(door.Faction==Faction.OfPlayer,"normal Claim command transfers blast door ownership");
     Check(!door.ClaimableBy(Faction.OfPlayer),"already owned blast door is not claimable again");
     manager.EnableClearedBunkers();Check(manager.Enabled(hatch),"cleared bunker enables without player toggle");
     manager.Toggle(hatch);manager.EnableClearedBunkers();Check(!manager.Enabled(hatch),"manual shutoff is respected");
     stage=1;LongEventHandler.QueueLongEvent(()=>{GameDataSaveLoader.SaveGame("SAB-ClearanceDisabled");GameDataSaveLoader.LoadGame("SAB-ClearanceDisabled");},null,false,null);
    }
    else
    {
     Check(manager.disabledByPlayer.Contains(hatch),"manual shutoff survives save/reload");manager.EnableClearedBunkers();Check(!manager.Enabled(hatch),"reload does not undo shutoff");
     manager.Toggle(hatch);Check(manager.Enabled(hatch)&&!manager.disabledByPlayer.Contains(hatch),"manual re-enable clears shutoff marker");
     var patient=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false));
     GenSpawn.Spawn(patient,hatch.exit.Position+IntVec3.East*4,hatch.PocketMap);
     for(int h=0;h<24;h++)patient.timetable.SetAssignment(h,TimeAssignmentDefOf.Sleep);
     var injury=patient.health.AddHediff(HediffDefOf.Cut,patient.RaceProps.body.corePart);injury.Severity=12;
     var bed=(Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed,ThingDefOf.WoodLog);bed.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(bed,hatch.Position+IntVec3.East*6,hatch.Map);patient.ownership.ClaimBedIfNonMedical(bed);
     Check(!bed.Medical&&HealthAIUtility.ShouldSeekMedicalRest(patient),"injured patient with ordinary owned recovery bed");
     var job=NeedProbe.Plan(patient,Purpose.Medical,null);Check(job?.def==Manager.TravelDef&&Broker.For(patient)?.target==bed,"medical route accepts vanilla ordinary recovery bed");
     if(job!=null)JobMaker.ReturnToPool(job);Broker.Cancel(Broker.For(patient));
     manager.enabledPortals.Clear();BunkerMod.Settings.enabled=false;manager.EnableClearedBunkers();Check(!manager.Enabled(hatch),"global disable prevents automatic activation");
     stage=2;Log.Message("[SAB CLEARANCE TEST] CLEARANCE ACCEPTANCE COMPLETE");Application.Quit();
    }
   }catch(Exception e){stage=2;Log.Error("[SAB CLEARANCE TEST] FAIL "+e);Application.Quit();}
  }
 }
}
