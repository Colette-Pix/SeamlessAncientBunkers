using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SeamlessAncientBunkers
{
 // Claiming changes ownership only; the door's normal hacking lock remains intact.
 [HarmonyPatch(typeof(Building),nameof(Building.ClaimableBy))]
 public static class BlastDoorClaims
 {
  public static void Postfix(Building __instance,Faction by,ref AcceptanceReport __result)
  {
   if(__instance.def.defName!="AncientBlastDoor"||!__instance.Spawned||by!=Faction.OfPlayer||__instance.Faction==by)return;
   var hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).FirstOrDefault(h=>h.PocketMap==__instance.Map);
   if(hatch==null)return;
   __result=__instance.Fogged()?(AcceptanceReport)false:Portal.Cleared(hatch)?(AcceptanceReport)true:new AcceptanceReport("Clear the ancient bunker of threats before claiming its blast doors.");
  }
 }
}

