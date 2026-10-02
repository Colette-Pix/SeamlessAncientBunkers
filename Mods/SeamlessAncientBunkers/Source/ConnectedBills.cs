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
 public static class ConnectedBills
 {
  public static Map GroupMap(ISlotGroup group)=>group==null?null:Find.Maps.FirstOrDefault(m=>m.haulDestinationManager.AllGroups.Contains(group));
  public static int Count(RecipeWorkerCounter counter,Bill_Production bill)
  {
   var def=counter.recipe.products[0].thingDef;var maps=LinkedResources.Maps(bill.Map);var seen=new HashSet<Thing>();int total=0;
   Action<Thing> add=t=>{if(t==null)return;var inner=t.GetInnerIfMinified();if(seen.Add(inner)&&counter.CountValidThing(inner,bill,def))total+=inner.stackCount*(t==inner?1:t.stackCount);};
   var group=bill.GetIncludeSlotGroup();
   if(group!=null){if(maps.Contains(GroupMap(group)))foreach(var t in group.HeldThings)add(t);}
   else foreach(var map in maps)
   {
    foreach(var t in map.listerThings.ThingsOfDef(def))add(t);
    if(def.Minifiable)foreach(var t in map.listerThings.ThingsInGroup(ThingRequestGroup.MinifiedThing))add(t);
    foreach(var source in map.haulDestinationManager.AllHaulSourcesListForReading)foreach(var t in source.GetDirectlyHeldThings())add(t);
    foreach(var p in map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer))add(p.carryTracker?.CarriedThing);
   }
   if(bill.includeEquipped)foreach(var p in maps.SelectMany(m=>m.mapPawns.FreeColonistsSpawned).Distinct())
   {
    if(p.equipment!=null)foreach(var t in p.equipment.AllEquipmentListForReading)add(t);
    if(p.apparel!=null)foreach(var t in p.apparel.WornApparel)add(t);
    if(p.inventory!=null)foreach(var t in p.inventory.innerContainer)add(t);
   }
   return total;
  }
  public static bool Valid(Bill_Production bill,ISlotGroup group)=>group!=null&&LinkedResources.Maps(bill.Map).Any(m=>m.haulDestinationManager.AllGroups.Contains(group));
 }
 [HarmonyPatch(typeof(RecipeWorkerCounter),nameof(RecipeWorkerCounter.CountProducts))]
 public static class ConnectedProductCounts
 {
  public static bool Prefix(RecipeWorkerCounter __instance,Bill_Production bill,ref int __result)
  {
   if(bill.Map==null||LinkedResources.Maps(bill.Map).Count<2||__instance.recipe.products?.Count!=1)return true;
   __result=ConnectedBills.Count(__instance,bill);return false;
  }
 }
 [HarmonyPatch(typeof(Dialog_BillConfig),"FillOutputDropdownOptions")]
 public static class ConnectedBillGroups
 {
  public static void Postfix(Bill_Production ___bill,List<FloatMenuOption> opts,string prefix,Action<ISlotGroup> selected)
  {
   foreach(var map in LinkedResources.Maps(___bill.Map).Where(m=>m!=___bill.Map))
   foreach(var group in map.haulDestinationManager.AllGroups)
   {
    var captured=group;bool compatible=___bill.recipe.WorkerCounter.CanPossiblyStore(___bill,group);
    opts.Add(new FloatMenuOption(string.Format(prefix,SlotGroup.GetGroupLabel(group))+" ("+map.Parent.LabelCap+")",compatible?(Action)(()=>selected(captured)):null));
   }
  }
 }
 [HarmonyPatch(typeof(Bill_Production),"ValidateGroup")]
 public static class ConnectedBillGroupValidation
 {
  public static bool Prefix(Bill_Production __instance,ref ISlotGroup slot)
  {
   if(slot==null||ConnectedBills.GroupMap(slot)==__instance.Map)return true;
   if(!ConnectedBills.Valid(__instance,slot))slot=null;
   return false;
  }
 }
 // The native recipe driver handles creation and all completion effects. Temporarily
 // select its floor-drop path for a remote output group, then queue ordinary hauling.
 [HarmonyPatch(typeof(Toils_Recipe),nameof(Toils_Recipe.FinishRecipeAndStartStoringProduct))]
 public static class ConnectedBillOutput
 {
  [ThreadStatic] static List<Thing> products;
  public static bool Preserve(Thing t)=>products?.Contains(t)==true;
  public static void Capture(List<Thing> list){if(products!=null)products.AddRange(list);}
  public static void Postfix(Toil __result)
  {
   var toil=__result;var original=toil.initAction;
   toil.initAction=()=>
   {
    var bill=toil.actor.CurJob?.bill as Bill_Production;var group=bill?.GetSlotGroup();
    if(group==null||ConnectedBills.GroupMap(group)==toil.actor.Map||!ConnectedBills.Valid(bill,group)){original();return;}
    var previous=products;products=new List<Thing>();bill.SetStoreMode(BillStoreModeDefOf.DropOnFloor);
    try{original();foreach(var t in products.Where(t=>t.Spawned&&!t.Destroyed))ServiceDeliveries.Current.Add(t,t.stackCount,bill:bill);}
    finally{bill.SetStoreMode(BillStoreModeDefOf.SpecificStockpile,group);products=previous;}
   };
  }
 }
 [HarmonyPatch(typeof(RecordsUtility),nameof(RecordsUtility.Notify_BillDone))]
 public static class ConnectedBillProducts
 {public static void Postfix(List<Thing> products)=>ConnectedBillOutput.Capture(products);}
 [HarmonyPatch(typeof(Thing),nameof(Thing.TryAbsorbStack))]
 public static class ConnectedBillKeepProductIdentity
 {public static bool Prefix(Thing other,ref bool __result){if(!ConnectedBillOutput.Preserve(other))return true;__result=false;return false;}}
 [HarmonyPatch(typeof(Thing),nameof(Thing.CanStackWith))]
 public static class ConnectedBillProductPlacement
 {
  public static bool Prefix(Thing __instance,Thing other,ref bool __result)
  {if(__instance==other||!ConnectedBillOutput.Preserve(__instance)&&!ConnectedBillOutput.Preserve(other))return true;__result=false;return false;}
 }
}
