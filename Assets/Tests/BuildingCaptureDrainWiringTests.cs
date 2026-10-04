using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Overpower.Data;
using Overpower.Match;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Lobby Task 15b (opus review of the owned-zone drain): the pure rules were tested, but nothing guarded the WIRING - that the step the
    /// master takes (UpdateDecay) and the rate it publishes (ComputeCurrentProgress) both use the drainer count, and the same one. These
    /// drive a real BuildingCapture (no scene, no Photon: ApplyDrain takes the decision and the roster directly) and check both ends
    /// from the one frame: reverting UpdateDecay to the single-drainer formula, or the publish to a fixed 1, fails them.
    /// </summary>
    public class BuildingCaptureDrainWiringTests
    {
        private GameObject go;
        private BuildingCapture tower;
        private TerritoryConfig config;

        [SetUp]
        public void SetUp()
        {
            config = ScriptableObject.CreateInstance<TerritoryConfig>();
            go = new GameObject("Tower");
            tower = go.AddComponent<BuildingCapture>();
            tower.tier = 2;
            tower.territoryConfig = config;
            tower.capturingID = 1;
            Set("isCaptured", true);
            Set("isDecaying", true);
            Set("controllingTeam", 0);
            Set("captureProgress", config.ForTier(2).captureSeconds);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(config);
        }

        private void Set(string field, object value) =>
            typeof(BuildingCapture).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tower, value);

        private T Get<T>(string field) =>
            (T)typeof(BuildingCapture).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tower);

        private void Roster(params int[] teams)
        {
            var list = Get<List<int>>("teamsInZone");
            list.Clear();
            list.AddRange(teams);
        }

        [TestCase(1, 1.0f)]
        [TestCase(2, 1.5f)]
        [TestCase(3, 1.75f)]
        public void TheDrainStepAndThePublishedRateAgreeOnTheSameDrainerCount(int drainers, float factor)
        {
            var teams = new int[drainers];
            for (int i = 0; i < drainers; i++) teams[i] = 1;
            Roster(teams);

            float captureSeconds = config.ForTier(2).captureSeconds, decaySeconds = config.ForTier(2).decaySeconds;
            float before = Get<float>("captureProgress");

            tower.ApplyDrain(new DrainRule.Decision(DrainRule.Step.Continue, 1), 0.5f);

            float expectedPerSecond = captureSeconds / decaySeconds * factor;
            Assert.AreEqual(before - expectedPerSecond * 0.5f, Get<float>("captureProgress"), 1e-4f, "the step takes " + factor + "x");
            CaptureProgress published = tower.ComputeCurrentProgress(1000);
            Assert.AreEqual(1, published.Team);
            Assert.AreEqual(-expectedPerSecond / captureSeconds, published.RatePerSecond01, 1e-5f, "the published rate is the same " + factor + "x");
        }

        [Test]
        public void OnlyTheDrainingTeamsPlayersCountEvenWithAnotherEnemyTeamInTheZone()
        {
            // Settled by Tudor (4 Oct): only the draining team's players speed the drain; a second enemy team in the zone doesn't help.
            Roster(1, 2, 2, 2, 1);
            float captureSeconds = config.ForTier(2).captureSeconds, decaySeconds = config.ForTier(2).decaySeconds;

            tower.ApplyDrain(new DrainRule.Decision(DrainRule.Step.Continue, 1), 0.25f);

            Assert.AreEqual(-(captureSeconds / decaySeconds * 1.5f) / captureSeconds, tower.ComputeCurrentProgress(1000).RatePerSecond01, 1e-5f);
        }

        [Test]
        public void ADrainThatStoppedPublishesNoCountFromAnEarlierFrame()
        {
            Roster(1, 1, 1);
            tower.ApplyDrain(new DrainRule.Decision(DrainRule.Step.Continue, 1), 0.1f);
            Assert.AreEqual(3, Get<int>("drainersThisFrame"));

            tower.ApplyDrain(new DrainRule.Decision(DrainRule.Step.Stop, -1), 0.1f);
            Assert.AreEqual(1, Get<int>("drainersThisFrame"), "no drain running: back to 1, never the last frame's count");
        }
    }
}
