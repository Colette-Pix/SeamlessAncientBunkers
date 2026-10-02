using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace SeamlessAncientBunkers
{
 // Keep each map's topology intact. Only the utility simulation sees the combined
 // component lists; no foreign cells or persistent cross-map net references are saved.
 public static class UtilityLinks
 {
  static Game game;
  static int tick = -1;
  static readonly Dictionary<object, List<object>> groups = new Dictionary<object, List<object>>();
  static readonly HashSet<object> active = new HashSet<object>();
  static readonly HashSet<object> powerTicked = new HashSet<object>();
  static bool builtPower, builtPipes;
  internal static readonly Type PipeType = AccessTools.TypeByName("DubsBadHygiene.CompPipe");
  internal static readonly Type NetType = AccessTools.TypeByName("DubsBadHygiene.PlumbingNet");
  static readonly FieldInfo pipeNet = PipeType == null ? null : AccessTools.Field(PipeType,"pipeNetRef");
  static readonly PropertyInfo closed = PipeType == null ? null : AccessTools.Property(PipeType,"closed");
  static readonly PropertyInfo mode = PipeType == null ? null : AccessTools.Property(PipeType,"mode");
  static readonly Dictionary<Type,FieldInfo[]> fields = new Dictionary<Type,FieldInfo[]>();

  public static void Invalidate() { tick = -1; }
  static void Refresh()
  {
   if(game == Current.Game && tick == Find.TickManager.TicksGame)return;
   game=Current.Game;tick=Find.TickManager.TicksGame;
   groups.Clear();powerTicked.Clear();builtPower=builtPipes=false;
  }
  public static IEnumerable<IntVec3> Terminals(Thing portal)
  {
   var rect=portal.OccupiedRect();
   foreach(var c in rect)yield return c;
   foreach(var c in rect.AdjacentCellsCardinal)if(c.InBounds(portal.Map))yield return c;
  }
  static IEnumerable<AncientHatch> Hatches() => Find.Maps.SelectMany(m=>m.listerThings.ThingsOfDef(ThingDefOf.AncientHatch)).OfType<AncientHatch>();
  public static bool Connected(MapPortal hatch)
  {
   var other=Portal.Other(hatch);
   return other!=null && Find.Maps.Contains(hatch.Map) && Find.Maps.Contains(other.Map) &&
    !hatch.LoadInProgress && !other.LoadInProgress && hatch.IsEnterable(out _) && other.IsEnterable(out _);
  }
  static void Join(IEnumerable<object> items)
  {
   var union=new HashSet<object>(items.Where(x=>x!=null));
   foreach(var item in union.ToArray())if(groups.TryGetValue(item,out var prior))union.UnionWith(prior);
   if(union.Count<2)return;
   var group=union.ToList();foreach(var item in group)groups[item]=group;
  }
  static void BuildPower()
  {
   if(builtPower)return;builtPower=true;
   foreach(var h in Hatches())
   {
    if(!Connected(h) || h.Map.gameConditionManager.ElectricityDisabled(h.Map) || h.exit.Map.gameConditionManager.ElectricityDisabled(h.exit.Map))continue;
    var a=Terminals(h).Select(c=>h.Map.powerNetGrid.TransmittedPowerNetAt(c)).Where(n=>n!=null).Distinct().ToArray();
    var b=Terminals(h.exit).Select(c=>h.exit.Map.powerNetGrid.TransmittedPowerNetAt(c)).Where(n=>n!=null).Distinct().ToArray();
    if(a.Length>0&&b.Length>0)Join(a.Cast<object>().Concat(b));
   }
  }
  static IEnumerable<ThingComp> Pipes(Thing portal) => Terminals(portal).SelectMany(c=>c.GetThingList(portal.Map)).OfType<ThingWithComps>().Distinct()
   .SelectMany(t=>t.AllComps).Where(c=>PipeType.IsInstanceOfType(c) && !(bool)closed.GetValue(c,null));
  static void BuildPipes()
  {
   if(builtPipes||PipeType==null)return;builtPipes=true;
   foreach(var h in Hatches())
   {
    if(!Connected(h))continue;
    var a=Pipes(h).ToArray();var b=Pipes(h.exit).ToArray();
    foreach(var type in a.Select(p=>mode.GetValue(p,null)).Distinct())
    {
     var an=a.Where(p=>Equals(mode.GetValue(p,null),type)).Select(p=>pipeNet.GetValue(p)).Where(n=>n!=null).ToArray();
     var bn=b.Where(p=>Equals(mode.GetValue(p,null),type)).Select(p=>pipeNet.GetValue(p)).Where(n=>n!=null).ToArray();
     if(an.Length>0&&bn.Length>0)Join(an.Concat(bn));
    }
   }
  }
  public static List<object> Group(object net)
  {
   Refresh();if(net is PowerNet)BuildPower();else BuildPipes();
   return groups.TryGetValue(net,out var group)?group:null;
  }
  public static T ReadList<T>(object net,string name) where T:class
  {
   var field=AccessTools.Field(NetType,name);
   if(active.Contains(net))return (T)field.GetValue(net);
   var group=Group(net);if(group==null)return (T)field.GetValue(net);
   var result=(IList)Activator.CreateInstance(field.FieldType);var seen=new HashSet<object>();
   foreach(var member in group)foreach(var component in (IList)field.GetValue(member))
    if(seen.Add(component)&&(!(component is ThingComp comp)||comp.parent.Spawned))result.Add(component);
   return (T)result;
  }
  public sealed class Scope : IDisposable
  {
   readonly object net;
   readonly List<KeyValuePair<FieldInfo,object>> saved=new List<KeyValuePair<FieldInfo,object>>();
   public Scope(object net,List<object> group)
   {
    this.net=net;active.Add(net);
    try
    {
     var type=net.GetType();
     if(!fields.TryGetValue(type,out var fs))fields[type]=fs=type.GetFields(BindingFlags.Public|BindingFlags.Instance)
      .Where(f=>typeof(IList).IsAssignableFrom(f.FieldType)).ToArray();
     foreach(var f in fs)
     {
      var combined=(IList)Activator.CreateInstance(f.FieldType);var seen=new HashSet<object>();
      foreach(var member in group)foreach(var component in (IList)f.GetValue(member))
       if(seen.Add(component) && (!(component is ThingComp comp)||comp.parent.Spawned))combined.Add(component);
      saved.Add(new KeyValuePair<FieldInfo,object>(f,f.GetValue(net)));f.SetValue(net,combined);
     }
     if(net is PowerNet pn)
     {
      var f=AccessTools.Field(typeof(PowerNet),nameof(PowerNet.hasPowerSource));
      saved.Add(new KeyValuePair<FieldInfo,object>(f,pn.hasPowerSource));
      pn.hasPowerSource=group.Cast<PowerNet>().Any(n=>n.hasPowerSource);
     }
    }catch{Dispose();throw;}
   }
   public void Dispose(){foreach(var pair in saved)pair.Key.SetValue(net,pair.Value);saved.Clear();active.Remove(net);}
  }
  public static bool Before(object net,string method,out Scope scope)
  {
   scope=null;if(active.Contains(net))return true;
   var group=Group(net);if(group==null)return true;
   if(net is PowerNet && method==nameof(PowerNet.PowerNetTick))
   {
    if(group.Any(n=>powerTicked.Contains(n)))return false;
    foreach(var n in group)powerTicked.Add(n);
   }
   if(!(net is PowerNet))
   {
    // InitNet repopulates the local lists and must run before the temporary union.
    var dirty=AccessTools.Field(NetType,"dirty");
    foreach(var n in group)if((bool)dirty.GetValue(n))
    {AccessTools.Method(NetType,"InitNet").Invoke(n,null);dirty.SetValue(n,false);}
   }
   scope=new Scope(net,group);return true;
  }
 }
 [HarmonyPatch]
 public static class BunkerPowerNetworks
 {
  static IEnumerable<MethodBase> TargetMethods()
  {
   foreach(var name in new[]{"PowerNetTick","CurrentEnergyGainRate","CurrentStoredEnergy","CanPowerNow","get_HasActivePowerSource"})yield return AccessTools.Method(typeof(PowerNet),name);
  }
  static bool Prefix(PowerNet __instance,MethodBase __originalMethod,out UtilityLinks.Scope __state)=>UtilityLinks.Before(__instance,__originalMethod.Name,out __state);
  static Exception Finalizer(Exception __exception,UtilityLinks.Scope __state){__state?.Dispose();return __exception;}
 }
 [HarmonyPatch]
 public static class BunkerPlumbingNetworks
 {
  static bool Prepare()=>UtilityLinks.NetType!=null;
  static IEnumerable<MethodBase> TargetMethods()=>UtilityLinks.NetType.GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly)
   .Where(m=>m.Name!="InitNet");
  static bool Prefix(object __instance,MethodBase __originalMethod,out UtilityLinks.Scope __state)=>UtilityLinks.Before(__instance,__originalMethod.Name,out __state);
  static Exception Finalizer(Exception __exception,UtilityLinks.Scope __state){__state?.Dispose();return __exception;}
 }
 // Dubs also reads public lists directly when deciding whether toilets, showers,
 // washing machines, sprinklers and other fixtures work. Preserve their native
 // eligibility rules while providing the same connected resources as PullWater.
 [HarmonyPatch]
 public static class BunkerPlumbingConsumers
 {
  static bool Prepare()=>UtilityLinks.NetType!=null;
  static bool NetworkList(CodeInstruction i)=>i.opcode==OpCodes.Ldfld && i.operand is FieldInfo f &&
   f.DeclaringType==UtilityLinks.NetType && typeof(IList).IsAssignableFrom(f.FieldType);
  static IEnumerable<MethodBase> TargetMethods()
  {
   foreach(var type in UtilityLinks.NetType.Assembly.GetTypes())
   {
    if(type==UtilityLinks.NetType||type.Name=="HygienePipeMapComp")continue;
    foreach(var m in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly))
     if(!m.ContainsGenericParameters&&m.GetMethodBody()!=null&&PatchProcessor.GetOriginalInstructions(m).Any(NetworkList))yield return m;
   }
  }
  static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
  {
   foreach(var i in instructions)
   {
    if(!NetworkList(i)){yield return i;continue;}
    var field=(FieldInfo)i.operand;
    yield return new CodeInstruction(OpCodes.Ldstr,field.Name).MoveLabelsFrom(i).MoveBlocksFrom(i);
    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(UtilityLinks),nameof(UtilityLinks.ReadList)).MakeGenericMethod(field.FieldType));
   }
  }
 }
}
