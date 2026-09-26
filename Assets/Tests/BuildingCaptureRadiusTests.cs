using NUnit.Framework;
using Overpower.Data;
using Overpower.Match;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Refactor 2026-09-26 (Tudor, after the QA meeting: "i cant find where i can easily modify the radius of the
    /// capture on each zone tier"): the capture radius moved off each tower and into TerritoryConfig's per-tier
    /// rows, next to capture time, gold, bounty and regen - one home instead of ten. Guards the wiring: a tower's
    /// CaptureRadius reads the row of the tier it plays as right now (EffectiveTier), live from the config rather
    /// than a value copied once at Start. Centre-circle-and-cut-rule, 2026-09-26: the centre no longer keeps its own
    /// Tier IV circle while a corner is cut - it shrinks to the Tier III one, matching the other Tier IIIs.
    /// TerritoryConfig has no public setter for a tier's row (same as the rest of the Data folder), so a test row
    /// is written the way the Inspector would: through SerializedObject on a CreateInstance'd asset - no existing
    /// TerritoryConfig test helper to reuse.
    /// </summary>
    public class BuildingCaptureRadiusTests
    {
        [Test]
        public void ATowersCaptureCircleComesFromItsTiersRow()
        {
            TerritoryConfig config = ScriptableObject.CreateInstance<TerritoryConfig>();
            SetTierCaptureRadius(config, tier: 3, radius: 42f);

            var go = new GameObject("Tower");
            try
            {
                BuildingCapture capture = go.AddComponent<BuildingCapture>();
                capture.tier = 3;
                capture.territoryConfig = config;

                Assert.AreEqual(42f, capture.CaptureRadius, 0.001f,
                    "a tier 3 tower should read tier 3's row of the config it points at");

                SetTierCaptureRadius(config, tier: 3, radius: 17f);
                Assert.AreEqual(17f, capture.CaptureRadius, 0.001f,
                    "CaptureRadius must follow a later change to its row, not cache the value from Start");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void TheCentresCaptureCircleReadsItsOwnTierWithNoLiveCutInEditMode()
        {
            TerritoryConfig config = ScriptableObject.CreateInstance<TerritoryConfig>();
            SetTierCaptureRadius(config, tier: 4, radius: 23f);
            SetTierCaptureRadius(config, tier: 3, radius: 9f);

            var go = new GameObject("Centre");
            try
            {
                BuildingCapture capture = go.AddComponent<BuildingCapture>();
                capture.tier = 4;
                capture.territoryConfig = config;

                // Edit mode has no MatchDirector.Instance, so BuildingCapture.EffectiveTier's cutActive test
                // (MatchDirector.Instance != null && ...) always reads false here - the same pure rule confirmed
                // directly below. The live behaviour (an actual cut shrinking this to the tier 3 row) needs a
                // MatchDirector, so it is covered in Play Mode instead (see the task's brief) - this only guards
                // the rule BuildingCapture.CaptureRadius is built on.
                Assert.AreEqual(4, PhaseTwoCutRules.EffectiveTier(4, cutActive: false),
                    "the rule BuildingCapture.EffectiveTier reads, with no cut active");
                Assert.AreEqual(23f, capture.CaptureRadius, 0.001f,
                    "with no live cut, a tier 4 tower (the centre) reads its own tier's row");

                Assert.AreEqual(3, PhaseTwoCutRules.EffectiveTier(4, cutActive: true),
                    "while a cut is active, the centre plays as tier 3 - CaptureRadius would feed 9f (tier 3's row) " +
                    "into ForTier if a live MatchDirector reported one");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(config);
            }
        }

        // TerritoryConfig's tiers array is a private [SerializeField], and TierSettings' fields have no public
        // setter - the only way to put a chosen value into one tier's row from a test is the same route the
        // Inspector uses.
        private static void SetTierCaptureRadius(TerritoryConfig config, int tier, float radius)
        {
            var serialized = new SerializedObject(config);
            SerializedProperty tierElement = serialized.FindProperty("tiers").GetArrayElementAtIndex(tier - 1);
            tierElement.FindPropertyRelative("captureRadius").floatValue = radius;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
