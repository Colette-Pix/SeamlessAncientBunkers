using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SABServicesProbe
{
 public class BedPreferenceProbe:GameComponent
 {
  int stage,start,nextLog,sleepStarted;AncientHatch hatch,farHatch;Pawn sleeper;Building_Bed owned,local,nearby,medical;bool woke;
  public BedPreferenceProbe(Game game){}
  bool Active=>GenCommandLine.CommandLineArgPassed("sab-services-bed-tests");
  void Check(bool ok,string label){if(!ok)throw new Exception(label);Log.Message("[SAB BEDS] PASS "+label);}
  IntVec3 Center(Map map)=>(map==hatch.Map?hatch.Position:map==hatch.PocketMap?hatch.exit.Position:farHatch.exit.Position)+new IntVec3(4,0,4);
  void Clear(Map map,IntVec3 center,float radius)
  {
   foreach(var c in GenRadial.RadialCellsAround(center,radius,true).Where(c=>c.InBounds(map)))
   {map.roofGrid.SetRoof(c,null);foreach(var t in c.GetThingList(map).ToList())if(!(t is MapPortal)&&!t.Destroyed&&t.def.destroyable)t.Destroy();map.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);map.areaManager.Home[c]=true;}
   map.fogGrid.ClearAllFog();
  }
  Building_Bed Bed(Map map,IntVec3? at=null)
  {
   var bed=(Building_Bed)ThingMaker.MakeThing(ThingDefOf.Bed,ThingDefOf.WoodLog);bed.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(bed,at??Center(map),map);bed.SetForbidden(false,false);return bed;
  }
  void ClearIntent(Job query=null)
  {
   if(query!=null)JobMaker.ReturnToPool(query);var i=Broker.For(sleeper);if(i!=null)Broker.Cancel(i);
   Manager.Current.cooldowns.Clear();((IDictionary)AccessTools.Field(typeof(Manager),"nextScan").GetValue(Manager.Current)).Clear();
  }
  Job Rest()=> (Job)AccessTools.Method(typeof(JobGiver_GetRest),"TryGiveJob").Invoke(new JobGiver_GetRest(),new object[]{sleeper});
  void Move(Map map)
  {sleeper.jobs.StopAll();ClearIntent();sleeper.DeSpawn();GenSpawn.Spawn(sleeper,Center(map)+new IntVec3(3,0,0),map);sleeper.needs.rest.CurLevelPercentage=.1f;}
  void ExpectOwned(string label)
  {var job=Rest();Check(job?.def==Manager.TravelDef&&Broker.For(sleeper)?.target==owned&&sleeper.ownership.OwnedBed==owned,label);ClearIntent(job);}
  void ExpectLocal(string label)
  {var job=Rest();Log.Message("[SAB BEDS] fallback diagnostic job="+job+" local="+local+" localValid="+RestUtility.IsValidBedFor(local,sleeper,sleeper,true)+" ownedForbidden="+owned.IsForbidden(sleeper)+" forbidComp="+(owned.TryGetComp<CompForbiddable>()!=null)+" intent="+Broker.For(sleeper)?.target);Check(job?.def==JobDefOf.LayDown&&job.targetA.Thing==local&&Broker.For(sleeper)==null,label);ClearIntent(job);}
  void Setup()
  {
   hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);
   Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.rest=true;BunkerMod.Settings.medical=true;BunkerMod.Settings.scanInterval=120;BunkerMod.Settings.minStay=600;Manager.Current.Toggle(hatch);
   Clear(hatch.Map,hatch.Position,48);Clear(hatch.PocketMap,hatch.exit.Position,25);
   farHatch=(AncientHatch)ThingMaker.MakeThing(hatch.def);farHatch.layout=hatch.layout;farHatch.stockpileType=hatch.stockpileType;
   GenSpawn.Spawn(farHatch,hatch.Position+new IntVec3(35,0,0),hatch.Map);farHatch.GetComp<CompHackable>().HackNow();var far=farHatch.GetOtherMap();Clear(far,farHatch.exit.Position,25);Manager.Current.Toggle(farHatch);
   foreach(var map in new[]{hatch.Map,hatch.PocketMap,far})
   {foreach(var p in map.mapPawns.AllPawnsSpawned.ToList())p.Destroy();foreach(var b in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial).OfType<Building_Bed>().ToList())b.Destroy();}
   sleeper=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,forceNoBackstory:true,fixedBiologicalAge:30,fixedChronologicalAge:30));
   foreach(var trait in sleeper.story.traits.allTraits.ToList())sleeper.story.traits.RemoveTrait(trait);
   GenSpawn.Spawn(sleeper,Center(hatch.Map)+new IntVec3(3,0,0),hatch.Map);for(int h=0;h<24;h++)sleeper.timetable.SetAssignment(h,TimeAssignmentDefOf.Anything);
   sleeper.workSettings.EnableAndInitialize();foreach(var type in DefDatabase<WorkTypeDef>.AllDefs)sleeper.workSettings.SetPriority(type,0);sleeper.needs.rest.CurLevelPercentage=.1f;
   owned=Bed(far);nearby=Bed(hatch.PocketMap);local=Bed(hatch.Map);sleeper.ownership.ClaimBedIfNonMedical(owned);
   Check(!HealthAIUtility.ShouldSeekMedicalRest(sleeper),"fixture pawn seeks ordinary rest");
   ExpectOwned("remote assigned bed outranks usable local unassigned bed");
   Manager.Current.CanScan(sleeper,Purpose.Rest,true);Manager.Current.cooldowns.Add(new Cooldown{pawn=sleeper,failedMap=owned.Map,failures=3,until=Manager.Now+9000});
   var first=Rest();Check(first?.def==Manager.TravelDef&&Broker.For(sleeper)?.target==owned,"valid assigned bed survives active scan throttle and destination failure history");
   JobMaker.ReturnToPool(first);Manager.Current.intents.Clear();
   var repeated=Rest();Check(repeated?.def==Manager.TravelDef&&Broker.For(sleeper)?.target==owned&&sleeper.ownership.OwnedBed==owned,"repeated same-tick GetRest keeps original assignment");ClearIntent(repeated);
   owned.Map.areaManager.TryMakeNewAllowed(out var allowed);foreach(var c in owned.Map.AllCells)allowed[c]=true;allowed[owned.Position]=false;
   using(new RemotePawnScope(sleeper,owned.Map,farHatch.exit.Position))sleeper.playerSettings.AreaRestrictionInPawnCurrentMap=allowed;
   ExpectLocal("assigned bed forbidden by allowed area retains native local fallback");
   using(new RemotePawnScope(sleeper,owned.Map,farHatch.exit.Position))sleeper.playerSettings.AreaRestrictionInPawnCurrentMap=null;
   var walls=new List<Thing>();
   foreach(var c in owned.OccupiedRect().ExpandedBy(1).EdgeCells)
   {var wall=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);wall.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(wall,c,owned.Map);walls.Add(wall);}
   ExpectLocal("unreachable assigned bed retains native local fallback");foreach(var wall in walls)wall.Destroy();
   var ownedPosition=owned.Position;var ownedMap=owned.Map;owned.DeSpawn();ExpectLocal("unspawned assigned bed retains native local fallback");GenSpawn.Spawn(owned,ownedPosition,ownedMap);
   sleeper.ownership.ClaimBedIfNonMedical(owned);local.Destroy();local=null;
   var routes=Graph.Reachable(sleeper);var nearRoute=routes.First(r=>r.map==nearby.Map);var farRoute=routes.First(r=>r.map==owned.Map);
   Check(nearRoute.cost+nearRoute.landing.DistanceTo(nearby.Position)<farRoute.cost+farRoute.landing.DistanceTo(owned.Position),"unassigned bed on another floor is closer than assigned bed");
   ExpectOwned("farther assigned bed wins against closer unassigned remote floor");
   var query=Rest();Check(query?.def==Manager.TravelDef&&Broker.For(sleeper)?.target==owned,"real sleep starts trip to assigned floor");sleeper.jobs.StartJob(query,JobCondition.InterruptForced);stage=1;start=Manager.Now;
  }
  public override void GameComponentUpdate()
  {if(!Active||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;}
  public override void GameComponentTick()
  {
   if(!Active||stage==99||LongEventHandler.AnyEventNowOrWaiting)return;
   try
   {
    if(stage==0){Setup();return;}
    if(sleeper.needs.food!=null)sleeper.needs.food.CurLevelPercentage=1;if(sleeper.needs.joy!=null)sleeper.needs.joy.CurLevelPercentage=1;if(sleeper.needs.mood!=null)sleeper.needs.mood.CurLevelPercentage=1;
    if(Manager.Now>=nextLog){nextLog=Manager.Now+1800;Log.Message("[SAB BEDS] progress stage="+stage+" ticks="+(Manager.Now-start)+" map="+sleeper.Map?.uniqueID+" job="+sleeper.CurJob+" rest="+sleeper.needs.rest.CurLevelPercentage+" owner="+sleeper.ownership.OwnedBed);}
    if(stage==1&&sleeper.CurrentBed()==owned&&sleeper.CurJobDef==JobDefOf.LayDown&&sleeper.needs.rest.CurLevelPercentage>.2f)
    {Check(sleeper.Map==owned.Map&&sleeper.ownership.OwnedBed==owned&&nearby.OwnersForReading.Count==0,"native sleep restores rest in assigned bed without claiming closer remote bed");sleeper.needs.rest.CurLevelPercentage=1;woke=true;stage=2;return;}
    if(stage==2&&woke&&sleeper.CurJobDef!=JobDefOf.LayDown)
    {
     Check(sleeper.ownership.OwnedBed==owned,"original bed assignment remains after native sleep completes");
     var same=Rest();Check(same?.def==JobDefOf.LayDown&&same.targetA.Thing==owned&&Broker.For(sleeper)==null,"same-floor sleep retains native owned-bed selection");ClearIntent(same);
     Move(hatch.Map);local=Bed(hatch.Map);medical=Bed(hatch.Map,Center(hatch.Map)+new IntVec3(0,0,5));medical.Medical=true;
     var flu=HediffMaker.MakeHediff(HediffDef.Named("Flu"),sleeper);flu.Severity=.4f;sleeper.health.AddHediff(flu);
     Check(HealthAIUtility.ShouldSeekMedicalRest(sleeper),"ill pawn qualifies for native medical rest");
     var job=Rest();Check(job?.def==JobDefOf.LayDown&&job.targetA.Thing==medical&&Broker.For(sleeper)==null,"GetRest preserves native appropriate medical bed ahead of distant owned bed");ClearIntent(job);
     job=NeedProbe.LocalMedical(sleeper);Check(job?.def==JobDefOf.LayDown&&job.targetA.Thing==medical&&Broker.For(sleeper)==null,"patient-rest node preserves native local medical decision");sleeper.jobs.StartJob(job,JobCondition.InterruptForced);stage=3;start=Manager.Now;return;
    }
    if(stage==3&&sleeper.CurrentBed()==medical)
    {
     if(sleepStarted==0)sleepStarted=Manager.Now;
     if(Manager.Now-sleepStarted>=180){Check(sleeper.ownership.OwnedBed==owned&&Broker.For(sleeper)==null,"actual medical bed rest preserves distant ordinary bed assignment");Log.Message("[SAB BEDS] ACCEPTANCE COMPLETE");stage=99;Application.Quit();return;}
    }
    if(Manager.Now-start>22000)throw new Exception("Timeout stage "+stage+" job="+sleeper.CurJob+" intent="+Broker.For(sleeper)?.purpose);
   }
   catch(Exception e){Log.Error("[SAB BEDS] FAIL "+e);stage=99;Application.Quit();}
  }
 }
}
