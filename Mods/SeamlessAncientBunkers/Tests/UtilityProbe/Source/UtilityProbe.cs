#if !POWER_ONLY
using System;
using System.Linq;
using DubsBadHygiene;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;

namespace SABUtilityProbe
{
 public class UtilityProbe:GameComponent
 {
  int stage;AncientHatch hatch;Thing battery,lamp,tower,toilet,septic,cutWire,cutPipe;
  public UtilityProbe(Game game){}
  public override void ExposeData()
  {
   Scribe_Values.Look(ref stage,"utilityStage");Scribe_References.Look(ref hatch,"utilityHatch");
   Scribe_References.Look(ref battery,"utilityBattery");Scribe_References.Look(ref lamp,"utilityLamp");
   Scribe_References.Look(ref tower,"utilityTower");Scribe_References.Look(ref toilet,"utilityToilet");
   Scribe_References.Look(ref septic,"utilitySeptic");Scribe_References.Look(ref cutWire,"utilityWire");Scribe_References.Look(ref cutPipe,"utilityPipe");
  }
  void Check(bool ok,string text){if(!ok)throw new Exception(text);Log.Message("[SAB UTILITY TEST] PASS "+text);}
  Thing Spawn(string name,Map map,IntVec3 pos)
  {
   var def=ThingDef.Named(name);var t=ThingMaker.MakeThing(def,def.MadeFromStuff?ThingDefOf.Steel:null);
   if(def.CanHaveFaction)t.SetFaction(Faction.OfPlayer);return GenSpawn.Spawn(t,pos,map);
  }
  void Refresh()
  {
   foreach(var m in new[]{hatch.Map,hatch.PocketMap}){m.powerNetManager.UpdatePowerNetsAndConnections_First();m.GetComponent<HygienePipeMapComp>().RegenPipeGrids();}
   UtilityLinks.Invalidate();
  }
  void ConnectedChecks()
  {
   Refresh();var bn=battery.TryGetComp<CompPowerBattery>();var load=lamp.TryGetComp<CompPowerTrader>();
   Check(load.PowerNet!=bn.PowerNet,"maps retain separate local power topology");
   Check(UtilityLinks.Group(load.PowerNet)?.Contains(bn.PowerNet)==true,"conduits at both hatch ends link the two grids");
   bn.AddEnergy(100);load.PowerOutput=-1000;load.PowerOn=true;
   float before=bn.StoredEnergy;load.PowerNet.PowerNetTick();bn.PowerNet.PowerNetTick();
   Check(Math.Abs(bn.StoredEnergy-(before+load.EnergyOutputPerTick))<0.001f,"bunker load draws exactly once from surface battery");
   Check(!load.PowerNet.batteryComps.Contains(bn),"temporary power component union is restored");
   var storage=tower.TryGetComp<CompWaterStorage>();storage.WaterStorage=50;storage.WaterQuality=ContaminationLevel.Contaminated;
   var net=toilet.TryGetComp<CompPipe>().pipeNet;
   Check(net!=tower.TryGetComp<CompPipe>().pipeNet,"maps retain separate local plumbing topology");
   Check(net.PullWater(7,out var quality),"bunker toilet draws water from surface storage");
   Check(Math.Abs(storage.WaterStorage-43)<0.001f,"remote water draw conserves volume");
   Check(quality==ContaminationLevel.Contaminated,"remote water preserves contamination");
   var sewer=septic.TryGetComp<CompSewageHandler>();float waste=sewer.sewageBuffer;
   Check(net.PushSewage(3),"bunker sewage reaches surface septic tank");
   Check(Math.Abs(sewer.sewageBuffer-waste-3)<0.001f,"remote sewage conserves volume");
   Check(!net.WaterTowers.Contains(storage),"temporary plumbing component union is restored");
   Check(((Building_AssignableFixture)toilet).Working().Accepted,"native toilet eligibility sees remote water and sewage");
  }
  public override void GameComponentUpdate()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-utility-tests")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||stage==9)return;
   Application.runInBackground=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
   try
   {
    if(stage==0)
    {
     hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);
     Manager.Current.Cleanup();BunkerMod.Settings.enabled=false;
     foreach(var portal in new MapPortal[]{hatch,hatch.exit})
     {
      foreach(var t in portal.Map.listerThings.AllThings.OfType<ThingWithComps>().Where(t=>!(t is MapPortal)&&(t.TryGetComp<CompPower>()!=null||t.TryGetComp<CompPipe>()!=null)).ToList())t.Destroy();
      foreach(var c in CellRect.CenteredOn(portal.Position,12).ClipInsideMap(portal.Map))
      {
       foreach(var t in c.GetThingList(portal.Map).Where(t=>t is Building&&!(t is MapPortal)).ToList())t.Destroy();
       portal.Map.terrainGrid.SetTerrain(c,TerrainDefOf.Concrete);
      }
      for(int x=2;x<=6;x++)
      {
       var w=Spawn("PowerConduit",portal.Map,portal.Position+IntVec3.East*x+IntVec3.North);
       var p=Spawn("sewagePipeStuff",portal.Map,portal.Position+IntVec3.East*x);
       if(portal==hatch.exit&&x==2){cutWire=w;cutPipe=p;}
      }
     }
     battery=Spawn("Battery",hatch.Map,hatch.Position+IntVec3.East*7+IntVec3.North);
     lamp=Spawn("StandingLamp",hatch.PocketMap,hatch.exit.Position+IntVec3.East*7+IntVec3.North);
     tower=Spawn("WaterButt",hatch.Map,hatch.Position+IntVec3.East*7);
     toilet=Spawn("ToiletStuff",hatch.PocketMap,hatch.exit.Position+IntVec3.East*7);
     for(int z=-1;z>=-3;z--)Spawn("sewagePipeStuff",hatch.Map,hatch.Position+IntVec3.East*5+IntVec3.North*z);
     septic=Spawn("SewageSepticTank",hatch.Map,hatch.Position+IntVec3.East*5+IntVec3.South*4);
     ConnectedChecks();stage=1;
     LongEventHandler.QueueLongEvent(()=>{GameDataSaveLoader.SaveGame("SAB-Utilities");GameDataSaveLoader.LoadGame("SAB-Utilities");},null,false,null);
    }
    else if(stage==1)
    {
     ConnectedChecks();Check(true,"utility connections work after save/reload with traffic disabled");
     cutWire.Destroy();cutPipe.Destroy();Refresh();
     var load=lamp.TryGetComp<CompPowerTrader>();var net=toilet.TryGetComp<CompPipe>().pipeNet;
     Check(UtilityLinks.Group(load.PowerNet)==null,"removing hatch-end conduit disconnects power");
     Check(!net.PullWater(1,out _),"removing hatch-end pipe disconnects water");
     Check(!net.PushSewage(1),"removing hatch-end pipe disconnects sewage");
     Check(!((Building_AssignableFixture)toilet).Working().Accepted,"native toilet becomes unavailable after pipe cut");
     // Reconnect using hidden lines; opposite direction draws from bunker resources.
     Spawn("HiddenConduit",hatch.PocketMap,hatch.exit.Position+IntVec3.East*2+IntVec3.North);
     Spawn("sewagePipeHidden",hatch.PocketMap,hatch.exit.Position+IntVec3.East*2);
     var underground=Spawn("WaterButt",hatch.PocketMap,hatch.exit.Position+IntVec3.East*5+IntVec3.South);
     tower.TryGetComp<CompWaterStorage>().WaterStorage=0;underground.TryGetComp<CompWaterStorage>().WaterStorage=20;
     Refresh();Check(UtilityLinks.Group(load.PowerNet)!=null,"hidden conduit reconnects power");
     Check(tower.TryGetComp<CompPipe>().pipeNet.PullWater(4,out _)&&Math.Abs(underground.TryGetComp<CompWaterStorage>().WaterStorage-16)<0.001f,"hidden pipes carry water from bunker to surface");
     stage=9;Log.Message("[SAB UTILITY TEST] ACCEPTANCE COMPLETE");Application.Quit();
    }
   }catch(Exception e){stage=9;Log.Error("[SAB UTILITY TEST] FAIL "+e);Application.Quit();}
  }
 }
}
#endif
