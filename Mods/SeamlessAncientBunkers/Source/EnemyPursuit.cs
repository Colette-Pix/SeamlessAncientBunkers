using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SeamlessAncientBunkers
{
 public static class EnemyPursuit
 {
  private static Game game;
  private static readonly Dictionary<Pawn,int> nextScan=new Dictionary<Pawn,int>();
  public static bool Friendly(Pawn p)=>p?.Faction==Faction.OfPlayer&&p.RaceProps.Animal&&p.roping?.IsRoped!=true&&
   p.GetLord()==null&&p.mindState?.duty==null&&
   ((p.def.defName=="VPE_SummonedSkeleton"&&p.MentalStateDef?.defName=="VPE_Manhunter")||
    (!p.InMentalState&&p.training?.HasLearned(TrainableDefOf.Release)==true));
  public static bool MentalViolence(Pawn p)=>p?.MentalState is MentalState_MurderousRage;
  public static bool ControlledAccess(Pawn p)=>Friendly(p)||p?.Faction==Faction.OfPlayer;
  public static bool Eligible(Pawn p)=>BunkerMod.Settings.enabled&&p?.Spawned==true&&!p.Dead&&!p.Downed&&(Friendly(p)||MentalViolence(p)||(p.HostileTo(Faction.OfPlayer)&&
   (p.MentalStateDef==MentalStateDefOf.Manhunter||p.MentalStateDef==MentalStateDefOf.ManhunterPermanent||
    (p.GetLord()?.CurLordToil?.GetType().Name.StartsWith("LordToil_AssaultColony")==true&&!p.InMentalState))));
  public static bool Victim(Pawn attacker,Pawn p)=>p?.Spawned==true&&!p.Dead&&
   (MentalViolence(attacker)?((MentalState_MurderousRage)attacker.MentalState).target==p:
   (Friendly(attacker)?!p.Downed&&!p.IsPrisoner&&p.HostileTo(Faction.OfPlayer)&&!p.Fogged()&&Portal.Allowed(attacker,p.Map,p.Position):p.Faction==Faction.OfPlayer&&attacker.HostileTo(p)));
  public static bool Landing(Pawn p,MapPortal exit,out IntVec3 cell)
  {
   cell=IntVec3.Invalid;if(exit?.Spawned!=true)return false;
   using(new RemotePawnScope(p,exit.Map,exit.Position))
   foreach(var c in GenRadial.RadialCellsAround(exit.Position,5f,true))
    if(c.InBounds(exit.Map)&&c.Standable(exit.Map)&&(!ControlledAccess(p)||(!c.Fogged(exit.Map)&&Portal.Allowed(p,exit.Map,c)))&&!c.ContainsStaticFire(exit.Map)&&(!p.HarmedByVacuum||c.GetVacuum(exit.Map)<0.5f)&&
     exit.Map.reachability.CanReach(c,exit,PathEndMode.Touch,TraverseParms.For(p,Danger.Deadly,TraverseMode.PassDoors))){cell=c;return true;}
   return false;
  }
  public static bool CanCross(Pawn p,MapPortal portal)
  {
   var other=Portal.Other(portal);
   return Eligible(p)&&portal?.Spawned==true&&p.Map==portal.Map&&other!=null&&!portal.LoadInProgress&&!other.LoadInProgress&&
    portal.IsEnterable(out _)&&other.IsEnterable(out _)&&(!ControlledAccess(p)||
     (Manager.Current?.Enabled(portal)==true&&!portal.IsForbidden(Faction.OfPlayer)&&!other.IsForbidden(Faction.OfPlayer)&&!portal.IsForbidden(p)&&!other.IsForbidden(p)&&!portal.Fogged()&&!other.Fogged()&&
      Portal.Allowed(p,portal.Map,portal.Position)&&Portal.Allowed(p,other.Map,other.Position)))&&Landing(p,other,out _)&&
    p.Map.reachability.CanReach(p.Position,portal,PathEndMode.Touch,TraverseParms.For(p,Danger.Deadly,TraverseMode.PassDoors));
  }
  public static Job Plan(Pawn p,Job local)
  {
   if(RemotePawnScope.Active||!Eligible(p)||p.CurJobDef?.defName=="SAB_Pursue")return null;
   if(local?.targetA.Thing is Thing localVictim&&localVictim.Map==p.Map&&Target(p,localVictim))return null;
   if(Friendly(p))
   {
    if(Manager.Current?.enabledPortals.Any(h=>Portal.Other(h)!=null&&(h.Map==p.Map||h.PocketMap==p.Map))!=true)return null;
    // Preserve feeding, rest, hauling, fleeing and explicit orders. Attack-trained
    // animals may seek combat while idle even when their master is on another map.
    if(p.CurJob?.playerForced==true||p.carryTracker?.CarriedThing!=null||Broker.For(p)!=null)return null;
    if(local!=null&&(local.playerForced||(local.def!=JobDefOf.GotoWander&&local.def!=JobDefOf.Wait_Wander&&local.def!=JobDefOf.FollowClose&&local.def!=JobDefOf.Wait)))return null;
    var nearby=p.Map.mapPawns.AllPawnsSpawned.Where(v=>Victim(p,v)&&p.CanReach(v,PathEndMode.Touch,Danger.Deadly)).OrderBy(v=>p.Position.DistanceToSquared(v.Position)).FirstOrDefault();
    if(nearby!=null){var attack=JobMaker.MakeJob(JobDefOf.AttackMelee,nearby);attack.expiryInterval=300;attack.checkOverrideOnExpire=true;return attack;}
   }
   if(game!=Current.Game){game=Current.Game;nextScan.Clear();}
   if(nextScan.TryGetValue(p,out int tick)&&tick>Manager.Now)return null;nextScan[p]=Manager.Now+120;
   var roots=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).ToList();
   var visited=new HashSet<Map>{p.Map};var queue=new Queue<Tuple<Map,IntVec3,MapPortal,int>>();queue.Enqueue(Tuple.Create(p.Map,p.Position,(MapPortal)null,0));
   while(queue.Count>0)
   {
    var state=queue.Dequeue();if(state.Item4>=Find.Maps.Count)continue;
    foreach(var root in roots)
    {
     MapPortal link=root.Map==state.Item1?root:root.exit;if(link?.Map!=state.Item1)continue;
     var exit=Portal.Other(link);if(exit==null||visited.Contains(exit.Map))continue;
     using(new RemotePawnScope(p,state.Item1,state.Item2))if(!CanCross(p,link))continue;
     visited.Add(exit.Map);var first=state.Item3??link;
     if(!Landing(p,exit,out var landing))continue;
     Thing victim;
     using(new RemotePawnScope(p,exit.Map,landing))
      victim=exit.Map.mapPawns.AllPawnsSpawned.Cast<Thing>().Concat(exit.Map.listerBuildings.allBuildingsColonist.Cast<Thing>()).FirstOrDefault(v=>Target(p,v)&&p.CanReach(v,PathEndMode.Touch,Danger.Deadly,true));
     if(victim!=null){var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("SAB_Pursue"),first,victim);job.canBashDoors=true;job.canBashFences=true;job.expiryInterval=4000;return job;}
     queue.Enqueue(Tuple.Create(exit.Map,landing,first,state.Item4+1));
    }
   }
   return null;
  }
  public static bool Target(Pawn p,Thing target)=>target is Pawn victim?Victim(p,victim):
   target is Building&&target.Spawned&&target.Faction==Faction.OfPlayer&&p.HostileTo(target)&&
   p.GetLord()?.CurLordToil?.GetType().Name.StartsWith("LordToil_AssaultColony")==true;
  public static void Transfer(Pawn p,MapPortal portal,IntVec3 cell)
  {
   var destination=Portal.Other(portal).Map;var oldLord=p.GetLord();var oldJob=oldLord?.LordJob as LordJob_AssaultColony;
   bool Flag(string name,bool fallback)=>oldJob==null?fallback:(bool)AccessTools.Field(typeof(LordJob_AssaultColony),name).GetValue(oldJob);
   var assault=oldJob==null?null:new LordJob_AssaultColony(p.Faction,Flag("canKidnap",true),Flag("canTimeoutOrFlee",true),Flag("sappers",false),Flag("useAvoidGridSmart",false),Flag("canSteal",true),Flag("breachers",false),Flag("canPickUpOpportunisticWeapons",false));
   oldLord?.Notify_PawnLost(p,PawnLostCondition.ExitedMap);
   p.DeSpawnOrDeselect();GenSpawn.Spawn(p,cell,destination);portal.OnEntered(p);
   BunkerDoorSecurity.Current?.Arrived(p,destination);
   if(assault!=null)
   {
    var lord=destination.lordManager.lords.FirstOrDefault(l=>l.faction==p.Faction&&l.LordJob is LordJob_AssaultColony&&l.CurLordToil.GetType().Name.StartsWith("LordToil_AssaultColony"));
    if(lord==null)LordMaker.MakeNewLord(p.Faction,assault,destination,new[]{p});else lord.AddPawn(p);
   }
  }
 }
 [HarmonyPatch(typeof(Pawn_JobTracker),"DetermineNextJob")]
 public static class PursuitPatch
 {
  static void Postfix(Pawn ___pawn,ref ThinkResult __result)
  {
   try{var job=EnemyPursuit.Plan(___pawn,__result.Job);if(job!=null){if(__result.Job!=null)JobMaker.ReturnToPool(__result.Job);__result=new ThinkResult(job,__result.SourceNode);}}
   catch(Exception e){Log.ErrorOnce("[SAB] Enemy pursuit failed: "+e,1870042);}
  }
 }
 public class JobDriver_Pursue:JobDriver
 {
  public override bool TryMakePreToilReservations(bool errorOnFailed)=>true;
  protected override IEnumerable<Toil> MakeNewToils()
  {
   this.FailOn(()=>!EnemyPursuit.Eligible(pawn)||!EnemyPursuit.Target(pawn,job.targetB.Thing)||job.targetB.Thing.Map==pawn.Map||Portal.Other(job.targetA.Thing as MapPortal)==null);
   yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
   yield return Toils_General.Wait(90).FailOnCannotTouch(TargetIndex.A,PathEndMode.Touch);
   var cross=ToilMaker.MakeToil("SAB_EnemyCross");cross.defaultCompleteMode=ToilCompleteMode.Instant;
   cross.initAction=()=>{var portal=job.targetA.Thing as MapPortal;if(!EnemyPursuit.CanCross(pawn,portal)||!EnemyPursuit.Landing(pawn,Portal.Other(portal),out var cell)){EndJobWith(JobCondition.Incompletable);return;}EnemyPursuit.Transfer(pawn,portal,cell);};yield return cross;
  }
 }
}
