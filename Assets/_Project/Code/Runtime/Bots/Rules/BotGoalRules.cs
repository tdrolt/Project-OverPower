using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Bots
{
    /// <summary>
    /// Where a Conquest bot goes when no fight holds it: its threatened capital; the centre just before a scan after the shrink; an enemy
    /// capital it may take with enough of its team near; else the closest zone its team may capture, a Tier III or bounty zone counted
    /// closer. "from" is the bot, or the human teammate it sticks with.
    /// </summary>
    public static class ConquestGoalRules
    {
        public struct Settings
        {
            public float TierThreeBonusMetres;
            public float BountyBonusMetres;
            public int PushCapitalTeammates;
        }

        public static int Choose(IReadOnlyList<ConquestZone> zones, Vector3 from, bool centreUrgent, Settings settings)
        {
            for (int i = 0; i < zones.Count; i++)
                if (zones[i].IsOwnCapital && zones[i].OwnCapitalThreatened) return zones[i].Id;

            if (centreUrgent)
                for (int i = 0; i < zones.Count; i++)
                    if (zones[i].IsCentre) return zones[i].Id;

            int push = -1;
            float pushDistance = float.MaxValue;
            for (int i = 0; i < zones.Count; i++)
            {
                ConquestZone z = zones[i];
                if (!z.IsEnemyCapital || !z.MayCapture || z.TeammatesNear < settings.PushCapitalTeammates) continue;
                float d = Flat(from, z.Centre);
                if (d < pushDistance) { pushDistance = d; push = z.Id; }
            }
            if (push >= 0) return push;

            int best = -1;
            float bestScore = float.MaxValue;
            for (int i = 0; i < zones.Count; i++)
            {
                ConquestZone z = zones[i];
                if (!z.MayCapture || z.IsEnemyCapital || z.IsOwnCapital) continue;
                float score = Flat(from, z.Centre);
                if (z.Tier >= 3) score -= settings.TierThreeBonusMetres;
                if (z.Bounty) score -= settings.BountyBonusMetres;
                if (score < bestScore) { bestScore = score; best = z.Id; }
            }
            return best;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }

    /// <summary>
    /// Where a Dominion bot goes, for 2v2 and 3v3v3 alike: the centre just before its payout (when the mode has one); a zone of its team
    /// being taken; the best-paying zone it does not hold, closest first; when it holds them all, its best-paying zone.
    /// </summary>
    public static class DominionGoalRules
    {
        public static int Choose(IReadOnlyList<DominionZone> zones, Vector3 from, bool centreSoon)
        {
            if (centreSoon)
                for (int i = 0; i < zones.Count; i++)
                    if (zones[i].IsCentre) return zones[i].Id;

            int defend = Closest(zones, from, z => z.Ours && z.BeingTakenFromUs);
            if (defend >= 0) return defend;

            int take = BestPaying(zones, from, z => z.Capturable && !z.Ours && !z.IsCentre);
            if (take >= 0) return take;

            return BestPaying(zones, from, z => z.Ours && !z.IsCentre);
        }

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

        private static int BestPaying(IReadOnlyList<DominionZone> zones, Vector3 from, System.Func<DominionZone, bool> wanted)
        {
            int best = -1;
            float bestPoints = float.MinValue, bestDistance = float.MaxValue;
            for (int i = 0; i < zones.Count; i++)
            {
                DominionZone z = zones[i];
                if (!wanted(z)) continue;
                float d = Flat(from, z.Centre);
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
