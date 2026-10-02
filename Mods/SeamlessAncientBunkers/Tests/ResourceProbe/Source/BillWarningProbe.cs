using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;

namespace SABResourceProbe
{
    public class BillWarningProbe : GameComponent
    {
        bool done;
        public BillWarningProbe(Game game) { }
        void Check(bool ok, string message)
        {
            if (!ok) throw new Exception(message);
            Log.Message("[SAB BILL WARNING] PASS " + message);
        }
        void AddBill(Building_WorkTable table, RecipeDef recipe, bool warning, string label)
        {
            Current.Game.CurrentMap = table.Map;
            Find.Selector.ClearSelection();
            Find.Selector.Select(table);
            foreach (var window in Find.WindowStack.Windows.ToList()) Find.WindowStack.TryRemove(window, false);
            var tab = new ITab_Bills();
            var maker = typeof(ITab_Bills).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(m => m.Name.Contains("OptionsMaker") && m.ReturnType == typeof(List<FloatMenuOption>));
            var options = (List<FloatMenuOption>)maker.Invoke(tab, null);
            int before = table.billStack.Count;
            options.First(o => o.Label == recipe.LabelCap.ToString()).action();
            Check(table.billStack.Count == before + 1, label + ": bill added");
            Check(Find.WindowStack.Windows.OfType<Dialog_MessageBox>().Any() == warning, label + ": correct warning state");
        }
        Building_WorkTable Table(Map map)
        {
            var cell = map.AllCells.First(c => c.DistanceTo(map.Center) < 25 &&
                CellRect.CenteredOn(c, 3).Cells.All(x => x.InBounds(map) && x.Standable(map) && x.GetEdifice(map) == null));
            return (Building_WorkTable)GenSpawn.Spawn(ThingMaker.MakeThing(ThingDef.Named("ElectricSmithy")), cell, map);
        }
        public override void GameComponentUpdate()
        {
            if (done || !GenCommandLine.CommandLineArgPassed("sab-bill-warning-tests") ||
                Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            done = true;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                BunkerMod.Settings.enabled = true;
                var hatch = Find.Maps.SelectMany(m => m.listerThings.AllThings.OfType<AncientHatch>())
                    .First(h => Portal.Other(h) != null);
                var surface = hatch.Map;
                var bunker = Portal.Other(hatch).Map;
                foreach (var map in Find.Maps) map.fogGrid.ClearAllFog();
                var pawns = Find.Maps.SelectMany(m => m.mapPawns.FreeColonistsSpawned).ToList();
                foreach (var pawn in pawns)
                {
                    pawn.DeSpawn();
                    GenSpawn.Spawn(pawn, surface.AllCells.First(c => c.Standable(surface)), surface);
                    foreach (var skill in pawn.skills.skills) skill.Level = 20;
                }
                var downstairs = Table(bunker);
                var upstairs = Table(surface);
                var recipe = downstairs.def.AllRecipes.First(r => r.AvailableNow && !r.mechanitorOnlyRecipe &&
                    r.skillRequirements != null && r.skillRequirements.Any(s => s.minLevel > 0) &&
                    pawns.Any(r.PawnSatisfiesSkillRequirements));
                Check(bunker.mapPawns.FreeColonists.Count == 0, "workshop level is empty");
                Check(LinkedResources.Maps(bunker).Contains(surface), "surface linked to bunker");
                Check(Harmony.GetPatchInfo(BillSkillWarning.TargetMethod()).Transpilers.Any(p => p.owner == "colet.seamlessancientbunkers"), "real add-bill callback patched");
                AddBill(downstairs, recipe, false, "skilled colonists upstairs");
                AddBill(upstairs, recipe, false, "skilled colonists local");
                foreach (var pawn in pawns) foreach (var skill in pawn.skills.skills) skill.Level = 0;
                AddBill(downstairs, recipe, true, "all linked colonists unskilled");
                foreach (var pawn in pawns) foreach (var skill in pawn.skills.skills) skill.Level = 20;
                BunkerMod.Settings.enabled = false;
                AddBill(downstairs, recipe, true, "mod disabled restores local warning");
                BunkerMod.Settings.enabled = true;
                var exit = hatch.exit;
                hatch.exit = null;
                AddBill(downstairs, recipe, true, "disconnected skilled colonists do not count");
                hatch.exit = exit;
                foreach (var pawn in pawns)
                {
                    pawn.DeSpawn();
                    GenSpawn.Spawn(pawn, bunker.AllCells.First(c => c.Standable(bunker)), bunker);
                }
                AddBill(upstairs, recipe, false, "skilled colonists downstairs");
                Check(surface.mapPawns.FreeColonists.Count == 0, "global pawn lists stay local");
                recipe.mechanitorOnlyRecipe = true;
                Check(!pawns.Any(MechanitorUtility.IsMechanitor), "mechanitor-warning fixture initially has no mechanitor");
                AddBill(upstairs, recipe, true, "no mechanitor still warns");
                var mechanitor = pawns.First(p => p.health.hediffSet.GetBrain() != null);
                mechanitor.health.AddHediff(DefDatabase<HediffDef>.GetNamed("MechlinkImplant"), mechanitor.health.hediffSet.GetBrain());
                Check(MechanitorUtility.IsMechanitor(mechanitor), "bunker colonist becomes a real mechanitor");
                AddBill(upstairs, recipe, false, "remote mechanitor satisfies production warning");
                Log.Message("[SAB BILL WARNING] ACCEPTANCE COMPLETE");
            }
            catch (Exception e) { Log.Error("[SAB BILL WARNING] FAIL " + e); }
            Application.Quit();
        }
    }
}
