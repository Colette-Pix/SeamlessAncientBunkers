using System;
using System.Linq;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using SeamlessAncientBunkers;
using UnityEngine;

namespace SABRuntimeTests
{
 public partial class RuntimeTests
 {
  private Building_WorkTable stove;
  private IntVec3 buildCell;
  private List<Pawn> crowd=new List<Pawn>();
  private List<int> lastMap=new List<int>(),crossings=new List<int>();
  private int stabilityStart;
  private void ExposeExtended()
  {
   Scribe_References.Look(ref stove,"stove");Scribe_Values.Look(ref buildCell,"buildCell");Scribe_Values.Look(ref stabilityStart,"stabilityStart");
   Scribe_Collections.Look(ref crowd,"crowd",LookMode.Reference);Scribe_Collections.Look(ref lastMap,"lastMap",LookMode.Value);Scribe_Collections.Look(ref crossings,"crossings",LookMode.Value);
  }
  private void Travel(Map destination)
  {
   var route=Graph.Reachable(pawn,false).First(r=>r.map==destination);
   var job=Broker.Begin(pawn,route,Purpose.Manual,forced:true);if(job==null)throw new Exception("Test manual route rejected");pawn.jobs.TryTakeOrderedJob(job);
  }
  private void ExtendedTick()
  {
   if(Manager.Now>deadline)throw new Exception("Extended timeout stage="+stage+" job="+pawn.CurJob+" map="+pawn.Map+" intent="+Broker.For(pawn)?.purpose);
   if(Manager.Now%3000==0)Report("Progress extended stage="+stage+" tick="+Manager.Now+" job="+pawn.CurJob);
   switch(stage)
   {
    case 109:
     ResetPawn();Travel(surface);second.TrySetForbidden(true);Pass("forbidden-entrance cancellation fixture",110);break;
    case 110:
     if(Broker.For(pawn)==null&&pawn.CurJobDef!=Manager.TravelDef)
     {
      if(pawn.Map!=second.PocketMap)throw new Exception("Pawn crossed forbidden exit");
      second.TrySetForbidden(false);Report("PASS forbidding the entrance cancels active travel without transfer");Travel(surface);Pass("return from second bunker",111);
     }break;
    case 111:
     if(pawn.Map==surface)
     {
      ResetPawn();
      var hostile=PawnGenerator.GeneratePawn(PawnKindDef.Named("Mech_Scyther"),Faction.OfMechanoids);GenSpawn.Spawn(hostile,hatch.exit.Position+new IntVec3(5,0,7),bunker);
      if(Portal.CanTravel(pawn,hatch,true,out _)||Graph.Reachable(pawn).Any(r=>r.map==bunker))throw new Exception("Hostile bunker permitted");
      hostile.Destroy(DestroyMode.Vanish);Report("PASS hostile bunker excluded from automatic traffic");
      pawn.drafter.Drafted=true;if(Portal.CanTravel(pawn,hatch,true,out _))throw new Exception("Drafted pawn allowed");pawn.drafter.Drafted=false;
      Report("PASS drafted pawn cannot cross automatically");Travel(second.PocketMap);var destroyable=second.def.destroyable;second.def.destroyable=true;second.Destroy(DestroyMode.Vanish);second.def.destroyable=destroyable;Pass("destroyed-entrance cancellation fixture",112);
     }break;
    case 112:
     if(Broker.For(pawn)==null&&pawn.CurJobDef!=Manager.TravelDef)
     {
      if(pawn.Map!=surface)throw new Exception("Destroyed entrance moved the pawn");Report("PASS destroyed entrance cancels travel on source map");
      ResetPawn(WorkTypeDefOf.Hauling);pawn.workSettings.SetPriority(DefDatabase<WorkTypeDef>.GetNamed("Cooking"),1);
      foreach(var map in new[]{surface,bunker})foreach(var t in map.listerThings.AllThings.Where(t=>t.def.category==ThingCategory.Item).ToList())t.Destroy(DestroyMode.Vanish);
      stove=(Building_WorkTable)Spawn("FueledStove",bunker,hatch.exit.Position+new IntVec3(6,0,4));stove.GetComp<CompRefuelable>().Refuel(20);
      var bill=(Bill_Production)DefDatabase<RecipeDef>.GetNamed("CookMealSimple").MakeNewBill();bill.repeatMode=BillRepeatModeDefOf.RepeatCount;bill.repeatCount=1;stove.BillStack.AddBill(bill);
      var rice=Spawn("RawRice",surface,surface.Center+new IntVec3(6,0,-6));rice.stackCount=10;
      Pass("bill supply fixture ready",113);
     }break;
    case 113:
     if(stove.BillStack.Bills.OfType<Bill_Production>().All(b=>b.repeatCount==0)&&bunker.listerThings.ThingsOfDef(ThingDef.Named("MealSimple")).Any())
     {
      Report("PASS remote ingredient supply followed by completed vanilla cooking bill");ResetPawn(WorkTypeDefOf.Hauling);Travel(surface);Pass("construction supply return",114);
     }break;
    case 114:
     if(pawn.Map==surface)
     {
      ResetPawn(WorkTypeDefOf.Hauling);pawn.workSettings.SetPriority(WorkTypeDefOf.Construction,1);
      buildCell=hatch.exit.Position+new IntVec3(-6,0,5);GenConstruct.PlaceBlueprintForBuild(ThingDefOf.Wall,buildCell,bunker,Rot4.North,Faction.OfPlayer,ThingDefOf.WoodLog);
      var wood=Spawn("WoodLog",surface,surface.Center+new IntVec3(6,0,-6));wood.stackCount=5;Pass("construction supply fixture ready",115);
     }break;
    case 115:
     if(buildCell.GetEdifice(bunker)?.def==ThingDefOf.Wall)
     {Report("PASS remote construction supply followed by completed vanilla wall");ResetPawn();Travel(surface);Pass("return for bed and recreation tests",116);}break;
    case 116:
     if(pawn.Map==surface)
     {
      ResetPawn();bed=(Building_Bed)Spawn("Bed",bunker,hatch.exit.Position+new IntVec3(-5,0,-5),ThingDefOf.WoodLog);pawn.ownership.ClaimBedIfNonMedical(bed);pawn.needs.rest.CurLevel=0.1f;
      for(int h=0;h<24;h++)pawn.timetable.SetAssignment(h,TimeAssignmentDefOf.Sleep);Pass("owned bunker bedroom fixture",117);
     }break;
    case 117:
     if(pawn.Map==bunker&&pawn.CurrentBed()==bed)
     {Report("PASS surface colonist returns to owned bunker bed at bedtime");ResetPawn();Spawn("HorseshoesPin",surface,surface.Center+new IntVec3(-5,0,-5),ThingDefOf.Steel);foreach(var kind in DefDatabase<JoyKindDef>.AllDefsListForReading)if(kind.defName!="Gaming_Dexterity")pawn.needs.joy.tolerances.Notify_JoyGained(10,kind);pawn.needs.joy.CurLevel=0.01f;for(int h=0;h<24;h++)pawn.timetable.SetAssignment(h,TimeAssignmentDefOf.Joy);Pass("remote recreation fixture",118);}break;
    case 118:
     if(pawn.needs.joy.tolerances.BoredOf(DefDatabase<JoyKindDef>.GetNamed("Gaming_Dexterity"))){var kind=DefDatabase<JoyKindDef>.GetNamed("Gaming_Dexterity");HarmonyLib.AccessTools.FieldRefAccess<JoyToleranceSet,DefMap<JoyKindDef,float>>("tolerances")(pawn.needs.joy.tolerances)[kind]=0;HarmonyLib.AccessTools.FieldRefAccess<JoyToleranceSet,DefMap<JoyKindDef,bool>>("bored")(pawn.needs.joy.tolerances)[kind]=false;}
     if(pawn.Map==surface&&pawn.CurJobDef?.joyKind!=null)
     {Report("PASS remote building recreation starts a vanilla joy job");ResetPawn();Pass("safety and large-colony fixture",119);}break;
    case 119:
     ResetPawn();pawn.health.AddHediff(HediffDefOf.Cut,pawn.RaceProps.body.corePart).Severity=6;
     pawn.workSettings.SetPriority(DefDatabase<WorkTypeDef>.GetNamed("PatientBedRest"),1);
     for(int h=0;h<24;h++)pawn.timetable.SetAssignment(h,TimeAssignmentDefOf.Anything);
     Pass("remote medical-bed fixture",122);break;
    case 122:
     if(pawn.workSettings.GetPriority(DefDatabase<WorkTypeDef>.GetNamed("PatientBedRest"))==0)pawn.workSettings.SetPriority(DefDatabase<WorkTypeDef>.GetNamed("PatientBedRest"),1);
     if(!(hatch.exit.Position+new IntVec3(-7,0,3)).GetThingList(bunker).OfType<Building_Bed>().Any())
     {var availableMedicalBed=(Building_Bed)Spawn("Bed",bunker,hatch.exit.Position+new IntVec3(-7,0,3),ThingDefOf.WoodLog);availableMedicalBed.Medical=true;pawn.jobs.StopAll();Manager.Current.cooldowns.Clear();}
     if(pawn.Map==bunker&&pawn.CurrentBed()?.Medical==true)
     {
      Report("PASS injured colonist independently travels to remote medical bed");
      foreach(var injury in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(h=>!h.IsPermanent()).ToList())pawn.health.RemoveHediff(injury);
      ResetPawn(DefDatabase<WorkTypeDef>.GetNamed("Firefighter"));
      var fireCell=surface.Center+new IntVec3(5,0,-5);surface.areaManager.Home[fireCell]=true;var fuel=Spawn("WoodLog",surface,fireCell);fuel.stackCount=75;var fire=(Fire)Spawn("Fire",surface,fireCell);fire.fireSize=1.2f;testTarget=fire;Pass("remote emergency firefighting fixture",123);
     }break;
    case 123:
     if(testTarget?.Spawned==true&&!testTarget.Position.GetThingList(surface).Any(t=>t.def==ThingDefOf.WoodLog)){var fuel=Spawn("WoodLog",surface,testTarget.Position);fuel.stackCount=75;}
     if(pawn.Map==surface&&pawn.CurJobDef==JobDefOf.BeatFire)
     {
      Report("PASS emergency firefighter crosses and starts vanilla firefighting");foreach(var f in surface.listerThings.ThingsOfDef(ThingDefOf.Fire).ToList())f.Destroy(DestroyMode.Vanish);
      ResetPawn(WorkTypeDefOf.Research);var bench=bunker.listerThings.ThingsOfDef(ThingDef.Named("SimpleResearchBench")).First();
      var options=Orders.WorkOrders(pawn,bunker,bench.Position);if(options.Count==0)throw new Exception("No remote prioritize option");options[0].action();Pass("remote right-click work command issued",124);
     }break;
    case 124:
     if(pawn.Map==bunker&&pawn.CurJobDef==JobDefOf.Research&&pawn.CurJob.playerForced)
     {
      Report("PASS Prioritize via entrance action starts forced vanilla research");
      SetupCrowd();Pass("20-colonist stability simulation started",120);stabilityStart=Manager.Now;
     }break;
    case 120:
     for(int n=0;n<crowd.Count;n++)
     {
      if(crowd[n].Map.uniqueID!=lastMap[n]){crossings[n]++;lastMap[n]=crowd[n].Map.uniqueID;}
      if(crossings[n]>2)throw new Exception("Repeated oscillation by "+crowd[n]+" crossings="+crossings[n]);
     }
     if(Manager.Now-stabilityStart>=6000)
     {
      Report("PASS 20 colonists over 6000 ticks; total transfers="+crossings.Sum()+", maximum per pawn="+crossings.Max());
      Report("PROFILE "+WorkProfile.Report());
      Manager.Current.Cleanup();BunkerMod.Settings.enabled=false;
      if(Manager.Current.intents.Count!=0||PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.Any(p=>p.CurJobDef==Manager.TravelDef||p.CurJobDef?.defName=="SAB_Collect"))throw new Exception("Cleanup left custom jobs");
      Report("PASS cleanup clears all custom jobs and routes");stabilityStart=Manager.Now;Pass("disabled vanilla behavior observation",121);
     }break;
    case 121:
     if(Manager.Now-stabilityStart>=600)
     {
      if(Manager.Current.intents.Count!=0||crowd.Any(p=>p.CurJobDef==Manager.TravelDef))throw new Exception("Disabled feature scheduled travel");
      if(!crowd.Any(p=>p.CurJobDef==JobDefOf.Research))throw new Exception("Disabled feature suppressed ordinary research");
      Report("PASS disabled feature leaves normal research functioning");
      if(GenCommandLine.CommandLineArgPassed("sab-all-tests")){Pass("extended integration fixtures",300);return;} if(GenCommandLine.CommandLineArgPassed("sab-modlist-tests")){Pass("user mod-list specific fixtures",200);return;}
      Report("ACCEPTANCE RUN COMPLETE");done=true;Application.Quit();
     }break;
   }
  }
  private void SetupCrowd()
  {
   Manager.Current.Cleanup();Manager.Current.Toggle(hatch);BunkerMod.Settings.enabled=true;BunkerMod.Settings.scanInterval=600;BunkerMod.Settings.minStay=1800;
   foreach(var p in Find.Maps.SelectMany(m=>m.mapPawns.FreeColonistsSpawned).ToList()){p.drafter.Drafted=true;p.jobs.StopAll();}
   Spawn("SimpleResearchBench",surface,surface.Center+new IntVec3(-6,0,5),ThingDefOf.WoodLog);Find.ResearchManager.SetCurrentProject(ResearchProjectDef.Named("MicroelectronicsBasics"));
   crowd=new List<Pawn>();lastMap=new List<int>();crossings=new List<int>();
   for(int n=0;n<20;n++)
   {
    Pawn p;do{p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false));}while(p.WorkTypeIsDisabled(WorkTypeDefOf.Research));
    var map=n<10?surface:bunker;var center=map==surface?surface.Center:hatch.exit.Position;GenSpawn.Spawn(p,center+new IntVec3(3+n%4,0,-3-n%3),map);
    foreach(var w in DefDatabase<WorkTypeDef>.AllDefsListForReading)if(!p.WorkTypeIsDisabled(w))p.workSettings.SetPriority(w,w==WorkTypeDefOf.Research?1:0);
    p.needs.food.CurLevel=1;p.needs.rest.CurLevel=1;p.needs.joy.CurLevel=1;for(int h=0;h<24;h++)p.timetable.SetAssignment(h,TimeAssignmentDefOf.Work);
    crowd.Add(p);lastMap.Add(map.uniqueID);crossings.Add(0);
   }
   var timer=System.Diagnostics.Stopwatch.StartNew();int routes=0;foreach(var p in crowd)for(int n=0;n<10;n++)routes+=Graph.Reachable(p).Count;timer.Stop();
   Report("PROFILE 200 graph queries: "+timer.ElapsedMilliseconds+" ms; routes="+routes);
   WorkProfile.Reset();
  }
 }
}



