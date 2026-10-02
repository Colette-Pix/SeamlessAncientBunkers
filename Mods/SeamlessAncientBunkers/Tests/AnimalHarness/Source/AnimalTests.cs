using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;
namespace SABAnimalTests
{
 public class AnimalTests:GameComponent
 {
  int stage,deadline;Pawn dog,master;Map surface,bunker;AncientHatch hatch;Thing food;Building_Bed bed;bool saved;
  public AnimalTests(Game game){}
  void Report(string s){Log.Message("[SAB ANIMAL TEST] "+s);}
  public override void ExposeData(){Scribe_Values.Look(ref stage,"animalStage");Scribe_Values.Look(ref deadline,"animalDeadline");Scribe_Values.Look(ref saved,"animalSaved");Scribe_References.Look(ref dog,"testDog");Scribe_References.Look(ref master,"testMaster");Scribe_References.Look(ref surface,"testSurface");Scribe_References.Look(ref bunker,"testBunker");Scribe_References.Look(ref hatch,"testHatch");Scribe_References.Look(ref food,"testFood");Scribe_References.Look(ref bed,"testBed");}
  void Check(bool value,string message){if(!value)throw new Exception(message);Report("PASS "+message);}
  void Next(int n){stage=n;deadline=Manager.Now+18000;Report("Stage "+n);}
  void Reset(){if(Broker.For(dog)!=null)Broker.Cancel(Broker.For(dog));dog.jobs.StopAll();Manager.Current.cooldowns.Clear();dog.needs.food.CurLevel=1;dog.needs.rest.CurLevel=1;}
  void Move(Pawn p,Map map,IntVec3 c){p.jobs.StopAll();p.DeSpawn();GenSpawn.Spawn(p,c,map);}
  Thing Spawn(string def,Map map,IntVec3 cell){var t=ThingMaker.MakeThing(ThingDef.Named(def));if(t is Building)t.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(t,cell,map);return t;}
  void Clear(Map map,IntVec3 pos){foreach(var c in GenRadial.RadialCellsAround(pos,16,true).Where(c=>c.InBounds(map))){map.roofGrid.SetRoof(c,null);foreach(var t in c.GetThingList(map).ToList())if(!(t is Pawn)&&!(t is MapPortal))t.Destroy();map.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);}map.fogGrid.ClearAllFog();}
  public override void GameComponentUpdate(){if(!GenCommandLine.CommandLineArgPassed("sab-animal-tests")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;}
  public override void GameComponentTick()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-animal-tests")||LongEventHandler.AnyEventNowOrWaiting)return;
   try
   {
    if(stage>0&&Manager.Now>deadline)throw new Exception("Timeout stage="+stage+" dog="+dog?.CurJob+" map="+dog?.Map+" intent="+Broker.For(dog)?.purpose);
    switch(stage)
    {
     case 0:
      hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);surface=hatch.Map;bunker=hatch.PocketMap;
      Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.debug=true;BunkerMod.Settings.scanInterval=60;BunkerMod.Settings.minStay=300;Manager.Current.Toggle(hatch);
      foreach(var p in Find.Maps.SelectMany(m=>m.mapPawns.FreeColonistsSpawned)){p.drafter.Drafted=true;p.jobs.StopAll();}master=surface.mapPawns.FreeColonistsSpawned.First();
      Clear(surface,hatch.Position);Clear(bunker,hatch.exit.Position);
      foreach(var m in new[]{surface,bunker})foreach(var t in m.listerThings.AllThings.Where(t=>t.def.ingestible!=null&&!(t is Pawn)).ToList())t.Destroy();
      dog=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named("LabradorRetriever"),Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false));GenSpawn.Spawn(dog,hatch.Position+new IntVec3(5,0,2),surface);Reset();
      Check(Portal.Eligible(dog),"tamed animal eligible");
      food=Spawn("Kibble",bunker,hatch.exit.Position+new IntVec3(4,0,3));food.stackCount=75;
      var local=Spawn("Kibble",surface,dog.Position+IntVec3.East);local.stackCount=10;dog.needs.food.CurLevel=0.2f;
      var node=new JobGiver_GetFood();var localJob=JobMaker.MakeJob(JobDefOf.Ingest,local);
      Check(AnimalSupport.Need(dog,Purpose.Food,localJob,node)==null,"nearby local food preferred");JobMaker.ReturnToPool(localJob);local.Destroy();
      dog.needs.food.CurLevel=0.01f;Next(1);break;
     case 1:
      if(!saved&&dog.CurJobDef==Manager.TravelDef){saved=true;LongEventHandler.QueueLongEvent(()=>{GameDataSaveLoader.SaveGame("SAB-AnimalTransit");GameDataSaveLoader.LoadGame("SAB-AnimalTransit");Report("PASS animal transit save/reload");},null,false,null);break;}
      if(dog.Map==bunker&&dog.needs.food.CurLevelPercentage>0.5f){Check(PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.Count(p=>p.ThingID==dog.ThingID)==1,"animal food route eats and identity stays unique");Reset();Move(dog,surface,hatch.Position+IntVec3.East*5);bed=(Building_Bed)Spawn("AnimalSleepingSpot",bunker,hatch.exit.Position+new IntVec3(4,0,4));dog.ownership.ClaimBedIfNonMedical(bed);dog.needs.rest.CurLevel=0.01f;Next(2);}break;
     case 2:
      if(dog.Map==bunker&&dog.CurJobDef==JobDefOf.LayDown&&dog.CurrentBed()==bed){Check(true,"animal crosses to assigned sleeping spot and sleeps");Reset();Move(dog,surface,hatch.Position+IntVec3.East*5);Move(master,bunker,hatch.exit.Position+new IntVec3(5,0,4));master.drafter.Drafted=true;dog.training.Train(TrainableDefOf.Obedience,master,true);dog.playerSettings.Master=master;dog.playerSettings.followDrafted=true;Check(AnimalSupport.Followee(dog)==master,"assigned master follow setting recognized");Next(3);}break;
     case 3:
      if(dog.Map==bunker){Check(dog.playerSettings.Master==master,"animal follows drafted master through hatch without losing assignment");Reset();dog.playerSettings.followDrafted=false;Check(AnimalSupport.Followee(dog)==null,"disabled follow setting respected");Move(dog,surface,hatch.Position+IntVec3.East*3);
       hatch.TrySetForbidden(true);Check(Graph.Reachable(dog).Count==0,"forbidden hatch blocks animal routes");hatch.TrySetForbidden(false);
       surface.areaManager.TryMakeNewAllowed(out var area);area[dog.Position]=true;dog.playerSettings.AreaRestrictionInPawnCurrentMap=area;Check(Graph.Reachable(dog).Count==0,"animal allowed area blocks hatch");dog.playerSettings.AreaRestrictionInPawnCurrentMap=null;
       Next(4);}break;
     case 4:
      dog.needs.food.CurLevel=1;dog.needs.rest.CurLevel=1;
      if(dog.Map==bunker){Check(true,"idle animal wanders through nearby opened hatch");Reset();Manager.Current.Toggle(hatch);Check(Graph.Reachable(dog).Count==0,"traffic toggle blocks animals");Manager.Current.Toggle(hatch);Move(dog,surface,hatch.Position+IntVec3.East*3);dog.SetFaction(null);Next(5);}break;
     case 5: dog.needs.food.CurLevel=1;dog.needs.rest.CurLevel=1;if(dog.Map==bunker){Check(true,"wild animal wanders through nearby opened hatch");Report("ANIMAL ACCEPTANCE COMPLETE");stage=99;Application.Quit();}break;
     case 99:break;
    }
   }catch(Exception e){Report("FAIL "+e);stage=99;Application.Quit();}
  }
 }
}
