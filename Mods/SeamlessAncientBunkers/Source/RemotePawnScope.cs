using System;
using HarmonyLib;
using Verse;

namespace SeamlessAncientBunkers
{
    // Synchronous read-only query context. Never tick, spawn, reserve or start jobs here.
    // Raw fields avoid Position's registration/notification setter. Always use with 'using'.
    public sealed class RemotePawnScope : IDisposable
    {
        private static readonly AccessTools.FieldRef<Thing, sbyte> MapIndex = AccessTools.FieldRefAccess<Thing, sbyte>("mapIndexOrState");
        private static readonly AccessTools.FieldRef<Thing, IntVec3> Position = AccessTools.FieldRefAccess<Thing, IntVec3>("positionInt");
        [ThreadStatic] private static int depth;
        public static bool Active => depth > 0;
        private readonly Pawn pawn;
        private readonly sbyte oldMap;
        private readonly IntVec3 oldPosition;
        private bool disposed;
        public RemotePawnScope(Pawn pawn, Map map, IntVec3 position)
        {
            int index = Find.Maps.IndexOf(map);
            if (index < 0 || index > 127 || !pawn.Spawned || !position.InBounds(map)) throw new ArgumentException("Invalid remote query context");
            this.pawn = pawn; oldMap = MapIndex(pawn); oldPosition = Position(pawn);
            MapIndex(pawn) = (sbyte)index; Position(pawn) = position; depth++;
        }
        public void Dispose()
        {
            if (disposed) return;
            MapIndex(pawn) = oldMap; Position(pawn) = oldPosition; depth--; disposed = true;
        }
    }
}
