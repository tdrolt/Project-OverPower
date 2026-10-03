using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Match;
using UnityEditor;

namespace Overpower.Tests
{
    /// <summary>How fast a zone is captured or drained with 1, 2, 3 players of a team in it (lobby Task 14). Lists here are literal: the rule is
    /// tested, never the numbers a designer chose in the Territory Config.</summary>
    public class CaptureSpeedRuleTests
    {
        private static readonly float[] Spread = { 1f, 1.5f, 1.75f };
        private static readonly float[] Odd = { 2f, 3f, 3.5f, 4f };

        [Test]
        public void NobodyInTheZoneCapturesAtSpeedZero()
        {
            Assert.AreEqual(0f, CaptureSpeedRule.For(0, Spread));
            Assert.AreEqual(0f, CaptureSpeedRule.For(-1, Spread));
            Assert.AreEqual(0f, CaptureSpeedRule.For(0, new float[0]));
            Assert.AreEqual(0f, CaptureSpeedRule.For(0, null));
        }

        [Test]
        public void OnePlayerIsTheListsFirstEntry()
        {
            Assert.AreEqual(1f, CaptureSpeedRule.For(1, Spread));
            Assert.AreEqual(2f, CaptureSpeedRule.For(1, Odd));
        }

        [Test]
        public void ThePlayerCountPicksTheMatchingEntry()
        {
            Assert.AreEqual(1.5f, CaptureSpeedRule.For(2, Spread));
            Assert.AreEqual(1.75f, CaptureSpeedRule.For(3, Spread));
            Assert.AreEqual(3.5f, CaptureSpeedRule.For(3, Odd));
        }

        [Test]
        public void PastTheEndOfTheListTheLastEntryIsUsed()
        {
            Assert.AreEqual(1.75f, CaptureSpeedRule.For(4, Spread));
            Assert.AreEqual(1.75f, CaptureSpeedRule.For(9, Spread));
            Assert.AreEqual(4f, CaptureSpeedRule.For(40, Odd));
        }

        [Test]
        public void AnEmptyOrMissingListFallsBackToOneTimesThePlayerCount()
        {
            Assert.AreEqual(1f, CaptureSpeedRule.For(1, new float[0]));
            Assert.AreEqual(2f, CaptureSpeedRule.For(2, new float[0]));
            Assert.AreEqual(3f, CaptureSpeedRule.For(3, new List<float>()));
            Assert.AreEqual(3f, CaptureSpeedRule.For(3, null));
        }

        [Test]
        public void MorePlayersAreNeverSlowerForAnyNonDecreasingList()
        {
            var lists = new List<float[]>
            {
                Spread, Odd, new[] { 1f }, new[] { 1f, 1f, 1f }, new[] { 0.5f, 2f }, new[] { 1f, 2f, 3f }, new float[0],
            };
            foreach (float[] list in lists)
            {
                float before = 0f;
                for (int players = 0; players <= 12; players++)
                {
                    float now = CaptureSpeedRule.For(players, list);
                    Assert.GreaterOrEqual(now, before, "list of " + list.Length + ", " + players + " players");
                    before = now;
                }
            }
        }

        [Test]
        public void TheTerritoryConfigListIsNeverSlowerWithMorePlayersAndStartsAboveZero()
        {
            var config = AssetDatabase.LoadAssetAtPath<TerritoryConfig>("Assets/Gameplay/Config/TerritoryConfig.asset");
            Assert.NotNull(config);
            Assert.IsNotNull(config.CaptureSpeedByPlayers);
            Assert.Greater(config.CaptureSpeedByPlayers.Count, 0);
            Assert.Greater(CaptureSpeedRule.For(1, config.CaptureSpeedByPlayers), 0f);
            for (int players = 1; players <= 6; players++)
                Assert.GreaterOrEqual(CaptureSpeedRule.For(players + 1, config.CaptureSpeedByPlayers), CaptureSpeedRule.For(players, config.CaptureSpeedByPlayers));
        }
    }
}
