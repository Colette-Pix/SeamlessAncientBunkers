using System;
using System.Linq;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
namespace SABSaveProbe
{
 public class SaveProbe:GameComponent
 {
  bool inspected;int start;AncientHatch hatch;Pawn[] pawns;
  public SaveProbe(Game g){}
  void Report(string s)=>Log.Message("[SAB SAVE PROBE] "+s);
  public override void GameComponentUpdate()
  {
   if(!GenCommandLine.CommandLineArgPassed("sab-save-probe")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting)return;
   Application.runInBackground=true;
   foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);
   try
   {
    if(!inspected)
    {
     inspected=true;hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);pawns=hatch.PocketMap.mapPawns.FreeColonistsSpawned.ToArray();
     Report("Loaded: globalEnabled="+BunkerMod.Settings.enabled+" hatchEnabled="+Manager.Current.Enabled(hatch)+" colonists="+pawns.Length);
     foreach(var m in new[]{hatch.Map,hatch.PocketMap})
     {Report("Map "+m+" safe="+Portal.Safe(m));foreach(var t in m.attackTargetsCache.TargetsHostileToFaction(Faction.OfPlayer))Report("Threat candidate="+t.Thing+" active="+GenHostility.IsActiveThreatTo(t,Faction.OfPlayer)+" pos="+t.Thing.Position);}
     foreach(var p in pawns)Inspect(p);
     BunkerMod.Settings.enabled=true;BunkerMod.Settings.debug=true;Report("Cleared detection="+Portal.Cleared(hatch));
     foreach(var p in pawns){p.drafter.Drafted=false;p.jobs.StopAll();Inspect(p);}
     start=Manager.Now;Report("Undrafted only in disposable copy; observing automatic activation and return");
    }
    Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
    if(Manager.Now-start>=12000){foreach(var p in pawns)Inspect(p);Report("OBSERVATION COMPLETE enabled="+Manager.Current.Enabled(hatch)+" surfacePawns="+pawns.Count(p=>p.Map==hatch.Map));Application.Quit();}
   }catch(Exception e){Report("FAIL "+e);Application.Quit();}
  }
  void Inspect(Pawn p)
  {
   var portal=p.Map==hatch.Map?(MapPortal)hatch:hatch.exit;var other=Portal.Other(portal);
   Report(p.LabelShort+" id="+p.ThingID+" map="+p.Map+" pos="+p.Position+" draft="+p.Drafted+" eligible="+Portal.Eligible(p)+" downed="+p.Downed+" mental="+p.InMentalState+" lodger="+p.IsQuestLodger()+" medicalRest="+HealthAIUtility.ShouldSeekMedicalRest(p)+" lord="+p.GetLord()+" duty="+p.mindState.duty+" job="+p.CurJob+" food="+p.needs.food?.CurLevelPercentage+" rest="+p.needs.rest?.CurLevelPercentage+" bed="+p.ownership?.OwnedBed+" bedMap="+p.ownership?.OwnedBed?.Map+" canTravel="+Portal.CanTravel(p,portal,true,out _)+" manual="+Portal.CanTravel(p,portal,false,out _)+" land="+Portal.Landing(p,other,out _)+" sourceReach="+p.CanReach(portal,PathEndMode.Touch,Danger.None)+" fog="+portal.Fogged()+"/"+other.Fogged()+" enterable="+portal.IsEnterable(out _)+"/"+other.IsEnterable(out _)+" autodraft="+portal.AutoDraftOnEnter+" allowed="+Portal.Allowed(p,portal.Map,portal.Position)+"/"+Portal.Allowed(p,other.Map,other.Position)+" intent="+Broker.For(p)?.purpose);
  }
 }
}
