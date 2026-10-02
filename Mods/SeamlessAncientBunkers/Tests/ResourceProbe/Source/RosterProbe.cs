using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SABResourceProbe
{
    public class RosterProbe : GameComponent
    {
        bool done;
        int checks;
        Map surface, bunker;
        AncientHatch hatch;
        Pawn worker, animal, mech;
        List<Pawn> colonists;
        public RosterProbe(Game game) { }
        void Check(bool ok, string text)
        {
            if (!ok) throw new Exception(text);
            checks++;
            Log.Message("[SAB ROSTER] PASS " + text);
        }
        IntVec3 Cell(Map map) => map.AllCells.First(c => c.DistanceTo(map.Center) < 35 &&
            CellRect.CenteredOn(c, 3).Cells.All(x => x.InBounds(map) && x.Standable(map) && x.GetEdifice(map) == null));
        Thing Spawn(string defName, Map map)
        {
            var def = ThingDef.Named(defName);
            var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.WoodLog : null);
            thing.SetFactionDirect(Faction.OfPlayer);
            return GenSpawn.Spawn(thing, Cell(map), map);
        }
        void Move(Pawn pawn, Map map)
        {
            if (pawn.Spawned) pawn.DeSpawn();
            GenSpawn.Spawn(pawn, Cell(map), map);
        }
        List<Pawn> Roster(MainTabWindow_PawnTable tab) => ((IEnumerable<Pawn>)AccessTools.PropertyGetter(tab.GetType(), "Pawns").Invoke(tab, null)).ToList();
        void WindowsClear()
        {
            foreach (var window in Find.WindowStack.Windows.ToList()) Find.WindowStack.TryRemove(window, false);
        }
        void Work(WorkTypeDef type, bool active)
        {
            foreach (var pawn in colonists)
                if (!pawn.WorkTypeIsDisabled(type)) pawn.workSettings.SetPriority(type, active ? 1 : 0);
        }
        void Hunt(bool assigned, bool armed, string label)
        {
            var args = new object[] { surface, false, false };
            AccessTools.Method(typeof(Designator_Hunt), "CheckHunters").Invoke(null, args);
            Check((bool)args[1] == assigned && (bool)args[2] == armed, label);
        }
        void Plant(bool warning, string label)
        {
            WindowsClear();
            var command = (Command_SetPlantToGrow)FormatterServices.GetUninitializedObject(typeof(Command_SetPlantToGrow));
            command.settable = new Zone_Growing(surface.zoneManager);
            AccessTools.Method(typeof(Command_SetPlantToGrow), "WarnAsAppropriate").Invoke(command, new object[] { ThingDef.Named("Plant_Healroot") });
            Check(Find.WindowStack.Windows.OfType<Dialog_MessageBox>().Any() == warning, label);
        }
        void Construction(bool expected, string label)
        {
            var build = new Designator_Build(ThingDefOf.Wall);
            var actual = (bool)AccessTools.Method(typeof(Designator_Build), "AnyColonistWithSkill")
                .Invoke(build, new object[] { 10, SkillDefOf.Construction, true });
            Check(actual == expected, label);
        }
        void Assignments()
        {
            var bed = (Building_Bed)Spawn("Bed", surface);
            Check(bed.CompAssignableToPawn.AssigningCandidates.Contains(worker), "human bed lists remote colonist");
            bed.CompAssignableToPawn.TryAssignPawn(worker);
            Check(worker.ownership.OwnedBed == bed && worker.Map == bunker, "remote bed ownership updates without moving pawn");
            var animalBed = (Building_Bed)Spawn("AnimalSleepingSpot", surface);
            Check(animalBed.CompAssignableToPawn.AssigningCandidates.Contains(animal), "animal bed lists remote animal");
            Check(!animalBed.CompAssignableToPawn.AssigningCandidates.Contains(worker), "animal bed keeps species filter");
            animalBed.CompAssignableToPawn.TryAssignPawn(animal);
            Check(animal.ownership.OwnedBed == animalBed && animal.Map == bunker, "remote animal bed assignment works");
            var meditation = Spawn("MeditationSpot", surface).TryGetComp<CompAssignableToPawn>();
            Check(meditation.AssigningCandidates.Contains(worker), "meditation spot lists remote colonist");
            meditation.TryAssignPawn(worker);
            Check(worker.ownership.AssignedMeditationSpot == meditation.parent, "remote meditation assignment works");
            var grave = Spawn("Grave", surface).TryGetComp<CompAssignableToPawn>();
            Check(grave.AssigningCandidates.Contains(worker), "grave lists remote colonist");
            grave.TryAssignPawn(worker);
            Check(worker.ownership.AssignedGrave == grave.parent, "remote grave assignment works");
            if (Faction.OfEmpire != null)
            {
                worker.royalty.SetTitle(Faction.OfEmpire, DefDatabase<RoyalTitleDef>.GetNamed("Knight"), false, false, false);
                var throne = Spawn("Throne", surface).TryGetComp<CompAssignableToPawn>();
                Check(throne.AssigningCandidates.Contains(worker), "throne already lists remote eligible noble");
                throne.TryAssignPawn(worker);
                Check(worker.ownership.AssignedThrone == throne.parent, "remote throne assignment works");
            }
            var deathrest = Spawn("DeathrestCasket", surface).TryGetComp<CompAssignableToPawn>();
            Check(deathrest.AssigningCandidates.Contains(worker), "deathrest candidates span levels");
            Check(!deathrest.CanAssignTo(worker).Accepted, "deathrest keeps bloodfeeder eligibility restriction");
        }
        void Areas(Pawn pawn, string label)
        {
            var ownArea = bunker.areaManager.Home;
            pawn.playerSettings.AreaRestrictionInPawnCurrentMap = ownArea;
            LinkedAreaControls.Set(pawn.playerSettings, surface.areaManager.Home);
            Check(LinkedAreaControls.Get(pawn.playerSettings) == surface.areaManager.Home, label + " edits viewed level area");
            Check(pawn.playerSettings.AreaRestrictionInPawnCurrentMap == ownArea, label + " keeps physical level area");
            LinkedAreaControls.Set(pawn.playerSettings, null);
            Check(LinkedAreaControls.Get(pawn.playerSettings) == null && pawn.playerSettings.AreaRestrictionInPawnCurrentMap == ownArea, label + " clears only viewed level area");
        }
        void Robots()
        {
            var creator = AccessTools.TypeByName("AIRobot.X2_Building_AIRobotCreator");
            if (creator == null) { Log.Message("[SAB ROSTER] Optional robot mod absent"); return; }
            var station = Spawn("AIRobot_RechargeStation_Cleaner", bunker);
            var robot = (Pawn)AccessTools.Method(creator, "CreateRobot", new[] { typeof(string), typeof(IntVec3), typeof(Map), typeof(Faction) }).Invoke(null, new object[] { "AIRobot_Cleaner", station.Position + IntVec3.East, bunker, Faction.OfPlayer });
            AccessTools.Field(station.GetType(), "robot").SetValue(station, robot);
            AccessTools.Field(station.GetType(), "robotSpawnedOnce").SetValue(station, true);
            AccessTools.Field(robot.GetType(), "rechargeStation").SetValue(robot, station);
            var tab = (MainTabWindow_PawnTable)Activator.CreateInstance(AccessTools.TypeByName("AIRobot.X2_MainTabWindow_Robots"));
            Check(Roster(tab).Count(p => p == robot) == 1, "robot tab lists remote robot once");
            Areas(robot, "robot");
            AccessTools.Method(station.GetType(), "AddRobotToContainer").Invoke(station, new object[] { robot });
            Check(Roster(tab).Contains(robot), "robot tab retains remote docked robot");
            Check(RobotSupport.Station(robot) == station, "robot charger identity unchanged");
            LinkedAreaControls.Set(robot.playerSettings, surface.areaManager.Home);
            Check(LinkedAreaControls.Get(robot.playerSettings) == surface.areaManager.Home, "docked robot accepts remote area assignment");
            Current.Game.CurrentMap = bunker;
            LinkedAreaControls.Set(robot.playerSettings, bunker.areaManager.Home);
            Check(LinkedAreaControls.Get(robot.playerSettings) == bunker.areaManager.Home, "docked robot accepts charger-level area assignment");
            Current.Game.CurrentMap = surface;
            Check(LinkedRobotShutdownAll.ColonyThings(surface.listerThings).Contains(station), "robot shutdown-all includes remote charger");
        }
        void MoreWarnings()
        {
            var rock = Spawn("Granite", surface);
            surface.designationManager.AddDesignation(new Designation(rock, DesignationDefOf.Mine));
            Work(WorkTypeDefOf.Mining, true);
            Check(!new Alert_NeedMiner().GetReport().active, "remote miner clears shortage alert");
            Work(WorkTypeDefOf.Mining, false);
            Check(new Alert_NeedMiner().GetReport().active, "unassigned miners retain shortage alert");
            var visitor = new ITab_Pawn_Prisoner();
            var warden = AccessTools.Method(typeof(ITab_Pawn_Visitor), "ColonyHasAnyWardenCapableOfViolence");
            Work(WorkTypeDefOf.Warden, true);
            Check((bool)warden.Invoke(visitor, new object[] { surface }), "remote assigned warden satisfies execution warning");
            Work(WorkTypeDefOf.Warden, false);
            Check(!(bool)warden.Invoke(visitor, new object[] { surface }), "unassigned wardens retain execution warning");
            var patient = colonists.First(p => p != worker);
            Move(patient, surface);
            patient.health.AddHediff(HediffDefOf.Cut, patient.RaceProps.body.corePart).Severity = 5f;
            Work(WorkTypeDefOf.Doctor, false);
            var doctorAlert = new Alert_NeedDoctor();
            var patients = AccessTools.PropertyGetter(typeof(Alert_NeedDoctor), "Patients");
            Check(((List<Pawn>)patients.Invoke(doctorAlert, null)).Count(p => p == patient) == 1, "doctor warning reports local patient once");
            Work(WorkTypeDefOf.Doctor, true);
            patient.workSettings.SetPriority(WorkTypeDefOf.Doctor, 0);
            Check(!((List<Pawn>)patients.Invoke(doctorAlert, null)).Contains(patient), "remote doctor clears local patient warning");
            var recipe = DefDatabase<RecipeDef>.GetNamed("InstallPegLeg");
            patient.skills.GetSkill(SkillDefOf.Medicine).Level = 0;
            WindowsClear(); HealthCardUtility.CreateSurgeryBill(patient, recipe, null);
            Check(!Find.WindowStack.Windows.OfType<Dialog_MessageBox>().Any(), "remote surgeon satisfies surgery skill warning");
            foreach (var pawn in colonists) pawn.skills.GetSkill(SkillDefOf.Medicine).Level = 0;
            WindowsClear(); HealthCardUtility.CreateSurgeryBill(patient, recipe, null);
            Check(Find.WindowStack.Windows.OfType<Dialog_MessageBox>().Any(), "insufficient surgery skill still warns");
            Move(patient, bunker);
            var wild = PawnGenerator.GeneratePawn(PawnKindDef.Named("Cougar")); Move(wild, surface);
            Work(WorkTypeDefOf.Handling, true);
            var messages = (List<Message>)AccessTools.Field(typeof(Messages), "liveMessages").GetValue(null);
            messages.Clear(); TameUtility.ShowDesignationWarnings(wild, false);
            Check(messages.Count == 0, "remote skilled handler satisfies taming warning");
            foreach (var pawn in colonists) pawn.skills.GetSkill(SkillDefOf.Animals).Level = 0;
            messages.Clear(); TameUtility.ShowDesignationWarnings(wild, false);
            Check(messages.Any(m => m.text.Contains("No handler capable")), "insufficient handling skill still warns");
            mech.relations.AddDirectRelation(PawnRelationDefOf.Overseer, worker);
            Check(MechanitorUtility.AnyPlayerMechCanDoWork(WorkTypeDefOf.Hauling, 0, out var capable) && capable == mech, "remote controlled mech satisfies mech capability check");
            Check(!MechanitorUtility.AnyPlayerMechCanDoWork(WorkTypeDefOf.Mining, 20, out _), "mech check retains work-type and skill restrictions");
            foreach (var target in LinkedWorkerWarnings.TargetMethods().Concat(LinkedManagementRosters.TargetMethods()))
                Check(Harmony.GetPatchInfo(target).Transpilers.Any(p => p.owner == "colet.seamlessancientbunkers"), "installed UI patch " + target.DeclaringType.Name + "." + target.Name);
        }
        public override void GameComponentUpdate()
        {
            if (done || !GenCommandLine.CommandLineArgPassed("sab-roster-tests") || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting) return;
            done = true;
            try
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                BunkerMod.Settings.enabled = true;
                Current.Game.playSettings.useWorkPriorities = true;
                hatch = Find.Maps.SelectMany(m => m.listerThings.AllThings.OfType<AncientHatch>()).First(h => Portal.Other(h) != null);
                surface = hatch.Map; bunker = Portal.Other(hatch).Map;
                foreach (var map in Find.Maps) map.fogGrid.ClearAllFog();
                colonists = Find.Maps.SelectMany(m => m.mapPawns.FreeColonistsSpawned).ToList();
                worker = colonists.First(p => !p.WorkTypeIsDisabled(WorkTypeDefOf.Hunting));
                foreach (var pawn in colonists) { Move(pawn, bunker); foreach (var skill in pawn.skills.skills) skill.Level = 20; }
                animal = PawnGenerator.GeneratePawn(PawnKindDef.Named("Husky"), Faction.OfPlayer); Move(animal, bunker);
                mech = PawnGenerator.GeneratePawn(PawnKindDef.Named("Mech_Lifter"), Faction.OfPlayer); Move(mech, bunker);
                Current.Game.CurrentMap = surface;
                Check(LinkedResources.Maps(surface).Contains(bunker), "loaded reciprocal levels form a colony");
                Check(surface.mapPawns.FreeColonists.Count == 0, "physical surface colonist roster remains empty");
                Check(!surface.mapPawns.ColonyAnimals.Contains(animal) && !surface.mapPawns.PawnsInFaction(Faction.OfPlayer).Contains(mech), "physical animal and mech lists remain local");
                var work = new MainTabWindow_Work(); var schedule = new MainTabWindow_Schedule();
                Check(Roster(work).Contains(worker) && Roster(work).Distinct().Count() == Roster(work).Count, "Work tab includes remote colonists without duplicates");
                Check(Roster(schedule).Contains(worker), "Schedule tab includes remote colonists");
                Check(Roster(new MainTabWindow_Animals()).Contains(animal), "Animals tab includes remote animal");
                Check(Roster(new MainTabWindow_Mechs()).Contains(mech), "Mechs tab includes remote mech");
                worker.timetable.SetAssignment(7, TimeAssignmentDefOf.Joy);
                Check(Roster(schedule).First(p => p == worker).timetable.GetAssignment(7) == TimeAssignmentDefOf.Joy, "remote schedule edits update actual pawn");
                Work(WorkTypeDefOf.Hunting, false); Hunt(false, false, "hunting warns when nobody assigned");
                worker.workSettings.SetPriority(WorkTypeDefOf.Hunting, 1);
                worker.equipment.DestroyAllEquipment(); Hunt(true, false, "hunting retains missing-weapon warning");
                worker.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("Gun_BoltActionRifle")));
                Hunt(true, true, "remote assigned armed hunter satisfies warning");
                Check(Roster(work).First(p => p == worker).workSettings.GetPriority(WorkTypeDefOf.Hunting) == 1, "remote work edits use actual pawn settings");
                Work(WorkTypeDefOf.Growing, true); Plant(false, "remote skilled grower satisfies warning");
                Work(WorkTypeDefOf.Growing, false); Plant(true, "no assigned grower still warns");
                Work(WorkTypeDefOf.Construction, true); Construction(true, "remote assigned builder satisfies warning");
                Work(WorkTypeDefOf.Construction, false); Construction(false, "unassigned builders still fail availability check");
                MoreWarnings(); Assignments(); Areas(worker, "colonist"); Areas(animal, "animal"); Areas(mech, "mech"); Robots();
                Alert_NeedColonistBeds.AvailableColonistBeds(surface, true, out int singlesA, out int doublesA, out int cribsA);
                Alert_NeedColonistBeds.AvailableColonistBeds(bunker, true, out int singlesB, out int doublesB, out int cribsB);
                Check(singlesA == singlesB && doublesA == doublesB && cribsA == cribsB, "bed availability counts connected population and beds once");
                work.def = DefDatabase<MainButtonDef>.GetNamed("Work"); work.PostOpen(); LinkedRosterRefresh.Prefix(work);
                var workTable = (PawnTable)AccessTools.Field(typeof(MainTabWindow_PawnTable), "table").GetValue(work);
                Check(workTable.PawnsListForReading.Contains(worker), "live Work table caches remote row");
                var originalExit = hatch.exit;
                hatch.exit = null;
                LinkedAreaControls.Set(worker.playerSettings, surface.areaManager.Home);
                Check(worker.playerSettings.AreaRestrictionInPawnCurrentMap == bunker.areaManager.Home, "stale disconnected row cannot overwrite physical area");
                Check(!Roster(work).Contains(worker) && !Roster(schedule).Contains(worker), "broken connection removes remote colonists from both tabs");
                Check(!Roster(new MainTabWindow_Animals()).Contains(animal) && !Roster(new MainTabWindow_Mechs()).Contains(mech), "broken connection excludes remote animals and mechs");
                var states = AccessTools.Field(typeof(LinkedRosterRefresh), "States").GetValue(null);
                AccessTools.Method(states.GetType(), "Remove").Invoke(states, new object[] { work });
                LinkedRosterRefresh.Prefix(work);
                Check(!workTable.PawnsListForReading.Contains(worker), "open Work table refresh removes disconnected row");
                Hunt(false, false, "disconnected hunter does not suppress warning");
                hatch.exit = originalExit;
                Check(Roster(work).Contains(worker), "reconnected roster restores pawn immediately");
                BunkerMod.Settings.enabled = false;
                Check(!Roster(work).Contains(worker) && !Roster(new MainTabWindow_Animals()).Contains(animal), "global disable restores local rosters");
                BunkerMod.Settings.enabled = true;
                Current.Game.CurrentMap = bunker;
                Check(Roster(work).Contains(worker) && Roster(new MainTabWindow_Animals()).Contains(animal), "local rosters remain intact");
                Check(worker.Map == bunker && animal.Map == bunker && mech.Map == bunker, "UI queries never teleport pawns");
                Log.Message("[SAB ROSTER] ACCEPTANCE COMPLETE: " + checks + " checks");
            }
            catch (Exception e) { Log.Error("[SAB ROSTER] FAIL " + e); }
            Application.Quit();
        }
    }
}
