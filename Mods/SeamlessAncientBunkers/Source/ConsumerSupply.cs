using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 // Stock is moved by the existing collect/travel/deliver protocol. A worker already
 // beside an empty consumer first walks to the stock and revalidates the return leg.
 public static class ConsumerSupply
 {
  const string Fetch="SAB.ConsumerFetch", Deliver="SAB.ConsumerSupply";
  public static bool IsFetch(Intent i)=>i?.needNode?.StartsWith(Fetch)==true||ServiceDeliveries.IsFetch(i)||Hauling.IsFetch(i);
  public static bool IsSupply(Intent i)=>IsFetch(i)||i?.needNode?.StartsWith(Deliver)==true||ServiceDeliveries.IsSupply(i);
  public static bool Enabled(Intent i)=>Hauling.IsFetch(i)?Hauling.FetchEnabled(i):ServiceDeliveries.IsSupply(i)?ServiceDeliveries.Enabled(i):i!=null&&
   (i.needNode?.EndsWith(":Haul")==true?BunkerMod.Settings.haul&&Hauling.CanHaul(i.pawn):BunkerMod.Settings.work&&i.giver!=null&&WorkProbe.Allowed(i.pawn,i.giver.Worker));
  public static bool Free(Pawn p,Thing t)
  {
   var holder=ServicesInventory.Holder(t);
   return holder!=null&&holder.Map==p.Map&&!holder.Fogged()&&!holder.IsForbidden(p)&&!holder.IsBurning()&&
    (t==holder||!t.IsForbidden(p))&&Portal.Allowed(p,holder.Map,holder.Position)&&p.CanReserveAndReach(holder,PathEndMode.ClosestTouch,Danger.None);
  }
  public static bool Accessible(Pawn p,Route route,Thing item)
  {using(new RemotePawnScope(p,route.map,route.landing))return Free(p,item);}
  static bool Supported(WorkGiver g)=>g is WorkGiver_DoBill||g is WorkGiver_ConstructDeliverResources||g is WorkGiver_Refuel||
   g is WorkGiver_FixBrokenDownBuilding||g is WorkGiver_FeedPatient||g is WorkGiver_BottleFeedBaby||g is WorkGiver_Warden_Feed||
   g is WorkGiver_Warden_DeliverFood||g is WorkGiver_Warden_DeliverHemogen||g is WorkGiver_FillFermentingBarrel||
   g is WorkGiver_HaulToGrowthVat||g is WorkGiver_HaulToBiosculpterPod||g is WorkGiver_HaulToSubcoreScanner||
   g is WorkGiver_HaulToAtomizer||g is WorkGiver_CreateXenogerm||g is WorkGiver_HaulToGeneBank||g is WorkGiver_Tend||g is WorkGiver_CookFillHopper||
   g.GetType().FullName=="ProcessorFramework.WorkGiver_FillProcessor";
  public static Job ForHauler(Pawn p,List<Route> routes)
  {
   foreach(var def in FairScan.Window(DefDatabase<WorkGiverDef>.AllDefsListForReading.Where(d=>Supported(d.Worker)&&WorkProbe.Supported(d.Worker)),6,FairScan.Key(p,"supplyKinds")))
   {var job=Plan(p,def.Worker,routes,true);if(job!=null)return job;}
   return null;
  }
  public static Job Plan(Pawn p,WorkGiver giver,List<Route> routes,bool hauling=false)
  {
   if(!Supported(giver)||routes.Count==0||p.carryTracker.CarriedThing!=null)return null;
   if(giver is WorkGiver_DoBill&&giver.def.workType==WorkTypeDefOf.Doctor)return null; // Existing surgery protocol owns this.
   var places=new List<Route>{new Route{map=p.Map,landing=p.Position}};places.AddRange(routes);
   int quota=Math.Max(4,BunkerMod.Settings.maxCandidates/places.Count);
   foreach(var destination in places)
   {
    IEnumerable<Thing> targets;
    using(new RemotePawnScope(p,destination.map,destination.landing))
    {
     var scanner=(WorkGiver_Scanner)giver;
     targets=(giver is WorkGiver_HaulToGeneBank?destination.map.listerThings.ThingsInGroup(ThingRequestGroup.GenepackHolder):
      scanner.PotentialWorkThingsGlobal(p)??destination.map.listerThings.ThingsMatching(scanner.PotentialWorkThingRequest)).ToList();
    }
    foreach(var target in FairScan.Window(targets,quota,FairScan.Key(p,"consumer:"+giver.def.defName,destination.map)))
    {
     if(Manager.Current.intents.Any(i=>i.deliveryTarget==target&&i.expires>Manager.Now))continue;
     using(new RemotePawnScope(p,destination.map,destination.landing))if(!Free(p,target))continue;
     foreach(var source in places.Where(s=>s.map!=destination.map))
     foreach(var item in FairScan.Window(ServicesInventory.Items(source.map),quota,
      FairScan.Key(p,"stock:"+giver.def.defName+":"+target.thingIDNumber,source.map)))
     {
      if(!item.def.EverHaulable||item is Pawn||Manager.Current.Claimed(item)||p.carryTracker.MaxStackSpaceEver(item.def)<=0)continue;
      using(new RemotePawnScope(p,source.map,source.landing))if(!Free(p,item))continue;
      int count;IntVec3 cell;
      using(new RemotePawnScope(p,destination.map,destination.landing))
      using(new ProbeAudit(destination.map))
      {
       count=Needed(p,giver,target,item);
       if(count<=0||!StageCell(p,target,item,out cell))continue;
      }
      return Begin(p,giver.def,item,target,count,source,destination,cell,hauling);
     }
    }
   }
   return null;
  }
  static IEnumerable<Thing> Available(Pawn p,Thing target,Predicate<Thing> accepts,float radius=99999f)=>
   ServicesInventory.Items(target.Map).Where(t=>accepts(t)&&
    t.PositionHeld.DistanceToSquared(target.Position)<=radius*radius&&!Manager.Current.Claimed(t)&&Free(p,t)).Concat(
    target.Map.mapPawns.SpawnedPawnsInFaction(p.Faction).Where(carrier=>carrier.CurJobDef==JobDefOf.HaulToCell&&
     carrier.CurJob.targetB.Cell.DistanceToSquared(target.Position)<=Math.Min(radius*radius,25))
     .Select(carrier=>carrier.carryTracker?.CarriedThing).Where(t=>t!=null&&accepts(t))).Distinct();
  static int Missing(Pawn p,Thing target,Thing item,int requested,Predicate<Thing> accepts)=>
   accepts(item)?Math.Max(0,requested-Available(p,target,accepts).Sum(t=>t.stackCount)):0;
  public static int Needed(Pawn p,WorkGiver giver,Thing target,Thing item)
  {
   if(target.Map!=p.Map||!Free(p,target))return 0;
   if(target is Pawn patient)return PawnNeed(p,giver,patient,item);
   if(target.Faction!=p.Faction)return 0;
   if(giver is WorkGiver_CookFillHopper&&target is ISlotGroupParent hopper)
   {
    var group=hopper.GetSlotGroup();var food=target.Position.GetFirstItem(target.Map);
    if(!Building_NutrientPasteDispenser.IsAcceptableFeedstock(item.def)||!group.Settings.AllowedToAccept(item)||
     StoreUtility.CurrentStoragePriorityOf(item)>=group.Settings.Priority||
     food!=null&&(!food.CanStackWith(item)||(float)food.stackCount/food.def.stackLimit>0.35f))return 0;
    return Math.Max(0,item.def.stackLimit-(food?.stackCount??0));
   }
   if(giver is WorkGiver_ConstructDeliverResources&&target is IConstructible build)
    return Missing(p,target,item,build.ThingCountNeeded(item.def),t=>t.def==item.def);
   if(giver is WorkGiver_DoBill&&target is IBillGiver bills)
   {
    int result=0;
    foreach(var bill in bills.BillStack.Bills.OfType<Bill_Production>().Where(b=>b.ShouldDoNow()&&b.PawnAllowedToStartAnew(p)&&b.recipe.FirstSkillRequirementPawnDoesntSatisfy(p)==null))
    foreach(var ingredient in bill.recipe.ingredients)
    {
     if(!ingredient.filter.Allows(item)||!bill.IsFixedOrAllowedIngredient(item))continue;
     float required=ingredient.CountRequiredOfFor(item.def,bill.recipe,bill);if(required<=0)continue;
     var local=Available(p,target,t=>ingredient.filter.Allows(t)&&bill.IsFixedOrAllowedIngredient(t),bill.ingredientSearchRadius);
     var fractions=local.GroupBy(t=>t.def).ToDictionary(g=>g.Key,g=>(float)g.Sum(t=>t.stackCount)/Math.Max(1,ingredient.CountRequiredOfFor(g.Key,bill.recipe,bill)));
     float supplied=bill.recipe.allowMixingIngredients?fractions.Values.Sum():fractions.Values.Any(v=>v>=1)?1:fractions.TryGetValue(item.def,out float n)?n:0;
     result=Math.Max(result,Mathf.CeilToInt(required*Math.Max(0,1-supplied)));
    }
    return result;
   }
   if(giver is WorkGiver_Refuel refuel)
   {
    var fuel=target.TryGetComp<CompRefuelable>();
    var interact=target.TryGetComp<CompInteractable>();
    if(fuel==null||!refuel.CanRefuelThing(target)||fuel.IsFull||!fuel.allowAutoRefuel||!fuel.ShouldAutoRefuelNow||
     fuel.FuelPercentOfMax>0&&!fuel.Props.allowRefuelIfNotEmpty||interact!=null&&interact.Props.cooldownPreventsRefuel&&interact.OnCooldown)return 0;
    return Missing(p,target,item,fuel.GetFuelCountToFullyRefuel(),t=>fuel.Props.fuelFilter.Allows(t));
   }
   if(giver is WorkGiver_FixBrokenDownBuilding)
    return target.IsBrokenDown()&&target.def.building.repairable&&target.Map.areaManager.Home[target.Position]&&
     target.Map.designationManager.DesignationOn(target,DesignationDefOf.Deconstruct)==null?
     Missing(p,target,item,1,t=>t.def==ThingDefOf.ComponentIndustrial):0;
   if(giver is WorkGiver_FillFermentingBarrel&&target is Building_FermentingBarrel barrel)
   {
    var temp=barrel.def.GetCompProperties<CompProperties_TemperatureRuinable>();
    return !barrel.Fermented&&barrel.AmbientTemperature>=temp.minSafeTemperature+2&&barrel.AmbientTemperature<=temp.maxSafeTemperature-2?
     Missing(p,target,item,barrel.SpaceLeftForWort,t=>t.def==ThingDefOf.Wort):0;
   }
   if(giver is WorkGiver_HaulToGrowthVat&&target is Building_GrowthVat vat)
   {
    if(vat.selectedEmbryo==item)return 1;
    return vat.NutritionNeeded>2.5f?Nutrition(p,target,item,vat.NutritionNeeded,t=>vat.CanAcceptNutrition(t)):0;
   }
   if(giver is WorkGiver_HaulToBiosculpterPod)
   {
    var pod=target.TryGetComp<CompBiosculpterPod>();
    return pod!=null&&pod.PowerOn&&pod.State==BiosculpterPodState.LoadingNutrition&&pod.autoLoadNutrition?
     Nutrition(p,target,item,pod.RequiredNutritionRemaining,t=>pod.CanAcceptNutrition(t)):0;
   }
   if(giver is WorkGiver_HaulToAtomizer)
   {
    var atom=target.TryGetComp<CompAtomizer>();
    return atom!=null&&!atom.Full&&atom.AutoLoad&&atom.FillPercent<=0.5f?Missing(p,target,item,atom.SpaceLeft,t=>t.def==atom.Props.thingDef):0;
   }
   if(giver is WorkGiver_HaulToSubcoreScanner&&target is Building_SubcoreScanner sub&&sub.State==SubcoreScannerState.WaitingForIngredients)
    return Missing(p,target,item,sub.GetRequiredCountOf(item.def),t=>t.def==item.def);
   if(giver is WorkGiver_CreateXenogerm&&target is Building_GeneAssembler assembler)
    return Missing(p,target,item,assembler.ArchitesRequiredNow,t=>t.def==ThingDefOf.ArchiteCapsule);
   if(giver is WorkGiver_HaulToGeneBank&&item is Genepack pack)
   {
    var bank=target.TryGetComp<CompGenepackContainer>();
    return pack.AutoLoad&&bank!=null&&!bank.Full&&(pack.Spawned||pack.targetContainer==target)&&
     (pack.targetContainer==target||pack.targetContainer==null&&bank.autoLoad)?1:0;
   }
   return ProcessorSupport.Needed(target,item,d=>Available(p,target,t=>t.def==d).Sum(t=>t.stackCount));
  }
  static int Nutrition(Pawn p,Thing target,Thing item,float requested,Predicate<Thing> accepts)
  {
   float nutrition=item.GetStatValue(StatDefOf.Nutrition);
   return accepts(item)&&nutrition>0?Mathf.CeilToInt(Math.Max(0,requested-Available(p,target,accepts).Sum(t=>t.stackCount*t.GetStatValue(StatDefOf.Nutrition)))/nutrition):0;
  }
  static int PawnNeed(Pawn worker,WorkGiver giver,Pawn patient,Thing item)
  {
   if(patient.Dead||patient==worker)return 0;
   if(giver is WorkGiver_Tend)
   {
    // Only stage medicine for patients actually awaiting treatment; native no-medicine
    // tending retains priority, so fetching never delays an available lifesaving job.
    if(!HealthAIUtility.ShouldBeTendedNowByPlayer(patient)||!item.def.IsMedicine||patient.playerSettings==null||
     !patient.playerSettings.medCare.AllowsMedicine(item.def))return 0;
    return Missing(worker,patient,item,1,t=>t.def.IsMedicine&&patient.playerSettings.medCare.AllowsMedicine(t.def));
   }
   if(giver is WorkGiver_Warden_DeliverHemogen)
   {
    var gene=patient.genes?.GetFirstGeneOfType<Gene_Hemogen>();
    return patient.IsPrisonerOfColony&&patient.guest.CanBeBroughtFood&&gene!=null&&gene.hemogenPacksAllowed&&gene.ShouldConsumeHemogenNow()?
     Missing(worker,patient,item,1,t=>t.def==ThingDefOf.HemogenPack):0;
   }
   if(!item.def.IsNutritionGivingIngestible)return 0;
   if(patient.needs?.food==null||patient.needs.food.CurLevelPercentage>=patient.needs.food.PercentageThreshHungry+0.02f)return 0;
   if(giver is WorkGiver_BottleFeedBaby)
   {
    var can=(bool)AccessTools.Method(typeof(WorkGiver_FeedBabyManually),"CanCreateManualFeedingJob").Invoke(giver,new object[]{worker,patient,false});
    if(!can||!patient.WillEat(item,worker))return 0;
   }
   else if(giver is WorkGiver_Warden)
   {
    if(!(bool)AccessTools.Method(typeof(WorkGiver_Warden),"ShouldTakeCareOfPrisoner").Invoke(giver,new object[]{worker,patient,false}))return 0;
    if(!patient.guest.CanBeBroughtFood)return 0;
   }
   else if(!(giver is WorkGiver_FeedPatient)||patient.DevelopmentalStage.Baby()||!FeedPatientUtility.ShouldBeFed(patient)||
    WardenFeedUtility.ShouldBeFed(patient)||giver.def.feedHumanlikesOnly&&!patient.RaceProps.Humanlike||giver.def.feedAnimalsOnly&&!patient.IsAnimal)return 0;
   Predicate<Thing> accepts=t=>t.def.IsNutritionGivingIngestible&&t.IngestibleNow&&!(t is Corpse)&&patient.WillEat(t,worker);
   return Nutrition(worker,patient,item,Math.Min(patient.needs.food.NutritionWanted,0.9f),accepts);
  }
  static bool StageCell(Pawn p,Thing target,Thing item,out IntVec3 cell)
  {
   if(target.def==ThingDefOf.Hopper)
   {
    cell=target.Position;
    return target is ISlotGroupParent hopper&&hopper.GetSlotGroup().Settings.AllowedToAccept(item)&&
     Portal.Allowed(p,p.Map,cell)&&!Broker.Claimed(p,p.Map,null,cell)&&
     StoreUtility.IsGoodStoreCell(cell,p.Map,item,p,p.Faction)&&p.CanReserveAndReach(cell,PathEndMode.OnCell,Danger.None);
   }
   // Staging beside the consumer keeps bill radius and prison-room constraints useful.
   float radius=3;
   if(target is IBillGiver bills)
   {var ranges=bills.BillStack.Bills.OfType<Bill_Production>().Where(b=>b.ShouldDoNow()&&b.IsFixedOrAllowedIngredient(item)).Select(b=>b.ingredientSearchRadius).ToList();if(ranges.Count>0)radius=Math.Min(radius,ranges.Min());}
   foreach(var c in GenRadial.RadialCellsAround(target.Position,radius,true))
    if(c.InBounds(p.Map)&&c.Standable(p.Map)&&!c.Fogged(p.Map)&&Portal.Allowed(p,p.Map,c)&&
     !Broker.Claimed(p,p.Map,null,c)&&StoreUtility.IsGoodStoreCell(c,p.Map,item,null,p.Faction)&&p.CanReserveAndReach(c,PathEndMode.OnCell,Danger.None))
    {cell=c;return true;}
   cell=IntVec3.Invalid;return false;
  }
  static Job Begin(Pawn p,WorkGiverDef giver,Thing item,Thing target,int count,Route source,Route destination,IntVec3 cell,bool hauling=false)
  {
   bool fetch=item.MapHeld!=p.Map;
   var job=Broker.Begin(p,fetch?source:destination,fetch?Purpose.Work:Purpose.Supply,item,cell,giver,
    count:Math.Min(count,Math.Min(item.stackCount,p.carryTracker.MaxStackSpaceEver(item.def))));
   if(job!=null){var i=Broker.For(p);i.deliveryTarget=target;i.needNode=(fetch?Fetch:Deliver)+(hauling?":Haul":"");}
   return job;
  }
  public static Job Arrive(Intent i)
  {
   if(Hauling.IsFetch(i))return Hauling.Arrive(i);
   if(ServiceDeliveries.IsFetch(i))return ServiceDeliveries.Arrive(i);
   var p=i.pawn;if(!Enabled(i)||i.deliveryTarget?.Spawned!=true||!Free(p,i.target))return null;
   var route=Graph.Reachable(p).FirstOrDefault(r=>r.map==i.deliveryTarget.Map&&Accessible(p,r,i.deliveryTarget));if(route==null)return null;
   int count;IntVec3 cell;
   using(new RemotePawnScope(p,route.map,route.landing))using(new ProbeAudit(route.map))
   {count=Needed(p,i.giver.Worker,i.deliveryTarget,i.target);if(count<=0||!StageCell(p,i.deliveryTarget,i.target,out cell))return null;}
   return Begin(p,i.giver,i.target,i.deliveryTarget,count,null,route,cell,i.needNode.EndsWith(":Haul"));
  }
  public static Job Delivery(Intent i)
  {
   if(ServiceDeliveries.IsSupply(i))return ServiceDeliveries.Delivery(i);
   var p=i.pawn;var cargo=p.carryTracker.CarriedThing;var target=i.deliveryTarget;
   if(!Enabled(i)||target?.Map!=p.Map||cargo==null||!Free(p,target))return null;
   if(i.giver.Worker is WorkGiver_HaulToGeneBank&&cargo is Genepack pack)
   {var bank=target.TryGetComp<CompGenepackContainer>();if(bank==null||bank.Full)return null;pack.targetContainer=target;}
   if(!StageCell(p,target,cargo,out var cell))return null;
   var drop=JobMaker.MakeJob(JobDefOf.HaulToCell,cargo,cell);drop.count=cargo.stackCount;drop.haulMode=HaulMode.ToCellNonStorage;return drop;
  }
 }
}
