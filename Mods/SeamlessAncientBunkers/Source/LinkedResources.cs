using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SeamlessAncientBunkers
{
    public static class LinkedResources
    {
        private static readonly AccessTools.FieldRef<ResourceCounter, Dictionary<ThingDef, int>> LocalAmounts =
            AccessTools.FieldRefAccess<ResourceCounter, Dictionary<ThingDef, int>>("countedAmounts");

        // Walk actual reciprocal, loaded bunker portals, never tiles, level labels or all colonies.
        // No cache: removing an exit or unloading a map must split the group immediately.
        public static List<Map> Maps(Map origin)
        {
            var maps = new List<Map>();
            if (origin == null) return maps;
            maps.Add(origin);
            if (BunkerMod.Settings?.enabled != true || !Find.Maps.Contains(origin)) return maps;
            var seen = new HashSet<Map> { origin };
            for (int i = 0; i < maps.Count; i++)
            foreach (var thing in maps[i].listerThings.ThingsInGroup(ThingRequestGroup.MapPortal))
            {
                var portal = thing as MapPortal;
                var other = Portal.Other(portal);
                if (other == null || !Find.Maps.Contains(other.Map) || portal.LoadInProgress || other.LoadInProgress ||
                    portal.Fogged() || other.Fogged() || !portal.IsEnterable(out _) || !other.IsEnterable(out _)) continue;
                if (seen.Add(other.Map)) maps.Add(other.Map);
            }
            return maps;
        }

        public static int LocalCount(Map map, ThingDef def)
            => LocalAmounts(map.resourceCounter).TryGetValue(def, out int count) ? count : 0;

        public static int Count(Map map, ThingDef def)
        {
            if (def.resourceReadoutPriority == ResourceCountPriority.Uncounted) return 0;
            int count = 0;
            foreach (var linked in Maps(map)) count += LocalCount(linked, def);
            return count;
        }

        // Keep every map's backing counter local. Summing already-patched getters would
        // recurse and multiply totals, and writing combined totals would corrupt future updates.
        public static Dictionary<ThingDef, int> Amounts(List<Map> maps)
        {
            var result = new Dictionary<ThingDef, int>();
            foreach (var map in maps)
            foreach (var pair in LocalAmounts(map.resourceCounter))
            {
                result.TryGetValue(pair.Key, out int previous);
                result[pair.Key] = previous + pair.Value;
            }
            return result;
        }

        // Only the build-material menu uses this list. Global thing lists, reservations,
        // construction jobs and hauling must continue to refer to physically local things.
        public static List<Thing> BuildMaterialThings(ListerThings local, ThingDef def, Designator_Build designator)
        {
            var things = local.ThingsOfDef(def);
            if (things.Count != 0) return things;
            foreach (var map in Maps(designator.Map))
                if (map != designator.Map && LocalCount(map, def) > 0)
                {
                    var remote = map.listerThings.ThingsOfDef(def);
                    if (remote.Count > 0) return remote;
                }
            return things;
        }
    }

    [HarmonyPatch(typeof(ResourceCounter), nameof(ResourceCounter.GetCount))]
    public static class LinkedResourceCountPatch
    {
        public static void Postfix(Map ___map, ThingDef rDef, ref int __result)
        {
            if (rDef.resourceReadoutPriority == ResourceCountPriority.Uncounted) return;
            foreach (var map in LinkedResources.Maps(___map))
                if (map != ___map) __result += LinkedResources.LocalCount(map, rDef);
        }
    }

    [HarmonyPatch(typeof(ResourceCounter), nameof(ResourceCounter.AllCountedAmounts), MethodType.Getter)]
    public static class LinkedResourceAmountsPatch
    {
        public static void Postfix(Map ___map, ref Dictionary<ThingDef, int> __result)
        {
            var maps = LinkedResources.Maps(___map);
            if (maps.Count > 1) __result = LinkedResources.Amounts(maps);
        }
    }

    [HarmonyPatch(typeof(ResourceCounter), nameof(ResourceCounter.GetCountIn), new[] { typeof(ThingRequestGroup) })]
    public static class LinkedResourceGroupPatch
    {
        public static void Postfix(Map ___map, ThingRequestGroup group, ref int __result)
        {
            foreach (var map in LinkedResources.Maps(___map))
            {
                if (map == ___map) continue;
                // Single-map snapshot bypasses the aggregated getter.
                foreach (var pair in LinkedResources.Amounts(new List<Map> { map }))
                    if (group.Includes(pair.Key)) __result += pair.Value;
            }
        }
    }

    [HarmonyPatch(typeof(Designator_Build), nameof(Designator_Build.ProcessInput))]
    public static class LinkedBuildMaterialPatch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(ListerThings), nameof(ListerThings.ThingsOfDef));
            var replacement = AccessTools.Method(typeof(LinkedResources), nameof(LinkedResources.BuildMaterialThings));
            int replacements = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    // The existing lister and def stay on the stack; add the designator's map context.
                    var context = new CodeInstruction(OpCodes.Ldarg_0);
                    context.labels.AddRange(instruction.labels);
                    context.blocks.AddRange(instruction.blocks);
                    yield return context;
                    yield return new CodeInstruction(OpCodes.Call, replacement);
                    replacements++;
                }
                else yield return instruction;
            }
            if (replacements != 1) throw new InvalidOperationException("Unexpected RimWorld build-material menu: expected one ThingsOfDef call.");
        }
    }
}
