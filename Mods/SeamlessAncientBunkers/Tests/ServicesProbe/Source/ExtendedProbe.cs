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
 public class ExtendedProbe:GameComponent
 {
  int stage,start,nextLog,receipt;AncientHatch hatch;Pawn worker,child,trader,blocker;Thing target,stock;readonly List<Thing> blockers=new List<Thing>();
  public ExtendedProbe(Game game){}
  bool Active=>GenCommandLine.CommandLineArgPassed("sab-services-extended-tests")||GenCommandLine.CommandLineArgPassed("sab-services-trade-tests");
  public override void ExposeData()
  {
   Scribe_Values.Look(ref stage,"extendedStage");Scribe_Values.Look(ref start,"extendedStart");
   Scribe_References.Look(ref hatch,"extendedHatch");Scribe_References.Look(ref worker,"extendedWorker");Scribe_References.Look(ref child,"extendedChild");Scribe_References.Look(ref trader,"extendedTrader");Scribe_References.Look(ref target,"extendedTarget");Scribe_References.Look(ref stock,"extendedStock");
  }
  void Check(bool ok,string label){if(!ok)throw new Exception(label);Log.Message("[SAB EXTENDED] PASS "+label);}
  IntVec3 Cell(Map m)=>(m==hatch.Map?hatch.Position:hatch.exit.Position)+new IntVec3(4,0,4);
  Thing Spawn(string def,Map m,ThingDef stuff=null,int count=1,IntVec3? at=null)
  {
   var t=ThingMaker.MakeThing(ThingDef.Named(def),stuff);t.stackCount=count;if(t.def.CanHaveFaction)t.SetFaction(Faction.OfPlayer);
   var c=at??GenRadial.RadialCellsAround(Cell(m),22,true).First(x=>x.InBounds(m)&&x.Standable(m)&&x.GetEdifice(m)==null&&x.GetFirstItem(m)==null);
   GenSpawn.Spawn(t,c,m);t.SetForbidden(false,false);return t;
  }
  Pawn Pawn(Map m,float age=30)
  {
   var p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,forceNoBackstory:true,canGeneratePawnRelations:false,allowDowned:true,fixedBiologicalAge:age,fixedChronologicalAge:age,developmentalStages:age<3?DevelopmentalStage.Baby:age<13?DevelopmentalStage.Child:DevelopmentalStage.Adult));
   foreach(var trait in p.story.traits.allTraits.ToList())p.story.traits.RemoveTrait(trait);GenSpawn.Spawn(p,Cell(m),m);p.workSettings.EnableAndInitialize();return p;
  }
  WorkGiver Giver(string name)=>DefDatabase<WorkGiverDef>.AllDefs.First(d=>d.giverClass.Name==name).Worker;
  void Reset(Map map,params WorkTypeDef[] types)
  {worker.jobs.StopAll();var i=Broker.For(worker);if(i!=null)Broker.Cancel(i);worker.DeSpawn();GenSpawn.Spawn(worker,Cell(map),map);foreach(var d in DefDatabase<WorkTypeDef>.AllDefs)worker.workSettings.SetPriority(d,types.Contains(d)?1:0);Manager.Current.cooldowns.Clear();((IDictionary)AccessTools.Field(typeof(Manager),"nextScan").GetValue(Manager.Current)).Clear();start=Manager.Now;}
  Job Supply(WorkGiver giver){Job j=null;for(int n=0;n<30&&j==null;n++)j=ConsumerSupply.Plan(worker,giver,Graph.Reachable(worker));return j;}
  void Begin(Job job){Check(job!=null,"physical job planned stage "+stage);worker.jobs.StartJob(job,JobCondition.InterruptForced);}
  void FilterCounts()
  {
   var def=ThingDef.Named("Apparel_Pants");var recipe=DefDatabase<RecipeDef>.AllDefs.First(r=>r.products?.Count==1&&r.products[0].thingDef==def);
   var table=(Building_WorkTable)Spawn("HandTailoringBench",hatch.Map,ThingDefOf.WoodLog);var bill=new Bill_Production(recipe);table.BillStack.AddBill(bill);bill.includeEquipped=true;bill.includeTainted=false;bill.qualityRange=new QualityRange(QualityCategory.Good,QualityCategory.Legendary);
   var good=(Apparel)Spawn("Apparel_Pants",hatch.PocketMap,ThingDefOf.Cloth);good.TryGetComp<CompQuality>().SetQuality(QualityCategory.Good,ArtGenerationContext.Colony);
   var poor=(Apparel)Spawn("Apparel_Pants",hatch.PocketMap,ThingDefOf.Cloth);poor.TryGetComp<CompQuality>().SetQuality(QualityCategory.Poor,ArtGenerationContext.Colony);
   var equipped=Pawn(hatch.PocketMap);equipped.drafter.Drafted=true;equipped.apparel.Wear(good,false);
   Check(recipe.WorkerCounter.CountProducts(bill)==1,"remote equipped apparel counts with native quality filter");
   good.WornByCorpse=true;Check(recipe.WorkerCounter.CountProducts(bill)==0,"remote tainted apparel excluded");
   bill.includeTainted=true;Check(recipe.WorkerCounter.CountProducts(bill)==1,"remote tainted inclusion honored without double counting equipped apparel");
   equipped.Destroy();poor.Destroy();table.Destroy();
  }
  void Flick()
  {
   stage=1;var giver=Giver("WorkGiver_Flick");Reset(hatch.Map,giver.def.workType);
   var far=GenRadial.RadialCellsAround(Cell(hatch.Map)+new IntVec3(52,0,0),8,true).First(c=>c.InBounds(hatch.Map)&&c.Standable(hatch.Map)&&c.GetEdifice(hatch.Map)==null);
   var local=Spawn("StandingLamp",hatch.Map,at:far);AccessTools.Field(typeof(CompFlickable),"wantSwitchOn").SetValue(local.TryGetComp<CompFlickable>(),false);FlickUtility.UpdateFlickDesignation(local);
   target=Spawn("StandingLamp",hatch.PocketMap);AccessTools.Field(typeof(CompFlickable),"wantSwitchOn").SetValue(target.TryGetComp<CompFlickable>(),false);FlickUtility.UpdateFlickDesignation(target);
   var native=WorkProbe.At(worker,giver.def,local,IntVec3.Invalid,false,false);Check(native!=null,"local flick fixture is a real native job");
   var planned=WorkProbe.Plan(worker,new ThinkResult(native,new JobGiver_Work()),false);Check(planned!=null&&Broker.For(worker).target==target,"closer remote work beats continuous equivalent local work");
   JobMaker.ReturnToPool(native);Broker.Cancel(Broker.For(worker));JobMaker.ReturnToPool(planned);local.Destroy();target.Destroy();
   BunkerMod.Settings.maxCandidates=32;
   blocker=Pawn(hatch.PocketMap);blocker.drafter.Drafted=true;var claim=JobMaker.MakeJob(JobDefOf.Wait);claim.expiryInterval=100000;blocker.jobs.StartJob(claim,JobCondition.InterruptForced);
   for(int n=0;n<140;n++){var b=Spawn("StandingLamp",hatch.PocketMap);AccessTools.Field(typeof(CompFlickable),"wantSwitchOn").SetValue(b.TryGetComp<CompFlickable>(),false);FlickUtility.UpdateFlickDesignation(b);hatch.PocketMap.reservationManager.Reserve(blocker,claim,b);blockers.Add(b);}
   target=Spawn("StandingLamp",hatch.PocketMap);AccessTools.Field(typeof(CompFlickable),"wantSwitchOn").SetValue(target.TryGetComp<CompFlickable>(),false);FlickUtility.UpdateFlickDesignation(target);
   planned=null;int attempts=0;while(planned==null&&attempts++<20){((IDictionary)AccessTools.Field(typeof(Manager),"nextScan").GetValue(Manager.Current)).Clear();Manager.Current.cooldowns.Clear();planned=WorkProbe.Plan(worker,ThinkResult.NoJob,false);}
   Log.Message("[SAB EXTENDED] fairness attempts="+attempts+" planned="+planned+" selected="+Broker.For(worker)?.target+" expected="+target);
   Check(planned!=null&&Broker.For(worker).target==target&&attempts>1,"native scanner eventually reaches valid target behind 140 rejected targets");Begin(planned);BunkerMod.Settings.maxCandidates=128;
  }
  void GeneBank()
  {
   stage=2;foreach(var b in blockers)b.Destroy();blocker.Destroy();target.Destroy();Reset(hatch.PocketMap,WorkTypeDefOf.Hauling);
   target=Spawn("GeneBank",hatch.PocketMap);stock=Spawn("Genepack",hatch.Map);((Genepack)stock).Initialize(new List<GeneDef>{GeneDefOf.Hemogenic});
   Begin(Supply(Giver("WorkGiver_HaulToGeneBank")));
  }
  void StageHeldGene()
  {
   stage=3;Reset(hatch.Map,WorkTypeDefOf.Hauling);
   var other=Spawn("GeneBank",hatch.Map);Check(ConsumerSupply.Needed(worker,Giver("WorkGiver_HaulToGeneBank"),other,stock)==0,"stored genepack is not shuttled between banks by automatic supply");other.Destroy();
   receipt=ServiceDeliveries.Current.Add(stock,1,destination:hatch.Map,cell:Cell(hatch.Map));Begin(ServiceDeliveries.Plan(worker));
  }
  void Vat()
  {
   stage=4;target.Destroy();Reset(hatch.Map,WorkTypeDefOf.Hauling);target=Spawn("GrowthVat",hatch.Map);child=Pawn(hatch.Map,5);
   AccessTools.Field(typeof(Building_GrowthVat),"selectedPawn").SetValue(target,child);stock=Spawn("RawPotatoes",hatch.PocketMap,count:75);Begin(Supply(Giver("WorkGiver_HaulToGrowthVat")));
  }
  void Lesson()
  {
   stage=5;target.Destroy();child.Destroy();Reset(hatch.Map,WorkTypeDefOf.Childcare);worker.skills.GetSkill(SkillDefOf.Intellectual).Level=20;
   target=Spawn("SchoolDesk",hatch.PocketMap,ThingDefOf.WoodLog);child=Pawn(hatch.PocketMap,7);child.needs.learning.CurLevel=0.01f;
   var desire=DefDatabase<LearningDesireDef>.AllDefs.First(d=>d.workerClass==typeof(LearningGiver_Lessontaking));child.learning.ActiveLearningDesires.Clear();child.learning.ActiveLearningDesires.Add(desire);
   var lesson=JobMaker.MakeJob(JobDefOf.Lessontaking,target);lesson.isLearningDesire=true;child.jobs.StartJob(lesson,JobCondition.InterruptForced);
   Check(SchoolUtility.FindTeacher(child)==worker,"student recognizes reachable remote teacher");
   using(new RemotePawnScope(worker,child.Map,Cell(child.Map)))using(new ProbeAudit(child.Map))
   {var j=WorkProbe.At(worker,Giver("WorkGiver_Teach").def,child,IntVec3.Invalid,false,true);Check(j!=null&&child.CurJob.targetB.Pawn==null,"teaching probe leaves pupil live job unchanged");JobMaker.ReturnToPool(j);}
  }
  void Trade()
  {
   stage=6;child?.Destroy();target?.Destroy();Reset(hatch.Map,WorkTypeDefOf.Hauling);
   trader=PawnGenerator.GeneratePawn(PawnKindDef.Named("Town_Trader"),Find.FactionManager.AllFactionsListForReading.First(f=>!f.IsPlayer&&!f.HostileTo(Faction.OfPlayer)&&f.def.humanlikeFaction));GenSpawn.Spawn(trader,Cell(hatch.Map)+new IntVec3(2,0,0),hatch.Map);
   trader.trader=new Pawn_TraderTracker(trader);trader.trader.traderKind=DefDatabase<TraderKindDef>.GetNamed("Caravan_Outlander_BulkGoods");trader.mindState.wantsToTradeWithColony=true;
   stock=Spawn("Silver",hatch.PocketMap,count:200);target=Spawn("ComponentIndustrial",hatch.PocketMap,count:8);
   var goods=trader.trader.ColonyThingsWillingToBuy(worker).ToList();Check(goods.Contains(stock)&&goods.Contains(target),"ground trader previews remote silver and merchandise");
   var currency=ThingMaker.MakeThing(ThingDefOf.Silver);currency.stackCount=2000;trader.inventory.innerContainer.TryAdd(currency);
   TradeSession.SetupWith(trader,worker,false);var row=TradeSession.deal.AllTradeables.First(t=>t.ThingDef==ThingDefOf.ComponentIndustrial);row.ForceToDestination(5);
   TradeSession.deal.UpdateCurrencyCount();
   Check(TradeSession.deal.TryExecute(out var traded)&&traded&&target.Map==hatch.PocketMap&&target.stackCount==3,"ground trade completes immediately with exact remote quantity");TradeSession.Close();
   Check(ServiceDeliveries.Current.requests.Count==0&&Broker.For(worker)==null,"direct trade needs no delivery or second confirmation");
   Check(!TradeUtility.AllLaunchableThingsForTrade(hatch.Map,trader).Contains(stock),"orbital trade excludes remote goods without beacon coverage");
   var beacon=(Building_OrbitalTradeBeacon)Spawn("OrbitalTradeBeacon",hatch.PocketMap,at:stock.Position+new IntVec3(2,0,0));beacon.TryGetComp<CompPowerTrader>().PowerOn=true;
   Check(TradeUtility.AllLaunchableThingsForTrade(hatch.Map,trader).Count(t=>t==stock)==1,"powered remote beacon exposes each covered stack once");
   beacon.TryGetComp<CompPowerTrader>().PowerOn=false;Check(!TradeUtility.AllLaunchableThingsForTrade(hatch.Map,trader).Contains(stock),"unpowered remote beacon removes remote orbital stock");
   Hopper();
  }
  void Hopper()
  {
   stage=7;Reset(hatch.Map,Giver("WorkGiver_CookFillHopper").def.workType);target=Spawn("Hopper",hatch.Map);stock=Spawn("RawRice",hatch.PocketMap,count:40);
   var storage=(ISlotGroupParent)target;storage.GetStoreSettings().filter.SetDisallowAll();storage.GetStoreSettings().filter.SetAllow(stock.def,true);storage.GetStoreSettings().Priority=StoragePriority.Important;
   Begin(Supply(Giver("WorkGiver_CookFillHopper")));
  }
  void Hemogen()
  {
   stage=8;Reset(hatch.Map);worker.genes.AddGene(GeneDefOf.Hemogenic,false);worker.genes.GetFirstGeneOfType<Gene_Hemogen>().Value=0;
   stock=Spawn("HemogenPack",hatch.PocketMap,count:5);Begin(ExtraNeeds.Query(new JobGiver_GetHemogen(),worker));
  }
  void Deathrest()
  {
   stage=9;Reset(hatch.Map);worker.genes.AddGene(DefDatabase<GeneDef>.GetNamed("Deathrest"),false);worker.needs.AddOrRemoveNeedsAsAppropriate();
   worker.needs.TryGetNeed<Need_Deathrest>().CurLevel=0;
   target=Spawn("DeathrestCasket",hatch.PocketMap);foreach(var c in target.OccupiedRect())target.Map.roofGrid.SetRoof(c,RoofDefOf.RoofConstructed);
   worker.ownership.ClaimBedIfNonMedical((Building_Bed)target);Begin(ExtraNeeds.Query(new JobGiver_GetDeathrest(),worker));
  }
  public override void GameComponentUpdate(){if(!Active||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;}
  public override void GameComponentTick()
  {
   if(!Active||stage==99||LongEventHandler.AnyEventNowOrWaiting)return;
   try
   {
    if(stage==0)
    {
     hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);Manager.Current.Cleanup();Manager.Current.Toggle(hatch);
     BunkerMod.Settings.enabled=true;BunkerMod.Settings.work=true;BunkerMod.Settings.haul=true;BunkerMod.Settings.scanInterval=60;BunkerMod.Settings.minStay=0;
     foreach(var m in new[]{hatch.Map,hatch.PocketMap})
     {foreach(var p in m.mapPawns.AllPawnsSpawned.ToList())p.Destroy();foreach(var c in GenRadial.RadialCellsAround(Cell(m),27,true).Where(c=>c.InBounds(m))){m.roofGrid.SetRoof(c,null);foreach(var t in c.GetThingList(m).ToList())if(!(t is MapPortal))t.Destroy();m.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);m.areaManager.Home[c]=true;}m.fogGrid.ClearAllFog();}
     worker=Pawn(hatch.Map);if(GenCommandLine.CommandLineArgPassed("sab-services-trade-tests")){Trade();return;}FilterCounts();Flick();return;
    }
    foreach(var p in new[]{worker,child,trader}.Where(p=>p!=null&&!p.Destroyed)){if(p.needs.food!=null)p.needs.food.CurLevelPercentage=1;if(p.needs.rest!=null)p.needs.rest.CurLevelPercentage=1;if(p.needs.joy!=null)p.needs.joy.CurLevelPercentage=1;}
    if(Manager.Now>=nextLog){nextLog=Manager.Now+2000;Log.Message("[SAB EXTENDED] progress "+stage+" ticks="+(Manager.Now-start)+" job="+worker.CurJob+" intent="+Broker.For(worker)?.purpose+" target="+target+" child="+child?.CurJob);}
    if(stage==1&&!target.TryGetComp<CompFlickable>().SwitchIsOn){Check(worker.Map==target.Map,"native remote flick action completed");GeneBank();return;}
    if(stage==2&&target.TryGetComp<CompGenepackContainer>().SearchableContents.Contains(stock)){Check(worker.Map==target.Map,"genepack physically delivered and inserted into remote gene bank");StageHeldGene();return;}
    if(stage==3&&ServiceDeliveries.Current.IsComplete(receipt)&&ServiceDeliveries.Current.Receipts(receipt).Any(r=>r.item?.Spawned==true))
    {Check(ServiceDeliveries.Current.Receipts(receipt).Single().count==1&&stock.Map==hatch.Map,"held gene-bank stock physically extracted and delivered with exact receipt");ServiceDeliveries.Current.Release(receipt);Vat();return;}
    if(stage==4&&((Building_GrowthVat)target).NutritionStored>0){Check(true,"remote food physically loaded into growth vat");Lesson();return;}
    if(stage==5&&child.needs.learning.CurLevel>0.2f&&worker.CurJobDef==JobDefOf.Lessongiving){Check(worker.Map==child.Map,"teacher traveled and native lesson increased learning");Trade();return;}
    if(stage==7&&target.Position.GetThingList(target.Map).Any(t=>t.def==ThingDef.Named("RawRice")&&t.stackCount==40))
    {
     Check(stock.Destroyed||stock.MapHeld==hatch.Map,"remote raw food physically loaded onto hopper cell");
     Hemogen();return;
    }
    if(stage==8&&worker.genes.GetFirstGeneOfType<Gene_Hemogen>().Value>0.3f)
    {Check(worker.Map==hatch.PocketMap,"urgent hemogen need crosses and physically consumes remote packs");Deathrest();return;}
    if(stage==9&&worker.Deathresting&&worker.CurrentBed()==target)
    {
     Check(worker.Map==hatch.PocketMap,"urgent deathrest uses assigned remote casket instead of local floor");
     var wait=JobMaker.MakeJob(JobDefOf.Wait);wait.expiryInterval=10000;worker.jobs.StartJob(wait,JobCondition.InterruptForced);
     var pending=ServiceDeliveries.Current.Add(stock,1,destination:hatch.PocketMap,cell:Cell(hatch.PocketMap));Manager.Current.Cleanup();
     Check(!ServiceDeliveries.Current.IsPending(pending)&&!ServiceDeliveries.Current.IsComplete(receipt)&&worker.CurJob==wait,"removal preparation clears delivery state and preserves unrelated native job");
     Log.Message("[SAB EXTENDED] ACCEPTANCE COMPLETE");stage=99;Application.Quit();return;
    }
    if(Manager.Now-start>25000)throw new Exception("Timeout stage="+stage+" job="+worker.CurJob+" intent="+Broker.For(worker)?.purpose+" requests="+ServiceDeliveries.Current.requests.Count);
   }
   catch(Exception e){Log.Error("[SAB EXTENDED] FAIL "+e);stage=99;Application.Quit();}
  }
 }
}
