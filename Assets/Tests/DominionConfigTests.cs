using NUnit.Framework;
using Overpower.Data;
using UnityEditor;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 1: the real DominionConfig asset has the right structure. It checks shapes and signs, never Tudor's numbers.</summary>
    public class DominionConfigTests
    {
        private static DominionConfig Load()
        {
            var config = AssetDatabase.LoadAssetAtPath<DominionConfig>("Assets/Gameplay/Config/DominionConfig.asset");
            Assert.IsNotNull(config, "Assets/Gameplay/Config/DominionConfig.asset exists");
            return config;
        }

        [Test] public void TheZonePointsTableHasOneEntryPerTierAndNoneNegative()
        {
            int[] table = Load().PointsPerZonePerSecond;
            Assert.AreEqual(4, table.Length);
            foreach (int p in table) Assert.GreaterOrEqual(p, 0);
        }

        [Test] public void TheBuildTablesHaveThreeEntriesNoneNegative()
        {
            DominionConfig c = Load();
            Assert.AreEqual(3, c.WeaponDepthByRound.Length);
            Assert.AreEqual(3, c.ArmorUpgradesByRound.Length);
            foreach (int d in c.WeaponDepthByRound) Assert.GreaterOrEqual(d, 0);
            foreach (int a in c.ArmorUpgradesByRound) Assert.GreaterOrEqual(a, 0);
        }

        [Test] public void EachCircleShrinksFromLargerToSmaller()
        {
            DominionConfig c = Load();
            Assert.Less(c.FinalRadius2v2, c.SuddenDeathStartRadius2v2);
            Assert.Less(c.FinalRadius3v3v3, c.SuddenDeathStartRadius3v3v3);
        }

        [Test] public void TheTimesAreSensible()
        {
            DominionConfig c = Load();
            Assert.Greater(c.RoundSeconds, 0f);
            Assert.Greater(c.SuddenDeathShrinkSeconds, 0f);
            Assert.Greater(c.CentrePayoutIntervalSeconds, 0f);
            Assert.GreaterOrEqual(c.BreakSeconds, 0f);
            Assert.LessOrEqual(c.BreakCountdownSeconds, c.BreakSeconds);
            Assert.GreaterOrEqual(c.RoundsToWin, 1);
            Assert.GreaterOrEqual(c.MaxRounds, c.RoundsToWin);
        }

        [Test] public void NothingThatCannotBeNegativeIsNegative()
        {
            DominionConfig c = Load();
            Assert.GreaterOrEqual(c.CentrePayoutPoints, 0);
            Assert.GreaterOrEqual(c.BountyPoints, 0);
            Assert.GreaterOrEqual(c.BountyHoldSeconds, 0f);
            Assert.GreaterOrEqual(c.RespawnSeconds2v2, 0f);
            Assert.GreaterOrEqual(c.RespawnSeconds3v3v3, 0f);
            Assert.GreaterOrEqual(c.ShieldSeconds, 0f);
            Assert.GreaterOrEqual(c.BlockedPopupSeconds, 0f);
            Assert.GreaterOrEqual(c.DamagePerSecondOutside, 0f);
            Assert.GreaterOrEqual(c.SpawnHealInCombatPerSecond, 0f);
            Assert.GreaterOrEqual(c.SpawnHealOutOfCombatPerSecond, 0f);
        }

        [Test] public void TheLockedLabelFormatTakesTheRoundNumber() =>
            StringAssert.Contains("{0}", Load().LockedTierLabelFormat);
    }
}
