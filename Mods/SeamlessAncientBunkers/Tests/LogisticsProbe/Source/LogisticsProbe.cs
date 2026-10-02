using System;
using System.Linq;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SABLogisticsTests
{
    public class LogisticsProbe : GameComponent
    {
        int stage, started, nextLog;
        Pawn worker, patient;
        AncientHatch hatch;
        Corpse corpse;
        Building_Grave grave;
        Building_Bed bed;
        Thing medicine, wood;
        bool operated;
        public LogisticsProbe(Game game) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage,"logisticsStage"); Scribe_Values.Look(ref started,"logisticsStarted");
            Scribe_References.Look(ref worker,"logisticsWorker"); Scribe_References.Look(ref patient,"logisticsPatient");
            Scribe_References.Look(ref hatch,"logisticsHatch"); Scribe_References.Look(ref corpse,"logisticsCorpse");
            Scribe_References.Look(ref grave,"logisticsGrave"); Scribe_References.Look(ref bed,"logisticsBed");
            Scribe_References.Look(ref medicine,"logisticsMedicine"); Scribe_References.Look(ref wood,"logisticsWood");
            Scribe_Values.Look(ref operated,"logisticsOperated");
        }
        void Check(bool ok,string label) { if(!ok)throw new Exception(label);Log.Message("[SAB LOGISTICS] PASS "+label); }
        IntVec3 Cell(Map map) => CellFinder.StandableCellNear(map==hatch.Map?hatch.Position:hatch.exit.Position,map,7);
        void Move(Pawn pawn,Map map) { pawn.jobs.StopAll();if(pawn.Spawned)pawn.DeSpawn();GenSpawn.Spawn(pawn,Cell(map),map); }
        Pawn NewPawn(Map map)
        {
            Pawn pawn;
            do { pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false)); }
            while(pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor)||pawn.WorkTypeIsDisabled(WorkTypeDefOf.Hauling));
            foreach(var trait in pawn.story.traits.allTraits.ToList())pawn.story.traits.RemoveTrait(trait);
            GenSpawn.Spawn(pawn,Cell(map),map);pawn.workSettings.EnableAndInitialize();
            foreach(var def in DefDatabase<WorkTypeDef>.AllDefsListForReading)if(!pawn.WorkTypeIsDisabled(def))pawn.workSettings.SetPriority(def,0);
            return pawn;
        }
        Thing Spawn(ThingDef def,Map map,ThingDef stuff=null)
        {
            var thing=ThingMaker.MakeThing(def,stuff);if(def.CanHaveFaction)thing.SetFaction(Faction.OfPlayer);
            var cell=GenRadial.RadialCellsAround(map==hatch.Map?hatch.Position:hatch.exit.Position,10,true).First(c=>c.InBounds(map)&&c.Standable(map)&&c.GetEdifice(map)==null&&!c.Fogged(map)&&c.GetSlotGroup(map)==null);
            GenSpawn.Spawn(thing,cell,map);thing.SetForbidden(false,false);return thing;
        }
        void Reset() { var i=Broker.For(worker);if(i!=null)Broker.Cancel(i);worker.jobs.StopAll();Manager.Current.cooldowns.Clear();started=Manager.Now; }
        void Burial(Map source,Map destination)
        {
            Reset();Move(worker,destination);worker.workSettings.SetPriority(WorkTypeDefOf.Hauling,1);worker.workSettings.SetPriority(WorkTypeDefOf.Doctor,0);
            var dead=NewPawn(source);dead.Kill(null);corpse=dead.Corpse;corpse.SetForbidden(false,false);
            grave=(Building_Grave)Spawn(ThingDef.Named("Grave"),destination);grave.GetStoreSettings().filter.SetAllow(corpse.def,true);
            dead.ownership.ClaimGrave(grave);
            var routes=Graph.Reachable(worker);var giver=DefDatabase<WorkGiverDef>.AllDefsListForReading.First(d=>d.Worker is WorkGiver_HaulGeneral).Worker;
            dead.ownership.UnclaimGrave();grave.GetStoreSettings().filter.SetDisallowAll();Check(LinkedLogistics.Plan(worker,giver,routes)==null,"grave filter rejection honored");grave.GetStoreSettings().filter.SetAllow(corpse.def,true);dead.ownership.ClaimGrave(grave);
            corpse.SetForbidden(true,false);Check(LinkedLogistics.Plan(worker,giver,routes)==null,"forbidden corpse rejected");corpse.SetForbidden(false,false);
            Check(Broker.For(worker)==null&&!RemotePawnScope.Active,"failed queries leave no intent or remote context");
        }
        void Surgery(Map destination,Map source)
        {
            Reset();Move(worker,destination);worker.workSettings.SetPriority(WorkTypeDefOf.Hauling,0);worker.workSettings.SetPriority(WorkTypeDefOf.Doctor,1);
            worker.skills.GetSkill(SkillDefOf.Medicine).Level=20;
            patient=NewPawn(destination);patient.playerSettings.medCare=MedicalCareCategory.Best;
            patient.workSettings.SetPriority(DefDatabase<WorkTypeDef>.GetNamed("Patient"),1);
            bed=(Building_Bed)Spawn(ThingDefOf.Bed,destination,ThingDefOf.WoodLog);bed.Medical=true;
            var recipe=DefDatabase<RecipeDef>.GetNamed("InstallPegLeg");var bill=new Bill_Medical(recipe,null);
            patient.BillStack.AddBill(bill);bill.Part=recipe.Worker.GetPartsToApplyOn(patient,recipe).First();
            Check(bill.CompletableEver,"surgery fixture has a valid body part");
            medicine=Spawn(ThingDefOf.MedicineIndustrial,source);medicine.stackCount=10;
            wood=Spawn(ThingDefOf.WoodLog,source);wood.stackCount=20;
            Check(LinkedLogistics.Needed(worker,patient,medicine)>0&&LinkedLogistics.Needed(worker,patient,wood)>0,"operation needs remote medicine and wood");
            patient.playerSettings.medCare=MedicalCareCategory.NoMeds;
            Check(LinkedLogistics.Needed(worker,patient,medicine)==0,"medical care restriction excludes industrial medicine");patient.playerSettings.medCare=MedicalCareCategory.Best;
            operated=false;
            if(destination==hatch.PocketMap)
            {
                Move(worker,source);
                Check(WorkGiver_PatientGoToBedTreatment.AnyAvailableDoctorFor(patient),"patient recognizes a reachable doctor on the other map");
                hatch.SetForbidden(true,false);
                Check(!WorkGiver_PatientGoToBedTreatment.AnyAvailableDoctorFor(patient),"forbidden entrance blocks remote doctor availability");
                hatch.SetForbidden(false,false);
            }
            Log.Message("[SAB LOGISTICS] surgery ready: routes="+Graph.Reachable(worker).Count+" bills="+patient.BillStack.Count+" doctor="+worker.workSettings.GetPriority(WorkTypeDefOf.Doctor));
        }
        public override void GameComponentUpdate()
        {
            if(!GenCommandLine.CommandLineArgPassed("sab-logistics-tests")||Current.ProgramState!=ProgramState.Playing||LongEventHandler.AnyEventNowOrWaiting||stage==99)return;
            Application.runInBackground=true;
            foreach(var w in Find.WindowStack.Windows.ToList())if(w.forcePause)Find.WindowStack.TryRemove(w,false);
            try
            {
                Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
                BunkerMod.Settings.debug=true;
                if(stage==0)
                {
                    hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);
                    Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.work=true;BunkerMod.Settings.haul=true;BunkerMod.Settings.debug=true;BunkerMod.Settings.scanInterval=120;BunkerMod.Settings.minStay=300;
                    foreach(var map in new[]{hatch.Map,hatch.PocketMap})
                    {
                        foreach(var p in map.mapPawns.AllPawnsSpawned.ToList())p.Destroy();
                        foreach(var t in map.listerThings.AllThings.Where(t=>t is Corpse||t.def.category==ThingCategory.Item||t is Building_Grave).ToList())t.Destroy();
                        foreach(var group in map.haulDestinationManager.AllGroupsListInPriorityOrder)group.Settings.filter.SetDisallowAll();
                    }
                    Manager.Current.EnableClearedBunkers();worker=NewPawn(hatch.Map);Burial(hatch.PocketMap,hatch.Map);stage=1;
                }
                worker.needs.food.CurLevelPercentage=1;worker.needs.rest.CurLevelPercentage=1;worker.needs.mood.CurLevelPercentage=1;
                if(patient!=null&&!patient.Dead){patient.needs.food.CurLevelPercentage=1;patient.needs.rest.CurLevelPercentage=1;patient.needs.mood.CurLevelPercentage=1;}
                if(stage==1&&worker.carryTracker.CarriedThing==corpse&&Broker.For(worker)?.purpose==Purpose.Burial)
                {
                    Check(true,"hauler travels to remote corpse and collects it");stage=2;
                    GameDataSaveLoader.SaveGame("SAB-LogisticsTransit");Log.Message("[SAB LOGISTICS] RELOAD REQUIRED");Application.Quit();stage=99;return;
                }
                if(stage==2)
                {
                    Check(Broker.For(worker)?.deliveryTarget==grave&&worker.carryTracker.CarriedThing==corpse,"carried corpse and destination grave survive fresh-process reload");stage=3;
                }
                if(stage==3&&grave.HasCorpse)
                {
                    Check(grave.Corpse==corpse,"bunker corpse physically buried on surface");Burial(hatch.Map,hatch.PocketMap);stage=4;
                }
                if(stage==4&&grave.HasCorpse)
                {
                    Check(grave.Corpse==corpse,"surface corpse physically buried in bunker");Surgery(hatch.PocketMap,hatch.Map);stage=5;
                }
                if(stage==5||stage==6)
                {
                    if(Manager.Now>=nextLog){nextLog=Manager.Now+1500;Log.Message("[SAB LOGISTICS] progress stage="+stage+" tick="+(Manager.Now-started)+" job="+worker.CurJob+" intent="+Broker.For(worker)?.purpose+" bills="+patient.BillStack.Count+" patient="+patient.CurJob+" medMap="+medicine.MapHeld+" woodMap="+wood.MapHeld+" routes="+Graph.Reachable(worker).Count);}
                    if(worker.CurJobDef==JobDefOf.DoBill&&worker.CurJob.targetA.Thing==patient)operated=true;
                    if(patient.BillStack.Count==0&&operated)
                    {
                        Check(true,"doctor fetched remote medicine and wood, then completed native operation (direction "+stage+")");
                        Check(worker.Map==patient.Map&&worker.workSettings.GetPriority(WorkTypeDefOf.Hauling)==0,"doctor supply delivery works with hauling disabled");
                        if(stage==5){patient.Destroy();bed.Destroy();foreach(var m in new[]{hatch.Map,hatch.PocketMap})foreach(var t in m.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).Where(t=>t.def==ThingDefOf.MedicineIndustrial||t.def==ThingDefOf.WoodLog).ToList())t.Destroy();Surgery(hatch.Map,hatch.PocketMap);stage=6;}
                        else {stage=99;Log.Message("[SAB LOGISTICS] ACCEPTANCE COMPLETE");Application.Quit();}
                    }
                }
                if(stage!=99&&Manager.Now-started>20000)throw new Exception("Timeout stage="+stage+" pawn="+worker.Position+" map="+worker.Map+" job="+worker.CurJob+" intent="+Broker.For(worker)?.purpose+" patient="+patient?.CurJob);
            }
            catch(Exception e){stage=99;Log.Error("[SAB LOGISTICS] FAIL "+e);Application.Quit();}
        }
    }
}



