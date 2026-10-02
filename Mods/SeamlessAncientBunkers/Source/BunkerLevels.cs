using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace SeamlessAncientBunkers
{
 public class BunkerLevel:IExposable
 {
  public Map map,surface;public int level;
  public void ExposeData(){Scribe_References.Look(ref map,"map");Scribe_References.Look(ref surface,"surface");Scribe_Values.Look(ref level,"level");}
 }
 public static class BunkerLevels
 {
  static AncientHatch Entrance(Map map)=>Find.Maps.SelectMany(m=>m.listerThings.AllThings.OfType<AncientHatch>()).FirstOrDefault(h=>h.PocketMap==map);
  public static Map Surface(Map map)
  {
   var seen=new HashSet<Map>();
   while(map!=null&&seen.Add(map)){var h=Entrance(map);if(h==null)return map;map=h.Map;}
   return null;
  }
  public static void Entered(Map map)
  {
   var manager=Manager.Current;if(manager==null||map==null||Entrance(map)==null||manager.levels.Any(l=>l.map==map))return;
   var surface=Surface(map);if(surface==null)return;
   foreach(var existing in manager.levels.Where(l=>l.map!=null&&Surface(l.map)==surface))existing.surface=surface;
   int last=manager.levels.Where(l=>l.surface==surface).Select(l=>l.level).DefaultIfEmpty(0).Min();
   manager.levels.Add(new BunkerLevel{map=map,surface=surface,level=last-1});
   Teach();
  }
  public static void Teach()=>LessonAutoActivator.TeachOpportunity(DefDatabase<ConceptDef>.GetNamed("SAB_BunkerTravel"),OpportunityType.Important);
  public static void RegisterExisting()
  {
   if(Manager.Current==null)return;
   // Older saves lack entry timestamps. Their map creation order is the deterministic migration order.
   foreach(var map in Find.Maps.OrderBy(m=>m.uniqueID))
   {
    var h=Entrance(map);
    if(h!=null&&(bool)AccessTools.Field(typeof(MapPortal),"beenEntered").GetValue(h))Entered(map);
   }
   if(Manager.Current.levels.Any(l=>l.map!=null))Teach();
  }
  public static List<Map> Stack(Map current)
  {
   var surface=Surface(current);var result=new List<Map>();if(surface==null||!Find.Maps.Contains(surface))return result;
   result.Add(surface);
   result.AddRange(Manager.Current.levels.Where(l=>l.map!=null&&Find.Maps.Contains(l.map)&&Surface(l.map)==surface).OrderByDescending(l=>l.level).Select(l=>l.map));
   return result;
  }
  public static Map Next(Map current,bool up)
  {
   var stack=Stack(current);int index=stack.IndexOf(current);if(index<0||stack.Count<2)return current;
   return stack[(index+(up?-1:1)+stack.Count)%stack.Count];
  }
  public static int Number(Map map)=>Manager.Current.levels.FirstOrDefault(l=>l.map==map)?.level??0;
  public static void Switch(bool up)
  {
   var map=Next(Find.CurrentMap,up);if(map==null||map==Find.CurrentMap)return;
   var selected=Find.Selector.SelectedPawns.Where(p=>p.Spawned&&p.Faction==Faction.OfPlayer).ToList();
   Current.Game.CurrentMap=map;
   // Select() jumps back to the pawn's map. Restore the existing selection directly.
   Find.Selector.SelectedObjects.AddRange(selected.Where(p=>!Find.Selector.IsSelected(p)).Cast<object>());
   Messages.Message("Level "+Number(map)+(Number(map)==0?" — surface":" — ancient bunker"),MessageTypeDefOf.SilentInput,false);
  }
 }
 [HarmonyPatch(typeof(MapPortal),nameof(MapPortal.OnEntered))]
 public static class RememberBunkerEntry
 {
  public static void Postfix(MapPortal __instance,Pawn pawn)
  {
   if(__instance is AncientHatch&&pawn?.Faction==Faction.OfPlayer)BunkerLevels.Entered(__instance.PocketMap);
  }
 }
 [HarmonyPatch(typeof(UIRoot_Play),nameof(UIRoot_Play.UIRootOnGUI))]
 public static class BunkerLevelKeys
 {
  public static void Prefix()
  {
   var e=Event.current;
   if(Manager.Current==null||Find.CurrentMap==null||WorldRendererUtility.WorldSelected||WorldComponent_GravshipController.CutsceneInProgress||e==null||e.type!=EventType.KeyDown||e.control||e.alt||e.command||GUIUtility.keyboardControl!=0||Find.WindowStack.Windows.Any(w=>w.absorbInputAroundWindow))return;
   if(DefDatabase<KeyBindingDef>.GetNamed("SAB_LevelUp").KeyDownEvent){BunkerLevels.Switch(true);e.Use();}
   else if(DefDatabase<KeyBindingDef>.GetNamed("SAB_LevelDown").KeyDownEvent){BunkerLevels.Switch(false);e.Use();}
  }
 }
}

