using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 // A curated vanilla scanner set, not arbitrary mod work givers. Never enumerate outside the scope.
 public static class WorkProbe
 {
  private static readonly HashSet<string> Audited=new HashSet<string>{
   "WorkGiver_FightFires","WorkGiver_Tend","WorkGiver_TendOtherUrgent","WorkGiver_TendOther_Humanlike","WorkGiver_TendOther_Animal","WorkGiver_FeedPatient","WorkGiver_ConstructFinishFrames",
   "WorkGiver_ConstructDeliverResourcesToBlueprints","WorkGiver_ConstructDeliverResourcesToFrames",
   "WorkGiver_Repair","WorkGiver_Miner","WorkGiver_GrowerSow","WorkGiver_GrowerHarvest",
   "WorkGiver_PlantsCut","WorkGiver_PlantsCut_Designated","WorkGiver_PlantsHarvest","WorkGiver_PlantsHarvest_Designated",
   "WorkGiver_DoBill","WorkGiver_Researcher","WorkGiver_CleanFilth","WorkGiver_HaulGeneral",
   "WorkGiver_HunterHunt","WorkGiver_Train","WorkGiver_Tame","WorkGiver_Milk","WorkGiver_Shear","WorkGiver_Slaughter",
   "WorkGiver_Refuel","WorkGiver_Refuel_Turret","WorkGiver_Deconstruct","WorkGiver_RepairMech","WorkGiver_HaulMechToCharger",
   "WorkGiver_Warden_Chat","WorkGiver_Warden_Convert","WorkGiver_Warden_DeliverFood","WorkGiver_Warden_DeliverHemogen",
   "WorkGiver_Warden_Feed","WorkGiver_Warden_Enslave","WorkGiver_Warden_EmancipateSlave","WorkGiver_Warden_ImprisonSlave",
   "WorkGiver_Warden_SuppressSlave","WorkGiver_Warden_SuppressActivity","WorkGiver_Warden_InterrogateIdentity","WorkGiver_Warden_TakeToBed",
   "WorkGiver_Warden_ReleasePrisoner","WorkGiver_Warden_DoExecution","WorkGiver_Warden_ExecuteSlave",
   "WorkGiver_BottleFeedBaby","WorkGiver_Breastfeed","WorkGiver_BreastfeedCarryToMom","WorkGiver_PlayWithBaby","WorkGiver_Teach",
   "WorkGiver_BuildRoof","WorkGiver_RemoveRoof","WorkGiver_ConstructSmoothFloor","WorkGiver_ConstructSmoothWall",
   "WorkGiver_ConstructRemoveFloor","WorkGiver_ConstructRemoveFoundation","WorkGiver_DeconstructForBlueprint","WorkGiver_Uninstall",
   "WorkGiver_Flick","WorkGiver_FixBrokenDownBuilding","WorkGiver_DeepDrill","WorkGiver_OperateScanner","WorkGiver_Fish",
   "WorkGiver_CookFillHopper","WorkGiver_FillFermentingBarrel","WorkGiver_TakeBeerOutOfFermentingBarrel","WorkGiver_EmptyEggBox",
   "WorkGiver_ClearPollution","WorkGiver_ClearSnowOrSand","WorkGiver_PaintBuilding","WorkGiver_PaintFloor",
   "WorkGiver_RemovePaintBuilding","WorkGiver_RemovePaintFloor","WorkGiver_Hack","WorkGiver_HaulCorpses",
   "WorkGiver_CreateXenogerm","WorkGiver_HaulToGeneBank","WorkGiver_HaulToGrowthVat","WorkGiver_HaulToBiosculpterPod",
   "WorkGiver_HaulToSubcoreScanner","WorkGiver_HaulToAtomizer","WorkGiver_EmptyWasteContainer","WorkGiver_PruneGauranlenTree",
   "WorkGiver_ChangeTreeMode","WorkGiver_StudyInteract","WorkGiver_DarkStudyInteract","WorkGiver_TendEntity",
   "WorkGiver_ExtractBioferrite","WorkGiver_TakeBioferriteOutOfHarvester"};
  private static readonly HashSet<string> Adapters=new HashSet<string>{"Quarry.WorkGiver_MineQuarry","ProcessorFramework.WorkGiver_FillProcessor","ProcessorFramework.WorkGiver_EmptyProcessor"};
  public static bool Supported(WorkGiver g)=>g is WorkGiver_Scanner && ((g.GetType().Assembly==typeof(WorkGiver).Assembly&&Audited.Contains(g.GetType().Name))||Adapters.Contains(g.GetType().FullName));
  public static bool Allowed(Pawn p,WorkGiver g)=>Supported(g)&&Manager.Current?.skippedGivers.Contains(g.def)!=true&&p.workSettings!=null&&p.workSettings.GetPriority(g.def.workType)>0&&
   !p.WorkTagIsDisabled(g.def.workTags)&&!p.WorkTypeIsDisabled(g.def.workType)&&!p.IsWorkTypeDisabledByAge(g.def.workType,out _)&&g.MissingRequiredCapacity(p)==null;
  public static Job At(Pawn p,WorkGiverDef def,Thing thing,IntVec3 cell,bool forced,bool probe)
  {
   var scanner=def.Worker as WorkGiver_Scanner;
   if(!Allowed(p,scanner)||scanner.ShouldSkip(p,forced)) return null;
   Job job;
   if(scanner is WorkGiver_Fish)job=scanner.NonScanJob(p);
   else if(thing!=null)
   {
    if(!thing.Spawned||thing.Map!=p.Map||thing.Fogged()||thing.IsForbidden(p)||!p.CanReach(thing,scanner.PathEndMode,Danger.Some)) return null;
    if(!scanner.HasJobOnThing(p,thing,forced)) return null;
    // JobOnThing assigns the teacher to the pupil's live job. Query without that
    // mutation; the actual assignment is made by vanilla only after arrival.
    job=probe&&scanner is WorkGiver_Teach?JobMaker.MakeJob(JobDefOf.Lessongiving,thing):scanner.JobOnThing(p,thing,forced);
   }
   else
   {
    if(!cell.InBounds(p.Map)||cell.Fogged(p.Map)||cell.IsForbidden(p)||!p.CanReach(cell,scanner.PathEndMode,Danger.Some)||!scanner.HasJobOnCell(p,cell,forced)) return null;
    job=scanner.JobOnCell(p,cell,forced);
   }
   if(job!=null) { job.workGiverDef=def; job.playerForced=forced; }
   return job;
  }
  public static Job Plan(Pawn p,ThinkResult local,bool emergency)
  {
   var manager=Manager.Current;
   if(!BunkerMod.Settings.work||manager==null||p.workSettings==null||local.Job?.playerForced==true) return null;
   if(!emergency&&(p.needs?.food?.CurCategory>=HungerCategory.UrgentlyHungry||p.needs?.rest?.CurLevelPercentage<0.18f)) return null;
   var givers=RobotSupport.Givers(p,emergency)??(emergency?p.workSettings.WorkGiversInOrderEmergency:p.workSettings.WorkGiversInOrderNormal);
   int localRank=local.Job?.workGiverDef==null?-1:givers.FindIndex(g=>g.def==local.Job.workGiverDef);
   bool normalScan=manager.CanScan(p,Purpose.Work,emergency);
   if(!normalScan&&(localRank<=0||!manager.CanScan(p,Purpose.Work,emergency,null,higherPriorityWork:true))) return null;
   var routes=Graph.Reachable(p).Where(r=>!Broker.Blacklisted(p,r.map)).ToList();
   Route bestRoute=null; Thing bestThing=null; IntVec3 bestCell=IntVec3.Invalid; WorkGiverDef bestGiver=null; float bestCost=float.MaxValue;
   float bestPriority=float.MinValue;
   for(int rank=0;rank<givers.Count;rank++)
   {
    var giver=givers[rank];
    if(bestGiver!=null) break; // Vanilla's ordered giver list decides priority; distance breaks ties within a giver.
    // During cooldowns only strictly earlier work may replace the local job.
    if(!normalScan&&rank>=localRank) break;
    if(!Allowed(p,giver)) continue;
    if(local.IsValid)
    {
     var localDef=local.Job.workGiverDef;
     if(localDef==null) break;
     if(localRank<0 || rank>localRank) break;
    }
    if(!emergency&&giver is WorkGiver_HaulGeneral)
    {
     var burial=LinkedLogistics.Plan(p,giver,routes);if(burial!=null)return burial;
     var hauling=Hauling.Plan(p,local);if(hauling!=null)return hauling;
    }
    var scanner=(WorkGiver_Scanner)giver;
    bool competing=local.IsValid&&local.Job.workGiverDef==giver.def;
    float localCost=competing&&local.Job.targetA.IsValid?p.Position.DistanceTo(local.Job.targetA.Cell):float.MaxValue;
    float localPriority=competing&&scanner.Prioritized?scanner.GetPriority(p,local.Job.targetA.ToTargetInfo(p.Map)):competing?0:float.MinValue;
    int quota=Math.Max(1,BunkerMod.Settings.maxCandidates/Math.Max(1,routes.Count));
    try {
    foreach(var route in routes)
    using(new RemotePawnScope(p,route.map,route.landing))
    using(new ProbeAudit(route.map,giver is WorkGiver_DoBill))
    {
     if(scanner.ShouldSkip(p)) continue;
     if(scanner is WorkGiver_Fish)
     {
      var fishing=scanner.NonScanJob(p);
      if(fishing!=null)
      {
       var spot=fishing.targetA.Cell;float cost=route.cost+route.landing.DistanceTo(fishing.targetB.Cell);
       if(Portal.Allowed(p,route.map,spot)&&!Broker.Claimed(p,route.map,null,spot)&&
        (!competing||cost+12<localCost)&&cost<bestCost)
       {bestRoute=route;bestCell=spot;bestThing=null;bestGiver=giver.def;bestCost=cost;bestPriority=0;}
       JobMaker.ReturnToPool(fishing);
      }
      continue;
     }
     if(scanner.def.scanThings)
     {
      var things=scanner.PotentialWorkThingsGlobal(p)??route.map.listerThings.ThingsMatching(scanner.PotentialWorkThingRequest);
      foreach(var thing in FairScan.Window(things,quota,FairScan.Key(p,giver.def.defName+":things",route.map)))
      {
       if(Broker.Claimed(p,route.map,thing,IntVec3.Invalid)) continue;
       if(giver is WorkGiver_CleanFilth && route.map.listerFilthInHomeArea.FilthInHomeArea.Count<3) continue;
       var job=At(p,giver.def,thing,IntVec3.Invalid,false,true);
       if(job==null) continue;
       float cost=route.cost+route.landing.DistanceTo(thing.Position);
       float priority=scanner.Prioritized?scanner.GetPriority(p,thing):0;
       JobMaker.ReturnToPool(job);
       if(competing&&(priority<localPriority||priority==localPriority&&cost+12>=localCost))continue;
       if(priority>bestPriority||priority==bestPriority&&cost<bestCost) { bestRoute=route; bestThing=thing; bestCell=IntVec3.Invalid; bestGiver=giver.def; bestCost=cost;bestPriority=priority; }
      }
     }
     if(scanner.def.scanCells)
      foreach(var cell in FairScan.Window(scanner.PotentialWorkCellsGlobal(p)??Enumerable.Empty<IntVec3>(),quota,FairScan.Key(p,giver.def.defName+":cells",route.map)))
      {
       if(Broker.Claimed(p,route.map,null,cell)) continue;
       var job=At(p,giver.def,null,cell,false,true);
       if(job==null) continue;
       float cost=route.cost+route.landing.DistanceTo(cell); JobMaker.ReturnToPool(job);
       float priority=scanner.Prioritized?scanner.GetPriority(p,cell):0;
       if(competing&&(priority<localPriority||priority==localPriority&&cost+12>=localCost))continue;
       if(priority>bestPriority||priority==bestPriority&&cost<bestCost) {bestRoute=route;bestThing=null;bestCell=cell;bestGiver=giver.def;bestCost=cost;bestPriority=priority;}
      }
    }
    if(bestGiver==null)
    {
     var care=CareServices.CarryToMom(p,giver,routes);if(care!=null)return care;
     var logistics=LinkedLogistics.Plan(p,giver,routes);
     if(logistics!=null)return logistics;
     if(!competing){var supply=ConsumerSupply.Plan(p,giver,routes);if(supply!=null)return supply;}
    }
    } catch(Exception ex) {manager.skippedGivers.Add(giver.def);Log.Warning("[SAB] Disabled remote probing for "+giver.def.defName+" for this session after a failed query: "+ex);}
   }
   return bestRoute==null?null:Broker.Begin(p,bestRoute,Purpose.Work,bestThing,bestCell,bestGiver);
  }
 }

 // Audited query side effects: grower scratch state, construction negative cache, bill search
 // cooldown / pause fields, RNG and JobFailReason. No lasting bills/reservations may be changed.
 public sealed class ProbeAudit:IDisposable
 {
  [ThreadStatic] public static bool Active;
  private readonly List<Action> restore=new List<Action>();
  private readonly bool previous;
  public ProbeAudit(Map map,bool auditBills=true)
  {
   previous=Active; Active=true; Rand.PushState();
   try {
   foreach(var name in new[]{"lastReason","lastCustomJobString","silent"}) Capture(null,AccessTools.Field(typeof(JobFailReason),name));
   Capture(null,AccessTools.Field(typeof(WorkGiver_Grower),"wantedPlantDef"));
   var cache=AccessTools.Field(typeof(WorkGiver_ConstructDeliverResources),"noReachableResourceCache");
   Capture(null,cache); cache.SetValue(null,Activator.CreateInstance(cache.FieldType));
   Capture(null,AccessTools.Field(typeof(WorkGiver_ConstructDeliverResources),"noReachableResourceCacheTick"));
   if(auditBills) foreach(var giver in map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver).OfType<IBillGiver>())
    foreach(var bill in giver.BillStack.Bills)
     for(var t=bill.GetType();t!=null&&t!=typeof(object);t=t.BaseType)
      foreach(var f in t.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
       if(!f.IsInitOnly&&(f.FieldType.IsPrimitive||f.FieldType.IsEnum||f.FieldType==typeof(string))) Capture(bill,f);
   } catch { Dispose(); throw; }
  }
  private void Capture(object obj,FieldInfo field) { if(field==null)return; var value=field.GetValue(obj); restore.Add(()=>field.SetValue(obj,value)); }
  public void Dispose() { try { for(int n=restore.Count-1;n>=0;n--) restore[n](); } finally {Rand.PopState();Active=previous;} }
 }
 [HarmonyPatch(typeof(BillStack),nameof(BillStack.RemoveIncompletableBills))]
 public static class ProbeBills { public static bool Prefix()=>!ProbeAudit.Active; }
 [HarmonyPatch(typeof(ReservationManager),nameof(ReservationManager.Reserve))]
 public static class ProbeReservations { public static bool Prefix(ref bool __result) { if(!ProbeAudit.Active)return true; __result=false; throw new InvalidOperationException("Work probe attempted a reservation"); } }
}
