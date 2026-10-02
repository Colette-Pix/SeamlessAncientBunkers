using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SeamlessAncientBunkers
{
    // Two physical legs: walk to the source, then carry to the recipient. Never expose
    // remote ingredients to a vanilla job or reserve things under RemotePawnScope.
    public static class LinkedLogistics
    {
        public static bool IsLogistics(Intent i) => i != null && (i.purpose == Purpose.FetchBurial || i.purpose == Purpose.Burial || i.purpose == Purpose.FetchMedicalSupply || i.purpose == Purpose.MedicalSupply);
        static bool Medical(Intent i) => i.purpose == Purpose.FetchMedicalSupply || i.purpose == Purpose.MedicalSupply;
        public static bool Enabled(Intent i) => i != null && BunkerMod.Settings.work &&
            (Medical(i) || BunkerMod.Settings.haul) && i.giver != null && WorkProbe.Allowed(i.pawn, i.giver.Worker);
        static bool Free(Pawn p, Thing t) => t.Spawned && !t.Fogged() && !t.IsForbidden(p) &&
            Portal.Allowed(p, t.Map, t.Position) && p.CanReserveAndReach(t, PathEndMode.ClosestTouch, Danger.None);
        static bool Claimed(Thing recipient) => Manager.Current.intents.Any(i => i.deliveryTarget == recipient && i.expires > Manager.Now);
        public static bool GraveReady(Pawn p, Building_Grave grave, Corpse corpse) => corpse != null && corpse.CanBeBuried() &&
            grave.Spawned && grave.Faction == p.Faction && grave.HaulDestinationEnabled && grave.Accepts(corpse) && Free(p, grave);

        public static Job Plan(Pawn p, WorkGiver giver, List<Route> routes)
        {
            bool medical = giver is WorkGiver_DoBill && giver.def.workType == WorkTypeDefOf.Doctor;
            bool burial = giver is WorkGiver_HaulGeneral && BunkerMod.Settings.haul;
            if ((!medical && !burial) || routes.Count == 0) return null;
            var places = new List<Route> { new Route { map = p.Map, landing = p.Position } };
            places.AddRange(routes.GroupBy(r => r.map).Select(g => g.First()));
            int budget = BunkerMod.Settings.maxCandidates;
            foreach (var destination in places)
            {
                var recipients = medical
                    ? destination.map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver).Where(t => t is Pawn patient && patient != p && patient.BillStack.Bills.OfType<Bill_Medical>().Any(b => b.ShouldDoNow())).ToList()
                    : destination.map.listerBuildings.allBuildingsColonist.OfType<Building_Grave>().Where(g => !g.HasCorpse).Cast<Thing>().ToList();
                foreach (var recipient in recipients)
                {
                    if (--budget < 0) return null;
                    if (Claimed(recipient)) continue;
                    using (new RemotePawnScope(p, destination.map, destination.landing))
                    {
                        if (!Free(p, recipient)) continue;
                        if (medical && !((WorkGiver_DoBill)giver).ThingIsUsableBillGiver(recipient)) continue;
                    }
                    foreach (var source in places.Where(s => s.map != destination.map))
                    {
                        var items = source.map.listerThings.ThingsInGroup(burial ? ThingRequestGroup.Corpse : ThingRequestGroup.HaulableEver);
                        foreach (var item in items.Where(t => !medical || ((Pawn)recipient).BillStack.Bills.OfType<Bill_Medical>().Any(b =>
                            b.uniqueRequiredIngredients?.Contains(t) == true || b.xenogerm == t || b.recipe.ingredients.Any(ing => ing.filter.Allows(t)) && Ingredient(p, (Pawn)recipient, b, t)))
                            .OrderByDescending(t => t.def.IsMedicine ? t.GetStatValue(StatDefOf.MedicalPotency) : 0).ThenBy(t => t.Position.DistanceToSquared(source.landing)))
                        {
                            if (--budget < 0) return null;
                            if (Manager.Current.Claimed(item)) continue;
                            int count;
                            using (new RemotePawnScope(p, source.map, source.landing))
                                if (!Free(p, item) || !Haulable(p, item)) continue;
                            using (new RemotePawnScope(p, destination.map, destination.landing))
                            {
                                if (burial) count = GraveReady(p, (Building_Grave)recipient, item as Corpse) ? 1 : 0;
                                else count = Needed(p, (Pawn)recipient, item);
                            }
                            if (count <= 0) continue;
                            // Do not remove a corpse from valid local storage for a worse grave.
                            if (burial && StoreUtility.CurrentStoragePriorityOf(item) >= ((Building_Grave)recipient).GetStoreSettings().Priority) continue;
                            return Begin(p, item, recipient, giver.def, medical, count, source);
                        }
                    }
                }
            }
            return null;
        }

        static bool Haulable(Pawn p, Thing item) => item.def.EverHaulable && !item.IsBurning() &&
            p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) && p.carryTracker.MaxStackSpaceEver(item.def) > 0;

        static bool Ingredient(Pawn doctor, Pawn patient, Bill_Medical bill, Thing item) =>
            bill.IsFixedOrAllowedIngredient(item) && (!item.def.IsMedicine || patient.playerSettings == null || patient.playerSettings.medCare.AllowsMedicine(item.def));

        // Called with the doctor on the patient's map, so availability honors real local
        // reachability/reservations and the recipe's ingredient-mixing rule.
        public static int Needed(Pawn doctor, Pawn patient, Thing item)
        {
            if (patient.Dead || !patient.Spawned || patient.BillStack.Count == 0) return 0;
            int result = 0;
            foreach (var bill in patient.BillStack.Bills.OfType<Bill_Medical>())
            {
                if (!bill.ShouldDoNow() || !bill.CompletableEver || !bill.PawnAllowedToStartAnew(doctor) || bill.recipe.FirstSkillRequirementPawnDoesntSatisfy(doctor) != null) continue;
                if (bill.IsSurgeryViolationOnExtraFactionMember(doctor)) continue;
                if (bill.uniqueRequiredIngredients?.Contains(item) == true || bill.xenogerm == item)
                {
                    if (!item.Spawned || item.Map != patient.Map) result = Math.Max(result, 1);
                    continue;
                }
                if (!Ingredient(doctor, patient, bill, item)) continue;
                foreach (var ingredient in bill.recipe.ingredients)
                {
                    if (!ingredient.filter.Allows(item)) continue;
                    float required = ingredient.CountRequiredOfFor(item.def, bill.recipe, bill);
                    if (required <= 0) continue;
                    float available = 0;
                    var byDef = new Dictionary<ThingDef, float>();
                    foreach (var local in patient.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
                    {
                        if (!ingredient.filter.Allows(local) || !Ingredient(doctor, patient, bill, local) || !Free(doctor, local)) continue;
                        if (local.Position.DistanceToSquared(patient.Position) > bill.ingredientSearchRadius * bill.ingredientSearchRadius && !local.def.IsMedicine) continue;
                        float perBatch = ingredient.CountRequiredOfFor(local.def, bill.recipe, bill);
                        if (perBatch > 0)
                        {
                            float fraction = local.stackCount / perBatch;
                            available += fraction;
                            byDef.TryGetValue(local.def, out var previous);
                            byDef[local.def] = previous + fraction;
                        }
                    }
                    if (!bill.recipe.allowMixingIngredients)
                        available = byDef.Values.Any(v => v >= 1) ? 1 : byDef.TryGetValue(item.def, out var same) ? same : 0;
                    result = Math.Max(result, Mathf.CeilToInt(required * Math.Max(0, 1 - available)));
                }
            }
            return result;
        }

        static Job Begin(Pawn p, Thing item, Thing recipient, WorkGiverDef giver, bool medical, int count, Route source = null)
        {
            bool fetch = item.Map != p.Map;
            Route route = fetch ? source ?? Graph.Reachable(p).FirstOrDefault(r => r.map == item.Map)
                : Graph.Reachable(p).FirstOrDefault(r => r.map == recipient.Map);
            if (route == null) return null;
            var purpose = medical ? (fetch ? Purpose.FetchMedicalSupply : Purpose.MedicalSupply) : (fetch ? Purpose.FetchBurial : Purpose.Burial);
            var job = Broker.Begin(p, route, purpose, item, giver: giver, count: Math.Min(count, Math.Min(item.stackCount, p.carryTracker.MaxStackSpaceEver(item.def))));
            if (job != null) Broker.For(p).deliveryTarget = recipient;
            return job;
        }

        public static Job Arrive(Intent i)
        {
            var p = i.pawn;
            var recipient = i.deliveryTarget;
            if (recipient?.Spawned != true || !Enabled(i)) return null;
            if (i.purpose == Purpose.FetchBurial || i.purpose == Purpose.FetchMedicalSupply)
            {
                if (i.target?.Map != p.Map || !Free(p, i.target)) return null;
                var route = Graph.Reachable(p).FirstOrDefault(r => r.map == recipient.Map);
                if (route == null) return null;
                int count;
                using (new RemotePawnScope(p, route.map, route.landing))
                    count = !Free(p, recipient) ? 0 : Medical(i) ? Needed(p, (Pawn)recipient, i.target) : GraveReady(p, (Building_Grave)recipient, i.target as Corpse) ? 1 : 0;
                return count > 0 ? Begin(p, i.target, recipient, i.giver, Medical(i), count) : null;
            }
            var cargo = p.carryTracker.CarriedThing;
            if (cargo == null || recipient.Map != p.Map || !Free(p, recipient)) return null;
            if (!Medical(i))
                return GraveReady(p, (Building_Grave)recipient, cargo as Corpse) ? HaulAIUtility.HaulToContainerJob(p, cargo, recipient) : null;
            if (Needed(p, (Pawn)recipient, cargo) <= 0) return null;
            foreach (var cell in GenRadial.RadialCellsAround(recipient.Position, 3, true))
            {
                if (!cell.InBounds(p.Map) || !Portal.Allowed(p, p.Map, cell) || cell.Fogged(p.Map) || !cell.Standable(p.Map) ||
                    !StoreUtility.IsGoodStoreCell(cell, p.Map, cargo, p, p.Faction) || !p.CanReserveAndReach(cell, PathEndMode.OnCell, Danger.None)) continue;
                var job = JobMaker.MakeJob(JobDefOf.HaulToCell, cargo, cell);
                job.count = cargo.stackCount; job.haulMode = HaulMode.ToCellNonStorage;
                return job;
            }
            return null;
        }
    }

    // Patients must recognize a reachable doctor before vanilla will put them in bed.
    // Keep the original local result and extend only the connected, traversable case.
    [HarmonyPatch(typeof(WorkGiver_PatientGoToBedTreatment), nameof(WorkGiver_PatientGoToBedTreatment.AnyAvailableDoctorFor))]
    public static class LinkedPatientDoctor
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        {
            if (__result || RemotePawnScope.Active || Manager.Current == null || !BunkerMod.Settings.enabled || !BunkerMod.Settings.work || pawn.MapHeld == null) return;
            foreach (var doctor in LinkedResources.Maps(pawn.MapHeld).Where(m => m != pawn.MapHeld)
                .SelectMany(m => m.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)).Take(BunkerMod.Settings.maxCandidates))
            {
                if (!Portal.Eligible(doctor, true) || !doctor.Awake() || doctor.InBed() || doctor.IsPrisoner ||
                    doctor.workSettings == null || !doctor.workSettings.EverWork || !doctor.workSettings.WorkIsActive(WorkTypeDefOf.Doctor) ||
                    !doctor.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) continue;
                var route = Graph.Reachable(doctor).FirstOrDefault(r => r.map == pawn.MapHeld);
                if (route == null) continue;
                using (new RemotePawnScope(doctor, route.map, route.landing))
                    if (Portal.Allowed(doctor, route.map, pawn.PositionHeld) && !pawn.IsForbidden(doctor) && doctor.CanReach(pawn, PathEndMode.Touch, Danger.None))
                    { __result = true; return; }
            }
        }
    }
}
