using System;
using System.IO;
using System.Linq;
using RimWorld;
using SeamlessAncientBunkers;
using Verse;
using Verse.AI;
using UnityEngine;
using HarmonyLib;

namespace SABRuntimeTests
{
 [StaticConstructorOnStartup]
 public static class Diagnostics
 {
  static Diagnostics() { if(GenCommandLine.CommandLineArgPassed("sab-tests")) new Harmony("colet.sabtests").PatchAll(); }
 }
 [HarmonyPatch(typeof(Hauling),nameof(Hauling.Plan))]
 public static class HaulDiagnostic
 {
  static int calls;
  public static void Prefix(Pawn p,ThinkResult local)
  {if(++calls%100==0||p.thingIDNumber==59286)Log.Message("[SAB TEST] Haul calls="+calls+" pawn="+p+" local="+local.Job+" eligible="+Portal.Eligible(p)+" cooldown="+Manager.Current.cooldowns.FirstOrDefault(c=>c.pawn==p)?.until+" now="+Manager.Now+" force="+p.CurJob?.playerForced+" interruptible="+p.jobs.IsCurrentJobPlayerInterruptible()+" enabled="+BunkerMod.Settings.haul);}
 }
 [HarmonyPatch(typeof(WorkProbe),nameof(WorkProbe.Plan))]
 public static class WorkProfile
 {
  public static long total,max;public static int calls;
  public static void Reset(){total=0;max=0;calls=0;}
  public static void Prefix(out long __state){__state=System.Diagnostics.Stopwatch.GetTimestamp();}
  public static void Postfix(long __state){long elapsed=System.Diagnostics.Stopwatch.GetTimestamp()-__state;total+=elapsed;max=Math.Max(max,elapsed);calls++;}
  public static string Report()=>"work-planner calls="+calls+", total="+(1000.0*total/System.Diagnostics.Stopwatch.Frequency).ToString("F2")+" ms, max="+(1000.0*max/System.Diagnostics.Stopwatch.Frequency).ToString("F2")+" ms";
 }
 [HarmonyPatch(typeof(Pawn_JobTracker),nameof(Pawn_JobTracker.EndCurrentJob))]
 public static class EndDiagnostic
 {
  public static void Prefix(Pawn ___pawn, JobCondition condition)
  {
   if(___pawn.CurJobDef==Manager.TravelDef) Log.Message("[SAB TEST] End travel "+condition+" tick="+Manager.Now+" pawn="+___pawn+" pos="+___pawn.Position+" map="+___pawn.Map+" driver="+___pawn.jobs.curDriver+"\n"+Environment.StackTrace);
  }
 }
 [HarmonyPatch(typeof(Thing),nameof(Thing.Destroy))]
 public static class DestroyDiagnostic
 {
  public static void Prefix(Thing __instance) { if(__instance is AncientHatch) Log.Message("[SAB TEST] Destroy hatch "+Environment.StackTrace); }
 }
 [HarmonyPatch(typeof(JobDriver_BunkerTravel),"TravelStillValid")]
 public static class TravelDiagnostic
 {
  public static void Postfix(JobDriver_BunkerTravel __instance, bool __result)
  {
   if(__result) return;
   var p=__instance.pawn; var portal=__instance.MapPortal; var other=Portal.Other(portal);
   Log.Message("[SAB TEST] Travel validation false: tick="+Manager.Now+", pawn="+p+", eligible="+Portal.Eligible(p)+", source="+p.Map+", portal="+portal+", other="+other+", intents="+Manager.Current.intents.Count+", safe="+Portal.Safe(p.Map)+"/"+Portal.Safe(other?.Map)+", landing="+(other!=null&&Portal.Landing(p,other,out _))+", reachable="+(portal?.Spawned==true&&p.CanReach(portal,PathEndMode.Touch,Danger.None)));
  }
 }
 public partial class RuntimeTests : GameComponent
 {
  private bool started, done, saved, carrySaved, claimChecked;
  private Pawn pawn;
  private Map surface, bunker;
  private AncientHatch hatch;
  private int stage, deadline, trips, issued;
  private Thing testTarget;
  private Pawn patient;
  private Building_Bed bed;
  private AncientHatch second;
  private int acceptanceStarted;
  private bool updateReported;
  public RuntimeTests(Game game) { if (Active) Log.Message("[SAB TEST] component created"); }
  public override void FinalizeInit() { if(Active) { Application.runInBackground=true; Report("Finalize init maps="+Find.Maps.Count); } }
  private static bool Active => GenCommandLine.CommandLineArgPassed("sab-tests");
  private void Report(string text) { Log.Message("[SAB TEST] " + text); File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "results.txt"), text + Environment.NewLine); }
  public override void ExposeData()
  {
   Scribe_Values.Look(ref started,"started"); Scribe_Values.Look(ref done,"done"); Scribe_Values.Look(ref saved,"saved"); Scribe_Values.Look(ref carrySaved,"carrySaved");
   Scribe_Values.Look(ref claimChecked,"claimChecked");
   Scribe_Values.Look(ref stage,"stage"); Scribe_Values.Look(ref deadline,"deadline"); Scribe_Values.Look(ref trips,"trips"); Scribe_Values.Look(ref issued,"issued");
   Scribe_References.Look(ref pawn,"pawn"); Scribe_References.Look(ref surface,"surface"); Scribe_References.Look(ref bunker,"bunker"); Scribe_References.Look(ref hatch,"hatch");
   Scribe_References.Look(ref testTarget,"testTarget"); Scribe_References.Look(ref patient,"patient"); Scribe_References.Look(ref bed,"bed"); Scribe_References.Look(ref second,"second"); Scribe_Values.Look(ref acceptanceStarted,"acceptanceStarted");
   ExposeExtended();
   ExposeUserCompatibility(); ExposeExpansion();
  }
  public override void LoadedGame() { if (Active) Report("Loaded isolated checkpoint; stage=" + stage + ", pawn=" + pawn?.ThingID + ", intents=" + Manager.Current.intents.Count); }
  public override void GameComponentUpdate()
  {
   if (!Active || done || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
   try
   {

    if(!updateReported) { updateReported=true; Report("Update active; maps="+Find.Maps.Count+", pawns="+Find.CurrentMap?.mapPawns.AllPawnsSpawned.Count+", windows="+Find.WindowStack.Windows.Count); }
    foreach(var window in Find.WindowStack.Windows.ToList()) if(window.forcePause) Find.WindowStack.TryRemove(window,false);
    Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
    if (!started && Find.CurrentMap?.mapPawns.FreeColonistsSpawned.Any() == true)
    {
     started = true;
     LongEventHandler.QueueLongEvent(Setup,null,false,null);
    }
   }
   catch(Exception e) { Fail(e.ToString()); }
  }
  private void Clear(Map map, IntVec3 center, int radius)
  {
   foreach(var cell in GenRadial.RadialCellsAround(center,radius,true).Where(c=>c.InBounds(map)))
   {
    map.roofGrid.SetRoof(cell,null);
    foreach(var thing in cell.GetThingList(map).ToList()) if (!(thing is Pawn) && !(thing is MapPortal)) thing.Destroy(DestroyMode.Vanish);
    map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);
   }
   map.fogGrid.ClearAllFog();
  }
  private void Setup()
  {
   try
   {
    surface=Find.CurrentMap; pawn=surface.mapPawns.FreeColonistsSpawned.First(p=>!p.Downed);
    pawn.drafter.Drafted=false; pawn.jobs.StopAll();
    Clear(surface,surface.Center,12);
    hatch=(AncientHatch)GenSpawn.Spawn(ThingMaker.MakeThing(ThingDef.Named("AncientHatch")), surface.Center,surface);
    hatch.GetComp<CompHackable>().HackNow();
    bunker=hatch.GetOtherMap(); // Test fixture deliberately opens one real Odyssey stockpile.
    foreach(var map in new[]{surface,bunker})
     foreach(var t in map.listerThings.AllThings.ToList())
      if(t.HostileTo(Faction.OfPlayer) || (t is Pawn p && p.Faction!=Faction.OfPlayer)) t.Destroy(DestroyMode.Vanish);
    Clear(bunker,hatch.exit.Position,12);
    pawn.DeSpawn(); GenSpawn.Spawn(pawn,surface.Center+new IntVec3(8,0,0),surface);
    hatch.TrySetForbidden(false); hatch.exit.TrySetForbidden(false);
    Manager.Current.Toggle(hatch);
    pawn.inventory.innerContainer.ClearAndDestroyContents();
    var steel=ThingMaker.MakeThing(ThingDefOf.Steel); steel.stackCount=7; pawn.inventory.innerContainer.TryAdd(steel);
    Report("Setup: actual Odyssey pocket map, pawn="+pawn.ThingID+", surface="+surface.uniqueID+", bunker="+bunker.uniqueID);
    Order(hatch); stage=1;
   }
   catch(Exception e) { Fail(e.ToString()); }
  }
  private void Order(MapPortal portal)
  {
   pawn.jobs.StopAll();
   var job=Manager.Current.Plan(pawn,portal,Purpose.Manual,null);
   if(job==null) {
    var other=Portal.Other(portal);
    throw new Exception("Manual plan rejected; eligible="+Portal.Eligible(pawn)+", safe source="+Portal.Safe(pawn.Map)+", safe dest="+Portal.Safe(other?.Map)+", enter="+portal.IsEnterable(out var why)+":"+why+", other enter="+other?.IsEnterable(out _)+", landing="+Portal.Landing(pawn,other,out var cell)+":"+cell+", reach="+pawn.CanReach(portal,PathEndMode.Touch,Danger.None)+", fog="+portal.Fogged()+"/"+other?.Fogged()+", forbidden="+portal.IsForbidden(pawn)+"/"+other?.IsForbidden(pawn.Faction));
   }
   pawn.jobs.TryTakeOrderedJob(job); issued=Manager.Now; deadline=issued+5000;
  }
  public override void GameComponentTick()
  {
   if(!Active || !started || done || stage==0) return;
   try
   {
    if(stage>=100) { AcceptanceTick(); return; }
    if(Manager.Now>deadline) throw new Exception("Transit timed out stage="+stage+", job="+pawn.CurJob+", pos="+pawn.Position+", intents="+Manager.Current.intents.Count);
    if(!saved && stage==1 && pawn.CurJobDef==Manager.TravelDef && Manager.Now-issued>=10)
    {
     saved=true;
     LongEventHandler.QueueLongEvent(()=> { GameDataSaveLoader.SaveGame("SAB-Transit"); Report("Saved walking transit checkpoint"); GameDataSaveLoader.LoadGame("SAB-Transit"); },null,false,null);
     return;
    }
    if((stage==1 && pawn.Map==bunker)||(stage==2 && pawn.Map==surface))
    {
     if(pawn.inventory.innerContainer.TotalStackCountOfDef(ThingDefOf.Steel)!=7) throw new Exception("Inventory not preserved");
     if(PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.Count(p=>p.ThingID==pawn.ThingID)!=1) throw new Exception("Pawn duplicated/lost");
     Report("PASS crossing "+(++trips)+" to "+(pawn.Map==surface?"surface":"bunker")+"; inventory=7; identity unique");
     if(trips>=10) { Report("PASS Phase 0: 5 round trips, inventory, walking save/load"); stage=100; deadline=Manager.Now+20000; LongEventHandler.QueueLongEvent(()=>GameDataSaveLoader.SaveGame("SAB-AcceptanceStart"),null,false,null); return; }
     stage=stage==1?2:1; Order(stage==1?(MapPortal)hatch:hatch.exit);
    }
   }
   catch(Exception e) { Fail(e.ToString()); }
  }
  private void Fail(string error) { done=true; Report("FAIL "+error); Application.Quit(); }
  private void Pass(string text,int next) {Report("PASS "+text);stage=next;deadline=Manager.Now+18000;LongEventHandler.QueueLongEvent(()=>GameDataSaveLoader.SaveGame("SAB-AcceptanceLast"),null,false,null);}
  private void ResetPawn(WorkTypeDef work=null)
  {
   Broker.Cancel(Broker.For(pawn)??new Intent{pawn=pawn}); Manager.Current.cooldowns.Clear();
   pawn.jobs.StopAll(); pawn.needs.food.CurLevel=1; pawn.needs.rest.CurLevel=1; if(pawn.needs.joy!=null)pawn.needs.joy.CurLevel=1;
   foreach(var need in pawn.needs.AllNeeds.Where(n=>n.GetType().Namespace=="DubsBadHygiene"))need.CurLevel=need.MaxLevel;
   foreach(var w in DefDatabase<WorkTypeDef>.AllDefsListForReading) if(!pawn.WorkTypeIsDisabled(w))pawn.workSettings.SetPriority(w,w==work?1:0);
   for(int h=0;h<24;h++)pawn.timetable.SetAssignment(h,TimeAssignmentDefOf.Work);
  }
  private Thing Spawn(string def,Map map,IntVec3 cell,ThingDef stuff=null)
  {var t=ThingMaker.MakeThing(ThingDef.Named(def),stuff);if(t is Building || t is Pawn)t.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(t,cell,map);t.TrySetForbidden(false);return t;}
  private void AcceptanceTick()
  {
   if(GenCommandLine.CommandLineArgPassed("sab-pursuit-door-test") && stage<300){stage=313;deadline=Manager.Now+18000;} if(GenCommandLine.CommandLineArgPassed("sab-extension-tests") && stage<300){stage=300;deadline=Manager.Now+18000;} if(stage>=300){ExpansionTick();return;} if(stage>=200){UserCompatibilityTick();return;}
   if(stage>=109){ExtendedTick();return;}
   if(Manager.Now%3000==0) Report("Progress stage="+stage+" tick="+Manager.Now+" job="+pawn.CurJob+" intent="+Broker.For(pawn)?.purpose);
   if(Manager.Now>deadline)throw new Exception("Acceptance timeout stage="+stage+" map="+pawn.Map+" job="+pawn.CurJob+" intents="+Manager.Current.intents.Count);
   switch(stage)
   {
    case 100:
     // Fresh versatile test pawn, not an edited player pawn. Keep the phase-zero subject intact.
     for(int n=0;n<100;n++)
     {
      var candidate=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);
      if(candidate.GetDisabledWorkTypes().Count==0) {pawn=candidate;break;}
     }
     GenSpawn.Spawn(pawn,surface.Center+new IntVec3(8,0,3),surface);
     foreach(var otherPawn in Find.Maps.SelectMany(m=>m.mapPawns.FreeColonistsSpawned).Where(p=>p!=pawn)) {otherPawn.drafter.Drafted=true;otherPawn.jobs.StopAll();}
     foreach(var skill in pawn.skills.skills)skill.Level=10;
     Find.PlaySettings.useWorkPriorities=true;
     ResetPawn(WorkTypeDefOf.Research);pawn.workSettings.SetPriority(WorkTypeDefOf.Cleaning,2);
     BunkerMod.Settings.debug=true;BunkerMod.Settings.scanInterval=120;BunkerMod.Settings.minStay=300;
     var originalMap=pawn.Map;var originalPos=pawn.Position;int registered=surface.mapPawns.AllPawnsSpawned.Count;
     try {using(new RemotePawnScope(pawn,bunker,hatch.exit.Position))using(new ProbeAudit(bunker)){if(pawn.Map!=bunker)throw new Exception("Scope didn't switch view");throw new InvalidOperationException("Intentional probe exception");}}
     catch(InvalidOperationException) { }
     if(pawn.Map!=originalMap||pawn.Position!=originalPos||surface.mapPawns.AllPawnsSpawned.Count!=registered||RemotePawnScope.Active||ProbeAudit.Active)throw new Exception("Probe leaked state after exception");
     Report("PASS remote scope restores map, position and probe state on exception; registration unchanged");
     testTarget=Spawn("SimpleResearchBench",bunker,hatch.exit.Position+new IntVec3(6,0,0),ThingDefOf.WoodLog);
     Find.ResearchManager.SetCurrentProject(ResearchProjectDef.Named("Batteries"));
     for(int n=0;n<4;n++){var c=surface.Center+new IntVec3(6+n,0,5);surface.areaManager.Home[c]=true;surface.terrainGrid.SetTerrain(c,DefDatabase<TerrainDef>.GetNamed("Concrete"));if(!FilthMaker.TryMakeFilth(c,surface,ThingDefOf.Filth_Dirt))throw new Exception("Could not create cleaning fixture");}
     foreach(var filth in surface.listerFilthInHomeArea.FilthInHomeArea)AccessTools.Field(typeof(Filth),"growTick").SetValue(filth,Manager.Now-1000);
     BunkerMod.Settings.enabled=false;var localWork=new JobGiver_Work().TryIssueJobPackage(pawn,default(JobIssueParams));BunkerMod.Settings.enabled=true;
     if(localWork.Job?.def!=JobDefOf.Clean)throw new Exception("Fixture must offer real local cleaning before priority comparison");JobMaker.ReturnToPool(localWork.Job);
     Report("PASS local cleaning exists at priority 2 before remote research selection at priority 1");
     acceptanceStarted=Manager.Now;Pass("acceptance fixture ready",101);break;
    case 101:
     if(pawn.Map==bunker&&pawn.CurJobDef==JobDefOf.Research)
     {
      Pass("higher-priority remote research selected over local cleaning; arrival is vanilla Research",102);
      // Keep research enabled to exercise autonomous return after eating.
      foreach(var t in bunker.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree).ToList())if(t.def.category==ThingCategory.Item)t.Destroy(DestroyMode.Vanish);
      var meal=Spawn("MealSimple",surface,surface.Center+new IntVec3(7,0,6));meal.stackCount=10;
      Manager.Current.cooldowns.Clear();pawn.needs.food.CurLevel=0.05f;pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
     }break;
    case 102:
     if(pawn.Map==surface&&pawn.needs.food.CurLevel>0.4f)Pass("bunker pawn crossed to eat a valid surface meal",103);break;
    case 103:
     if(pawn.Map==bunker&&pawn.CurJobDef==JobDefOf.Research)
     {
      Pass("pawn autonomously resumed underground research after eating",104);ResetPawn();
      bed=(Building_Bed)Spawn("Bed",surface,surface.Center+new IntVec3(7,0,-5),ThingDefOf.WoodLog);pawn.ownership.ClaimBedIfNonMedical(bed);
      pawn.needs.rest.CurLevel=0.1f;for(int h=0;h<24;h++)pawn.timetable.SetAssignment(h,TimeAssignmentDefOf.Sleep);
      Manager.Current.Cool(pawn);
      if(Manager.Current.CanScan(pawn,Purpose.Work))throw new Exception("Post-trip cooldown must still block ordinary work scans");
      var sleep=(Job)AccessTools.Method(typeof(JobGiver_GetRest),"TryGiveJob").Invoke(new JobGiver_GetRest(),new object[]{pawn});
      if(sleep?.def!=Manager.TravelDef||Broker.For(pawn)?.target!=bed)throw new Exception("Floor sleep must route to the owned bed during post-trip cooldown");
      pawn.jobs.StartJob(sleep,JobCondition.InterruptForced);
     }break;
    case 104:
     if(pawn.Map==surface&&pawn.CurrentBed()==bed)
     {
      Pass("owned-bed routing during post-trip cooldown and vanilla sleep",105);ResetPawn(WorkTypeDefOf.Doctor);
      bed=(Building_Bed)Spawn("Bed",bunker,hatch.exit.Position+new IntVec3(-6,0,0),ThingDefOf.WoodLog);bed.Medical=true;
      patient=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);GenSpawn.Spawn(patient,bed.Position,bunker);
      foreach(var w in DefDatabase<WorkTypeDef>.AllDefsListForReading)if(!patient.WorkTypeIsDisabled(w))patient.workSettings.SetPriority(w,0);
      patient.needs.food.CurLevel=1;patient.needs.rest.CurLevel=1;
      var wound=patient.health.AddHediff(HediffDefOf.Cut,patient.RaceProps.body.corePart);wound.Severity=10;
      var resting=JobMaker.MakeJob(JobDefOf.LayDown,bed);resting.restUntilHealed=true;patient.jobs.StartJob(resting);
     }break;
    case 105:
     if(!claimChecked&&Broker.For(pawn)?.target==patient)
     {
      Pawn contender;do{contender=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);}while(contender.WorkTypeIsDisabled(WorkTypeDefOf.Doctor));
      GenSpawn.Spawn(contender,surface.Center+new IntVec3(7,0,7),surface);
      foreach(var w in DefDatabase<WorkTypeDef>.AllDefsListForReading)if(!contender.WorkTypeIsDisabled(w))contender.workSettings.SetPriority(w,w==WorkTypeDefOf.Doctor?1:0);
      contender.needs.food.CurLevel=1;contender.needs.rest.CurLevel=1;
      if(!Broker.Claimed(contender,bunker,patient,IntVec3.Invalid))throw new Exception("Travelling doctor did not claim patient");
      var duplicate=WorkProbe.Plan(contender,ThinkResult.NoJob,false);
      if(Broker.For(contender)?.target==patient)throw new Exception("Two doctors claimed same patient");
      if(duplicate!=null)JobMaker.ReturnToPool(duplicate);if(Broker.For(contender)!=null)Broker.Cancel(Broker.For(contender));contender.drafter.Drafted=true;
      claimChecked=true;Report("PASS second doctor cannot claim the travelling doctor's patient");
     }
     if(patient.health.hediffSet.hediffs.Any(h=>h is Hediff_Injury&&h.IsTended()))
     {
      Pass("doctor crossed and completed a vanilla tend",106);ResetPawn(WorkTypeDefOf.Hauling);
      // Put the hauler on surface using a real portal order before preparing cargo.
      var route=Graph.Reachable(pawn,false).First(r=>r.map==surface);pawn.jobs.TryTakeOrderedJob(Broker.Begin(pawn,route,Purpose.Manual,forced:true));
     }break;
    case 106:
     if(pawn.Map==surface)
     {
      ResetPawn(WorkTypeDefOf.Hauling);
      foreach(var map in new[]{surface,bunker})foreach(var steel in map.listerThings.ThingsOfDef(ThingDefOf.Steel).ToList())steel.Destroy(DestroyMode.Vanish);
      var zone=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,bunker.zoneManager);bunker.zoneManager.RegisterZone(zone);zone.AddCell(hatch.exit.Position+new IntVec3(5,0,5));zone.settings.filter.SetDisallowAll();zone.settings.filter.SetAllow(ThingDefOf.Steel,true);zone.settings.Priority=StoragePriority.Critical;
      testTarget=Spawn("Steel",surface,surface.Center+new IntVec3(6,0,6));testTarget.stackCount=25;
      Pass("stockpile fixture ready",107);
     }break;
    case 107:
     if(Manager.Now%1000==0)
     {
      var routes=Graph.Reachable(pawn);Report("Haul diagnostic: capable="+Hauling.CanHaul(pawn)+" routes="+routes.Count+" source reachable="+(testTarget?.Spawned==true&&pawn.CanReserveAndReach(testTarget,PathEndMode.ClosestTouch,Danger.None))+" items="+pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).Count);
      Hauling.FindStorage(pawn,testTarget,pawn.Map,StoreUtility.CurrentStoragePriorityOf(testTarget),out var localCell,out var localPriority);Report("Haul priority="+StoreUtility.CurrentStoragePriorityOf(testTarget)+" local="+localPriority+" claimed="+Manager.Current.Claimed(testTarget)+" candidate rank="+pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).OrderBy(t=>t.Position.DistanceToSquared(pawn.Position)).ToList().IndexOf(testTarget));
      foreach(var r in routes)using(new RemotePawnScope(pawn,r.map,r.landing))Report("Storage: "+Hauling.FindStorage(pawn,testTarget,r.map,StoragePriority.Unstored,out var c,out var priority)+" cell="+c+" groups="+r.map.haulDestinationManager.AllGroupsListInPriorityOrder.Count);
     }
     if(!carrySaved&&pawn.CurJobDef==Manager.TravelDef&&pawn.jobs.curDriver.CurToilIndex==1&&pawn.carryTracker.CarriedThing!=null)
     {
      carrySaved=true;LongEventHandler.QueueLongEvent(()=>{GameDataSaveLoader.SaveGame("SAB-CarryingWait");Report("Saved carrying portal-wait checkpoint; cargo="+pawn.carryTracker.CarriedThing.stackCount);GameDataSaveLoader.LoadGame("SAB-CarryingWait");},null,false,null);return;
     }
     if(bunker.listerThings.ThingsOfDef(ThingDefOf.Steel).Any(t=>t.GetSlotGroup()!=null&&t.stackCount>=25))
     {
      if(!carrySaved||surface.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t=>t.stackCount)!=0||bunker.listerThings.ThingsOfDef(ThingDefOf.Steel).Sum(t=>t.stackCount)!=25||pawn.carryTracker.CarriedThing!=null)throw new Exception("Haul/save conservation failed");
      Pass("cross-map stockpile hauling delivered 25 steel",108);ResetPawn();
      var beforeMap=pawn.Map;var beforePos=pawn.Position;
      var other=Portal.Other(hatch.exit);var cells=GenRadial.RadialCellsAround(other.Position,5,true).Where(c=>c.InBounds(other.Map)&&c.Standable(other.Map)).ToList();
      // Test allowed-area denial without damaging the fixture.
      surface.areaManager.TryMakeNewAllowed(out var area);var areas=AccessTools.FieldRefAccess<Pawn_PlayerSettings,System.Collections.Generic.Dictionary<Map,Area>>("allowedAreas")(pawn.playerSettings);areas[surface]=area;
      if(Portal.CanTravel(pawn,hatch.exit,true,out _))throw new Exception("Empty destination area was accepted");areas[surface]=null;
      if(pawn.Map!=beforeMap||pawn.Position!=beforePos)throw new Exception("Denied travel changed pawn state");
      Report("PASS destination allowed-area denial preserves pawn state");
      LongEventHandler.QueueLongEvent(()=>{
       second=(AncientHatch)Spawn("AncientHatch",surface,surface.Center+new IntVec3(-8,0,0));second.GetComp<CompHackable>().HackNow();
       if(Portal.Other(second)!=null)throw new Exception("Unopened hatch unexpectedly has link");Manager.Current.Toggle(second);
       if(Graph.Reachable(pawn).Any(r=>r.portals.Contains(second)))throw new Exception("Unopened hatch in graph");Report("PASS unopened hatch excluded without generation");
       var map=second.GetOtherMap();foreach(var t in map.listerThings.AllThings.ToList())if(t.HostileTo(Faction.OfPlayer))t.Destroy(DestroyMode.Vanish);Clear(map,second.exit.Position,12);
       var route=Graph.Reachable(pawn).First(r=>r.map==map);if(route.portals.Count!=2)throw new Exception("Expected two-hop route");
       pawn.jobs.TryTakeOrderedJob(Broker.Begin(pawn,route,Purpose.Manual,forced:true));
      },null,false,null);
     }break;
    case 108:
     if(second?.PocketMap!=null&&pawn.Map==second.PocketMap)
     {Pass("two-hop bunker-to-bunker manual route",109);}break;
   }
  }
 }
}






