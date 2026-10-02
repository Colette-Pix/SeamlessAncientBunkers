using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SeamlessAncientBunkers;
using UnityEngine;
using Verse;
using Verse.AI;
using HarmonyLib;

namespace SABOrderProbe
{
    [StaticConstructorOnStartup]
    public static class MenuLayout
    {
        static MenuLayout()
        {
            if(GenCommandLine.CommandLineArgPassed("sab-order-tests"))
            {
                new Harmony("colet.saborderprobe").Patch(AccessTools.Method(typeof(FloatMenuOption),nameof(FloatMenuOption.SetSizeMode)),prefix:new HarmonyMethod(typeof(MenuLayout),nameof(Prefix)));
                new Harmony("colet.saborderprobe.feedback").Patch(AccessTools.Method(typeof(FleckMaker),nameof(FleckMaker.Static),new[]{typeof(Vector3),typeof(Map),typeof(FleckDef),typeof(float)}),postfix:new HarmonyMethod(typeof(MenuLayout),nameof(Feedback)));
                new Harmony("colet.saborderprobe.lines").Patch(AccessTools.Method(typeof(GenDraw),nameof(GenDraw.DrawLineBetween),new[]{typeof(Vector3),typeof(Vector3),typeof(float)}),postfix:new HarmonyMethod(typeof(MenuLayout),nameof(Line)));
            }
        }
        public static Map feedbackMap;
        public static Vector3 feedbackPosition, lineFrom, lineTo;
        public static int lines;
        public static void Feedback(Vector3 loc,Map map,FleckDef fleckDef){if(fleckDef==FleckDefOf.FeedbackGoto){feedbackMap=map;feedbackPosition=loc;}}
        public static void Line(Vector3 __0,Vector3 __1){lines++;lineFrom=__0;lineTo=__1;}
        // Hidden test processes have no GUI skin. Skip text measurement only, not menu logic.
        public static bool Prefix()=>Event.current!=null;
    }
    public class OrderProbe : GameComponent
    {
        int stage, started;
        Pawn pawn;
        AncientHatch hatch;
        Thing item, medicine, wall;
        IntVec3 destination;
        public OrderProbe(Game game) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage,"orderStage");Scribe_Values.Look(ref started,"orderStarted");
            Scribe_References.Look(ref pawn,"orderPawn");Scribe_References.Look(ref hatch,"orderHatch");
            Scribe_References.Look(ref item,"orderItem");Scribe_References.Look(ref medicine,"orderMedicine");Scribe_References.Look(ref wall,"orderWall");Scribe_Values.Look(ref destination,"orderDestination");
        }
        public override void LoadedGame()
        {
            if(stage!=2||!GenCommandLine.CommandLineArgPassed("sab-order-tests"))return;
            Current.Game.CurrentMap=pawn.Map;Find.Selector.ClearSelection();Find.Selector.Select(pawn);BunkerLevels.Switch(true);
            Check(Broker.For(pawn)?.orderedJob?.def==JobDefOf.Goto,"pending drafted move survives fresh-process save reload");
            Visuals(hatch.Map,destination.ToVector3Shifted(),false,"reloaded move");
        }
        void Check(bool value, string label)
        {
            if (!value) throw new Exception(label);
            Log.Message("[SAB ORDER TEST] PASS " + label);
        }
        List<FloatMenuOption> Menu(Thing target) => FloatMenuMakerMap.GetOptions(new List<Pawn>{pawn}, target.DrawPos, out _);
        void Visuals(Map map,Vector3 target,bool feedback,string label)
        {
            var intent=Broker.For(pawn);
            Check(OrderFeedback.DestinationSegment(pawn,map,out var from,out var to)&&from==Portal.Other(intent.route.Last()).DrawPos&&to==target,label+" line uses destination entrance and target");
            Check(!OrderFeedback.DestinationSegment(pawn,pawn.Map,out _,out _),label+" does not project target onto source map");
            int before=MenuLayout.lines;
            SelectionDrawer.DrawSelectionOverlays();
            Check(MenuLayout.lines>before&&MenuLayout.lineFrom==from&&MenuLayout.lineTo==to,label+" selection overlay invokes native line renderer");
            if(feedback)Check(MenuLayout.feedbackMap==map&&MenuLayout.feedbackPosition.ToIntVec3()==target.ToIntVec3(),label+" native green feedback spawned at destination");
            Find.Selector.ClearSelection();before=MenuLayout.lines;SelectionDrawer.DrawSelectionOverlays();
            Check(MenuLayout.lines==before,label+" deselection hides line");Find.Selector.Select(pawn);
        }
        void Choose(List<FloatMenuOption> options, string text)
        {
            Log.Message("[SAB ORDER TEST] Options: " + string.Join(" | ", options.Select(o => o.Label + (o.Disabled ? " [disabled]" : ""))));
            var option = options.FirstOrDefault(o => !o.Disabled && o.Label.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            Check(option != null, "native menu offers " + text);
            option.action();
        }
        Thing Spawn(ThingDef def, Map map, IntVec3 cell, ThingDef stuff=null)
        {
            var thing=ThingMaker.MakeThing(def,stuff);
            if(def.CanHaveFaction)thing.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(thing,cell,map);thing.SetForbidden(false,false);return thing;
        }
        public override void GameComponentUpdate()
        {
            if(!GenCommandLine.CommandLineArgPassed("sab-order-tests") || Current.ProgramState != ProgramState.Playing || LongEventHandler.AnyEventNowOrWaiting || stage==99)return;
            Application.runInBackground=true;
            foreach(var window in Find.WindowStack.Windows.ToList())if(window.forcePause)Find.WindowStack.TryRemove(window,false);
            try
            {
                Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
                if(stage==0)
                {
                    hatch=Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).First(h=>Portal.Other(h)!=null);
                    Manager.Current.Cleanup();BunkerMod.Settings.enabled=true;BunkerMod.Settings.debug=true;
                    foreach(var map in new[]{hatch.Map,hatch.PocketMap})
                    {
                        foreach(var group in map.haulDestinationManager.AllGroupsListInPriorityOrder.ToList())group.Settings.filter.SetDisallowAll();
                        foreach(var p in map.mapPawns.AllPawnsSpawned.ToList())if(p.HostileTo(Faction.OfPlayer))p.Destroy();
                    }
                    pawn=PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist,Faction.OfPlayer,forceGenerateNewPawn:true,canGeneratePawnRelations:false));
                    foreach(var trait in pawn.story.traits.allTraits.ToList())pawn.story.traits.RemoveTrait(trait);
                    GenSpawn.Spawn(pawn,CellFinder.StandableCellNear(hatch.Position,hatch.Map,5),hatch.Map);
                    pawn.workSettings.EnableAndInitialize();pawn.workSettings.SetPriority(WorkTypeDefOf.Hauling,1);pawn.workSettings.SetPriority(WorkTypeDefOf.Construction,1);
                    Check(Hauling.CanHaul(pawn)&&!pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction),"fixture colonist can haul and construct");
                    pawn.needs.food.CurLevelPercentage=1;pawn.needs.rest.CurLevelPercentage=1;
                    BunkerLevels.Entered(hatch.PocketMap);
                    Current.Game.CurrentMap=hatch.Map;Find.Selector.ClearSelection();Find.Selector.Select(pawn);
                    BunkerLevels.Switch(false);
                    Check(Find.CurrentMap==hatch.PocketMap&&Find.Selector.IsSelected(pawn)&&pawn.Map==hatch.Map,"level navigation retains real remote selection");
                    BunkerLevels.Switch(true);
                    var route=Graph.Reachable(pawn,false).First(r=>r.map==hatch.PocketMap);
                    destination=GenRadial.RadialCellsAround(route.landing,6,true).First(c=>c.Standable(hatch.PocketMap)&&c.GetEdifice(hatch.PocketMap)==null&&!c.Fogged(hatch.PocketMap)&&!c.GetThingList(hatch.PocketMap).Any(t=>t.def.category==ThingCategory.Item)&&c.GetSlotGroup(hatch.PocketMap)==null);
                    var zone=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,hatch.PocketMap.zoneManager);hatch.PocketMap.zoneManager.RegisterZone(zone);zone.AddCell(destination);zone.GetStoreSettings().filter.SetDisallowAll();zone.GetStoreSettings().filter.SetAllow(ThingDefOf.Silver,true);
                    item=Spawn(ThingDefOf.Silver,hatch.Map,pawn.Position);item.stackCount=3;
                    var options=Menu(item);
                    Check(Broker.For(pawn)==null&&!RemotePawnScope.Active,"menu query restores context without creating an intent");
                    Choose(options,"Prioritize hauling");Check(Broker.For(pawn)?.purpose==Purpose.Haul,"native haul order creates entrance delivery");
                    started=Manager.Now;stage=1;
                }
                else if(stage==1&&pawn.Map==hatch.PocketMap&&pawn.carryTracker.CarriedThing==null&&destination.GetThingList(hatch.PocketMap).Any(t=>t.def==ThingDefOf.Silver))
                {
                    Check(true,"item physically delivered to remote stockpile");
                    pawn.drafter.Drafted=true;Current.Game.CurrentMap=pawn.Map;Find.Selector.ClearSelection();Find.Selector.Select(pawn);BunkerLevels.Switch(true);
                    Check(Find.Selector.IsSelected(pawn)&&Find.CurrentMap==hatch.Map,"drafted selection survives level switch");
                    destination=CellFinder.StandableCellNear(hatch.Position+IntVec3.East*5,hatch.Map,4);
                    var options=FloatMenuMakerMap.GetOptions(new List<Pawn>{pawn},destination.ToVector3Shifted(),out _);
                    Check(pawn.Map==hatch.PocketMap&&!RemotePawnScope.Active,"drafted movement menu restores pawn map");
                    Choose(options,"Go here");Check(Broker.For(pawn)?.orderedJob?.def==JobDefOf.Goto,"remote movement preserves native goto job");
                    Visuals(hatch.Map,destination.ToVector3Shifted(),true,"drafted move");
                    var replaced=Broker.For(pawn);Broker.Cancel(replaced);
                    Check(!OrderFeedback.DestinationSegment(pawn,hatch.Map,out _,out _),"cancelled order leaves no destination line");
                    Choose(options,"Go here");
                    started=Manager.Now;stage=2;
                    GameDataSaveLoader.SaveGame("SAB-OrderTransit");
                }
                else if(stage==2&&pawn.Map==hatch.Map&&pawn.Position==destination)
                {
                    Check(pawn.Drafted,"drafted pawn walks through entrance to exact clicked destination while remaining drafted");
                    Check(Find.Selector.IsSelected(pawn),"selection survives actual portal crossing");
                    Check(!OrderFeedback.DestinationSegment(pawn,hatch.Map,out _,out _),"arrival hands overlays back to native pawn rendering");
                    pawn.drafter.Drafted=false;
                    var route=Graph.Reachable(pawn,false).First(r=>r.map==hatch.PocketMap);
                    medicine=Spawn(ThingDef.Named("Penoxycyline"),hatch.PocketMap,route.landing);medicine.stackCount=1;
                    Current.Game.CurrentMap=hatch.Map;Find.Selector.ClearSelection();Find.Selector.Select(pawn);BunkerLevels.Switch(false);
                    Choose(Menu(medicine),"Pick up");Check(Broker.For(pawn)?.orderedJob?.def==JobDefOf.TakeInventory,"remote pickup preserves native inventory job");
                    Visuals(hatch.PocketMap,medicine.DrawPos,true,"manual pickup");
                    started=Manager.Now;stage=3;
                }
                else if(stage==3&&pawn.inventory.Contains(medicine))
                {
                    Check(pawn.Map==hatch.PocketMap,"remote pickup completes on destination map");
                    var route=Graph.Reachable(pawn,false).First(r=>r.map==hatch.Map);
                    wall=Spawn(ThingDefOf.Wall,hatch.Map,CellFinder.StandableCellNear(hatch.Position+IntVec3.East*7,hatch.Map,4),ThingDefOf.WoodLog);wall.HitPoints=wall.MaxHitPoints-20;
                    hatch.Map.areaManager.Home[wall.Position]=true;
                    hatch.Map.listerBuildingsRepairable.Notify_BuildingTookDamage((Building)wall);
                    Current.Game.CurrentMap=pawn.Map;Find.Selector.ClearSelection();Find.Selector.Select(pawn);BunkerLevels.Switch(true);
                    Choose(Menu(wall),"Prioritize repairing");Check(Broker.For(pawn)?.orderedJob?.workGiverDef!=null,"native manual work saved for arrival");
                    Visuals(hatch.Map,wall.DrawPos,true,"manual repair");
                    started=Manager.Now;stage=4;
                }
                else if(stage==4&&wall.HitPoints==wall.MaxHitPoints)
                {
                    Check(pawn.Map==hatch.Map,"remote repair completes after portal travel");
                    stage=99;Log.Message("[SAB ORDER TEST] ACCEPTANCE COMPLETE");Application.Quit();
                }
                if(stage>0&&stage!=99&&Manager.Now-started>9000)throw new Exception("Timeout stage="+stage+" pawn="+pawn.Position+" map="+pawn.Map+" job="+pawn.CurJob+" intent="+Broker.For(pawn)?.purpose);
            }
            catch(Exception ex){stage=99;Log.Error("[SAB ORDER TEST] FAIL "+ex);Application.Quit();}
        }
    }
}

