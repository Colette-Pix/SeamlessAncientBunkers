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
 public static class ConnectedTrade
 {
  public static IEnumerable<Thing> Ground(Pawn trader,Pawn negotiator,IEnumerable<Thing> original)
  {
   var seen=new HashSet<Thing>();foreach(var t in original)if(seen.Add(t))yield return t;
   if(negotiator?.Spawned!=true||trader?.Map!=negotiator.Map)yield break;
   foreach(var route in Graph.Reachable(negotiator))
   {
    var available=new List<Thing>();
    using(new RemotePawnScope(negotiator,route.map,route.landing))
    {
     foreach(var t in ServicesInventory.Items(route.map))
     {
      var holder=ServicesInventory.Holder(t);
      if(holder==null||!(route.map.areaManager.Home[holder.Position]||t.IsInAnyStorage()||holder!=t)||
       !TradeUtility.PlayerSellableNow(t,trader)||!ConsumerSupply.Free(negotiator,t))continue;
      available.Add(t);
     }
     if(trader.GetLord()!=null)foreach(var p in TradeUtility.AllSellableColonyPawns(route.map))
      if(!p.Downed&&TradeUtility.PlayerSellableNow(p,trader)&&ConsumerSupply.Free(negotiator,p))available.Add(p);
    }
    foreach(var t in available)if(seen.Add(t))yield return t;
   }
  }
 }
 [HarmonyPatch(typeof(Pawn_TraderTracker),nameof(Pawn_TraderTracker.ColonyThingsWillingToBuy))]
 public static class ConnectedGroundTrade
 {public static void Postfix(Pawn ___pawn,Pawn playerNegotiator,ref IEnumerable<Thing> __result){if(BunkerMod.Settings.enabled)__result=ConnectedTrade.Ground(___pawn,playerNegotiator,__result);}}
 // TradeDeal retains its native validation and transaction. Item/silver splitting,
 // ownership, payment and purchase placement already work with remote source stacks.
 [HarmonyPatch(typeof(Pawn_TraderTracker),nameof(Pawn_TraderTracker.GiveSoldThingToTrader))]
 public static class SoldStockDeliveryCleanup
 {
  public static void Prefix(Thing toGive,int countToGive)
  {
   // This hook runs only after native transaction validation. Fully sold stock must
   // no longer be kept distinct or requested by an unrelated pending output delivery.
   if(countToGive<toGive.stackCount||ServiceDeliveries.Current==null)return;
   foreach(var r in ServiceDeliveries.Current.requests.Where(r=>r.item==toGive||r.remainingSources.Contains(toGive)).ToList())
    ServiceDeliveries.Current.Release(r.id);
  }
 }
 [HarmonyPatch(typeof(Pawn_TraderTracker),"AddPawnToStock")]
 public static class ConnectedSoldPawn
 {
  public static void Prefix(Pawn ___pawn,Pawn newPawn)
  {
   // Native PreTraded has already dropped possessions and updated ownership on the
   // source floor. Native AddPawnToStock spawns an unspawned pawn beside the trader
   // and assigns the trader's faction/lord, avoiding a cross-map lord membership.
   if(BunkerMod.Settings.enabled&&newPawn.Spawned&&___pawn.Spawned&&newPawn.Map!=___pawn.Map&&
    ___pawn.GetLord()!=null&&LinkedResources.Maps(___pawn.Map).Contains(newPawn.Map))newPawn.DeSpawn();
  }
 }
 [HarmonyPatch(typeof(TradeUtility),nameof(TradeUtility.AllLaunchableThingsForTrade))]
 public static class ConnectedOrbitalTrade
 {
  [ThreadStatic] static bool local;
  public static void Postfix(Map map,ITrader trader,ref IEnumerable<Thing> __result)
  {
   if(local||!BunkerMod.Settings.enabled)return;
   var maps=LinkedResources.Maps(map);if(maps.Count<2)return;
   var result=__result.ToList();
   try
   {
    local=true;
    // Each floor must provide its own powered beacon and native coverage. The ship
    // still owns the purchase drop map; there is no coordinate or coverage sharing.
    foreach(var other in maps.Where(m=>m!=map))result.AddRange(TradeUtility.AllLaunchableThingsForTrade(other,trader));
    // Check contained genepacks themselves. Some vanilla container paths test the
    // bank's building def rather than the contained item's tradeability.
    if(ModsConfig.BiotechActive)foreach(var linked in maps)
    foreach(var beacon in Building_OrbitalTradeBeacon.AllPowered(linked))
    foreach(var c in beacon.TradeableCells)
    foreach(var bank in c.GetThingList(linked).Select(t=>t.TryGetComp<CompGenepackContainer>()).Where(b=>b!=null))
     result.AddRange(bank.ContainedGenepacks.Where(pack=>TradeUtility.PlayerSellableNow(pack,trader)));
   }
   finally{local=false;}
   __result=result.Distinct().ToList();
  }
 }
 [HarmonyPatch(typeof(TradeShip),nameof(TradeShip.ColonyThingsWillingToBuy))]
 public static class ConnectedOrbitalPawns
 {
  public static void Postfix(TradeShip __instance,ref IEnumerable<Thing> __result)
  {
   if(!BunkerMod.Settings.enabled)return;
   var extra=new List<Thing>();
   foreach(var map in LinkedResources.Maps(__instance.Map).Where(m=>m!=__instance.Map))
   {
    var cells=new HashSet<IntVec3>(Building_OrbitalTradeBeacon.AllPowered(map).SelectMany(b=>b.TradeableCells.ToList()));
    extra.AddRange(TradeUtility.AllSellableColonyPawns(map,false).Where(p=>cells.Contains(p.Position)&&!p.IsForbidden(Faction.OfPlayer)&&TradeUtility.PlayerSellableNow(p,__instance)));
   }
   __result=__result.Concat(extra).Distinct();
  }
 }
}
