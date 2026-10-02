using System;
using System.Linq;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace SABServicesProbe
{
 public class ImmediateTradeProbe:GameComponent
 {
  bool done;AncientHatch hatch;Pawn worker,trader,carrier;Dialog_Trade dialog;
  bool Active=>GenCommandLine.CommandLineArgPassed("sab-services-immediate-trade-tests");
  public ImmediateTradeProbe(Game game){}
  void Check(bool ok,string label){if(!ok)throw new Exception(label);Log.Message("[SAB IMMEDIATE TRADE] PASS "+label);}
  IntVec3 Cell(Map m)=>(m==hatch.Map?hatch.Position:hatch.exit.Position)+new IntVec3(4,0,4);
  Thing Spawn(ThingDef def,Map map,int count=1)
  {
   var thing=ThingMaker.MakeThing(def);thing.stackCount=count;if(def.CanHaveFaction)thing.SetFaction(Faction.OfPlayer);
   var c=GenRadial.RadialCellsAround(Cell(map),18,true).First(x=>x.InBounds(map)&&x.Standable(map)&&x.GetEdifice(map)==null&&x.GetFirstItem(map)==null);
   GenSpawn.Spawn(thing,c,map);thing.SetForbidden(false,false);return thing;
  }
  Thing Inventory(ThingDef def,int count)
  {var thing=ThingMaker.MakeThing(def);thing.stackCount=count;Check(carrier.inventory.innerContainer.TryAdd(thing),"trader inventory fixture accepts "+def.defName);return thing;}
  int Stock(ThingDef def)=>trader.trader.Goods.Where(t=>t.def==def).Sum(t=>t.stackCount);
  int Local(ThingDef def)=>hatch.Map.listerThings.ThingsOfDef(def).Sum(t=>t.stackCount);
  Tradeable Row(ThingDef def)=>TradeSession.deal.AllTradeables.First(t=>t.ThingDef==def);
  void Open(string kind="Caravan_Outlander_BulkGoods")
  {trader.trader.traderKind=DefDatabase<TraderKindDef>.GetNamed(kind);dialog=new Dialog_Trade(worker,trader);Find.WindowStack.Add(dialog);}
  void Close(){Find.WindowStack.TryRemove(dialog,false);TradeSession.Close();dialog=null;}
  void NoStaging()=>Check(ServiceDeliveries.Current.requests.Count==0&&Broker.For(worker)==null,"transaction adds no delivery request or travel intent");
  public override void GameComponentUpdate()
  {
   if(!Active||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;
   Application.runInBackground=true;foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
  }
  public override void GameComponentTick()
  {
   if(!Active||done||LongEventHandler.AnyEventNowOrWaiting)return;done=true;
   try
   {
    hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);
    Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.work=true;BunkerMod.Settings.haul=false;Manager.Current.Toggle(hatch);
    foreach(var map in new[]{hatch.Map,hatch.PocketMap})
    {
     foreach(var p in map.mapPawns.AllPawnsSpawned.ToList())p.Destroy();
     foreach(var c in GenRadial.RadialCellsAround(Cell(map),27,true).Where(c=>c.InBounds(map)))
     {map.roofGrid.SetRoof(c,null);foreach(var t in c.GetThingList(map).ToList())if(!(t is MapPortal))t.Destroy();map.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);map.areaManager.Home[c]=true;}
     map.fogGrid.ClearAllFog();
    }
    worker=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,forceNoBackstory:true));GenSpawn.Spawn(worker,Cell(hatch.Map),hatch.Map);worker.workSettings.EnableAndInitialize();
    foreach(var type in DefDatabase<WorkTypeDef>.AllDefs)worker.workSettings.SetPriority(type,0);
    var faction=Find.FactionManager.AllFactionsListForReading.First(f=>!f.IsPlayer&&!f.HostileTo(Faction.OfPlayer)&&f.def.humanlikeFaction);
    trader=PawnGenerator.GeneratePawn(PawnKindDef.Named("Town_Trader"),faction);GenSpawn.Spawn(trader,Cell(hatch.Map)+new IntVec3(2,0,0),hatch.Map);trader.trader=new Pawn_TraderTracker(trader);trader.mindState.wantsToTradeWithColony=true;
    carrier=PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"),faction);GenSpawn.Spawn(carrier,Cell(hatch.Map)+new IntVec3(3,0,0),hatch.Map);Inventory(ThingDefOf.Silver,2000);
    var lord=LordMaker.MakeNewLord(faction,new LordJob_TradeWithColony(faction,trader.Position),hatch.Map,new[]{trader,carrier});

    var merchandise=Spawn(ThingDefOf.ComponentIndustrial,hatch.PocketMap,8);Open();var row=Row(merchandise.def);Check(row.thingsColony.Contains(merchandise),"native trade dialog includes remote merchandise");row.ForceToDestination(5);TradeSession.deal.UpdateCurrencyCount();int proceeds=TradeSession.deal.CurrencyTradeable.CountToTransferToSource;
    Check(TradeSession.deal.TryExecute(out var traded)&&traded,"remote merchandise sells in one native transaction");
    Check(merchandise.Spawned&&merchandise.Map==hatch.PocketMap&&merchandise.stackCount==3&&Stock(merchandise.def)==5,"exact sold quantity enters trader inventory and unsold remainder stays remote");
    Check(Local(ThingDefOf.Silver)==proceeds&&Stock(ThingDefOf.Silver)==2000-proceeds,"native sale payment is exact and arrives beside the local trader");NoStaging();Close();

    foreach(var silver in hatch.Map.listerThings.ThingsOfDef(ThingDefOf.Silver).ToList())silver.Destroy();
    var funds=Spawn(ThingDefOf.Silver,hatch.PocketMap,500);var gold=Inventory(ThingDefOf.Gold,100);var medicine=Inventory(ThingDefOf.MedicineIndustrial,10);
    var animal=PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"),Faction.OfPlayer);GenSpawn.Spawn(animal,Cell(hatch.PocketMap)+new IntVec3(3,0,0),hatch.PocketMap);
    Open();Row(ThingDefOf.Gold).ForceToSource(100);Row(merchandise.def).ForceToDestination(1);TradeSession.deal.AllTradeables.First(r=>r.thingsColony.Contains(animal)).ForceToDestination(1);TradeSession.deal.UpdateCurrencyCount();
    Check(TradeSession.deal.CurrencyTradeable.CountPostDealFor(Transactor.Colony)<0,"failure fixture exceeds all remote funds and sale proceeds");int traderSilver=Stock(ThingDefOf.Silver);
    Check(!TradeSession.deal.TryExecute(out traded)&&!traded,"native insufficient-funds validation rejects remote trade");
    Check(funds.stackCount==500&&funds.Map==hatch.PocketMap&&gold.stackCount==100&&merchandise.stackCount==3&&Stock(merchandise.def)==5&&Stock(ThingDefOf.Silver)==traderSilver&&Local(ThingDefOf.Gold)==0,"failed transaction leaves goods and payment untouched");
    Check(animal.Map==hatch.PocketMap&&animal.Faction==Faction.OfPlayer&&animal.GetLord()==null,"failed transaction leaves remote animal identity ownership and map untouched");NoStaging();Close();

    Open();Check(Local(ThingDefOf.Silver)==0&&Row(ThingDefOf.Silver).thingsColony.Contains(funds),"purchase is funded solely by remote silver");Row(ThingDefOf.MedicineIndustrial).ForceToSource(2);TradeSession.deal.UpdateCurrencyCount();int cost=TradeSession.deal.CurrencyTradeable.CountToTransferToDestination;
    Check(cost>0&&cost<=funds.stackCount&&TradeSession.deal.TryExecute(out traded)&&traded,"remote silver purchase completes with the ordinary confirmation");
    Check(funds.stackCount==500-cost&&funds.Map==hatch.PocketMap&&Stock(ThingDefOf.Silver)==traderSilver+cost&&Stock(ThingDefOf.MedicineIndustrial)==8&&Local(ThingDefOf.MedicineIndustrial)==2,"purchase subtracts exact remote silver and places goods on trader map");NoStaging();Close();

    var bank=Spawn(ThingDefOf.GeneBank,hatch.PocketMap);var pack=(Genepack)ThingMaker.MakeThing(ThingDefOf.Genepack);pack.Initialize(new System.Collections.Generic.List<GeneDef>{GeneDefOf.Hemogenic});bank.TryGetComp<CompGenepackContainer>().innerContainer.TryAdd(pack);
    Open("Caravan_Outlander_Exotic");var packRow=TradeSession.deal.AllTradeables.First(r=>r.thingsColony.Contains(pack));Check(packRow.TraderWillTrade,"native dialog includes tradeable remote bank-held genepack");packRow.ForceToDestination(1);TradeSession.deal.UpdateCurrencyCount();
    Check(TradeSession.deal.TryExecute(out traded)&&traded&&!bank.TryGetComp<CompGenepackContainer>().ContainedGenepacks.Contains(pack)&&trader.trader.Goods.Contains(pack),"native sale transfers the same genepack directly from bank to trader inventory");NoStaging();Close();

    var animalCargo=ThingMaker.MakeThing(ThingDefOf.WoodLog);animalCargo.stackCount=3;animal.inventory.innerContainer.TryAdd(animalCargo);Open();var animalRow=TradeSession.deal.AllTradeables.First(r=>r.thingsColony.Contains(animal));Check(animalRow.TraderWillTrade,"native dialog includes remote sale animal");animalRow.ForceToDestination(1);TradeSession.deal.UpdateCurrencyCount();
    Check(TradeSession.deal.TryExecute(out traded)&&traded&&animal.Map==trader.Map&&animal.Faction==trader.Faction&&animal.GetLord()==lord&&lord.ownedPawns.Count(p=>p==animal)==1,"remote animal sale preserves identity and joins native trader faction and group on trader map");
    Check(animalCargo.Spawned&&animalCargo.Map==hatch.PocketMap&&animalCargo.stackCount==3&&Find.Maps.Sum(m=>m.mapPawns.AllPawnsSpawned.Count(p=>p==animal))==1,"native pre-sale possessions drop on source floor and pawn is not duplicated");NoStaging();Close();

    int legacy=ServiceDeliveries.Current.Add(merchandise,1,trader:trader);int unrelated=ServiceDeliveries.Current.Add(merchandise,1,destination:hatch.Map,cell:Cell(hatch.Map));ServiceDeliveries.Current.LoadedGame();
    Check(!ServiceDeliveries.Current.IsPending(legacy)&&ServiceDeliveries.Current.IsPending(unrelated)&&merchandise.stackCount==3,"loading retires obsolete trade staging without moving stock or cancelling other deliveries");ServiceDeliveries.Current.Release(unrelated);
    Log.Message("[SAB IMMEDIATE TRADE] ACCEPTANCE COMPLETE");
   }
   catch(Exception e){Log.Error("[SAB IMMEDIATE TRADE] FAIL "+e);}
   finally{if(dialog!=null)Close();Application.Quit();}
  }
 }
}
