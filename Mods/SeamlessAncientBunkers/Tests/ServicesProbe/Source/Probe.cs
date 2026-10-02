using System;
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
 public class Probe:GameComponent
 {
  int stage,started,nextLog;bool saved,reloaded;AncientHatch hatch;Pawn worker,baby;Thing consumer,stock;Corpse corpse;Bill_Production bill;Zone_Stockpile output;
  public Probe(Game game){}
  bool Active=>GenCommandLine.CommandLineArgPassed("sab-services-tests")||GenCommandLine.CommandLineArgPassed("sab-services-care-tests");
  void Check(bool ok,string label){if(!ok)throw new Exception(label);Log.Message("[SAB SERVICES] PASS "+label);}
  public override void ExposeData()
  {
   Scribe_Values.Look(ref stage,"servicesStage");Scribe_Values.Look(ref started,"servicesStarted");Scribe_Values.Look(ref saved,"servicesSaved");
   Scribe_References.Look(ref hatch,"servicesHatch");Scribe_References.Look(ref worker,"servicesWorker");Scribe_References.Look(ref baby,"servicesBaby");
   Scribe_References.Look(ref consumer,"servicesConsumer");Scribe_References.Look(ref stock,"servicesStock");Scribe_References.Look(ref corpse,"servicesCorpse");
   Scribe_References.Look(ref bill,"servicesBill");Scribe_References.Look(ref output,"servicesOutput");
  }
  public override void LoadedGame(){reloaded=true;}
  IntVec3 Cell(Map m)=>(m==hatch.Map?hatch.Position:hatch.exit.Position)+new IntVec3(4,0,4);
  Thing Spawn(string name,Map map,int count=1,ThingDef stuff=null,IntVec3? at=null)
  {
   var t=ThingMaker.MakeThing(ThingDef.Named(name),stuff);if(t.def.CanHaveFaction)t.SetFaction(Faction.OfPlayer);t.stackCount=count;
   var c=at??GenRadial.RadialCellsAround(Cell(map),14,true).First(x=>x.InBounds(map)&&x.Standable(map)&&x.GetEdifice(map)==null&&x.GetFirstItem(map)==null&&x.GetSlotGroup(map)==null);
   GenSpawn.Spawn(t,c,map);t.SetForbidden(false,false);return t;
  }
  Pawn Pawn(Map m,float age=30)
  {
   Pawn p;
   do{p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false,fixedBiologicalAge:age,fixedChronologicalAge:age,allowDowned:true,developmentalStages:age<3?DevelopmentalStage.Baby:age<13?DevelopmentalStage.Child:DevelopmentalStage.Adult));}
   while(age>3&&new[]{WorkTypeDefOf.Hauling,WorkTypeDefOf.Construction,WorkTypeDefOf.Childcare,DefDatabase<WorkTypeDef>.GetNamed("Cooking")}.Any(p.WorkTypeIsDisabled));
   foreach(var t in p.story.traits.allTraits.ToList())p.story.traits.RemoveTrait(t);
   GenSpawn.Spawn(p,Cell(m),m);p.workSettings.EnableAndInitialize();return p;
  }
  void Work(params WorkTypeDef[] types)
  {worker.jobs.StopAll();var i=Broker.For(worker);if(i!=null)Broker.Cancel(i);Manager.Current.cooldowns.Clear();foreach(var w in DefDatabase<WorkTypeDef>.AllDefs)worker.workSettings.SetPriority(w,types.Contains(w)?1:0);started=Manager.Now;}
  void Move(Map m){worker.jobs.StopAll();worker.DeSpawn();GenSpawn.Spawn(worker,Cell(m),m);Manager.Current.cooldowns.Clear();}
  WorkGiver Giver(string type)=>DefDatabase<WorkGiverDef>.AllDefs.First(d=>d.giverClass.Name==type&&(type!="WorkGiver_DoBill"||d.workType.defName=="Cooking")).Worker;
  void Start(Job job){Check(job!=null,"planned physical service trip stage "+stage);worker.jobs.StartJob(job,JobCondition.InterruptForced);}
  Job Supply(WorkGiver giver){Job job=null;for(int n=0;n<30&&job==null;n++)job=ConsumerSupply.Plan(worker,giver,Graph.Reachable(worker));return job;}
  void Counts()
  {
   var bench=(Building_WorkTable)Spawn("FueledStove",hatch.Map);var recipe=DefDatabase<RecipeDef>.GetNamed("CookMealSimple");var b=new Bill_Production(recipe);bench.BillStack.AddBill(b);
   var a=Spawn("MealSimple",hatch.Map,3);var c=Spawn("MealSimple",hatch.PocketMap,4);var carried=Spawn("MealSimple",hatch.PocketMap,2);
   var carrier=Pawn(hatch.PocketMap);carrier.carryTracker.TryStartCarry(carried);carrier.drafter.Drafted=true;
   Check(recipe.WorkerCounter.CountProducts(b)==9,"bill stack counts include both maps and carried stock exactly once");
   b.hpRange=new FloatRange(0.8f,1);c.HitPoints=1;
   Check(recipe.WorkerCounter.CountProducts(b)==5,"filtered bill hit points applied on both maps");
   output=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,hatch.PocketMap.zoneManager);hatch.PocketMap.zoneManager.RegisterZone(output);output.AddCell(c.Position);output.GetStoreSettings().filter.SetAllow(ThingDefOf.MealSimple,true);
   b.SetIncludeGroup(output.GetSlotGroup());b.hpRange=new FloatRange(0,1);b.ValidateSettings();Check(b.GetIncludeSlotGroup()==output.GetSlotGroup()&&recipe.WorkerCounter.CountProducts(b)==4,"remote count stockpile survives native validation");
   foreach(var t in new[]{a,c,carried})if(!t.Destroyed)t.Destroy();carrier.Destroy();bench.Destroy();output.Delete();output=null;
   var list=Enumerable.Range(0,260).ToList();var seen=new HashSet<int>();for(int n=0;n<3;n++)foreach(var x in FairScan.Window(list,128,"probe"))seen.Add(x);
   Check(seen.Count==260,"fair scan reaches candidates beyond first budget");
   int supported=DefDatabase<WorkGiverDef>.AllDefs.Count(d=>WorkProbe.Supported(d.Worker));Check(supported>=65,"expanded audited work-giver coverage ("+supported+")");
  }
  void Repair()
  {
   stage=1;Move(hatch.PocketMap);Work(WorkTypeDefOf.Construction);worker.skills.GetSkill(SkillDefOf.Construction).Level=20;
   consumer=Spawn("Heater",hatch.PocketMap);consumer.TryGetComp<CompBreakdownable>().DoBreakdown();stock=Spawn("ComponentIndustrial",hatch.Map,3);
   var g=Giver("WorkGiver_FixBrokenDownBuilding");Check(ConsumerSupply.Needed(worker,g,consumer,stock)==1,"broken building requests one remote component");
   stock.SetForbidden(true,false);Check(ConsumerSupply.Plan(worker,g,Graph.Reachable(worker))==null,"forbidden stock excluded");stock.SetForbidden(false,false);
   Check(Graph.Reachable(worker).Any(r=>r.map==stock.Map),"component source has a usable route");
   var sourceRoute=Graph.Reachable(worker).First(r=>r.map==stock.Map);using(new RemotePawnScope(worker,sourceRoute.map,sourceRoute.landing))Check(ConsumerSupply.Free(worker,stock),"component source is reservable and reachable");
   Start(Supply(g));reloaded=false;
  }
  void Refuel()
  {
   stage=2;consumer.Destroy();if(stock?.Destroyed==false)stock.Destroy();Move(hatch.PocketMap);Work(WorkTypeDefOf.Hauling);
   consumer=Spawn("FueledStove",hatch.Map);stock=Spawn("WoodLog",hatch.PocketMap,75);
   Start(Supply(Giver("WorkGiver_Refuel")));
  }
  void Butcher()
  {
   stage=3;consumer.Destroy();if(stock?.Destroyed==false)stock.Destroy();if(worker.WorkTypeIsDisabled(DefDatabase<WorkTypeDef>.GetNamed("Cooking"))){worker.Destroy();worker=Pawn(hatch.PocketMap);}Move(hatch.PocketMap);Work(DefDatabase<WorkTypeDef>.GetNamed("Cooking"),WorkTypeDefOf.Hauling);
   consumer=Spawn("TableButcher",hatch.PocketMap,stuff:ThingDefOf.WoodLog);worker.skills.GetSkill(SkillDefOf.Cooking).Level=20;
   var animal=PawnGenerator.GeneratePawn(PawnKindDef.Named("Hare"));GenSpawn.Spawn(animal,Cell(hatch.Map),hatch.Map);animal.Kill(null);corpse=animal.Corpse;corpse.SetForbidden(false,false);
   var recipe=DefDatabase<RecipeDef>.AllDefs.First(r=>r.defName=="ButcherCorpseFlesh");bill=new Bill_Production(recipe);((IBillGiver)consumer).BillStack.AddBill(bill);bill.repeatCount=1;
   output=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,hatch.Map.zoneManager);hatch.Map.zoneManager.RegisterZone(output);
   foreach(var c in GenRadial.RadialCellsAround(Cell(hatch.Map)+new IntVec3(8,0,2),2,true))if(c.InBounds(hatch.Map)&&c.Standable(hatch.Map)&&c.GetEdifice(hatch.Map)==null)output.AddCell(c);
   output.GetStoreSettings().filter.SetAllow(animal.def.race.meatDef,true);output.GetStoreSettings().filter.SetAllow(animal.def.race.leatherDef,true);
   bill.SetStoreMode(BillStoreModeDefOf.SpecificStockpile,output.GetSlotGroup());
   Start(Supply(Giver("WorkGiver_DoBill")));
  }
  void Baby()
  {
   stage=4;consumer.Destroy();Move(hatch.Map);Work(WorkTypeDefOf.Childcare);baby=Pawn(hatch.PocketMap,0.5f);baby.needs.food.CurLevelPercentage=0.05f;
   foreach(var t in ServicesInventory.Items(baby.Map).Where(t=>t.def.IsNutritionGivingIngestible&&baby.WillEat(t,worker)).ToList())t.Destroy();
   stock=Spawn("Milk",hatch.Map,30);var g=Giver("WorkGiver_BottleFeedBaby");
   using(new RemotePawnScope(worker,baby.Map,Cell(baby.Map)))
   {
    var can=ChildcareUtility.CanHaulBabyNow(worker,baby,false,out var reason);var suckle=ChildcareUtility.CanSuckleNow(baby,out var suckleReason);
    Log.Message("[SAB SERVICES] baby diagnostic age="+baby.ageTracker.AgeBiologicalYearsFloat+" stage="+baby.DevelopmentalStage+" free="+ConsumerSupply.Free(worker,baby)+" canHaul="+can+" reason="+reason+" suckle="+suckle+" reason="+suckleReason+" willEat="+baby.WillEat(stock,worker)+" hunger="+baby.needs.food.CurLevelPercentage+" wanted="+baby.needs.food.NutritionWanted+" stockNutrition="+stock.GetStatValue(StatDefOf.Nutrition));
    Check(ConsumerSupply.Needed(worker,g,baby,stock)>0,"baby requests permitted remote milk");
   }
   Start(Supply(g));
  }
  public override void GameComponentUpdate()
  {if(!Active||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;}
  public override void GameComponentTick()
  {
   if(!Active||stage==99||LongEventHandler.AnyEventNowOrWaiting)return;
   try
   {
    if(stage==0)
    {
     hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);
     Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.work=true;BunkerMod.Settings.haul=true;BunkerMod.Settings.scanInterval=60;BunkerMod.Settings.minStay=0;Manager.Current.Toggle(hatch);
     foreach(var m in new[]{hatch.Map,hatch.PocketMap})
     {
      foreach(var p in m.mapPawns.AllPawnsSpawned.ToList())p.Destroy();
      foreach(var c in GenRadial.RadialCellsAround(Cell(m),24,true).Where(c=>c.InBounds(m))){m.roofGrid.SetRoof(c,null);foreach(var t in c.GetThingList(m).ToList())if(!(t is MapPortal))t.Destroy();m.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);m.areaManager.Home[c]=true;}m.fogGrid.ClearAllFog();
     }
     worker=Pawn(hatch.Map);if(GenCommandLine.CommandLineArgPassed("sab-services-care-tests")){consumer=Spawn("Heater",hatch.Map);Baby();return;}Counts();Repair();return;
    }
    if(worker.needs.food!=null)worker.needs.food.CurLevel=1;if(worker.needs.rest!=null)worker.needs.rest.CurLevel=1;
    if(Manager.Now>=nextLog){nextLog=Manager.Now+1500;Log.Message("[SAB SERVICES] progress stage="+stage+" ticks="+(Manager.Now-started)+" job="+worker.CurJob+" intent="+Broker.For(worker)?.purpose+" cargo="+worker.carryTracker.CarriedThing+" babyFood="+baby?.needs?.food?.CurLevel);}
    if(stage==1&&worker.carryTracker.CarriedThing?.def==ThingDefOf.ComponentIndustrial&&!saved)
    {saved=true;GameDataSaveLoader.SaveGame("ServicesTransit");Log.Message("[SAB SERVICES] RELOAD REQUIRED");Application.Quit();stage=99;return;}
    if(stage==1&&saved&&reloaded){Check(worker.carryTracker.CarriedThing?.def==ThingDefOf.ComponentIndustrial&&Broker.For(worker)?.deliveryTarget==consumer,"component cargo and consumer survive fresh-process reload");reloaded=false;}
    if(stage==1&&!consumer.IsBrokenDown()){Check(worker.Map==consumer.Map&&worker.workSettings.GetPriority(WorkTypeDefOf.Hauling)==0,"worker fetched component and completed native breakdown repair with hauling disabled");Refuel();return;}
    if(stage==2&&consumer.TryGetComp<CompRefuelable>().Fuel>0){Check(worker.Map==consumer.Map,"fuel physically delivered and stove refueled in opposite direction");Butcher();return;}
    if(stage==3&&corpse.Destroyed&&ServiceDeliveries.Current.requests.Count==0&&output.GetSlotGroup().HeldThings.Any())
    {Check(output.GetSlotGroup().HeldThings.Any(t=>t.def.IsMeat),"remote carcass butchered and meat physically delivered to explicit output stockpile");Baby();return;}
    if(stage==4&&baby.needs.food.CurLevelPercentage>0.7f&&worker.CurJobDef!=JobDefOf.BottleFeedBaby&&baby.Spawned)
    {Check(worker.Map==baby.MapHeld,"remote milk fetched and native bottlefeeding completed");Log.Message("[SAB SERVICES] ACCEPTANCE COMPLETE");stage=99;Application.Quit();return;}
    if(Manager.Now-started>25000)throw new Exception("Timeout stage="+stage+" job="+worker.CurJob+" map="+worker.Map+" intent="+Broker.For(worker)?.purpose+" consumer="+consumer+" requests="+ServiceDeliveries.Current.requests.Count);
   }
   catch(Exception e){Log.Error("[SAB SERVICES] FAIL "+e);stage=99;Application.Quit();}
  }
 }
}

