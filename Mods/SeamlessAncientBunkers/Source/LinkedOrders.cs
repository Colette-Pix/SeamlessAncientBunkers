using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
    public static class LinkedOrders
    {
        [ThreadStatic] public static Map OrderMap;
        [ThreadStatic] public static Thing WorkTarget;
        public static bool HaulRequest(Job job) => job?.def.defName == "SAB_Collect";

        public static Route Storage(Pawn pawn, Thing item, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (Manager.Current == null || !BunkerMod.Settings.enabled || !Hauling.CanHaul(pawn) ||
                item?.Spawned != true || item.Map != pawn.Map || !HaulAIUtility.PawnCanAutomaticallyHaul(pawn, item, true)) return null;
            return Hauling.Storage(pawn, item, true, out cell, out _);
        }

        public static Job StartHaul(Pawn pawn, Thing item) => Hauling.StartHaul(pawn, item, true);

        public static bool Issue(Pawn pawn, Job job, Map map, Thing workTarget = null, IntVec3? workCell = null, bool? queue = null)
        {
            if (map == null || !pawn.Spawned || !Find.Maps.Contains(map)) return false;
            bool queueRequested=queue??(UnityEngine.Event.current!=null&&KeyBindingDefOf.QueueOrder.IsDownEvent);
            if(queueRequested&&(Broker.For(pawn)!=null||pawn.CurJob?.def.isIdle==false||Manager.Current.queuedOrders.Any(q=>q.pawn==pawn)))
                return QueuedTransitOrders.Add(pawn,job,map,workTarget,workCell);
            if(!QueuedTransitOrders.Dispatching&&!queueRequested)QueuedTransitOrders.Clear(pawn);
            var passenger=PawnTransit.TryWrap(pawn,job);
            if(passenger!=null)return pawn.jobs.TryTakeOrderedJob(passenger);
            if (map == pawn.Map)
            {
                if (!HaulRequest(job)) return false;
                var hauling = StartHaul(pawn, job.targetA.Thing);
                return hauling != null && pawn.jobs.TryTakeOrderedJob(hauling);
            }
            var route = Graph.Reachable(pawn, false).FirstOrDefault(r =>
            {
                if(r.map!=map)return false;
                using(new RemotePawnScope(pawn,r.map,r.landing))
                    return pawn.CanReach(job.targetA,job.targetA.HasThing?PathEndMode.Touch:PathEndMode.OnCell,Danger.Deadly);
            });
            if (route == null) return false;
            var travel = Broker.Begin(pawn, route, Purpose.Manual, job.targetA.Thing, job.targetA.Cell, forced: true);
            if (travel == null) return false;
            var intent = Broker.For(pawn);
            if (job.workGiverDef != null) { intent.target = workTarget ?? job.targetA.Thing; intent.cell = workCell ?? job.targetA.Cell; }
            job.playerForced = true;
            intent.orderedJob = job;
            if (pawn.jobs.TryTakeOrderedJob(travel))
            {
                OrderFeedback.Accepted(map, intent.cell);
                return true;
            }
            Broker.Cancel(intent);
            return false;
        }

        public static Job Arrive(Intent intent)
        {
            var pawn = intent.pawn;
            var job = intent.orderedJob;
            intent.orderedJob = null;
            foreach (var target in (job.workGiverDef == null ? new[] { job.targetA, job.targetB, job.targetC }
                .Concat(job.targetQueueA ?? new List<LocalTargetInfo>()).Concat(job.targetQueueB ?? new List<LocalTargetInfo>())
                : new[] { intent.target != null ? new LocalTargetInfo(intent.target) : new LocalTargetInfo(intent.cell) }))
            {
                if (target.HasThing && (target.Thing.Destroyed || target.Thing.MapHeld != pawn.Map)) return null;
                if (!target.HasThing && target.IsValid && !target.Cell.InBounds(pawn.Map)) return null;
            }
            if (HaulRequest(job)) return StartHaul(pawn, job.targetA.Thing);
            if (job.workGiverDef?.Worker is WorkGiver_Scanner scanner)
            {
                if (scanner.ShouldSkip(pawn, true)) return null;
                if (intent.target != null)
                {
                    if (intent.target.IsForbidden(pawn) || !scanner.HasJobOnThing(pawn, intent.target, true)) return null;
                    job = scanner.JobOnThing(pawn, intent.target, true);
                }
                else
                {
                    if (intent.cell.IsForbidden(pawn) || !scanner.HasJobOnCell(pawn, intent.cell, true)) return null;
                    job = scanner.JobOnCell(pawn, intent.cell, true);
                }
                if (job == null) return null;
                job.workGiverDef = scanner.def;
                if (HaulRequest(job)) return StartHaul(pawn, job.targetA.Thing);
            }
            job.playerForced = true;
            return job;
        }
    }

    // Only menu queries see a pawn at the destination entrance. All actions run after restoration.
    [HarmonyPatch(typeof(FloatMenuMakerMap), nameof(FloatMenuMakerMap.GetOptions))]
    public static class LinkedOrderMenu
    {
        public sealed class State : IDisposable
        {
            public List<RemotePawnScope> scopes = new List<RemotePawnScope>();
            public void Dispose() { for (int i = scopes.Count - 1; i >= 0; i--) scopes[i].Dispose(); scopes.Clear(); }
        }
        public static void Prefix(List<Pawn> selectedPawns, out State __state)
        {
            __state = new State();
            if (Manager.Current == null || !BunkerMod.Settings.enabled) return;
            foreach (var pawn in selectedPawns)
            {
                if (pawn.Map == Find.CurrentMap) continue;
                var route = Graph.Reachable(pawn, false).FirstOrDefault(r => r.map == Find.CurrentMap);
                if (route != null) __state.scopes.Add(new RemotePawnScope(pawn, route.map, route.landing));
            }
        }
        public static void Postfix(List<FloatMenuOption> __result, FloatMenuContext context, State __state)
        {
            bool remote = __state.scopes.Count > 0;
            __state.Dispose();
            if (!remote) return;
            var map = Find.CurrentMap;
            foreach (var option in __result)
            {
                if (option.action == null) continue;
                if (option.isGoto)
                {
                    var pawns = context.allSelectedPawns.ToList();
                    var cell = context.ClickedCell;
                    option.isGoto = false;
                    option.action = () =>
                    {
                        foreach (var pawn in pawns)
                        {
                            var route = pawn.Map == map ? null : Graph.Reachable(pawn, false).FirstOrDefault(r => r.map == map);
                            if (pawn.Map != map && route == null) continue;
                            IntVec3 destination;
                            using (var scope = route == null ? null : new RemotePawnScope(pawn, map, route.landing))
                            {
                                destination = RCellFinder.BestOrderedGotoDestNear(cell, pawn);
                                if (!pawn.CanReach(destination, PathEndMode.OnCell, Danger.Deadly)) continue;
                            }
                            var move = JobMaker.MakeJob(JobDefOf.Goto, destination);
                            if (pawn.Map == map)
                            {
                                if (pawn.jobs.TryTakeOrderedJob(move)) OrderFeedback.Accepted(map, destination);
                            }
                            else LinkedOrders.Issue(pawn, move, map);
                        }
                    };
                    continue;
                }
                var action = option.action;
                var target = option.iconThing;
                option.isGoto = false; // Avoid the local-map multiselect drag controller.
                option.action = () =>
                {
                    var previous = LinkedOrders.OrderMap;
                    var previousTarget = LinkedOrders.WorkTarget;
                    try { LinkedOrders.OrderMap = map; LinkedOrders.WorkTarget = target; action(); }
                    finally { LinkedOrders.OrderMap = previous; LinkedOrders.WorkTarget = previousTarget; }
                };
            }
        }
        public static void Finalizer(State __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulToStorageJob))]
    public static class LinkedHaulMenu
    {
        public static void Postfix(Pawn p, Thing t, bool forced, ref Job __result)
        {
            if (!forced || LinkedOrders.Storage(p, t, out _) == null) return;
            // A query must not create an intent or reserve anything. Resolve again when ordered.
            if (__result != null) JobMaker.ReturnToPool(__result);
            __result = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("SAB_Collect"), t);
            __result.count = Math.Min(t.stackCount, p.carryTracker.MaxStackSpaceEver(t.def));
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    public static class LinkedOrderedJob
    {
        public static bool Prefix(Pawn ___pawn, Job job, bool requestQueueing, ref bool __result)
        {
            if (Manager.Current == null || !BunkerMod.Settings.enabled || job == null || job.def == Manager.TravelDef || job.def.defName=="SAB_CollectPassenger") return true;
            // A real collect job already has an intent; menu placeholders do not.
            if (LinkedOrders.HaulRequest(job) && Broker.For(___pawn)?.target == job.targetA.Thing && Broker.Transport(Broker.For(___pawn))) return true;
            var map = LinkedOrders.OrderMap ?? job.targetA.Thing?.MapHeld ?? ___pawn.Map;
            bool queued=requestQueueing||(UnityEngine.Event.current!=null&&KeyBindingDefOf.QueueOrder.IsDownEvent);
            if(queued&&(Broker.For(___pawn)!=null||map!=___pawn.Map||Manager.Current.queuedOrders.Any(q=>q.pawn==___pawn)))
            {__result=LinkedOrders.Issue(___pawn,job,map,queue:true);return false;}
            if (map == ___pawn.Map && !LinkedOrders.HaulRequest(job))
            {
                var passenger=PawnTransit.TryWrap(___pawn,job);
                if(passenger!=null){__result=___pawn.jobs.TryTakeOrderedJob(passenger);return false;}
                if(!QueuedTransitOrders.Dispatching)QueuedTransitOrders.Clear(___pawn);
                var old = Broker.For(___pawn);
                if (old != null) Broker.Cancel(old);
                return true;
            }
            __result = LinkedOrders.Issue(___pawn, job, map,queue:queued);
            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJobPrioritizedWork))]
    public static class LinkedPrioritizedJob
    {
        public static bool Prefix(Pawn ___pawn, Job job, WorkGiver giver, IntVec3 cell, ref bool __result)
        {
            var map = LinkedOrders.OrderMap ?? job.targetA.Thing?.MapHeld ?? ___pawn.Map;
            if (map == ___pawn.Map && !LinkedOrders.HaulRequest(job)) return true;
            job.workGiverDef = giver.def;
            LinkedOrders.Issue(___pawn, job, map, LinkedOrders.WorkTarget, cell);
            // Do not install destination coordinates as sustained work on the source map.
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(SelectionDrawer), nameof(SelectionDrawer.DrawSelectionOverlays))]
    public static class LinkedSelectionOverlays
    {
        public static void Postfix() => OrderFeedback.Draw();
        public static List<object> Visible(Selector selector) => selector.SelectedObjects
            .Where(o => !(o is Thing thing) || thing.MapHeld == Find.CurrentMap).ToList();
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(Selector), nameof(Selector.SelectedObjects));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = System.Reflection.Emit.OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(LinkedSelectionOverlays), nameof(Visible));
                }
                yield return instruction;
            }
        }
    }
}
