using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace SeamlessAncientBunkers
{
 public class Route
 {
  public Map map;
  public IntVec3 landing;
  public float cost;
  public List<MapPortal> portals = new List<MapPortal>();
 }
 public static class Graph
 {
  public static List<Route> Reachable(Pawn pawn, bool automatic=true)
  {
   var result=new List<Route>();
   if(!Portal.Eligible(pawn,true,!automatic)) return result;
   var roots=automatic ? Manager.Current.enabledPortals.ToList() : Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).Cast<MapPortal>().ToList();
   var open=new List<Route>{new Route{map=pawn.Map,landing=pawn.Position}};
   var best=new Dictionary<MapPortal,float>();
   while(open.Count>0)
   {
    var state=open.OrderBy(r=>r.cost).First(); open.Remove(state);
    if(state.portals.Count>0) result.Add(state);
    // Routes never repeat a portal pair; the loaded map count bounds useful depth.
    if(state.portals.Count>=Find.Maps.Count) continue;
    foreach(var root in roots)
    {
     if(root?.Spawned!=true) continue;
     MapPortal portal=root.Map==state.map?root:root.exit;
     if(portal?.Map!=state.map || state.portals.Contains(portal) || state.portals.Contains(Portal.Other(portal))) continue;
     using(new RemotePawnScope(pawn,state.map,state.landing))
     {
      if(!Portal.CanTravel(pawn,portal,automatic,out var dest) || !Portal.Landing(pawn,Portal.Other(portal),out var landing)) continue;
      float cost=state.cost+state.landing.DistanceTo(portal.Position)+8f+Manager.Current.intents.Count(i=>i.portal==portal)*6;
      if(best.TryGetValue(portal,out float known)&&known<=cost) continue;
      best[portal]=cost;
      var route=new Route{map=dest,landing=landing,cost=cost,portals=new List<MapPortal>(state.portals)};
      route.portals.Add(portal); open.Add(route);
     }
    }
   }
   // Different entrances can lead into disconnected regions of the same map.
   // Keep their distinct arrival points so target queries can try each one.
   return result.Where(r=>r.map!=pawn.Map).GroupBy(r=>r.portals.Last()).Select(g=>g.OrderBy(r=>r.cost).First()).OrderBy(r=>r.cost).ToList();
  }
 }
}
