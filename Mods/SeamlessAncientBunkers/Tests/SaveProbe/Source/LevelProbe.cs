using System;
using System.Linq;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
namespace SABSaveProbe
{
 public class LevelProbe:GameComponent
 {
  int stage;AncientHatch first,second;
  public LevelProbe(Game g){}
  public override void ExposeData(){Scribe_Values.Look(ref stage,"levelStage");Scribe_References.Look(ref first,"levelFirst");Scribe_References.Look(ref second,"levelSecond");}
  void Check(bool value,string text){if(!value)throw new Exception(text);Log.Message("[SAB LEVEL TEST] PASS "+text);}
  public override void GameComponentUpdate()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-level-tests")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||stage==3)return;
   Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);
   try
   {
    if(stage==0)
    {
     first=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);
     BunkerLevels.Entered(first.PocketMap);Check(BunkerLevels.Number(first.PocketMap)==-1,"existing first bunker is level -1");
     stage=1;LongEventHandler.QueueLongEvent(()=>{second=(AncientHatch)GenSpawn.Spawn(ThingMaker.MakeThing(ThingDef.Named("AncientHatch")),first.Position+IntVec3.West*8,first.Map);second.GetComp<CompHackable>().HackNow();second.GetOtherMap();},null,false,null);
    }
    else if(stage==1)
    {
     Check(!BunkerLevels.Stack(first.Map).Contains(second.PocketMap),"generated but unentered bunker is excluded");
     var pawn=Find.Maps.SelectMany(m=>m.mapPawns.FreeColonistsSpawned).First();second.OnEntered(pawn);
     Check(BunkerLevels.Number(second.PocketMap)==-2,"entry callback assigns next bunker level -2");
     Check(BunkerLevels.Next(first.Map,false)==first.PocketMap&&BunkerLevels.Next(first.PocketMap,false)==second.PocketMap&&BunkerLevels.Next(second.PocketMap,false)==first.Map,"down cycles 0, -1, -2, 0");
     Check(BunkerLevels.Next(first.Map,true)==second.PocketMap&&BunkerLevels.Next(second.PocketMap,true)==first.PocketMap&&BunkerLevels.Next(first.PocketMap,true)==first.Map,"up cycles 0, -2, -1, 0");
     Current.Game.CurrentMap=first.Map;BunkerLevels.Switch(false);Check(Find.CurrentMap==first.PocketMap,"navigation switches the displayed game map");
     Check(DefDatabase<KeyBindingDef>.GetNamed("SAB_LevelUp").defaultKeyCodeA==KeyCode.Equals&&DefDatabase<KeyBindingDef>.GetNamed("SAB_LevelDown").defaultKeyCodeA==KeyCode.Minus,"rebindable defaults are equals and minus");
     stage=2;LongEventHandler.QueueLongEvent(()=>{GameDataSaveLoader.SaveGame("SAB-Levels");GameDataSaveLoader.LoadGame("SAB-Levels");},null,false,null);
    }
    else{Check(BunkerLevels.Number(first.PocketMap)==-1&&BunkerLevels.Number(second.PocketMap)==-2,"entry order survives save and reload");stage=3;Log.Message("[SAB LEVEL TEST] LEVEL ACCEPTANCE COMPLETE");Application.Quit();}
   }catch(Exception e){stage=3;Log.Error("[SAB LEVEL TEST] FAIL "+e);Application.Quit();}
  }
 }
}
