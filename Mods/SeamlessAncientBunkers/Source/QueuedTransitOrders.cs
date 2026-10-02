using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
    public class QueuedTransitOrder : IExposable
    {
        public Pawn pawn;
        public Map map;
        public Job job;
        public Thing workTarget;
        public IntVec3 cell = IntVec3.Invalid;
        public void ExposeData()
        {
            Scribe_References.Look(ref pawn,"pawn"); Scribe_References.Look(ref map,"map");
            Scribe_Deep.Look(ref job,"job"); Scribe_References.Look(ref workTarget,"workTarget");
            Scribe_Values.Look(ref cell,"cell",IntVec3.Invalid);
        }
    }
    public static class QueuedTransitOrders
    {
        public static bool Dispatching;
        public static bool Add(Pawn p, Job job, Map map, Thing target = null, IntVec3? cell = null)
        {
            if (map == null || !Find.Maps.Contains(map)) return false;
            Manager.Current.queuedOrders.Add(new QueuedTransitOrder { pawn=p,job=job,map=map,workTarget=target,cell=cell??job.targetA.Cell });
            OrderFeedback.Accepted(map, cell??job.targetA.Cell); return true;
        }
        public static void Clear(Pawn pawn) => Manager.Current?.queuedOrders.RemoveAll(q=>q.pawn==pawn);
        public static void PreserveNativeQueue(Pawn p)
        {
            var preserved = new List<QueuedTransitOrder>();
            while (p.jobs.jobQueue.Count > 0)
            {
                var q = p.jobs.jobQueue.Dequeue();
                if (q.job.playerForced) preserved.Add(new QueuedTransitOrder { pawn=p,map=q.job.targetA.Thing?.MapHeld??p.Map,job=q.job,cell=q.job.targetA.Cell });
                else q.Cleanup(p,true);
            }
            Manager.Current.queuedOrders.InsertRange(0,preserved);
        }
        public static void Tick()
        {
            var manager=Manager.Current;
            manager.queuedOrders.RemoveAll(q=>q.pawn==null||q.pawn.Dead||q.map==null||!Find.Maps.Contains(q.map)||q.job?.def==null);
            foreach(var group in manager.queuedOrders.GroupBy(q=>q.pawn).ToList())
            {
                var p=group.Key;
                if(!p.Spawned||p.Downed||p.InMentalState||Broker.For(p)!=null||p.CurJob?.playerForced==true||!p.jobs.IsCurrentJobPlayerInterruptible())continue;
                var q=group.First(); manager.queuedOrders.Remove(q);
                try
                {
                    Dispatching=true;
                    bool accepted=LinkedOrders.Issue(p,q.job,q.map,q.workTarget,q.cell,false);
                    if(!accepted&&q.map==p.Map)accepted=p.jobs.TryTakeOrderedJob(q.job);
                    if(!accepted){Clear(p);Messages.Message("Queued cross-level order cancelled: the target or route is unavailable.",p,MessageTypeDefOf.RejectInput,false);}
                }
                finally{Dispatching=false;}
            }
        }
    }
}
