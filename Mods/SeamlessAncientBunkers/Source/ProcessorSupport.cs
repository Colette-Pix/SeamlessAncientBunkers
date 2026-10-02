using System;
using System.Collections;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
namespace SeamlessAncientBunkers
{
 public static class ProcessorSupport
 {
  public static ThingComp Comp(Thing target)=>(target as ThingWithComps)?.AllComps.FirstOrDefault(c=>c.GetType().FullName=="ProcessorFramework.CompProcessor");
  public static int Needed(Thing target,Thing item,Func<ThingDef,int> available)
  {
   var comp=Comp(target);if(comp==null)return 0;
   var type=comp.GetType();var enabled=AccessTools.Field(type,"enabledProcesses").GetValue(comp) as IDictionary;
   if(enabled==null)return 0;
   foreach(DictionaryEntry entry in enabled)
   {
    var allowed=(AccessTools.Field(entry.Value.GetType(),"allowedIngredients").GetValue(entry.Value) as IEnumerable).Cast<ThingDef>().ToList();
    if(!allowed.Contains(item.def))continue;
    int capacity=(int)AccessTools.Method(type,"SpaceLeftFor").Invoke(comp,new object[]{entry.Key,1f});
    // Quantity limits include already staged and in-flight alternatives for this process.
    float supplied=allowed.Sum(d=>available(d));
    if((bool)AccessTools.Field(entry.Key.GetType(),"useStatForEfficiency").GetValue(entry.Key))
    {
     var stat=(StatDef)AccessTools.Field(entry.Key.GetType(),"efficiencyStat").GetValue(entry.Key);
     float baseline=(float)AccessTools.Field(entry.Key.GetType(),"statBaselineValue").GetValue(entry.Key);
     if(baseline<=0)return 0;
     supplied=allowed.Sum(d=>available(d)*d.GetStatValueAbstract(stat)/baseline);
     float factor=item.GetStatValue(stat)/baseline;if(factor<=0)return 0;
     return Math.Max(0,(int)Math.Ceiling((capacity-supplied)/factor));
    }
    return Math.Max(0,capacity-(int)supplied);
   }
   return 0;
  }
 }
}
