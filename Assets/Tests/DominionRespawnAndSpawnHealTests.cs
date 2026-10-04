using NUnit.Framework;
using UnityEngine;
using Overpower.Dominion;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 6: the fixed respawn wait and the spawn's healing. Every number is a literal made up for the test, never the asset's.</summary>
    public class DominionRespawnAndSpawnHealTests
    {
        // ---- the respawn wait

        [Test] public void InDominionEveryDeathWaitsTheSameFixedTime()
        {
            float first = RespawnDelayRules.DelayFor(true, 7f, 1, 5f, 1f, 10f);
            float fifth = RespawnDelayRules.DelayFor(true, 7f, 5, 5f, 1f, 10f);
            Assert.AreEqual(7f, first);
            Assert.AreEqual(first, fifth);
        }

        [Test] public void InConquestTheWaitGrowsWithEachDeathUpToTheCap()
        {
            Assert.AreEqual(5f, RespawnDelayRules.DelayFor(false, 7f, 1, 5f, 1f, 10f));
            Assert.AreEqual(8f, RespawnDelayRules.DelayFor(false, 7f, 4, 5f, 1f, 10f));
            Assert.AreEqual(10f, RespawnDelayRules.DelayFor(false, 7f, 9, 5f, 1f, 10f));
        }

        [Test] public void ARejoinerInDominionWaitsTheFixedTimeAndInConquestTheFlatRejoinTime()
        {
            Assert.AreEqual(7f, RespawnDelayRules.RejoinDelayFor(true, 7f, 3f));
            Assert.AreEqual(3f, RespawnDelayRules.RejoinDelayFor(false, 7f, 3f));
        }

        [Test] public void TheFixedWaitFollowsTheMatchSize()
        {
            Assert.AreEqual(6f, DominionHealRules.RespawnSeconds(2, 6f, 9f));
            Assert.AreEqual(9f, DominionHealRules.RespawnSeconds(3, 6f, 9f));
        }

        // ---- the heal rate (out of combat 10/s after 6 s, in combat 4/s)

        private static float Rate(bool inOwnSpawn, float sinceCombat) => DominionHealRules.RatePerSecond(inOwnSpawn, sinceCombat, 10f, 4f, 6f);

        [Test] public void InTheOwnSpawnAndInCombatTheInCombatRateHeals() => Assert.AreEqual(4f, Rate(true, 0f));
        [Test] public void InTheOwnSpawnOutOfCombatBeforeTheDelayStillTheInCombatRate() => Assert.AreEqual(4f, Rate(true, 5.9f));
        [Test] public void InTheOwnSpawnOutOfCombatPastTheDelayTheFasterRateHeals()
        {
            Assert.AreEqual(10f, Rate(true, 6f), "exactly the delay counts");
            Assert.AreEqual(10f, Rate(true, 30f));
        }

        [Test] public void OutsideTheOwnSpawnTheSpawnRatesGiveNothing()
        {
            Assert.AreEqual(0f, Rate(false, 0f));
            Assert.AreEqual(0f, Rate(false, 30f), "an enemy's spawn or open ground is not this player's spawn");
        }

        [Test] public void OutsideTheSpawnTheOrdinaryZoneRegenApplies()
        {
            Assert.AreEqual(2f, DominionHealRules.HealRate(false, 30f, 10f, 4f, 6f, 2f));
            Assert.AreEqual(4f, DominionHealRules.HealRate(true, 0f, 10f, 4f, 6f, 2f), "in the spawn the spawn's rate replaces the zone regen");
            Assert.AreEqual(0f, DominionHealRules.HealRate(false, 0f, 10f, 4f, 6f, 0f));
        }

        // ---- the spawn area

        private static SpawnHealArea MakeArea(int team, SpawnHealArea.AreaShape shape, Vector3 at, float yaw, Vector2 box, float radius)
        {
            var go = new GameObject("test spawn area");
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var area = go.AddComponent<SpawnHealArea>();
            area.Configure(team, shape, box, radius);
            SpawnHealArea.Register(area); // edit-mode tests do not run OnEnable
            return area;
        }

        private static void Remove(SpawnHealArea area)
        {
            SpawnHealArea.Unregister(area);
            Object.DestroyImmediate(area.gameObject);
        }

        [Test] public void ABoxContainsItsInsideAndNotItsOutsideAndIgnoresHeight()
        {
            SpawnHealArea area = MakeArea(0, SpawnHealArea.AreaShape.Box, new Vector3(10f, 0f, 20f), 0f, new Vector2(8f, 4f), 1f);
            try
            {
                Assert.IsTrue(SpawnHealArea.Contains(0, new Vector3(10f, 50f, 20f)), "centre, any height");
                Assert.IsTrue(SpawnHealArea.Contains(0, new Vector3(13.9f, 0f, 21.9f)), "inside the corner");
                Assert.IsFalse(SpawnHealArea.Contains(0, new Vector3(14.2f, 0f, 20f)), "just outside across");
                Assert.IsFalse(SpawnHealArea.Contains(0, new Vector3(10f, 0f, 22.2f)), "just outside along");
            }
            finally { Remove(area); }
        }

        [Test] public void ATurnedBoxTurnsItsLongSide()
        {
            // 8 wide across its right, 2 deep; turned 90 degrees its wide side runs along world Z.
            SpawnHealArea area = MakeArea(1, SpawnHealArea.AreaShape.Box, Vector3.zero, 90f, new Vector2(8f, 2f), 1f);
            try
            {
                Assert.IsTrue(SpawnHealArea.Contains(1, new Vector3(0.5f, 0f, 3.5f)));
                Assert.IsFalse(SpawnHealArea.Contains(1, new Vector3(3.5f, 0f, 0.5f)));
            }
            finally { Remove(area); }
        }

        [Test] public void ACircleContainsItsInsideAndTheEdgeButNotBeyond()
        {
            SpawnHealArea area = MakeArea(2, SpawnHealArea.AreaShape.Circle, new Vector3(-5f, 3f, 5f), 0f, Vector2.one, 5f);
            try
            {
                Assert.IsTrue(SpawnHealArea.Contains(2, new Vector3(-5f, 0f, 5f)));
                Assert.IsTrue(SpawnHealArea.Contains(2, new Vector3(-1f, 0f, 5f)), "4 m out");
                Assert.IsTrue(SpawnHealArea.Contains(2, new Vector3(0f, 0f, 5f)), "the edge");
                Assert.IsFalse(SpawnHealArea.Contains(2, new Vector3(0.1f, 0f, 5f)));
            }
            finally { Remove(area); }
        }

        [Test] public void AnAreaOnlyCountsForItsOwnTeam()
        {
            SpawnHealArea area = MakeArea(0, SpawnHealArea.AreaShape.Circle, Vector3.zero, 0f, Vector2.one, 5f);
            try
            {
                Assert.IsTrue(SpawnHealArea.Contains(0, Vector3.zero));
                Assert.IsFalse(SpawnHealArea.Contains(1, Vector3.zero), "the other team's player in this spawn");
                Assert.IsFalse(SpawnHealArea.Contains(2, Vector3.zero));
            }
            finally { Remove(area); }
        }

        [Test] public void AnUnregisteredAreaIsGone()
        {
            SpawnHealArea area = MakeArea(0, SpawnHealArea.AreaShape.Circle, Vector3.zero, 0f, Vector2.one, 5f);
            Remove(area);
            Assert.IsFalse(SpawnHealArea.Contains(0, Vector3.zero));
        }
    }
}
