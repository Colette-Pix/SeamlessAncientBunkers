using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SeamlessAncientBunkers
{
 // Track actual incoming pursuers rather than inferring clearance from fog or
 // dormant encounter contents. Native defenders retain their original access.
 public class BunkerDoorSecurity : GameComponent
 {
  public List<Pawn> incoming = new List<Pawn>();
  public BunkerDoorSecurity(Game game) { }
  public static BunkerDoorSecurity Current => Verse.Current.Game?.GetComponent<BunkerDoorSecurity>();
  public override void ExposeData()
  {
   Scribe_Collections.Look(ref incoming,"sabIncomingEnemies",LookMode.Reference);
   if(Scribe.mode==LoadSaveMode.PostLoadInit)
   {
    incoming=incoming??new List<Pawn>();
    incoming.RemoveAll(p=>p==null||p.Dead);
   }
  }
  public void Arrived(Pawn pawn,Map destination)
  {
   if(pawn.HostileTo(Faction.OfPlayer)&&IsBunker(destination)&&!incoming.Contains(pawn))incoming.Add(pawn);
  }
  public static bool IsBunker(Map map)=>map!=null&&Find.Maps.Any(m=>m.listerThings.AllThings.OfType<AncientHatch>().Any(h=>h.PocketMap==map));
 }
 [HarmonyPatch(typeof(Building_HackableDoor),nameof(Building_HackableDoor.PawnCanOpen))]
 public static class IncomingEnemyDoorAccess
 {
  public static void Postfix(Building_HackableDoor __instance,Pawn p,ref bool __result)
  {
   if(__result&&__instance.def.defName=="AncientBlastDoor"&&p!=null&&
      p.HostileTo(Faction.OfPlayer)&&BunkerDoorSecurity.Current?.incoming.Contains(p)==true&&
      BunkerDoorSecurity.IsBunker(__instance.Map))__result=false;
  }
 }
}
