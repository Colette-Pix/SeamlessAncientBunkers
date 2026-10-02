using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 public static class CareServices
 {
  public static Route TeachingRoute(Pawn teacher,Pawn child)
  {
   foreach(var route in Graph.Reachable(teacher).Where(r=>r.map==child.Map))
    using(new RemotePawnScope(teacher,route.map,route.landing))
     if(teacher.CanReach(child,PathEndMode.Touch,Danger.None))return route;
   return null;
  }
  public static Job CarryToMom(Pawn p,WorkGiver giver,List<Route> routes)
  {
   if(!(giver is WorkGiver_BreastfeedCarryToMom))return null;
   var maps=LinkedResources.Maps(p.Map);
   foreach(var baby in FairScan.Window(maps.SelectMany(m=>m.mapPawns.FreeColonistsAndPrisonersSpawned).Where(b=>b.DevelopmentalStage.Baby()),BunkerMod.Settings.maxCandidates,FairScan.Key(p,"carryToMom")))
   {
    var source=baby.Map==p.Map?new Route{map=p.Map,landing=p.Position}:routes.FirstOrDefault(r=>r.map==baby.Map&&ConsumerSupply.Accessible(p,r,baby));if(source==null)continue;
    using(new RemotePawnScope(p,source.map,source.landing))
     if(!ChildcareUtility.CanHaulBabyNow(p,baby,false,out _)||!ChildcareUtility.WantsSuckle(baby,out _))continue;
    foreach(var mom in maps.SelectMany(m=>m.mapPawns.FreeColonistsAndPrisonersSpawned).Where(m=>m.Map!=baby.Map&&(m.Downed||m.IsPrisoner)))
    {
     var dest=mom.Map==p.Map?new Route{map=p.Map,landing=p.Position}:routes.FirstOrDefault(r=>r.map==mom.Map&&ConsumerSupply.Accessible(p,r,mom));if(dest==null)continue;
     using(new RemotePawnScope(p,dest.map,dest.landing))using(new RemotePawnScope(baby,dest.map,dest.landing))
      if(!ChildcareUtility.CanHaulBabyToDownedMomToBreastfeedNow(p,mom,baby,false,out _))continue;
     var native=ChildcareUtility.MakeBreastfeedCarryToMomJob(baby,mom);var job=PawnTransit.Plan(p,baby,mom,native);
     if(job!=null)return job;JobMaker.ReturnToPool(native);
    }
   }
   return null;
  }
  public static Job Safety(Pawn p,AutofeedMode mode)
  {
   if(!ModsConfig.BiotechActive||RemotePawnScope.Active||Manager.Current==null||Broker.For(p)!=null||!BunkerMod.Settings.work||
    !Portal.Eligible(p,true)||p.workSettings?.WorkIsActive(WorkTypeDefOf.Childcare)!=true||!Manager.Current.CanScan(p,Purpose.Medical,true))return null;
   var routes=Graph.Reachable(p);var places=new List<Route>{new Route{map=p.Map,landing=p.Position}};places.AddRange(routes);
   foreach(var source in places)
   foreach(var baby in FairScan.Window(source.map.mapPawns.FreeHumanlikesSpawnedOfFaction(p.Faction).Where(b=>b.DevelopmentalStage.Baby()),
    BunkerMod.Settings.maxCandidates,FairScan.Key(p,"babySafety",source.map)))
   {
    if(baby.mindState.AutofeedSetting(p)!=mode)continue;
    using(new RemotePawnScope(p,source.map,source.landing))if(!ChildcareUtility.CanHaulBabyNow(p,baby,false,out _))continue;
    foreach(var dest in places.Where(r=>r.map!=baby.Map))
    {
     Building_Bed bed;
     using(new RemotePawnScope(p,dest.map,dest.landing))using(new RemotePawnScope(baby,dest.map,dest.landing))
     {
      bed=RestUtility.FindBedFor(baby,p,true,false,baby.GuestStatus);
      if(bed==null||!baby.ComfortableTemperatureAtCell(bed.Position,dest.map)||baby.CurrentBed()!=null&&baby.ComfortableTemperatureAtCell(baby.CurrentBed().Position,baby.CurrentBed().Map))continue;
     }
     var native=ChildcareUtility.MakeBringBabyToSafetyJob(p,baby);var job=PawnTransit.Plan(p,baby,bed,native);
     if(job!=null)return job;JobMaker.ReturnToPool(native);
    }
   }
   return null;
  }
 }
 [HarmonyPatch]
 public static class ConnectedBabySafety
 {
  public static IEnumerable<MethodBase> TargetMethods()
  {yield return AccessTools.Method(typeof(WorkGiver_BringBabyToSafety),"NonScanJob");yield return AccessTools.Method(typeof(JobGiver_BringBabyToSafety),"TryGiveJob");}
  public static void Postfix(object __instance,Pawn pawn,ref Job __result)
  {if(__result==null)__result=CareServices.Safety(pawn,__instance is WorkGiver?AutofeedMode.Childcare:AutofeedMode.Urgent);}
 }
 [HarmonyPatch(typeof(SchoolUtility),nameof(SchoolUtility.FindTeacher))]
 public static class ConnectedTeachers
 {
  public static void Postfix(Pawn child,ref Pawn __result)
  {
   if(__result!=null||Manager.Current==null||!BunkerMod.Settings.enabled||!BunkerMod.Settings.work)return;
   foreach(var teacher in LinkedResources.Maps(child.Map).Where(m=>m!=child.Map).SelectMany(m=>m.mapPawns.FreeColonistsSpawned))
   {
    if(!SchoolUtility.CanTeachNow(teacher))continue;
    var route=CareServices.TeachingRoute(teacher,child);if(route==null)continue;
    using(new RemotePawnScope(teacher,route.map,route.landing))
     if(teacher.CanReach(child,PathEndMode.Touch,Danger.None)){__result=teacher;return;}
   }
  }
 }
 [HarmonyPatch(typeof(SchoolUtility),nameof(SchoolUtility.ClosestSchoolDesk))]
 public static class ConnectedSchoolDesk
 {
  public static bool Prefix(Pawn child,Pawn teacher,ref Thing __result)
  {
   if(teacher==null||child.Map==teacher.Map||!BunkerMod.Settings.enabled)return true;
   var route=CareServices.TeachingRoute(teacher,child);if(route==null){__result=null;return false;}
   using(new RemotePawnScope(teacher,route.map,route.landing))
    __result=GenClosest.ClosestThingReachable(child.Position,child.Map,ThingRequest.ForDef(ThingDefOf.SchoolDesk),PathEndMode.InteractionCell,TraverseParms.For(child),9999f,
     d=>child.CanReserveSittableOrSpot(SchoolUtility.DeskSpotStudent(d))&&teacher.CanReserveSittableOrSpot(SchoolUtility.DeskSpotTeacher(d))&&!d.IsForbidden(child)&&!d.IsForbidden(teacher));
   return false;
  }
 }
}
