using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SeamlessAncientBunkers
{
 // Vanilla already chooses a pocket-map portal when exiting. Keep that decision,
 // but preserve stolen/carried things and continue the exit duty on the next floor.
 [HarmonyPatch(typeof(JobGiver_ExitMap),"TryGiveJob")]
 public static class ConnectedExitJob
 {
  public static void Postfix(Pawn pawn,ref Job __result)
  {
   if(!BunkerMod.Settings.enabled||RemotePawnScope.Active||__result?.def!=JobDefOf.EnterPortal||
    !(__result.targetA.Thing is MapPortal portal)||Portal.Other(portal)==null)return;
   JobMaker.ReturnToPool(__result);
   __result=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("SAB_ExitConnected"),portal);
   __result.canBashDoors=true;__result.canBashFences=true;
  }
 }
 public class JobDriver_ConnectedExit:JobDriver
 {
  public override bool TryMakePreToilReservations(bool errorOnFailed)=>true;
  protected override IEnumerable<Toil> MakeNewToils()
  {
   this.FailOnDespawnedOrNull(TargetIndex.A);
   this.FailOn(()=>pawn.Downed||Portal.Other(job.targetA.Thing as MapPortal)==null);
   yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
   yield return Toils_General.Wait(90).FailOnCannotTouch(TargetIndex.A,PathEndMode.Touch);
   var cross=ToilMaker.MakeToil("SAB_ContinueExit");cross.defaultCompleteMode=ToilCompleteMode.Instant;
   cross.initAction=()=>
   {
    var portal=job.targetA.Thing as MapPortal;var other=Portal.Other(portal);
    if(other==null||!portal.IsEnterable(out _)||!other.IsEnterable(out _)||
     (pawn.Faction==Faction.OfPlayer&&(portal.IsForbidden(pawn)||other.IsForbidden(pawn)))||
     !EnemyPursuit.Landing(pawn,other,out var cell)){EndJobWith(JobCondition.Incompletable);return;}
    var lord=pawn.GetLord();lord?.Notify_PawnLost(pawn,PawnLostCondition.ExitedMap);
    pawn.DeSpawnOrDeselect();GenSpawn.Spawn(pawn,cell,other.Map);portal.OnEntered(pawn);
    if(!pawn.InMentalState)
    {
     var destinationLord=other.Map.lordManager.lords.FirstOrDefault(l=>l.faction==pawn.Faction&&l.LordJob is LordJob_ExitMapBest);
     if(destinationLord==null)LordMaker.MakeNewLord(pawn.Faction,new LordJob_ExitMapBest(LocomotionUrgency.Jog),other.Map,new[]{pawn});
     else destinationLord.AddPawn(pawn);
    }
   };
   yield return cross;
  }
 }
}
