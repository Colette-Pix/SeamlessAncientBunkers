using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
 public static class AnimalSupport
 {
  // Roped animals and tree-bound dryads retain their native movement behavior.
  public static bool CanWander(Pawn p)=>p?.RaceProps.Animal==true&&!p.RaceProps.Dryad&&
   (p.Faction==Faction.OfPlayer||p.Faction==null)&&p.roping?.IsRoped!=true;
  public static bool Supported(Pawn p)=>CanWander(p)&&p.Faction==Faction.OfPlayer;
  public static Pawn Followee(Pawn p)
  {
   if(!Supported(p))return null;
   var settings=p.playerSettings;var master=settings?.RespectedMaster;
   if(master?.Spawned!=true||master.Dead)return null;
   return (settings.followDrafted&&master.Drafted)||(settings.followFieldwork&&master.mindState.lastJobTag==JobTag.Fieldwork)?master:null;
  }
  public static bool ValidIntent(Intent i)=>i.purpose!=Purpose.AnimalFollow ||
   (i.target is Pawn master&&Followee(i.pawn)==master&&master.Map==i.finalMap);
  private static bool Usable(Pawn p,Thing t)=>t?.Spawned==true&&t.Map==p.Map&&!t.Fogged()&&!t.IsForbidden(p)&&
   Portal.Allowed(p,p.Map,t.Position)&&p.CanReserveAndReach(t,PathEndMode.Touch,Danger.None);
  public static Job Need(Pawn p,Purpose purpose,Job local,JobGiver_GetFood foodNode)
  {
   if(!Supported(p)||RemotePawnScope.Active||Broker.For(p)!=null||Manager.Current==null)return null;
   if(purpose!=Purpose.Food&&purpose!=Purpose.Rest)return null;
   if(purpose==Purpose.Food&&(!BunkerMod.Settings.food||p.needs?.food==null||p.needs.food.CurLevelPercentage>=p.RaceProps.FoodLevelPercentageWantEat))return null;
   if(purpose==Purpose.Rest&&(!BunkerMod.Settings.rest||local?.def!=JobDefOf.LayDown))return null;
   if(!Manager.Current.CanScan(p,purpose,purpose==Purpose.Food&&p.needs.food.CurCategory>=HungerCategory.UrgentlyHungry))return null;
   // Keep a local assigned bed. Otherwise compare available food/beds with the entire route cost.
   if(purpose==Purpose.Rest&&local.targetA.Thing==p.ownership?.OwnedBed&&local.targetA.Thing!=null)return null;
   float best=local?.targetA.Thing?.Spawned==true?p.Position.DistanceTo(local.targetA.Thing.Position):float.MaxValue;
   Route chosen=null;Thing target=null;
   foreach(var route in Graph.Reachable(p).Where(r=>!Broker.Blacklisted(p,r.map)))
   using(new RemotePawnScope(p,route.map,route.landing))
   using(new ProbeAudit(route.map,false))
   {
    Thing candidate=null;bool owned=false;
    if(purpose==Purpose.Food)
    {
     var job=(Job)AccessTools.Method(typeof(JobGiver_GetFood),"TryGiveJob").Invoke(foodNode??new JobGiver_GetFood(),new object[]{p});
     if(job?.def==JobDefOf.Ingest)candidate=job.targetA.Thing;
     if(job!=null)JobMaker.ReturnToPool(job);
    }
    else
    {
     candidate=RestUtility.FindBedFor(p);
     owned=candidate!=null&&candidate==p.ownership?.OwnedBed;
    }
    if(!Usable(p,candidate)||Broker.Claimed(p,route.map,candidate,IntVec3.Invalid))continue;
    float cost=owned?-1:route.cost+route.landing.DistanceTo(candidate.Position);
    if(cost>=best)continue;
    best=cost;chosen=route;target=candidate;
   }
   return chosen==null?null:Broker.Begin(p,chosen,purpose,target);
  }
  public static Job Idle(Pawn p,Job local)
  {
   if(!CanWander(p)||RemotePawnScope.Active||Manager.Current==null||Broker.For(p)!=null)return null;
   // A handler owns changes of pen; idle wandering must not bypass its enclosure.
   if(AnimalPenUtility.NeedsToBeManagedByRope(p)&&AnimalPenUtility.GetCurrentPenOf(p,false)!=null)return null;
   // Never override feeding, sleeping, fleeing, fighting, trained work or a player order.
   if(local!=null&&local.def!=JobDefOf.GotoWander&&local.def!=JobDefOf.Wait_Wander&&local.def!=JobDefOf.FollowClose)return null;
   var master=Followee(p);
   if(master!=null)
   {
    if(master.Map==p.Map||!Manager.Current.CanScan(p,Purpose.AnimalFollow,true))return null;
    foreach(var route in Graph.Reachable(p).Where(r=>r.map==master.Map&&!Broker.Blacklisted(p,r.map)))
    {
     bool reachable;
     using(new RemotePawnScope(p,route.map,route.landing))
      reachable=Portal.Allowed(p,route.map,master.Position)&&p.CanReach(master,PathEndMode.Touch,Danger.None);
     if(reachable)return Broker.Begin(p,route,Purpose.AnimalFollow,master);
    }
    return null;
   }
   if(local?.def!=JobDefOf.GotoWander||!Manager.Current.CanScan(p,Purpose.AnimalWander))return null;
   // Only an idle animal near an entrance may wander through; saved cooldown prevents bouncing.
   if(!Rand.Chance(0.15f))return null;
   var nearby=Graph.Reachable(p).FirstOrDefault(r=>r.portals.Count==1&&p.Position.DistanceTo(r.portals[0].Position)<=10f&&!Broker.Blacklisted(p,r.map));
   return nearby==null?null:Broker.Begin(p,nearby,Purpose.AnimalWander);
  }
 }
}
