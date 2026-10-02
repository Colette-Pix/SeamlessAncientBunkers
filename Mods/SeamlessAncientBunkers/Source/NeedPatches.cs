using System;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
namespace SeamlessAncientBunkers
{
 [HarmonyPatch(typeof(JobGiver_GetJoy),"TryGiveJob")]
 public static class JoyPatch
 {
  public static void Postfix(Pawn pawn,ref Job __result){try{var job=NeedProbe.Plan(pawn,Purpose.Joy,__result);if(job!=null)__result=job;}catch(Exception e){Log.ErrorOnce("[SAB] Joy planning: "+e,1870041);}}
 }
 [HarmonyPatch(typeof(JobGiver_PatientGoToBed),"TryGiveJob")]
 public static class MedicalPatch
 {
  public static void Postfix(JobGiver_PatientGoToBed __instance,Pawn pawn,ref Job __result)
  {
   if(__instance.urgentOnly&&!HealthAIUtility.ShouldSeekMedicalRestUrgent(pawn))return;
   if(__result?.def==JobDefOf.LayDown&&__result.targetA.Thing is Building_Bed)return;
   try{var job=NeedProbe.Plan(pawn,Purpose.Medical,__result,medicalNode:__instance);if(job!=null){if(__result!=null)JobMaker.ReturnToPool(__result);__result=job;}}catch(Exception e){Log.ErrorOnce("[SAB] Medical planning: "+e,1870042);}
  }
 }
}

