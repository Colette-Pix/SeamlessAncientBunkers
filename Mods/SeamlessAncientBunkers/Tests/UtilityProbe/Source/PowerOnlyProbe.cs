#if POWER_ONLY
using System;
using System.Linq;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
namespace SABUtilityProbe
{
 public class PowerOnlyProbe:GameComponent
 {
  bool done;
  public PowerOnlyProbe(Game game){}
  void Check(bool ok,string text){if(!ok)throw new Exception(text);Log.Message("[SAB POWER ONLY] PASS "+text);}
  public override void GameComponentUpdate()
  {
   if(done||!GenCommandLine.CommandLineArgPassed("sab-utility-tests")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;
   done=true;
   try
   {
    Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
    Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="BadHygiene"),"Dubs absent and mod loads successfully");
    var h=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(x=>Portal.Other(x)!=null);
    foreach(var p in new MapPortal[]{h,h.exit})
    {
     foreach(var c in CellRect.CenteredOn(p.Position,10).ClipInsideMap(p.Map))
      foreach(var t in c.GetThingList(p.Map).Where(t=>t is Building&&!(t is MapPortal)).ToList())t.Destroy();
     for(int x=2;x<=6;x++)GenSpawn.Spawn(ThingDef.Named("PowerConduit"),p.Position+IntVec3.East*x,p.Map);
    }
    var source=GenSpawn.Spawn(ThingDef.Named("Battery"),h.exit.Position+IntVec3.East*7,h.PocketMap).TryGetComp<CompPowerBattery>();
    var load=GenSpawn.Spawn(ThingDef.Named("StandingLamp"),h.Position+IntVec3.East*7,h.Map).TryGetComp<CompPowerTrader>();
    foreach(var m in new[]{h.Map,h.PocketMap})m.powerNetManager.UpdatePowerNetsAndConnections_First();
    UtilityLinks.Invalidate();source.AddEnergy(100);load.PowerOutput=-1000;load.PowerOn=true;
    Check(load.PowerNet.CanPowerNow(load),"surface device can use bunker battery");
    float before=source.StoredEnergy;source.PowerNet.PowerNetTick();load.PowerNet.PowerNetTick();
    Check(Math.Abs(source.StoredEnergy-before-load.EnergyOutputPerTick)<0.001f,"reverse-direction power is charged exactly once");
    Log.Message("[SAB POWER ONLY] ACCEPTANCE COMPLETE");
   }catch(Exception e){Log.Error("[SAB POWER ONLY] FAIL "+e);}
   Application.Quit();
  }
 }
}
#endif
