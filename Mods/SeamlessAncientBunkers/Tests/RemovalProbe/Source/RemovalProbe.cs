using System;
using System.IO;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;
namespace SABRemovalProbe
{
 public class Probe:GameComponent
 {
  bool saved,done;
  public Probe(Game game){}
  public override void ExposeData(){Scribe_Values.Look(ref saved,"saved");}
  void Report(string text){Log.Message("[SAB REMOVAL] "+text);File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"removal-results.txt"),text+Environment.NewLine);}
  public override void GameComponentUpdate()
  {
   if(done||!GenCommandLine.CommandLineArgPassed("sab-removal-test")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;
   try{
    if(AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="SeamlessAncientBunkers"))throw new Exception("Production mod assembly still loaded");
    var pawns=PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead;
    if(pawns.GroupBy(p=>p.ThingID).Any(g=>g.Count()>1))throw new Exception("Duplicate pawn identity");
    if(pawns.Any(p=>p.CurJobDef?.defName.StartsWith("SAB_")==true))throw new Exception("Custom job survived removal");
    if(!saved){saved=true;Report("PASS original cleaned save loads without production mod; pawn identities unique; no custom jobs");LongEventHandler.QueueLongEvent(()=>{GameDataSaveLoader.SaveGame("SAB-AfterRemoval");GameDataSaveLoader.LoadGame("SAB-AfterRemoval");},null,false,null);return;}
    Report("PASS save/reload after removal; maps="+Find.Maps.Count+"; colonists="+Find.Maps.Sum(m=>m.mapPawns.FreeColonistsSpawned.Count));done=true;Application.Quit();
   }catch(Exception ex){Report("FAIL "+ex);done=true;Application.Quit();}
  }
 }
}
