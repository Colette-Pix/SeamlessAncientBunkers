using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SeamlessAncientBunkers
{
 public static class ServicesInventory
 {
  public static Thing Holder(Thing t)
  {
   if(t?.Spawned==true)return t;
   var parent=t?.SpawnedParentOrMe;
   if(parent==null||!parent.Spawned)return null;
   if(parent is IHaulSource source&&source.GetDirectlyHeldThings().Contains(t))return parent;
   if(parent.TryGetComp<CompGenepackContainer>()?.SearchableContents.Contains(t)==true)return parent;
   return null;
  }
  public static IEnumerable<Thing> Items(Map map)
  {
   var seen=new HashSet<Thing>();
   foreach(var t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))if(seen.Add(t))yield return t;
   foreach(var source in map.haulDestinationManager.AllHaulSourcesListForReading)
    foreach(var t in source.GetDirectlyHeldThings())if(seen.Add(t))yield return t;
   if(ModsConfig.BiotechActive)foreach(var bank in map.listerBuildings.AllBuildingsColonistOfDef(ThingDefOf.GeneBank))
   {var comp=bank.TryGetComp<CompGenepackContainer>();if(comp!=null)foreach(var t in comp.SearchableContents)if(seen.Add(t))yield return t;}
  }
 }
 // Native haul-source scans can include a pack already inside a bank. Its physical
 // extraction is handled separately; the native insertion query requires a map.
 [HarmonyPatch(typeof(WorkGiver_HaulToGeneBank),"FindGeneBank")]
 public static class StoredGenepackQuery
 {
  public static bool Prefix(Thing genepackThing,ref Thing __result)
  {if(genepackThing?.Spawned==true)return true;__result=null;return false;}
 }
}
