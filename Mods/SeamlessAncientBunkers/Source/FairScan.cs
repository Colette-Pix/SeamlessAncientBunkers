using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SeamlessAncientBunkers
{
 // Cursors are query state, not colony state. Every bounded query advances, including
 // unsuccessful queries. Separate streams prevent busy scanners starving quiet ones.
 public static class FairScan
 {
  static Game game;
  static readonly Dictionary<string,int> cursors=new Dictionary<string,int>();
  public static IEnumerable<T> Window<T>(IEnumerable<T> source,int budget,string key)
  {
   if(game!=Current.Game){game=Current.Game;cursors.Clear();}
   var list=source as IList<T>??source.ToList();
   if(list.Count==0||budget<=0)return Enumerable.Empty<T>();
   cursors.TryGetValue(key,out int start);start%=list.Count;
   int count=Math.Min(budget,list.Count);
   cursors[key]=(start+count)%list.Count;
   var result=new List<T>(count);
   for(int n=0;n<count;n++)result.Add(list[(start+n)%list.Count]);
   return result;
  }
  public static string Key(Pawn p,string stream,Map map=null)=>p.thingIDNumber+":"+stream+":"+(map?.uniqueID??-1);
 }
}
