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
 public static class RobotSupport
 {
  private static readonly Type RobotType=AccessTools.TypeByName("AIRobot.X2_AIRobot");
  private static readonly FieldInfo StationField=RobotType==null?null:AccessTools.Field(RobotType,"rechargeStation");
  private static readonly MethodInfo WorkMethod=RobotType==null?null:AccessTools.Method(RobotType,"GetWorkGivers");
  internal static readonly HashSet<Pawn> Transferring=new HashSet<Pawn>();
  public static bool IsRobot(Pawn p)=>RobotType!=null&&RobotType.IsInstanceOfType(p)&&p.Faction==Faction.OfPlayer;
  public static Thing Station(Pawn p)=>IsRobot(p)?StationField.GetValue(p) as Thing:null;
  public static List<WorkGiver> Givers(Pawn p,bool emergency)=>IsRobot(p)?WorkMethod.Invoke(p,new object[]{emergency}) as List<WorkGiver>:null;
  public static bool NeedsReturn(Pawn p)=>IsRobot(p)&&Station(p)?.Map!=p.Map&&p.needs?.rest?.CurLevel<0.55f;
  public static Job Return(Pawn p)
  {
   var station=Station(p);
   if(!BunkerMod.Settings.enabled||station?.Spawned!=true||station.Map==p.Map||!Portal.Eligible(p)||Broker.For(p)!=null)return null;
   var route=Graph.Reachable(p,false).FirstOrDefault(r=>r.map==station.Map);
   return route==null?null:Broker.Begin(p,route,Purpose.Manual,station);
  }
  public static void SpawnTransferred(Pawn p,IntVec3 cell,Map map)
  {
   if(IsRobot(p))Transferring.Add(p);
   try{GenSpawn.Spawn(p,cell,map,Rot4.Random);}finally{Transferring.Remove(p);}
  }
 }
 [HarmonyPatch]
 public static class RobotInitPatch
 {
  static bool Prepare()=>AccessTools.TypeByName("AIRobot.X2_AIRobot")!=null;
  static MethodBase TargetMethod()=>AccessTools.Method(AccessTools.TypeByName("AIRobot.X2_AIRobot"),"InitPawn_Setup");
  static bool Prefix(Pawn __instance)=>!RobotSupport.Transferring.Contains(__instance);
 }
 [HarmonyPatch]
 public static class RobotWorkPatch
 {
  static bool Prepare()=>AccessTools.TypeByName("AIRobot.X2_JobGiver_Work")!=null;
  static MethodBase TargetMethod()=>AccessTools.Method(AccessTools.TypeByName("AIRobot.X2_JobGiver_Work"),"TryIssueJobPackage");
  static void Postfix(Pawn pawn,ref ThinkResult __result)
  {
   if(RemotePawnScope.Active||!RobotSupport.IsRobot(pawn)||Broker.For(pawn)!=null)return;
   try{
    Job job=null;
    if(RobotSupport.NeedsReturn(pawn))job=RobotSupport.Return(pawn);
    else if(pawn.needs?.rest?.CurLevel>=0.55f){job=WorkProbe.Plan(pawn,__result,false);if(job==null)job=Hauling.Plan(pawn,__result);}
    if(job!=null){if(__result.Job!=null)JobMaker.ReturnToPool(__result.Job);__result=new ThinkResult(job,__result.SourceNode);}
   }catch(Exception e){Log.ErrorOnce("[SAB] Robot planning failed: "+e,1870040);}
  }
 }
 [HarmonyPatch]
 public static class RobotReturnPatch
 {
  static bool Prepare()=>AccessTools.TypeByName("AIRobot.X2_AIRobot")!=null;
  static IEnumerable<MethodBase> TargetMethods()
  {
   foreach(var name in new[]{"X2_JobGiver_RechargeEnergy","X2_JobGiver_RechargeEnergyIdle","X2_JobGiver_Return2BaseDespawn","X2_JobGiver_Return2BaseAndWait","X2_JobGiver_Return2BaseRoom"})
   {var type=AccessTools.TypeByName("AIRobot."+name);var method=type==null?null:AccessTools.DeclaredMethod(type,"TryGiveJob");if(method!=null)yield return method;}
  }
  static bool Prefix(Pawn pawn,ref Job __result)
  {
   var station=RobotSupport.Station(pawn);
   if(station?.Spawned!=true||station.Map==pawn.Map)return true;
   __result=RobotSupport.Return(pawn);return false;
  }
 }
 [HarmonyPatch]
 public static class RobotIdlePatch
 {
  static bool Prepare()=>AccessTools.TypeByName("AIRobot.X2_AIRobot")!=null;
  static IEnumerable<MethodBase> TargetMethods()
  {
   foreach(var name in new[]{"X2_JobGiver_RechargeEnergyIdle","X2_JobGiver_Return2BaseRoom"})
    yield return AccessTools.Method(AccessTools.TypeByName("AIRobot."+name),"TryIssueJobPackage");
  }
  static bool Prefix(Pawn pawn,ref ThinkResult __result)
  {
   var station=RobotSupport.Station(pawn);if(station?.Spawned!=true||station.Map==pawn.Map)return true;
   var job=RobotSupport.Return(pawn);__result=job==null?ThinkResult.NoJob:new ThinkResult(job,null);return false;
  }
 }
 [HarmonyPatch]
 public static class RobotDistancePatch
 {
  static bool Prepare()=>AccessTools.TypeByName("AIRobot.X2_AIRobot")!=null;
  static MethodBase TargetMethod()=>AccessTools.Method(AccessTools.TypeByName("AIRobot.X2_AIRobot"),"IsInDistanceToStation");
  static bool Prefix(Pawn __instance,ref bool __result){if(RobotSupport.Station(__instance)?.Map==__instance.Map)return true;__result=false;return false;}
 }
}
