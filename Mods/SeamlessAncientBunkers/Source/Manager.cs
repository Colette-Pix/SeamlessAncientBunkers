using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
    public enum Purpose { Manual, Food, Rest, Clean, Research, Work, Joy, Medical, Haul, Supply, AnimalFollow, AnimalWander, Drug, Apparel, Thirst, Dining, Gathering, Boarding, FetchBurial, Burial, FetchMedicalSupply, MedicalSupply, FetchPassenger, Passenger, Evacuate, MechCharge, GroupTravel, AnimalCargo, MentalWander }

    // Map references survive map-list changes. Explicit orders also retain their native job payload.
    // A target reference also acts as a temporary soft claim, never a remote reservation.
    public class Intent : IExposable
    {
        public Pawn pawn;
        public MapPortal portal;
        public Map destination;
        public Thing target;
        public Purpose purpose;
        public int created, expires;
        public List<MapPortal> route;
        public int hop;
        public Map finalMap;
        public IntVec3 cell = IntVec3.Invalid;
        public WorkGiverDef giver;
        public JoyGiverDef joyGiver;
        public bool forced, carrying;
        public int count;
        public Thing cargo;
        public Thing deliveryTarget;
        public string needNode;
        public Verse.AI.Job orderedJob;
        public TravelPermission permission;
        public Verse.AI.Group.Lord destinationLord;
        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_References.Look(ref portal, "portal");
            Scribe_References.Look(ref destination, "destination");
            Scribe_References.Look(ref target, "target");
            Scribe_Values.Look(ref purpose, "purpose");
            Scribe_Values.Look(ref created, "created");
            Scribe_Values.Look(ref expires, "expires");
            Scribe_Collections.Look(ref route, "route", LookMode.Reference);
            Scribe_Values.Look(ref hop, "hop");
            Scribe_References.Look(ref finalMap, "finalMap");
            Scribe_Values.Look(ref cell, "cell", IntVec3.Invalid);
            Scribe_Defs.Look(ref giver, "giver");
            Scribe_Defs.Look(ref joyGiver, "joyGiver");
            Scribe_Values.Look(ref forced, "forced");
            Scribe_Values.Look(ref carrying, "carrying");
            Scribe_Values.Look(ref count, "count");
            Scribe_References.Look(ref cargo, "cargo");
            Scribe_References.Look(ref deliveryTarget, "deliveryTarget");
            Scribe_Values.Look(ref needNode,"needNode");
            Scribe_Deep.Look(ref orderedJob,"orderedJob");
            Scribe_Values.Look(ref permission,"travelPermission",TravelPermission.None);
            Scribe_References.Look(ref destinationLord,"destinationLord");
        }
    }

    public class Cooldown : IExposable
    {
        public Pawn pawn;
        public int until;
        public Map failedMap;
        public int failures;
        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref until, "until");
            Scribe_References.Look(ref failedMap, "failedMap");
            Scribe_Values.Look(ref failures, "failures");
        }
    }

    public class Manager : GameComponent
    {
        public static Manager Current => Verse.Current.Game?.GetComponent<Manager>();
        public static JobDef TravelDef => DefDatabase<JobDef>.GetNamed("SAB_Travel");
        public List<MapPortal> enabledPortals = new List<MapPortal>();
        public List<MapPortal> disabledByPlayer = new List<MapPortal>();
        public List<Gathering> gatherings = new List<Gathering>();
        public List<BunkerLevel> levels = new List<BunkerLevel>();
        public List<Intent> intents = new List<Intent>();
        public List<QueuedTransitOrder> queuedOrders = new List<QueuedTransitOrder>();
        public List<Cooldown> cooldowns = new List<Cooldown>();
        // Per-purpose scan throttle is ephemeral; meaningful post-trip cooldowns are saved.
        private readonly Dictionary<string, int> nextScan = new Dictionary<string, int>();
        public readonly HashSet<WorkGiverDef> skippedGivers = new HashSet<WorkGiverDef>();
        public Manager(Game game) { }
        public override void LoadedGame() { EnableClearedBunkers(); BunkerLevels.RegisterExisting(); }
        public static int Now => Find.TickManager.TicksGame;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref enabledPortals, "sabEnabledPortals", LookMode.Reference);
            Scribe_Collections.Look(ref disabledByPlayer, "sabDisabledByPlayer", LookMode.Reference);
            Scribe_Collections.Look(ref gatherings,"sabGatherings",LookMode.Deep);
            Scribe_Collections.Look(ref levels,"sabLevels",LookMode.Deep);
            Scribe_Collections.Look(ref intents, "sabIntents", LookMode.Deep);
            Scribe_Collections.Look(ref queuedOrders,"sabQueuedOrders",LookMode.Deep);
            Scribe_Collections.Look(ref cooldowns, "sabCooldowns", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                enabledPortals = enabledPortals ?? new List<MapPortal>();
                disabledByPlayer = disabledByPlayer ?? new List<MapPortal>();
                gatherings = gatherings ?? new List<Gathering>();
                levels = levels ?? new List<BunkerLevel>();
                intents = intents ?? new List<Intent>();
                queuedOrders = queuedOrders ?? new List<QueuedTransitOrder>();
                cooldowns = cooldowns ?? new List<Cooldown>();
                intents.RemoveAll(i => i == null || i.pawn == null || (i.finalMap == null && i.destination == null));
                cooldowns.RemoveAll(c => c == null || c.pawn == null);
                enabledPortals.RemoveAll(p => p == null || p.Destroyed);
            }
        }
        public bool Enabled(MapPortal p) => Portal.Root(p) is MapPortal root && enabledPortals.Contains(root);
        public void Toggle(MapPortal p)
        {
            var root = Portal.Root(p);
            if (root == null) return;
            if (enabledPortals.Remove(root)) { if(!disabledByPlayer.Contains(root))disabledByPlayer.Add(root); }
            else { disabledByPlayer.Remove(root);enabledPortals.Add(root); }
        }
        public void EnableClearedBunkers()
        {
            if(!BunkerMod.Settings.enabled)return;
            foreach(var root in Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()))
            {
                if(enabledPortals.Contains(root)||disabledByPlayer.Contains(root)||!Portal.Cleared(root))continue;
                enabledPortals.Add(root);
                Messages.Message("Bunker cleared: automatic traffic enabled. Undrafted colonists can now use the hatch.",root,MessageTypeDefOf.PositiveEvent,false);
            }
        }
        public bool CanScan(Pawn p, Purpose purpose, bool urgent = false) => CanScan(p,purpose,urgent,null);
        public bool CanScan(Pawn p, Purpose purpose, bool urgent, string discriminator) => CanScan(p,purpose,urgent,discriminator,false);
        public bool CanScan(Pawn p, Purpose purpose, bool urgent, string discriminator, bool higherPriorityWork)
        {
            if (RemotePawnScope.Active || !BunkerMod.Settings.enabled || !Portal.Eligible(p) || p.CurJobDef == TravelDef ||
                p.CurJob?.playerForced == true || intents.Any(i => i.pawn == p) ||
                (!urgent && cooldowns.Any(c => c.pawn == p && c.until > Now && (!higherPriorityWork || c.failures > 0))) ||
                (p.CurJob != null && !p.jobs.IsCurrentJobPlayerInterruptible())) return false;
            // A successful crossing or recent scan must not make lower-priority local
            // work win. This admission is only for givers ahead of the local result.
            if (higherPriorityWork) return true;
            string key = p.thingIDNumber + ":" + purpose + ":" + urgent + ":" + discriminator;
            if (nextScan.TryGetValue(key, out int next) && next > Now) return false;
            nextScan[key] = Now + (urgent ? 120 : BunkerMod.Settings.scanInterval);
            return true;
        }
        public bool Claimed(Thing target) => intents.Any(i => i.target == target && i.expires > Now);
        public Job Plan(Pawn pawn, MapPortal portal, Purpose purpose, Thing target)
        {
            if (!Portal.CanTravel(pawn, portal, purpose != Purpose.Manual, out Map destination)) return null;
            return Broker.Begin(pawn, new Route { map = destination, portals = new List<MapPortal> { portal } }, purpose, target);
        }
        public void Cool(Pawn pawn)
        {
            var old=cooldowns.FirstOrDefault(c=>c.pawn==pawn);
            cooldowns.RemoveAll(c => c.pawn == pawn);
            cooldowns.Add(new Cooldown { pawn = pawn, until = Now + BunkerMod.Settings.minStay,failures=old?.failures??0,failedMap=old?.failedMap });
        }
        public override void GameComponentTick()
        {
            if (Now % 30 != 0) return;
            if(Now%300==0)EnableClearedBunkers();
            Broker.Tick();
            Gatherings.Tick();
            QueuedTransitOrders.Tick();
            cooldowns.RemoveAll(c => c.pawn == null || c.pawn.Dead || (c.until + 30000 <= Now));
            enabledPortals.RemoveAll(p => p == null || p.Destroyed);
            disabledByPlayer.RemoveAll(p => p == null || p.Destroyed);
            if (nextScan.Count > 4096) nextScan.Clear();
        }
        public void Cleanup()
        {
            BunkerMod.Settings.enabled = false;
            BunkerDoorSecurity.Current?.incoming.Clear();
            foreach(var group in gatherings.ToList())Gatherings.Cancel(group,false);
            Departures.CancelAll();
            Orders.orderingPawn = null;
            enabledPortals.Clear();
            disabledByPlayer.Clear();
            intents.Clear();
            queuedOrders.Clear();
            Verse.Current.Game.GetComponent<DepartureStaging>().requests.Clear();
            cooldowns.Clear();
            nextScan.Clear();
            foreach (var pawn in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.ToList())
                {
                    pawn.jobs?.jobQueue.RemoveAll(pawn, j => j.def == TravelDef || j.def.defName == "SAB_ReleaseAfterTransit" || j.def.defName == "SAB_ExitConnected" || j.def.defName == "SAB_CollectPassenger" || j.def.defName == "SAB_Collect" || j.def.defName == "SAB_Pursue" || j.def.defName == "SAB_Dine");
                    if (pawn.CurJobDef == TravelDef || pawn.CurJobDef?.defName == "SAB_ReleaseAfterTransit" || pawn.CurJobDef?.defName == "SAB_ExitConnected" || pawn.CurJobDef?.defName == "SAB_CollectPassenger" || pawn.CurJobDef?.defName == "SAB_Collect" || pawn.CurJobDef?.defName == "SAB_Pursue" || pawn.CurJobDef?.defName == "SAB_Dine")
                        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: pawn.Spawned);
                }
        }
    }
}


