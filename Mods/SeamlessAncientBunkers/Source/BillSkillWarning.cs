using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SeamlessAncientBunkers
{
    // Expand only the add-bill skill warning, not global pawn lists or work eligibility.
    [HarmonyPatch]
    public static class BillSkillWarning
    {
        public static List<Pawn> Colonists(MapPawns local) => LinkedPawnRoster.FreeColonists(local);

        private static IEnumerable<Type> TabTypes(Type type)
        {
            yield return type;
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (var child in TabTypes(nested)) yield return child;
        }

        public static MethodBase TargetMethod()
        {
            var dialog = AccessTools.Method(typeof(Bill), nameof(Bill.CreateNoPawnsWithSkillDialog));
            // Locate the compiler-generated menu callback by behavior, not its unstable name.
            return TabTypes(typeof(ITab_Bills)).SelectMany(AccessTools.GetDeclaredMethods)
                .Single(m => m.GetMethodBody() != null &&
                    PatchProcessor.GetOriginalInstructions(m).Any(i => i.Calls(dialog)));
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // Both skill and mechanitor warnings use the same connected roster.
            return LinkedPawnRoster.ReplaceQueries(instructions);
        }
    }
}
