using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Bots
{
    /// <summary>
    /// Where a Conquest bot goes when no fight holds it: its threatened capital, unless it is finishing a capture it keeps on with (a capital
    /// trade); the centre just before a scan after the shrink; the free zone it stands on (BotCaptureRules), or the enemy capital it stands in
    /// and may take; a zone of its team being taken close by; an enemy capital it pushes anyway (exposed, or next to the Tier II it holds);
    /// the best free zone; an enemy capital it may take with enough of its team near; else the best zone its team may capture. "Best" is the closest, a Tier III or bounty zone counted closer, a zone with enemies known near it counted
    /// further (it pushes where nobody defends), and a zone other bots of its team head for counted further (always for free zones, so
    /// they split over them; for the others only while spreading out). "from" is the bot, or the teammate it sticks with.
    /// </summary>
    public static class ConquestGoalRules
    {
        public struct Settings
        {
            public float TierThreeBonusMetres;
            public float BountyBonusMetres;
            public int PushCapitalTeammates;
            public float EnemyNearPenaltyMetres;
            public float DefendZoneMetres;
            /// <summary>Per other bot of its team heading for a zone, while spreading out; 0 otherwise.</summary>
            public float SpreadMetres;
            /// <summary>Per other bot of its team heading for a free zone, always (the larger of this and SpreadMetres counts).</summary>
            public float FreeZoneSpreadMetres;
        }

        public static int Choose(IReadOnlyList<ConquestZone> zones, Vector3 from, bool centreUrgent, Settings settings)
        {
            for (int i = 0; i < zones.Count; i++)
                if (zones[i].IsOwnCapital && zones[i].OwnCapitalThreatened) return KeptOn(zones, zones[i].Id);

            if (centreUrgent)
                for (int i = 0; i < zones.Count; i++)
                    if (zones[i].IsCentre) return zones[i].Id;

            for (int i = 0; i < zones.Count; i++)
                if (BotCaptureRules.StayOnZone(zones[i].Standing, Free(zones[i]))) return zones[i].Id;
            // A capital capture under way is finished first: it is how a team ends the game.
            for (int i = 0; i < zones.Count; i++)
                if (zones[i].Standing && zones[i].IsEnemyCapital && zones[i].MayCapture) return zones[i].Id;

            int defend = -1;
            float defendDistance = float.MaxValue;
            for (int i = 0; i < zones.Count; i++)
            {
                ConquestZone z = zones[i];
                if (!z.OursUnderAttack) continue;
                float d = Flat(from, z.Centre);
                if (d <= settings.DefendZoneMetres && d < defendDistance) { defendDistance = d; defend = z.Id; }
            }
            if (defend >= 0) return defend;

            int forced = ClosestPush(zones, from, settings, true);
            if (forced >= 0) return forced;

            int free = Best(zones, from, settings, Mathf.Max(settings.SpreadMetres, settings.FreeZoneSpreadMetres), true);
            if (free >= 0) return free;

            int push = ClosestPush(zones, from, settings, false);
            if (push >= 0) return push;

            return Best(zones, from, settings, settings.SpreadMetres, false);
        }

        /// <summary>The closest enemy capital its team may take that it pushes anyway (anywayOnly) or with enough of its team near; -1 when none.</summary>
        private static int ClosestPush(IReadOnlyList<ConquestZone> zones, Vector3 from, Settings settings, bool anywayOnly)
        {
            int push = -1;
            float pushDistance = float.MaxValue;
            for (int i = 0; i < zones.Count; i++)
            {
                ConquestZone z = zones[i];
                if (!z.IsEnemyCapital || !z.MayCapture) continue;
                if (anywayOnly ? !z.PushAnyway : z.TeammatesNear < settings.PushCapitalTeammates && !z.PushAnyway) continue;
                float d = Flat(from, z.Centre);
                if (d < pushDistance) { pushDistance = d; push = z.Id; }
            }
            return push;
        }

        private static bool Free(ConquestZone z) => BotCaptureRules.FreeZone(z.MayCapture, z.IsOwnCapital || z.IsEnemyCapital, z.EnemyOnIt);

        /// <summary>The best-scored zone its team may capture (capitals left out), only free ones when freeOnly; -1 when none.</summary>
        private static int Best(IReadOnlyList<ConquestZone> zones, Vector3 from, Settings settings, float headingMetres, bool freeOnly)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < zones.Count; i++)
            {
                ConquestZone z = zones[i];
                if (!z.MayCapture || z.IsEnemyCapital || z.IsOwnCapital) continue;
                if (freeOnly && !Free(z)) continue;
                float score = Flat(from, z.Centre) + settings.EnemyNearPenaltyMetres * z.EnemiesNear + headingMetres * z.TeammatesHeading;
                if (z.Tier >= 3) score -= settings.TierThreeBonusMetres;
                if (z.Bounty) score -= settings.BountyBonusMetres;
                if (score < bestScore) { bestScore = score; best = z.Id; }
            }
            return best;
        }

        /// <summary>The zone whose capture the bot keeps on with, else otherwise.</summary>
        private static int KeptOn(IReadOnlyList<ConquestZone> zones, int otherwise)
        {
            for (int i = 0; i < zones.Count; i++)
                if (zones[i].KeepTaking) return zones[i].Id;
            return otherwise;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }

    /// <summary>
    /// Where a Dominion bot goes, for 2v2 and 3v3v3 alike: the centre just before its payout (when the mode has one); the free zone it
    /// stands on (BotCaptureRules); a zone of its team being taken; the best-paying free zone; the best-paying zone it does not hold; when
    /// it holds them all, its best-paying zone. Of equally paying zones the closest, each enemy known near a zone counting
    /// enemyNearPenaltyMetres further (it takes the undefended one), and each other bot of its team heading for a zone counting further:
    /// freeZoneSpreadMetres for a free zone always (the larger of the two while spreading out), spreadMetres for the others while spreading out.
    /// </summary>
    public static class DominionGoalRules
    {
        public static int Choose(IReadOnlyList<DominionZone> zones, Vector3 from, bool centreSoon, float enemyNearPenaltyMetres, float spreadMetres = 0f,
            float freeZoneSpreadMetres = 0f)
        {
            if (centreSoon)
                for (int i = 0; i < zones.Count; i++)
                    if (zones[i].IsCentre) return zones[i].Id;

            for (int i = 0; i < zones.Count; i++)
                if (BotCaptureRules.StayOnZone(zones[i].Standing, Free(zones[i]))) return zones[i].Id;

            int defend = Closest(zones, from, z => z.Ours && z.BeingTakenFromUs);
            if (defend >= 0) return defend;

            int free = BestPaying(zones, from, enemyNearPenaltyMetres, Mathf.Max(spreadMetres, freeZoneSpreadMetres), Free);
            if (free >= 0) return free;

            int take = BestPaying(zones, from, enemyNearPenaltyMetres, spreadMetres, z => z.Capturable && !z.Ours && !z.IsCentre);
            if (take >= 0) return take;

            return BestPaying(zones, from, 0f, spreadMetres, z => z.Ours && !z.IsCentre);
        }

        private static bool Free(DominionZone z) => BotCaptureRules.FreeZone(z.Capturable && !z.Ours, z.IsCentre, z.EnemyOnIt);

        public static bool InsideCircle(Vector2 position, Vector2 centre, float radius, float margin) =>
            Vector2.Distance(position, centre) <= Mathf.Max(0f, radius - margin);

        private static int Closest(IReadOnlyList<DominionZone> zones, Vector3 from, System.Func<DominionZone, bool> wanted)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < zones.Count; i++)
            {
                if (!wanted(zones[i])) continue;
                float d = Flat(from, zones[i].Centre);
                if (d < bestDistance) { bestDistance = d; best = zones[i].Id; }
            }
            return best;
        }

        private static int BestPaying(IReadOnlyList<DominionZone> zones, Vector3 from, float enemyNearPenaltyMetres, float spreadMetres,
            System.Func<DominionZone, bool> wanted)
        {
            int best = -1;
            float bestPoints = float.MinValue, bestDistance = float.MaxValue;
            for (int i = 0; i < zones.Count; i++)
            {
                DominionZone z = zones[i];
                if (!wanted(z)) continue;
                float d = Flat(from, z.Centre) + enemyNearPenaltyMetres * z.EnemiesNear + spreadMetres * z.TeammatesHeading;
                if (z.PointsPerSecond > bestPoints + 1e-4f || (Mathf.Abs(z.PointsPerSecond - bestPoints) <= 1e-4f && d < bestDistance))
                {
                    bestPoints = z.PointsPerSecond;
                    bestDistance = d;
                    best = z.Id;
                }
            }
            return best;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }

    /// <summary>
    /// What a bot buys. Conquest: of the build's steps it can take now (the weapon tree one step at a time, armour within the shop's cap),
    /// the dearest it can afford, by list price. Dominion: the loadout each round opens (weapon depth and armour count by round; the three
    /// abilities from the first break, as for players).
    /// </summary>
    public static class BotShopRules
    {
        public static List<ShopStep> Missing(BuildPlan plan, BotLoadout now, int armourCap)
        {
            var steps = new List<ShopStep>();
            if (now.WeaponId == plan.BaselineId && plan.FamilyId != 0) steps.Add(new ShopStep(ShopStepKind.Weapon, plan.FamilyId));
            else if (now.WeaponId == plan.FamilyId && plan.UpgradeId != 0) steps.Add(new ShopStep(ShopStepKind.Weapon, plan.UpgradeId));
            if (plan.AttachmentId != 0 && now.AttachmentId != plan.AttachmentId) steps.Add(new ShopStep(ShopStepKind.Attachment, plan.AttachmentId));
            if (plan.MovementId != 0 && now.MovementId != plan.MovementId) steps.Add(new ShopStep(ShopStepKind.Movement, plan.MovementId));
            if (plan.UltimateId != 0 && now.UltimateId != plan.UltimateId) steps.Add(new ShopStep(ShopStepKind.Ultimate, plan.UltimateId));
            bool armourRoom = now.Absorb + now.Recharge < armourCap;
            if (armourRoom && now.Absorb < plan.Absorb) steps.Add(new ShopStep(ShopStepKind.Absorb, 0));
            if (armourRoom && now.Recharge < plan.Recharge) steps.Add(new ShopStep(ShopStepKind.Recharge, 0));
            return steps;
        }

        /// <summary>The index of the dearest offer (list price) whose price now fits gold; the first of equals; -1 when none.</summary>
        public static int MostExpensiveAffordable(IReadOnlyList<ShopOffer> offers, int gold)
        {
            int best = -1;
            for (int i = 0; i < offers.Count; i++)
            {
                if (offers[i].Price > gold) continue;
                if (best < 0 || offers[i].ListPrice > offers[best].ListPrice) best = i;
            }
            return best;
        }

        public static BotLoadout DominionTarget(BuildPlan plan, int weaponDepth, int armourCount)
        {
            int absorb = Mathf.Min(plan.Absorb, armourCount);
            int recharge = Mathf.Min(plan.Recharge, Mathf.Max(0, armourCount - absorb));
            return new BotLoadout
            {
                WeaponId = weaponDepth >= 2 ? plan.UpgradeId : weaponDepth == 1 ? plan.FamilyId : plan.BaselineId,
                AttachmentId = plan.AttachmentId,
                MovementId = plan.MovementId,
                UltimateId = plan.UltimateId,
                Absorb = absorb,
                Recharge = recharge,
            };
        }
    }
}
