using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SeamlessAncientBunkers
{
    // A shared colony roster for management UI and availability warnings only.
    // MapPawns itself remains physical: AI, spawning, reservations and combat depend on it.
    public static class LinkedPawnRoster
    {
        public static readonly AccessTools.FieldRef<MapPawns, Map> OwnerMap =
            AccessTools.FieldRefAccess<MapPawns, Map>("map");

        private static List<Pawn> Collect(MapPawns local, Func<MapPawns, IEnumerable<Pawn>> query)
        {
            var result = new List<Pawn>();
            var seen = new HashSet<Pawn>();
            foreach (var map in LinkedResources.Maps(OwnerMap(local)))
                foreach (var pawn in query(map.mapPawns))
                    if (seen.Add(pawn)) result.Add(pawn);
            return result;
        }

        public static List<Pawn> FreeColonists(MapPawns local) => Collect(local, p => p.FreeColonists);
        public static List<Pawn> FreeColonistsSpawned(MapPawns local) => Collect(local, p => p.FreeColonistsSpawned);
        public static List<Pawn> ColonyAnimals(MapPawns local) => Collect(local, p => p.ColonyAnimals);
        public static List<Pawn> ColonySubhumansControllable(MapPawns local) => Collect(local, p => p.ColonySubhumansControllable);
        public static List<Pawn> PawnsInFaction(MapPawns local, Faction faction) => Collect(local, p => p.PawnsInFaction(faction));
        // Membership is sufficient for tabs; availability warnings require a usable
        // route. Keep local candidates unchanged so vanilla applies its own filters.
        public static List<Pawn> ReachableWorkers(MapPawns local)
        {
            var map=OwnerMap(local);
            return FreeColonistsSpawned(local).Where(p=>p.Map==map||BunkerMod.Settings.work&&Portal.Eligible(p,true)&&Graph.Reachable(p).Any(r=>r.map==map)).ToList();
        }

        internal static readonly Dictionary<MethodInfo, MethodInfo> Queries = new Dictionary<MethodInfo, MethodInfo>
        {
            { AccessTools.PropertyGetter(typeof(MapPawns), nameof(MapPawns.FreeColonists)), AccessTools.Method(typeof(LinkedPawnRoster), nameof(FreeColonists)) },
            { AccessTools.PropertyGetter(typeof(MapPawns), nameof(MapPawns.FreeColonistsSpawned)), AccessTools.Method(typeof(LinkedPawnRoster), nameof(FreeColonistsSpawned)) },
            { AccessTools.PropertyGetter(typeof(MapPawns), nameof(MapPawns.ColonyAnimals)), AccessTools.Method(typeof(LinkedPawnRoster), nameof(ColonyAnimals)) },
            { AccessTools.PropertyGetter(typeof(MapPawns), nameof(MapPawns.ColonySubhumansControllable)), AccessTools.Method(typeof(LinkedPawnRoster), nameof(ColonySubhumansControllable)) },
            { AccessTools.Method(typeof(MapPawns), nameof(MapPawns.PawnsInFaction)), AccessTools.Method(typeof(LinkedPawnRoster), nameof(PawnsInFaction)) }
        };

        public static IEnumerable<CodeInstruction> ReplaceQueries(IEnumerable<CodeInstruction> instructions, int limit = int.MaxValue)
        {
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (replaced < limit && instruction.operand is MethodInfo method && Queries.TryGetValue(method, out var replacement) && instruction.Calls(method))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced == 0) throw new InvalidOperationException("Expected a local pawn roster query in a linked-colony UI target.");
        }
    }

    [HarmonyPatch]
    public static class LinkedManagementRosters
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var type in new[] { typeof(MainTabWindow_PawnTable), typeof(MainTabWindow_Schedule), typeof(MainTabWindow_Animals), typeof(MainTabWindow_Mechs) })
                yield return AccessTools.DeclaredPropertyGetter(type, "Pawns");
            // Shared assignment contract also covers modded beds/buildings using vanilla queries.
            foreach (var type in GenTypes.AllTypes.Where(t => typeof(CompAssignableToPawn).IsAssignableFrom(t)))
            {
                var getter = AccessTools.DeclaredPropertyGetter(type, nameof(CompAssignableToPawn.AssigningCandidates));
                if (getter == null || getter.IsAbstract) continue;
                var iterator = getter.GetCustomAttribute<IteratorStateMachineAttribute>();
                var target = iterator == null ? getter : AccessTools.Method(iterator.StateMachineType, "MoveNext");
                if (PatchProcessor.GetOriginalInstructions(target).Any(i => i.operand is MethodInfo m && LinkedPawnRoster.Queries.ContainsKey(m)))
                    yield return target;
            }
        }
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => LinkedPawnRoster.ReplaceQueries(instructions);
    }

    [HarmonyPatch]
    public static class LinkedWorkerWarnings
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Designator_Hunt), "CheckHunters");
            yield return AccessTools.Method(typeof(TameUtility), nameof(TameUtility.ShowDesignationWarnings));
            yield return AccessTools.Method(typeof(Command_SetPlantToGrow), "WarnAsAppropriate");
            yield return AccessTools.Method(typeof(Designator_Build), "AnyColonistWithSkill");
            yield return AccessTools.Method(typeof(Designator_Build), nameof(Designator_Build.DrawPanelReadout));
            yield return AccessTools.Method(typeof(HealthCardUtility), nameof(HealthCardUtility.CreateSurgeryBill));
            yield return AccessTools.Method(typeof(Alert_NeedMiner), nameof(Alert_NeedMiner.GetReport));
            yield return AccessTools.Method(typeof(MechanitorUtility), nameof(MechanitorUtility.AnyPlayerMechCanDoWork));
            foreach (var name in new[] { "ColonyHasAnyWardenCapableOfViolence", "ColonyHasAnyWardenCapableOfEnslavement", "ColonyHasAnyWardenOfIdeo" })
                yield return AccessTools.Method(typeof(ITab_Pawn_Visitor), name);
        }
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => LinkedPawnRoster.ReplaceQueries(instructions);
    }

    [HarmonyPatch(typeof(CompAssignableToPawn_Grave), nameof(CompAssignableToPawn_Grave.AssigningCandidates), MethodType.Getter)]
    public static class LinkedGraveCandidates
    {
        public static void Postfix(CompAssignableToPawn_Grave __instance, ref IEnumerable<Pawn> __result)
        {
            if (!__instance.parent.Spawned) return;
            var maps = LinkedResources.Maps(__instance.parent.Map);
            if (maps.Count <= 1) return;
            __result = __result.Concat(maps.Where(m => m != __instance.parent.Map)
                .SelectMany(m => m.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>())
                .Select(c => c.InnerPawn).Where(p => p.IsColonist)).Distinct().ToList();
        }
    }

    [HarmonyPatch(typeof(Alert_NeedDoctor), "Patients", MethodType.Getter)]
    public static class LinkedDoctorWarning
    {
        // Only the doctor search expands; each map still reports its own patients once.
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => ReachableServiceWarning.Replace(instructions,1);
    }

    [HarmonyPatch(typeof(Alert_NeedWarden),nameof(Alert_NeedWarden.GetReport))]
    public static class ReachableServiceWarning
    {
        public static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions,int limit=int.MaxValue)
        {
            int count=0;
            foreach(var code in instructions)
            {
                if(count<limit&&code.operand is MethodInfo method&&LinkedPawnRoster.Queries.ContainsKey(method)&&
                    (method.Name=="get_FreeColonists"||method.Name=="get_FreeColonistsSpawned"))
                {code.opcode=OpCodes.Call;code.operand=AccessTools.Method(typeof(LinkedPawnRoster),nameof(LinkedPawnRoster.ReachableWorkers));count++;}
                yield return code;
            }
        }
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)=>Replace(instructions);
    }

    [HarmonyPatch(typeof(MainTabWindow_PawnTable), nameof(MainTabWindow_PawnTable.DoWindowContents))]
    public static class LinkedRosterRefresh
    {
        private class State { public float next; public Map map; public HashSet<Pawn> pawns = new HashSet<Pawn>(); }
        private static readonly ConditionalWeakTable<MainTabWindow_PawnTable, State> States = new ConditionalWeakTable<MainTabWindow_PawnTable, State>();
        public static void Prefix(MainTabWindow_PawnTable __instance)
        {
            var state = States.GetValue(__instance, _ => new State());
            if (state.map == Find.CurrentMap && Time.realtimeSinceStartup < state.next) return;
            state.next = Time.realtimeSinceStartup + 0.5f;
            var getter = AccessTools.PropertyGetter(__instance.GetType(), "Pawns");
            var pawns = new HashSet<Pawn>((IEnumerable<Pawn>)getter.Invoke(__instance, null));
            if (state.map != Find.CurrentMap || !state.pawns.SetEquals(pawns))
            {
                state.map = Find.CurrentMap;
                state.pawns = pawns;
                __instance.Notify_PawnsChanged();
            }
        }
    }

    public static class LinkedAreaControls
    {
        private static readonly AccessTools.FieldRef<Pawn_PlayerSettings, Pawn> Owner = AccessTools.FieldRefAccess<Pawn_PlayerSettings, Pawn>("pawn");
        private static readonly AccessTools.FieldRef<Pawn_PlayerSettings, Dictionary<Map, Area>> Areas = AccessTools.FieldRefAccess<Pawn_PlayerSettings, Dictionary<Map, Area>>("allowedAreas");
        private static Map ControlMap(Pawn_PlayerSettings settings)
        {
            var pawn = Owner(settings);
            var origin = pawn.MapHeld ?? RobotSupport.Station(pawn)?.Map;
            var viewed = Find.CurrentMap;
            if (viewed == null) return pawn.MapHeld;
            return origin != null && (origin == viewed || LinkedResources.Maps(origin).Contains(viewed)) ? viewed : null;
        }
        public static Area Get(Pawn_PlayerSettings settings)
        {
            var map = ControlMap(settings);
            return map != null && Areas(settings).TryGetValue(map, out var area) ? area : null;
        }
        public static void Set(Pawn_PlayerSettings settings, Area area)
        {
            var map = ControlMap(settings);
            // A stale row after disconnect must never put another level's area on this pawn.
            if (map == null) return;
            if (map == Owner(settings).MapHeld) settings.AreaRestrictionInPawnCurrentMap = area;
            else Areas(settings)[map] = area;
        }
    }

    [HarmonyPatch]
    public static class LinkedAreaControlsPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(AreaAllowedGUI), "DoAreaSelector");
            yield return AccessTools.Method(typeof(PawnColumnWorker_AllowedArea), "HeaderClicked");
            yield return AccessTools.Method(typeof(PawnColumnWorker_AllowedArea), "GetValueToCompare");
        }
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var get = AccessTools.PropertyGetter(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.AreaRestrictionInPawnCurrentMap));
            var set = AccessTools.PropertySetter(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.AreaRestrictionInPawnCurrentMap));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(get) || instruction.Calls(set))
                {
                    instruction.operand = AccessTools.Method(typeof(LinkedAreaControls), instruction.Calls(get) ? nameof(LinkedAreaControls.Get) : nameof(LinkedAreaControls.Set));
                    instruction.opcode = OpCodes.Call;
                }
                yield return instruction;
            }
        }
    }

    [HarmonyPatch]
    public static class LinkedRobotRoster
    {
        private static readonly Type TabType = AccessTools.TypeByName("AIRobot.X2_MainTabWindow_Robots");
        private static readonly Type StationType = AccessTools.TypeByName("AIRobot.X2_Building_AIRobotRechargeStation");
        public static bool Prepare() => TabType != null && StationType != null;
        public static MethodBase TargetMethod() => AccessTools.PropertyGetter(TabType, "Pawns");
        public static void Postfix(ref IEnumerable<Pawn> __result)
        {
            var maps = LinkedResources.Maps(Find.CurrentMap);
            if (maps.Count <= 1) return;
            var pawns = new HashSet<Pawn>(__result ?? Enumerable.Empty<Pawn>());
            var robot = AccessTools.PropertyGetter(StationType, "GetRobot");
            foreach (var map in maps)
            {
                foreach (var building in map.listerBuildings.allBuildingsColonist)
                    if (StationType.IsInstanceOfType(building) && robot.Invoke(building, null) is Pawn pawn) pawns.Add(pawn);
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                    if (RobotSupport.IsRobot(pawn)) pawns.Add(pawn);
            }
            __result = pawns.OrderBy(p => p.LabelShort).ToList();
        }
    }

    [HarmonyPatch]
    public static class LinkedRobotShutdownAll
    {
        private static readonly Type WorkerType = AccessTools.TypeByName("AIRobot.X2_PawnColumnWorker_ShutDownAll");
        public static bool Prepare() => WorkerType != null;
        public static MethodBase TargetMethod() => AccessTools.Method(WorkerType, "DoCell");
        public static List<Thing> ColonyThings(ListerThings local)
        {
            var maps = LinkedResources.Maps(Find.CurrentMap);
            return maps.Count <= 1 ? local.AllThings : maps.SelectMany(m => m.listerThings.AllThings).ToList();
        }
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(ListerThings), nameof(ListerThings.AllThings));
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(LinkedRobotShutdownAll), nameof(ColonyThings));
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Expected one robot shutdown-all station query.");
        }
    }

    [HarmonyPatch]
    public static class LinkedBedAvailability
    {
        public static List<Building> Buildings(ListerBuildings local)
        {
            var owner = Find.Maps.FirstOrDefault(m => m.listerBuildings == local);
            return owner == null ? local.allBuildingsColonist : LinkedResources.Maps(owner)
                .SelectMany(m => m.listerBuildings.allBuildingsColonist).ToList();
        }
        // Vanilla pairs partners on the same map when budgeting double beds.
        public static Map ColonyMap(Thing pawn) => LinkedResources.Maps(pawn.Map).OrderBy(m => m.uniqueID).FirstOrDefault();
        public static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Alert_NeedColonistBeds), nameof(Alert_NeedColonistBeds.AvailableColonistBeds));
            yield return AccessTools.Method(typeof(Alert_NeedSlaveBeds), nameof(Alert_NeedSlaveBeds.CheckSlaveBeds));
            // Vanilla Gravship Expanded bundles BunkBeds, whose prefix replaces the
            // entire vanilla calculation. Extend its queries too, retaining its
            // separate sleeping slots for bunk beds instead of treating them as doubles.
            var bunkBeds = AccessTools.TypeByName("BunkBeds.Alert_NeedColonistBeds_AvailableColonistBeds_Patch");
            var replacement = bunkBeds == null ? null : AccessTools.Method(bunkBeds, "Prefix");
            if (replacement != null) yield return replacement;
        }
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var buildings = AccessTools.Field(typeof(ListerBuildings), nameof(ListerBuildings.allBuildingsColonist));
            var map = AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Map));
            foreach (var instruction in LinkedPawnRoster.ReplaceQueries(instructions))
            {
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, buildings))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(LinkedBedAvailability), nameof(Buildings));
                }
                else if (instruction.Calls(map))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(LinkedBedAvailability), nameof(ColonyMap));
                }
                yield return instruction;
            }
        }
    }
}
