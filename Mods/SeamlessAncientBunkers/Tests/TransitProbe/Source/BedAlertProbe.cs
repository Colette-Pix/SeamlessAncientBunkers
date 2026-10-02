using System;
using System.Linq;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
namespace SABTransitTests
{
 public class BedAlertProbe:GameComponent
 {
  bool done; AncientHatch hatch;
  public BedAlertProbe(Game game){}
  void Check(bool value,string label){if(!value)throw new Exception(label);Log.Message("[SAB BED ALERT] PASS "+label);}
  IntVec3 Cell(Map map)=>CellFinder.StandableCellNear(map==hatch.Map?hatch.Position:hatch.exit.Position,map,8);
  Pawn Colonist(Map map){var p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false));GenSpawn.Spawn(p,Cell(map),map);return p;}
  Building_Bed Bed(Map map,string def="Bed")
  {
   var bed=(Building_Bed)ThingMaker.MakeThing(ThingDef.Named(def),ThingDefOf.WoodLog);bed.SetFaction(Faction.OfPlayer);
   var c=GenRadial.RadialCellsAround(Cell(map),18,true).First(x=>GenAdj.OccupiedRect(x,Rot4.North,bed.def.size).All(z=>z.InBounds(map)&&z.Standable(map)&&z.GetEdifice(map)==null&&!z.Fogged(map)));
   GenSpawn.Spawn(bed,c,map);return bed;
  }
  void Count(int expectedSingle,int expectedDouble,string label)
  {
   foreach(var map in new[]{hatch.Map,hatch.PocketMap}){Alert_NeedColonistBeds.AvailableColonistBeds(map,false,out int single,out int dbl,out _);Check(single==expectedSingle&&dbl==expectedDouble,label+" map="+map.uniqueID+" singles="+single+" doubles="+dbl);}
  }
  public override void GameComponentTick()
  {
   if(done||!GenCommandLine.CommandLineArgPassed("sab-bed-alert-tests")||Current.ProgramState!=ProgramState.Playing)return;done=true;
   try
   {
    hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;
    foreach(var map in Find.Maps){foreach(var p in map.mapPawns.AllPawnsSpawned.ToList())p.Destroy();foreach(var b in map.listerThings.AllThings.OfType<Building_Bed>().ToList())b.Destroy();}
    Manager.Current.EnableClearedBunkers();Check(LinkedResources.Maps(hatch.Map).Contains(hatch.PocketMap),"floors form one connected colony");
    var pawn=Colonist(hatch.Map);var bed=Bed(hatch.PocketMap);Count(0,0,"remote bed meets surface colonist demand");
    Check(!new Alert_NeedColonistBeds().GetReport().active,"actual colonist-bed warning is clear with remote bed");
    bed.Medical=true;Count(-1,0,"medical bed does not satisfy ordinary sleeping demand");bed.Medical=false;
    var second=Colonist(hatch.PocketMap);Count(-1,0,"genuine colony-wide bed shortage remains visible");
    bed.Destroy();bed=Bed(hatch.Map,"DoubleBed");pawn.relations.AddDirectRelation(PawnRelationDefOf.Spouse,second);Count(0,0,"partners on separate floors share double-bed demand");
    pawn.relations.RemoveDirectRelation(PawnRelationDefOf.Spouse,second);Count(-1,0,"unrelated colonists retain native double-bed rules");
    pawn.Destroy();Count(0,0,"surface bed meets underground colonist demand");
    BunkerMod.Settings.enabled=false;Alert_NeedColonistBeds.AvailableColonistBeds(hatch.PocketMap,false,out int local,out _,out _);Check(local==-1,"disabled connectivity restores native local shortage");BunkerMod.Settings.enabled=true;
    var exit=hatch.exit;exit.DeSpawn();Alert_NeedColonistBeds.AvailableColonistBeds(hatch.PocketMap,false,out local,out _,out _);Check(local==-1,"disconnected floor cannot borrow another floor's beds");
    Log.Message("[SAB BED ALERT] ACCEPTANCE COMPLETE");
   }catch(Exception e){Log.Error("[SAB BED ALERT] FAIL "+e);}Application.Quit();
  }
 }
}

