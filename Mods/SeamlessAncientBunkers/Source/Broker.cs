using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 public static class Broker
 {
  public static Intent For(Pawn pawn)=>Manager.Current?.intents.FirstOrDefault(i=>i.pawn==pawn);
  public static bool Transport(Intent i)=>i!=null&&(i.purpose==Purpose.Haul||i.purpose==Purpose.Supply||i.purpose==Purpose.Dining||i.purpose==Purpose.Burial||i.purpose==Purpose.MedicalSupply||i.purpose==Purpose.Passenger||i.purpose==Purpose.AnimalCargo);
  public static bool Enabled(Intent i)
  {
   if(i.forced||i.purpose==Purpose.Manual)return true;
   var s=BunkerMod.Settings;if(!s.enabled)return false;
   if(ConsumerSupply.IsSupply(i))return ConsumerSupply.Enabled(i);
   if(LinkedLogistics.IsLogistics(i))return LinkedLogistics.Enabled(i);
   if(i.purpose==Purpose.Drug||i.purpose==Purpose.Apparel||i.purpose==Purpose.Thirst||i.purpose==Purpose.Dining||i.purpose==Purpose.Gathering||i.purpose==Purpose.Boarding)return true;
   switch(i.purpose){case Purpose.MentalWander:case Purpose.AnimalFollow:case Purpose.AnimalWander:return true;case Purpose.Food:return s.food;case Purpose.Rest:return s.rest;case Purpose.Joy:return s.joy;case Purpose.Medical:return s.medical;case Purpose.Haul:case Purpose.Supply:return s.haul;default:return s.work;}
  }
  public static Job Begin(Pawn pawn,Route route,Purpose purpose,Thing target=null,IntVec3? cell=null,WorkGiverDef giver=null,JoyGiverDef joy=null,bool forced=false,int count=0)
  {
   if(route.portals.Count==0 || !Portal.CanTravel(pawn,route.portals[0],purpose!=Purpose.Manual&&!forced,out var next)) return null;
   var manager=Manager.Current;
   manager.intents.RemoveAll(i=>i.pawn==pawn);
   var intent=new Intent{pawn=pawn,portal=route.portals[0],destination=next,finalMap=route.map,route=route.portals,
    purpose=purpose,target=target,cell=cell??IntVec3.Invalid,giver=giver,joyGiver=joy,forced=forced,
    created=Manager.Now,expires=Manager.Now+30000,count=count,permission=TransitPolicy.For(pawn)};
   manager.intents.Add(intent);
   if(BunkerMod.Settings.debug) Log.Message("[SAB] "+pawn.LabelShort+" plans "+purpose+" via "+route.portals.Count+" portals");
   if(Transport(intent)) { var collect=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed(purpose==Purpose.Passenger?"SAB_CollectPassenger":"SAB_Collect"),target); collect.count=count; return collect; }
   return JobMaker.MakeJob(Manager.TravelDef,intent.portal);
  }
  public static void Cancel(Intent intent,bool failure=false)
  {
   var manager=Manager.Current;
   manager.intents.Remove(intent);
   if(intent.pawn==null) return;
   if(failure&&PawnTransit.IsPassenger(intent)&&intent.pawn.Spawned&&intent.pawn.carryTracker?.CarriedThing==intent.target)
    intent.pawn.carryTracker.TryDropCarriedThing(intent.pawn.Position,ThingPlaceMode.Near,out _);
   int failures=manager.cooldowns.FirstOrDefault(c=>c.pawn==intent.pawn)?.failures??0;
   manager.Cool(intent.pawn);
   if(!failure){var reset=manager.cooldowns.First(c=>c.pawn==intent.pawn);reset.failures=0;reset.failedMap=null;}
   if(failure) { var cooldown=manager.cooldowns.First(c=>c.pawn==intent.pawn); cooldown.failures=failures+1; cooldown.failedMap=intent.finalMap; cooldown.until=Manager.Now+Math.Min(15000,1800*(failures+1)); }
  }
  public static bool Blacklisted(Pawn pawn,Map map)=>Manager.Current.cooldowns.Any(c=>c.pawn==pawn&&c.failedMap==map&&c.failures>=2&&c.until>Manager.Now);
  public static bool Claimed(Pawn pawn,Map map,Thing target,IntVec3 cell)=>Manager.Current.intents.Any(i=>i.pawn!=pawn&&i.expires>Manager.Now&&((target!=null&&i.target==target)||(cell.IsValid&&i.finalMap==map&&i.cell==cell)));
  public static bool Replan(Intent i)
  {
   if(i.pawn?.Spawned!=true||i.finalMap==null||i.finalMap==i.pawn.Map)return false;
   using(new TransitPolicy(i.pawn,i.permission))
   {
    var route=Graph.Reachable(i.pawn,!i.forced&&i.purpose!=Purpose.Manual).FirstOrDefault(r=>
    {
     if(r.map!=i.finalMap)return false;
     var target=i.deliveryTarget??(i.target?.MapHeld==i.finalMap?i.target:null);
     using(new RemotePawnScope(i.pawn,r.map,r.landing))
      return target!=null?i.pawn.CanReach(target,PathEndMode.Touch,Danger.Some):!i.cell.IsValid||i.pawn.CanReach(i.cell,PathEndMode.OnCell,Danger.Some);
    });
    if(route==null)return false;
    i.route=route.portals;i.hop=0;i.portal=route.portals[0];i.destination=Portal.Other(i.portal)?.Map;
    return i.destination!=null;
   }
  }
  public static void Tick()
  {
   foreach(var i in Manager.Current.intents.ToList())
   {
    if(i.route==null||i.route.Count==0) { i.route=new List<MapPortal>{i.portal}; i.finalMap=i.finalMap??i.destination; }
    if(i.pawn==null||i.pawn.Dead||!Portal.Eligible(i.pawn,true,i.forced||i.purpose==Purpose.Manual)||i.expires<=Manager.Now||i.finalMap==null||!Find.Maps.Contains(i.finalMap)||
       !Enabled(i)||!AnimalSupport.ValidIntent(i)||!MentalTransit.Valid(i)) { Cancel(i,true); continue; }
    if(i.pawn.CurJob?.playerForced==true&&i.pawn.CurJobDef!=Manager.TravelDef&&i.pawn.CurJobDef?.defName!="SAB_Collect"&&i.pawn.CurJobDef?.defName!="SAB_CollectPassenger") { Cancel(i); continue; }
    if((i.portal?.Spawned!=true||Portal.Other(i.portal)==null)&&!Replan(i)) { Cancel(i,true); continue; }
    // A genuine need can interrupt travel; retain the intent until the normal AI asks again.
    if(i.pawn.Map!=i.portal.Map&&i.pawn.Map!=i.destination) Cancel(i,true);
   }
  }
  public static Job Resume(Pawn pawn,Job vanilla,JobTag? tag=null)
  {
   var i=For(pawn);
   if(i==null||RemotePawnScope.Active||pawn.CurJobDef==Manager.TravelDef||pawn.CurJobDef?.defName=="SAB_Collect"||pawn.CurJobDef?.defName=="SAB_CollectPassenger") return null;
   // The work-giver postfix just created this intent; allow its pickup job to start before resuming travel.
   if(vanilla?.def==Manager.TravelDef&&vanilla.targetA.Thing==i.portal)return null;
   if((vanilla?.def.defName=="SAB_Collect"||vanilla?.def.defName=="SAB_CollectPassenger")&&vanilla.targetA.Thing==i.target&&!i.carrying)return null;
   if(!Portal.Eligible(pawn,true,i.forced||i.purpose==Purpose.Manual)||i.expires<=Manager.Now||!Enabled(i)) { Cancel(i,true); return null; }
   if(i.route==null||i.route.Count==0) { i.route=new List<MapPortal>{i.portal}; i.finalMap=i.finalMap??i.destination; }
   if(pawn.Map==i.destination)
   {
    i.hop++;
    if(i.hop<i.route.Count) { i.portal=i.route[i.hop]; i.destination=Portal.Other(i.portal)?.Map; }
    else
    {
     Job job=null;
     try { job=Arrive(i); } catch(Exception e) { Log.ErrorOnce("[SAB] Arrival validation failed: "+e,1870031); }
     Cancel(i,job==null&&i.purpose!=Purpose.Manual&&i.purpose!=Purpose.MentalWander&&i.purpose!=Purpose.AnimalWander&&i.purpose!=Purpose.AnimalFollow&&i.purpose!=Purpose.Gathering&&i.purpose!=Purpose.Boarding);
     return job;
    }
   }
   // Let the normal tree satisfy local needs before resuming a suspended route, including modded needs.
   if(vanilla!=null&&tag==JobTag.SatisfyingNeeds&&!(i.purpose==Purpose.Dining&&vanilla.def==JobDefOf.Ingest&&vanilla.targetA.Thing==pawn.carryTracker.CarriedThing))return null;
   if(pawn.needs?.food?.CurCategory>=HungerCategory.UrgentlyHungry && vanilla?.def==JobDefOf.Ingest) return null;
   if(HealthAIUtility.ShouldSeekMedicalRestUrgent(pawn)&&i.purpose!=Purpose.Medical) return null;
   if(i.purpose!=Purpose.Evacuate&&(vanilla?.def==JobDefOf.BeatFire || vanilla?.def.defName=="FleeAndCower" || vanilla?.def.defName=="Flee")) return null;
   if((!Portal.CanTravel(pawn,i.portal,!i.forced&&i.purpose!=Purpose.Manual,out var dest)||dest!=i.destination)&&!Replan(i)) { Cancel(i,true); return null; }
   if(Transport(i)&&pawn.carryTracker.CarriedThing==null) { Cancel(i,true); return null; }
   return JobMaker.MakeJob(Manager.TravelDef,i.portal);
  }
  public static Job Arrive(Intent i)
  {
   var p=i.pawn;
   if(p.Map!=i.finalMap) return null;
   if(ConsumerSupply.IsFetch(i))return ConsumerSupply.Arrive(i);
   if(PawnTransit.IsPassenger(i))return PawnTransit.Arrive(i);
   if(i.purpose==Purpose.MechCharge)return MechTransit.Arrive(i);
   if(i.purpose==Purpose.GroupTravel)return GroupTransit.Arrive(i);
   if(i.purpose==Purpose.Evacuate)return JobMaker.MakeJob(JobDefOf.Goto,i.cell);
   if(LinkedLogistics.IsLogistics(i))return LinkedLogistics.Arrive(i);
   if(i.orderedJob!=null)return LinkedOrders.Arrive(i);
   if(i.purpose==Purpose.Dining)return Dining.Arrive(i);
   if(Transport(i)) return Hauling.Delivery(i);
   if(i.purpose==Purpose.Manual||i.purpose==Purpose.MentalWander) return null;
   if(i.purpose==Purpose.Gathering){var wait=JobMaker.MakeJob(JobDefOf.Wait);wait.expiryInterval=120;wait.reportStringOverride=Gatherings.Waiting;return wait;}
   if(i.purpose==Purpose.Boarding)return ExtraNeeds.Query(new JobGiver_BoardOrLeaveGravship(),p);
   if(i.needNode!=null)return ExtraNeeds.Arrive(i);
   if(i.purpose==Purpose.AnimalFollow||i.purpose==Purpose.AnimalWander) return null;
   if(i.giver!=null)
   {
    var work=WorkProbe.At(p,i.giver,i.target,i.cell,i.forced,false);
    return LinkedOrders.HaulRequest(work)?LinkedOrders.StartHaul(p,work.targetA.Thing):PawnTransit.TryWrap(p,work)??work;
   }
   if(i.purpose==Purpose.Food) return NeedProbe.LocalFood(p);
   if(i.purpose==Purpose.Joy) return NeedProbe.LocalJoy(p,i.joyGiver);
   if(i.purpose==Purpose.Medical) return NeedProbe.LocalMedical(p);
   if(i.purpose==Purpose.Rest && i.target is Building_Bed bed && RestUtility.IsValidBedFor(bed,p,p,true)) return JobMaker.MakeJob(JobDefOf.LayDown,bed);
   return null;
  }
 }
 [HarmonyPatch(typeof(Pawn_JobTracker),"DetermineNextJob")]
 public static class ResumePatch
 {
  public static void Postfix(Pawn ___pawn,ref ThinkResult __result)
  {
   try {
    var job=Broker.Resume(___pawn,__result.Job,__result.Tag);
    if(job==null&&Broker.For(___pawn)==null)job=AnimalSupport.Idle(___pawn,__result.Job);
    if(job!=null) { if(__result.Job!=null) JobMaker.ReturnToPool(__result.Job); __result=new ThinkResult(job,__result.SourceNode,job.workGiverDef?.tagToGive??__result.Tag); }
   } catch(Exception e) { Log.ErrorOnce("[SAB] Intent resume failed: "+e,1870032); }
  }
 }
}

