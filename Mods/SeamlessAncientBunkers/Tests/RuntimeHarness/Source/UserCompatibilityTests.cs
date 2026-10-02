using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;
using SeamlessAncientBunkers;
using UnityEngine;
namespace SABRuntimeTests
{
 public partial class RuntimeTests
 {
  private Building_Storage adaptiveStorage;
  private bool hygieneObserved;
  private void ExposeUserCompatibility(){Scribe_References.Look(ref adaptiveStorage,"adaptiveStorage");Scribe_Values.Look(ref hygieneObserved,"hygieneObserved");}
  private void UserCompatibilityTick()
  {
   if(Manager.Now>deadline)throw new Exception("User compatibility timeout stage="+stage+" job="+pawn.CurJob+" map="+pawn.Map+" intent="+Broker.For(pawn)?.purpose);
   switch(stage)
   {
    case 200:
     pawn.drafter.Drafted=false;ResetPawn(WorkTypeDefOf.Hauling);BunkerMod.Settings.enabled=true;Manager.Current.Toggle(hatch);
     if(pawn.Map!=surface)Travel(surface);Pass("adaptive storage return",201);break;
    case 201:
     if(pawn.Map==surface)
     {
      ResetPawn(WorkTypeDefOf.Hauling);
      foreach(var map in new[]{surface,bunker})
      {
       foreach(var group in map.haulDestinationManager.AllGroupsListInPriorityOrder)group.Settings.filter.SetDisallowAll();
       foreach(var t in map.listerThings.ThingsOfDef(ThingDefOf.Steel).ToList())t.Destroy(DestroyMode.Vanish);
      }
      var def=DefDatabase<ThingDef>.GetNamed("sbz_GravshipCrate");adaptiveStorage=(Building_Storage)Spawn(def.defName,bunker,hatch.exit.Position+new IntVec3(7,0,7),def.MadeFromStuff?ThingDefOf.Steel:null);
      adaptiveStorage.GetStoreSettings().filter.SetDisallowAll();adaptiveStorage.GetStoreSettings().filter.SetAllow(ThingDefOf.Steel,true);adaptiveStorage.GetStoreSettings().Priority=StoragePriority.Critical;
      testTarget=Spawn("Steel",surface,surface.Center+new IntVec3(6,0,6));testTarget.stackCount=25;
      Pass("haul to actual sbz adaptive gravship crate",202);
     }break;
    case 202:
     if(adaptiveStorage.OccupiedRect().Cells.SelectMany(c=>c.GetThingList(bunker)).Any(t=>t.def==ThingDefOf.Steel&&t.stackCount==25))
     {
      Report("PASS actual Adaptive Storage Framework / sbz crate accepts cross-map delivery");ResetPawn();
      var stack=adaptiveStorage.OccupiedRect().Cells.SelectMany(c=>c.GetThingList(bunker)).First(t=>t.def==ThingDefOf.Steel);stack.stackCount=stack.def.stackLimit;bunker.listerMergeables.Notify_ThingStackChanged(stack);
      AccessTools.Property(adaptiveStorage.GetType(),"CurrentSlotLimit").SetValue(adaptiveStorage,1,null);
      var item=Spawn("Steel",surface,surface.Center+new IntVec3(7,0,6));item.stackCount=25;
      if(((ISlotGroupParent)adaptiveStorage).Accepts(item)||Hauling.FindStorage(pawn,item,bunker,StoragePriority.Unstored,out _,out _))throw new Exception("Full adaptive storage offered a destination");
      Report("PASS full adaptive storage capacity is respected");
      Spawn("PitLatrine",bunker,hatch.exit.Position+new IntVec3(-4,0,6),ThingDefOf.WoodLog);
      var bladder=pawn.needs.AllNeeds.First(n=>n.def.defName=="Bladder");bladder.CurLevel=0.05f;
      var route=Graph.Reachable(pawn,false).First(r=>r.map==surface);var pending=Broker.Begin(pawn,route,Purpose.Manual,forced:true);if(pending==null)throw new Exception("Could not prepare interrupted route");JobMaker.ReturnToPool(pending);
      pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);Pass("Dubs hygiene interruption fixture",203);
     }break;
    case 203:
     if(pawn.CurJobDef?.defName=="UseToilet"||pawn.CurJobDef?.defName=="haveWildPoo")
     {
      if(pawn.Map!=bunker||Broker.For(pawn)==null)throw new Exception("Hygiene job lost or prematurely crossed route");
      if(!hygieneObserved){hygieneObserved=true;Report("PASS Dubs local bladder job takes precedence while route remains saved");}
     }
     if(hygieneObserved&&pawn.Map==surface&&pawn.needs.AllNeeds.First(n=>n.def.defName=="Bladder").CurLevel>0.5f)
     {Report("PASS Dubs bladder need completed and suspended route resumed");Manager.Current.Cleanup();Report("USER MOD-LIST COMPATIBILITY RUN COMPLETE");done=true;Application.Quit();}break;
   }
  }
 }
}
