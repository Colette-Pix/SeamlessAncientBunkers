using System;
using System.Linq;
using Verse.AI;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
namespace SABSaveProbe
{
 public class DoorSecurityProbe:GameComponent
 {
  int stage;int bashStart;Pawn raider;Building_HackableDoor door;
  public DoorSecurityProbe(Game g){}
  public override void ExposeData(){Scribe_Values.Look(ref stage,"doorSecurityStage");Scribe_References.Look(ref raider,"doorSecurityRaider");Scribe_References.Look(ref door,"doorSecurityDoor");}
  void Check(bool yes,string message){if(!yes)throw new Exception(message);Log.Message("[SAB DOOR TEST] PASS "+message);}
  public override void GameComponentUpdate()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-door-tests")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||stage==3)return;
   Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);
   try
   {
    if(stage==0)
    {
     var hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);
     raider=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfAncientsHostile,forceGenerateNewPawn:true,canGeneratePawnRelations:false));
     GenSpawn.Spawn(raider,hatch.Position+IntVec3.East*3,hatch.Map);
     door=(Building_HackableDoor)ThingMaker.MakeThing(ThingDef.Named("AncientBlastDoor"));door.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(door,hatch.exit.Position+IntVec3.East*8,hatch.PocketMap);
     Check(!door.PawnCanOpen(raider),"unhacked lock remains effective");door.Hackable.HackNow();
     Check(door.PawnCanOpen(raider),"native hacked door ignores ownership before incoming transfer");
     Check(EnemyPursuit.Landing(raider,hatch.exit,out var cell),"incoming landing available");EnemyPursuit.Transfer(raider,hatch,cell);
     Check(BunkerDoorSecurity.Current.incoming.Contains(raider),"real pursuit transfer registers incoming enemy");
     Check(!door.PawnCanOpen(raider),"incoming hostile cannot open player blast door");
     Check(!door.CanPhysicallyPass(raider),"closed blast door physically blocks incoming enemy");
     var colonist=Find.Maps.SelectMany(m=>m.mapPawns.FreeColonistsSpawned).First();Check(door.PawnCanOpen(colonist),"colonist access is unchanged");
     door.SetFaction(Faction.OfAncients);Check(!door.PawnCanOpen(raider),"protection does not require claiming");
     var native=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfAncientsHostile,forceGenerateNewPawn:true,canGeneratePawnRelations:false));GenSpawn.Spawn(native,cell+IntVec3.East,hatch.PocketMap);
     Check(door.PawnCanOpen(native),"original bunker defender access is unchanged");
     stage=1;LongEventHandler.QueueLongEvent(()=>{GameDataSaveLoader.SaveGame("SAB-DoorSecurity");GameDataSaveLoader.LoadGame("SAB-DoorSecurity");},null,false,null);
    }
    else if(stage==1)
    {
     Check(BunkerDoorSecurity.Current.incoming.Contains(raider),"incoming enemy identity survives reload");
     Check(!door.PawnCanOpen(raider)&&!door.CanPhysicallyPass(raider),"door remains blocking after reload with threats present");
     raider.SetFaction(Faction.OfPlayer);Check(door.PawnCanOpen(raider),"recruited former enemy regains normal access");
     raider.SetFaction(Faction.OfAncientsHostile);door.SetFaction(Faction.OfPlayer);
     var map=door.Map;door.DeSpawn();raider.DeSpawn();
     foreach(var c in CellRect.FromLimits(8,8,24,16))
     {
      foreach(var t in c.GetThingList(map).ToList())if(t.def.category==ThingCategory.Building||t.def.category==ThingCategory.Plant)t.Destroy();
      map.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);
     }
     for(int x=10;x<=22;x++)for(int z=10;z<=14;z++)
      if(x==10||x==22||z==10||z==14||(x==16&&z!=12))
       GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel),new IntVec3(x,0,z),map);
     GenSpawn.Spawn(door,new IntVec3(16,0,12),map);GenSpawn.Spawn(raider,new IntVec3(12,0,12),map);
     var go=JobMaker.MakeJob(JobDefOf.Goto,new IntVec3(20,0,12));go.canBashDoors=true;go.canBashFences=true;
     raider.jobs.StartJob(go,JobCondition.InterruptForced);bashStart=Manager.Now;stage=2;
    }
    else
    {
     Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
     if(door.HitPoints<door.MaxHitPoints){Check(!door.Open,"native path follower damages closed blocking door instead of opening it");stage=3;Log.Message("[SAB DOOR TEST] ACCEPTANCE COMPLETE");Application.Quit();}
     else if(Manager.Now-bashStart>2500)throw new Exception("Native door bashing timed out: "+raider.CurJob);
    }
   }catch(Exception e){stage=3;Log.Error("[SAB DOOR TEST] FAIL "+e);Application.Quit();}
  }
 }
}


