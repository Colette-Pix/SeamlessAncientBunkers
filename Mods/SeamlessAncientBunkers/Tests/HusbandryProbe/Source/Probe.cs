using System;
using System.Linq;
using HarmonyLib;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SABHusbandryProbe
{
 public class Probe:GameComponent
 {
  int stage,deadline; AncientHatch hatch; Pawn worker,animal,fighter,enemy; CompHasGatherableBodyResource resource; bool crossed;
  public Probe(Game game){}
  bool Active=>GenCommandLine.CommandLineArgPassed("sab-husbandry-tests");
  void Check(bool ok,string label){if(!ok)throw new Exception(label);Log.Message("[SAB HUSBANDRY] PASS "+label);}
  IntVec3 Cell(Map m)=> (m==hatch.Map?hatch.Position:hatch.exit.Position)+new IntVec3(4,0,3);
  void Move(Pawn p,Map m){p.jobs.StopAll();if(p.Spawned)p.DeSpawn();GenSpawn.Spawn(p,Cell(m),m);}
  Pawn Make(string kind,Faction faction,Map m){var p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDef.Named(kind),faction,forceGenerateNewPawn:true,canGeneratePawnRelations:false));GenSpawn.Spawn(p,Cell(m),m);return p;}
  void StartWork(string kind,string giver,Map destination)
  {
   if(animal?.Spawned==true)animal.Destroy();
   Move(worker,destination==hatch.Map?hatch.PocketMap:hatch.Map);worker.drafter.Drafted=false;
   animal=Make(kind,Faction.OfPlayer,destination);animal.gender=Gender.Female;animal.ageTracker.AgeBiologicalTicks=5L*3600000;
   resource=giver=="Milk"?(CompHasGatherableBodyResource)animal.TryGetComp<CompMilkable>():animal.TryGetComp<CompShearable>();
   AccessTools.Field(typeof(CompHasGatherableBodyResource),"fullness").SetValue(resource,1f);
   foreach(var w in DefDatabase<WorkTypeDef>.AllDefsListForReading)worker.workSettings.SetPriority(w,0);
   worker.workSettings.SetPriority(WorkTypeDefOf.Handling,1);Manager.Current.cooldowns.Clear();
   var def=DefDatabase<WorkGiverDef>.AllDefsListForReading.First(d=>d.giverClass.Name=="WorkGiver_"+giver);
   using(new RemotePawnScope(worker,destination,Cell(destination)))using(new ProbeAudit(destination))
   {var j=WorkProbe.At(worker,def,animal,IntVec3.Invalid,false,true);Check(j!=null,giver+" remote query");JobMaker.ReturnToPool(j);}
   deadline=Manager.Now+22000;
  }
  void StartCombat(string kind,Map destination)
  {
   worker.drafter.Drafted=true;worker.jobs.StopAll();if(animal?.Spawned==true)animal.Destroy();
   if(fighter?.Spawned==true)fighter.Destroy();if(enemy?.Spawned==true)enemy.Destroy();
   fighter=Make(kind,Faction.OfPlayer,destination==hatch.Map?hatch.PocketMap:hatch.Map);
   if(kind=="LabradorRetriever")
   {
    Check(!EnemyPursuit.Friendly(fighter),"untrained animal excluded");
    fighter.training.Train(TrainableDefOf.Obedience,worker,true);fighter.training.Train(TrainableDefOf.Release,worker,true);
   }
   else fighter.mindState.mentalStateHandler.TryStartMentalState(DefDatabase<MentalStateDef>.GetNamed("VPE_Manhunter"),forceWake:true);
   enemy=Make("Pirate",Faction.OfAncientsHostile,destination);enemy.Position=Cell(destination)+new IntVec3(4,0,0);enemy.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait),JobCondition.InterruptForced);
   Check(EnemyPursuit.Friendly(fighter),kind+" eligible");
   var portal=fighter.Map==hatch.Map?(MapPortal)hatch:hatch.exit;
   portal.TrySetForbidden(true);Check(!EnemyPursuit.CanCross(fighter,portal),"forbidden combat entrance respected");portal.TrySetForbidden(false);
   Check(EnemyPursuit.CanCross(fighter,portal),"combat can cross despite destination threat");
   crossed=false;deadline=Manager.Now+22000;
  }
  public override void GameComponentUpdate(){if(!Active||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;}
  public override void GameComponentTick()
  {
   if(!Active||stage==99||LongEventHandler.AnyEventNowOrWaiting)return;
   try{
    if(stage>0&&Manager.Now>deadline)throw new Exception("Timeout stage="+stage+" worker="+worker?.CurJob+" fighter="+fighter?.CurJob+" mental="+fighter?.MentalStateDef+" resource="+resource?.Fullness);
    foreach(var p in new[]{worker,animal,fighter,enemy}.Where(p=>p!=null&&!p.Dead)){if(p.needs.food!=null)p.needs.food.CurLevel=1;if(p.needs.rest!=null)p.needs.rest.CurLevel=1;}
    if(stage==0)
    {
     hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);
     Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.work=true;BunkerMod.Settings.scanInterval=60;BunkerMod.Settings.minStay=0;Manager.Current.Toggle(hatch);
     foreach(var m in new[]{hatch.Map,hatch.PocketMap})
     {
      foreach(var p in m.mapPawns.AllPawnsSpawned.ToList())p.Destroy();
      foreach(var c in GenRadial.RadialCellsAround(Cell(m),18,true).Where(c=>c.InBounds(m))){m.roofGrid.SetRoof(c,null);foreach(var t in c.GetThingList(m).ToList())if(!(t is MapPortal))t.Destroy();m.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);}m.fogGrid.ClearAllFog();
     }
     worker=Make("Colonist",Faction.OfPlayer,hatch.Map);worker.workSettings.EnableAndInitialize();worker.skills.GetSkill(SkillDefOf.Animals).Level=20;
     StartWork("Cow","Milk",hatch.PocketMap);stage=1;return;
    }
    if(stage<=4&&resource.Fullness<0.1f)
    {
     Check(worker.Map==animal.Map,"physical travel and completed "+(stage<=2?"milking":"shearing")+" direction "+stage);
     if(stage==1)StartWork("Cow","Milk",hatch.Map);
     if(stage==2)StartWork("Alpaca","Shear",hatch.PocketMap);
     if(stage==3)StartWork("Alpaca","Shear",hatch.Map);
     if(stage==4)
     {
      animal.Destroy();Move(worker,hatch.Map);foreach(var w in DefDatabase<WorkTypeDef>.AllDefsListForReading)worker.workSettings.SetPriority(w,0);worker.workSettings.SetPriority(WorkTypeDefOf.Hunting,1);
      worker.skills.GetSkill(SkillDefOf.Shooting).Level=20;worker.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("Gun_AssaultRifle")));
      animal=Make("Hare",null,hatch.PocketMap);hatch.PocketMap.designationManager.AddDesignation(new Designation(animal,DesignationDefOf.Hunt));Manager.Current.cooldowns.Clear();deadline=Manager.Now+22000;
     }
     stage++;return;
    }
    if(stage==5&&animal.Dead)
    {
     Check(worker.Map==hatch.PocketMap,"hunter travels and kills designated prey in bunker");
     worker.jobs.StopAll();animal=Make("Hare",null,hatch.Map);hatch.Map.designationManager.AddDesignation(new Designation(animal,DesignationDefOf.Hunt));Manager.Current.cooldowns.Clear();deadline=Manager.Now+22000;stage=6;return;
    }
    if(stage==6&&animal.Dead){Check(worker.Map==hatch.Map,"hunter travels and kills designated prey on surface");StartCombat("LabradorRetriever",hatch.PocketMap);stage=7;return;}
    if(stage>=7&&stage<=10)
    {
     if(fighter.Map==enemy.Map)crossed=true;
     if(crossed&&(enemy.Dead||enemy.health.summaryHealth.SummaryHealthPercent<0.99f))
     {
      Check(true,"pursuit crosses and damages enemy stage "+stage);
      if(stage==7)StartCombat("LabradorRetriever",hatch.Map);
      if(stage==8)StartCombat("VPE_SummonedSkeleton",hatch.PocketMap);
      if(stage==9)StartCombat("VPE_SummonedSkeleton",hatch.Map);
      if(stage==10){Log.Message("[SAB HUSBANDRY] ACCEPTANCE COMPLETE");stage=99;Application.Quit();return;}
      stage++;
     }
    }
   }catch(Exception e){Log.Error("[SAB HUSBANDRY] FAIL "+e);stage=99;Application.Quit();}
  }
 }
}
