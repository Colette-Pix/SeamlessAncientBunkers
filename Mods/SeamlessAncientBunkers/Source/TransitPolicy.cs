using System;
using System.Linq;
using RimWorld;
using Verse;

namespace SeamlessAncientBunkers
{
    [Flags]
    public enum TravelPermission { None = 0, Emergency = 1, Group = 2, Mental = 4, AnimalWork = 8 }

    // Permissions belong to one particular journey, never to a global pawn/map getter.
    public sealed class TransitPolicy : IDisposable
    {
        [ThreadStatic] static Pawn currentPawn;
        [ThreadStatic] static TravelPermission current;
        readonly Pawn previousPawn;
        readonly TravelPermission previous;
        public TransitPolicy(Pawn pawn, TravelPermission permission)
        { previousPawn = currentPawn; previous = current; currentPawn = pawn; current = permission; }
        public static TravelPermission For(Pawn pawn) => currentPawn == pawn ? current : Broker.For(pawn)?.permission ?? TravelPermission.None;
        public static bool Has(Pawn pawn, TravelPermission permission) => (For(pawn) & permission) != 0;
        public void Dispose() { currentPawn = previousPawn; current = previous; }
        public static bool ClearOfEnemies(Pawn pawn, Map map, IntVec3 cell)
            => !map.attackTargetsCache.TargetsHostileToFaction(pawn.Faction ?? Faction.OfPlayer)
                .Any(t => t.Thing.Spawned && GenHostility.IsActiveThreatTo(t, pawn.Faction ?? Faction.OfPlayer) && t.Thing.Position.DistanceToSquared(cell) < 625);
        public static bool Mech(Pawn p) => p?.IsColonyMech == true && p.GetOverseer()?.Spawned == true;
    }
}
