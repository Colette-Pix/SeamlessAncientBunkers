using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SeamlessAncientBunkers
{
    public static class Portal
    {
        private static readonly AccessTools.FieldRef<Pawn_PlayerSettings, Dictionary<Map, Area>> Areas =
            AccessTools.FieldRefAccess<Pawn_PlayerSettings, Dictionary<Map, Area>>("allowedAreas");
        public static bool Allowed(Pawn pawn, Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return false;
            var settings = pawn.playerSettings;
            return settings == null || !settings.RespectsAllowedArea ||
                !Areas(settings).TryGetValue(map, out Area area) || area == null || area[cell];
        }
        public static MapPortal Root(MapPortal p) => p is AncientHatch ? p : (p as PocketMapExit)?.entrance is AncientHatch h ? h : null;
        public static MapPortal Other(MapPortal p)
        {
            var root = Root(p);
            if (root == null || !root.Spawned || root.PocketMap == null || root.exit?.Spawned != true ||
                root.exit.entrance != root || root.exit.Map != root.PocketMap) return null;
            return p == root ? root.exit : p == root.exit ? root : null;
        }
        public static bool Eligible(Pawn p, bool allowCarry=false, bool allowDrafted=false) => p != null && p.Spawned &&
            (((p.IsFreeNonSlaveColonist || p.IsSlaveOfColony) && (p.DevelopmentalStage == DevelopmentalStage.Adult || p.DevelopmentalStage == DevelopmentalStage.Child)) || RobotSupport.IsRobot(p) || TransitPolicy.Mech(p) || AnimalSupport.CanWander(p) || TransitPolicy.Has(p,TravelPermission.Group)) && !p.Dead && !p.Downed && (allowDrafted || !p.Drafted) &&
            (!p.InMentalState || TransitPolicy.Has(p,TravelPermission.Mental)) && ((p.GetLord() == null && p.mindState?.duty == null) || TransitPolicy.Has(p,TravelPermission.Group)) &&
            (allowCarry || p.carryTracker?.CarriedThing == null);

        public static bool Safe(Map map) => map != null && Find.Maps.Contains(map) &&
            !GenHostility.AnyHostileActiveThreatTo(map, Faction.OfPlayer);

        public static bool Cleared(MapPortal root)
        {
            var other=Other(root);
            if(other==null||root.LoadInProgress||other.LoadInProgress||root.Fogged()||other.Fogged()||
                !root.IsEnterable(out _)||!other.IsEnterable(out _))return false;
            // Inspect the bunker itself, including sleeping/fogged defenders; corpses do not block it.
            var map=root.PocketMap;
            return !GenHostility.AnyHostileActiveThreatTo(map,Faction.OfPlayer,true,true)&&
                !map.attackTargetsCache.TargetsHostileToFaction(Faction.OfPlayer).Any(t=>
                    GenHostility.IsActiveThreatTo(t,Faction.OfPlayer,false,true)||
                    (t.Thing is Pawn p&&!p.Dead&&!p.Downed&&!p.IsPrisoner));
        }

        public static bool Landing(Pawn pawn, MapPortal other, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (other?.Spawned != true) return false;
            using (new RemotePawnScope(pawn, other.Map, other.Position))
            {
            // AncientHatch itself is IMPASSABLE. Use a safe reachable cell near its footprint.
            foreach (var candidate in GenRadial.RadialCellsAround(other.Position, 5f, true))
            {
                if (!candidate.InBounds(other.Map) || !candidate.Standable(other.Map) || candidate.Fogged(other.Map) ||
                    !Allowed(pawn, other.Map, candidate) || candidate.GetDangerFor(pawn, other.Map) != Danger.None ||
                    candidate.ContainsStaticFire(other.Map) ||
                    (TransitPolicy.Has(pawn,TravelPermission.Emergency) && !TransitPolicy.ClearOfEnemies(pawn,other.Map,candidate)) ||
                    (pawn.HarmedByVacuum && candidate.GetVacuum(other.Map) >= 0.5f)) continue;
                if (!other.Map.reachability.CanReach(candidate, other, PathEndMode.Touch, TraverseParms.For(pawn, Danger.None))) continue;
                cell = candidate;
                return true;
            }
            return false;
            }
        }

        public static bool CanTravel(Pawn pawn, MapPortal portal, bool automatic, out Map destination)
            => BlockReason(pawn,portal,automatic,out destination)==null;
        public static string BlockReason(Pawn pawn, MapPortal portal, bool automatic, out Map destination)
        {
            destination = null;
            var other = Other(portal);
            if(automatic&&pawn?.Drafted==true)return "Drafted: undraft to use automatic traffic.";
            if(!Eligible(pawn,Broker.Transport(Broker.For(pawn)),!automatic))return "Pawn unavailable: check health, mental state, carried items or assigned duty.";
            if(portal?.Spawned!=true||pawn.Map!=portal.Map||other==null)return "No opened, loaded connection.";
            if(portal.LoadInProgress||other.LoadInProgress)return "Entrance is busy loading.";
            if(automatic&&portal.AutoDraftOnEnter)return "Entrance requires drafted entry.";
            if(!portal.IsEnterable(out var reason))return "Entrance blocked: "+reason;
            if(!other.IsEnterable(out reason))return "Other entrance blocked: "+reason;
            if(portal.IsForbidden(pawn.Faction)||portal.IsForbidden(pawn)||other.IsForbidden(pawn.Faction)||
                (pawn.RaceProps.Animal&&(portal.IsForbidden(Faction.OfPlayer)||other.IsForbidden(Faction.OfPlayer))))return "An entrance is forbidden.";
            if(portal.Fogged()||other.Fogged())return "An entrance has not been revealed.";
            if(automatic&&!BunkerMod.Settings.enabled)return "Automatic traffic disabled in mod settings.";
            if(automatic&&Manager.Current?.Enabled(portal)!=true)return "Hatch traffic off: enable it here, or clear the bunker for automatic activation.";
            if(automatic&&!Safe(portal.Map)&&!TransitPolicy.Has(pawn,TravelPermission.Emergency))return "Active hostile threat on this map.";
            if(automatic&&!Safe(other.Map)&&!TransitPolicy.Has(pawn,TravelPermission.Emergency))return "Active hostile threat on the destination map.";
            if(TransitPolicy.Has(pawn,TravelPermission.Emergency)&&(!TransitPolicy.ClearOfEnemies(pawn,portal.Map,portal.Position)||!TransitPolicy.ClearOfEnemies(pawn,other.Map,other.Position)))return "Hostiles are too close to the emergency route.";
            if(!Allowed(pawn,portal.Map,portal.Position)||!Allowed(pawn,other.Map,other.Position))return "Allowed area excludes an entrance.";
            if(!Landing(pawn,other,out _))return "No safe, allowed landing beside the other entrance.";
            if(!pawn.CanReach(portal,PathEndMode.Touch,Danger.None))return "No safe path to this entrance.";
            destination = other.Map;
            return null;
        }
        public static IEnumerable<MapPortal> Links(Pawn pawn)
        {
            // Only enabled references are inspected. GetOtherMap is never used for discovery.
            foreach (var root in Manager.Current.enabledPortals)
            {
                if (root == null) continue;
                MapPortal candidate = root.Map == pawn.Map ? root : root.exit;
                if (candidate?.Map == pawn.Map && CanTravel(pawn, candidate, true, out _)) yield return candidate;
            }
        }
        public static bool ReachTarget(Pawn pawn, MapPortal from, Thing target, PathEndMode mode)
        {
            var other = Other(from);
            if (other == null || target?.Spawned != true || target.Map != other.Map || target.Fogged() || target.IsForbidden(pawn.Faction) ||
                !Allowed(pawn, target.Map, target.Position) || target.Position.GetDangerFor(pawn, target.Map) != Danger.None || target.IsBurning()) return false;
            if (target.Map.reservationManager.IsReserved(target)) return false;
            if (!Landing(pawn, other, out IntVec3 start)) return false;
            using (new RemotePawnScope(pawn, other.Map, start))
                return target.Map.reachability.CanReach(start, target, mode, TraverseParms.For(pawn, Danger.None));
        }
    }
}
