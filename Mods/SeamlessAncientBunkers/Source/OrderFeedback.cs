using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SeamlessAncientBunkers
{
    public static class OrderFeedback
    {
        public static void Accepted(Map map, IntVec3 cell)
        {
            if (map != null && cell.InBounds(map))
                FleckMaker.Static(cell, map, FleckDefOf.FeedbackGoto);
        }

        // Use the saved route, never the remote pawn's coordinates on the viewed map.
        public static bool DestinationSegment(Pawn pawn, Map map, out Vector3 from, out Vector3 to)
        {
            from = to = default;
            var intent = Broker.For(pawn);
            if (pawn?.Spawned != true || pawn.Dead || pawn.Map == map || map == null ||
                intent == null || !intent.forced || intent.expires <= Manager.Now ||
                intent.finalMap != map || intent.route == null || intent.route.Count == 0) return false;
            var entrance = Portal.Other(intent.route.Last());
            if (entrance?.Spawned != true || entrance.Map != map) return false;
            // Haul targets remain on the source level; their destination is the storage cell.
            if (!Broker.Transport(intent) && intent.target != null)
            {
                if (!intent.target.Spawned || intent.target.Map != map) return false;
                to = intent.target.DrawPos;
            }
            else
            {
                if (!intent.cell.InBounds(map)) return false;
                to = intent.cell.ToVector3Shifted();
            }
            from = entrance.DrawPos;
            return true;
        }

        public static void Draw()
        {
            if (Find.CurrentMap == null || Find.ScreenshotModeHandler.Active) return;
            foreach (var pawn in Find.Selector.SelectedObjects.OfType<Pawn>())
                if (DestinationSegment(pawn, Find.CurrentMap, out var from, out var to))
                    GenDraw.DrawLineBetween(from, to, AltitudeLayer.Item.AltitudeFor());
        }
    }
}
