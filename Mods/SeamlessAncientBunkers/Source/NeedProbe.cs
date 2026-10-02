using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 public static class NeedProbe
 {
  private static readonly System.Reflection.MethodInfo FoodMethod=AccessTools.Method(typeof(JobGiver_GetFood),"TryGiveJob");
  private static readonly System.Reflection.MethodInfo MedicalMethod=AccessTools.Method(typeof(JobGiver_PatientGoToBed),"TryGiveJob");
  public static Job LocalFood(Pawn p)=>(Job)FoodMethod.Invoke(new JobGiver_GetFood(),new object[]{p});
  public static Job LocalMedical(Pawn p)=>(Job)MedicalMethod.Invoke(new JobGiver_PatientGoToBed(),new object[]{p});
  public static Job LocalJoy(Pawn p,JoyGiverDef def)
  {
   if(def==null||p.needs?.joy==null||p.needs.joy.tolerances.BoredOf(def.joyKind)||!def.Worker.CanBeGivenTo(p)) return null;
   return def.Worker.TryGiveJob(p);
  }
  public static Job Plan(Pawn p,Purpose purpose,Job local,JobGiver_GetFood foodNode=null,JobGiver_PatientGoToBed medicalNode=null)
  {
   if(p.RaceProps.Animal) return AnimalSupport.Need(p,purpose,local,foodNode);
   if(RemotePawnScope.Active||Broker.For(p)!=null||Manager.Current==null) return null;
   var s=BunkerMod.Settings;
   if((purpose==Purpose.Food&&!s.food)||(purpose==Purpose.Rest&&!s.rest)||(purpose==Purpose.Medical&&!s.medical)||(purpose==Purpose.Joy&&!s.joy)) return null;
   if(purpose==Purpose.Food&&(p.needs?.food==null||p.needs.food.CurLevelPercentage>=p.RaceProps.FoodLevelPercentageWantEat)) return null;
   if(purpose==Purpose.Medical&&!HealthAIUtility.ShouldSeekMedicalRest(p)) return null;
   if(purpose==Purpose.Joy&&(local!=null||p.needs?.joy==null)) return null;
   if(purpose==Purpose.Rest&&(local?.def!=JobDefOf.LayDown||HealthAIUtility.ShouldSeekMedicalRest(p)||local.targetA.Thing is Building_Bed&&
    (p.ownership?.OwnedBed==null||p.ownership.OwnedBed==local.targetA.Thing||p.ownership.OwnedBed.Map==p.Map))) return null;
   // The assigned bed is a specific native preference, not a broad work scan.
   // Check it before scan throttles and map failure history: otherwise the local
   // unassigned fallback can claim a new bed during the throttle interval.
   if(purpose==Purpose.Rest)
   {
    var assigned=AssignedBed(p);if(assigned!=null)return assigned;
    if(local?.targetA.Thing is Building_Bed)return null;
   }
   // A vanilla floor-sleep decision must not be trapped by the post-trip stay timer.
   // Rest reaches this point for floor sleep or a usable remote owned bed; route safety and
   // failed-destination checks still apply, as does the urgent scan throttle.
   bool urgent=purpose==Purpose.Rest||purpose==Purpose.Medical||p.needs?.food?.CurCategory>=HungerCategory.UrgentlyHungry;
   if(!Manager.Current.CanScan(p,purpose,urgent)) return null;
   var routes=Graph.Reachable(p).Where(r=>!Broker.Blacklisted(p,r.map)).ToList();
   Route best=null; Thing target=null; JoyGiverDef joy=null; float score=float.MinValue;
   float localFood=local?.targetA.Thing?.def.ingestible==null?float.MinValue:FoodUtility.FoodOptimality(p,local.targetA.Thing,local.targetA.Thing.def,p.Position.DistanceTo(local.targetA.Cell));
   foreach(var route in routes)
   using(new RemotePawnScope(p,route.map,route.landing))
   using(new ProbeAudit(route.map,false))
   {
    Job candidate=null; JoyGiverDef selectedJoy=null;
    if(purpose==Purpose.Food) candidate=(Job)FoodMethod.Invoke(foodNode??new JobGiver_GetFood(),new object[]{p});
    else if(purpose==Purpose.Medical) { candidate=(Job)MedicalMethod.Invoke(medicalNode??new JobGiver_PatientGoToBed(),new object[]{p}); if(candidate?.def!=JobDefOf.LayDown||!(candidate.targetA.Thing is Building_Bed)) {if(candidate!=null)JobMaker.ReturnToPool(candidate);continue;} }
    else if(purpose==Purpose.Rest)
    {
     var bed=RestUtility.FindBedFor(p);
     if(bed?.Map==route.map&&RestUtility.IsValidBedFor(bed,p,p,true)&&
      (!(local?.targetA.Thing is Building_Bed)||bed==p.ownership.OwnedBed)) candidate=JobMaker.MakeJob(JobDefOf.LayDown,bed);
    }
    else if(purpose==Purpose.Joy)
     foreach(var def in DefDatabase<JoyGiverDef>.AllDefsListForReading.Where(d=>d.Worker is JoyGiver_InteractBuilding&&d.Worker.GetType().Assembly==typeof(JoyGiver).Assembly))
     {candidate=LocalJoy(p,def);if(candidate!=null){selectedJoy=def;break;}}
    if(candidate==null) continue;
    var thing=candidate.targetA.Thing;
    if(thing?.Spawned!=true||thing.Map!=route.map||Broker.Claimed(p,route.map,thing,IntVec3.Invalid)) {JobMaker.ReturnToPool(candidate);continue;}
    float value= -route.cost-route.landing.DistanceTo(thing.Position);
    if(purpose==Purpose.Food)
    {
     if(candidate.def!=JobDefOf.Ingest) {JobMaker.ReturnToPool(candidate);continue;}
     value=FoodUtility.FoodOptimality(p,thing,thing.def,route.cost+route.landing.DistanceTo(thing.Position));
     if(local!=null&&value<localFood+40f) {JobMaker.ReturnToPool(candidate);continue;}
    }
    if(value>score){score=value;best=route;target=thing;joy=selectedJoy;}
    JobMaker.ReturnToPool(candidate);
   }
   return best==null?null:Broker.Begin(p,best,purpose,target,joy:joy);
  }
  static Job AssignedBed(Pawn p)
  {
   var owned=p.ownership?.OwnedBed;
   if(owned?.Spawned!=true||owned.Map==p.Map||owned.Medical||!BunkerMod.Settings.enabled||!Portal.Eligible(p)||
    p.CurJobDef==Manager.TravelDef||p.CurJob?.playerForced==true||p.CurJob!=null&&!p.jobs.IsCurrentJobPlayerInterruptible())return null;
   foreach(var route in Graph.Reachable(p).Where(r=>r.map==owned.Map))
   {
    bool valid;
    using(new RemotePawnScope(p,route.map,route.landing))
     valid=!owned.Fogged()&&!owned.IsBurning()&&Portal.Allowed(p,route.map,owned.Position)&&
      !Broker.Claimed(p,route.map,owned,IntVec3.Invalid)&&RestUtility.IsValidBedFor(owned,p,p,true,guestStatus:p.GuestStatus);
    if(valid)return Broker.Begin(p,route,Purpose.Rest,owned);
   }
   return null;
  }
 }
}

