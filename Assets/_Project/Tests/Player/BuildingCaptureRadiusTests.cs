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
        public void TheCentresCaptureCircleReadsTheTierItPlaysAsRightNow()
        {
            // Cut-rule-followups, 2026-09-26: the old version of this test (once titled "...WithNoLiveCutInEditMode")
            // asserted PhaseTwoCutRules.EffectiveTier directly and only checked capture.CaptureRadius for the
            // no-cut case - it passed unchanged on the code before BuildingCapture.CaptureRadiusFor existed, so it
            // guarded nothing about the wiring. This calls CaptureRadiusFor itself (the exact rule CaptureRadius is
            // built on) for both cutActive values, with no MonoBehaviour or MatchDirector to fake.
            TerritoryConfig config = ScriptableObject.CreateInstance<TerritoryConfig>();
            SetTierCaptureRadius(config, tier: 4, radius: 23f);
            SetTierCaptureRadius(config, tier: 3, radius: 9f);

            try
            {
                Assert.AreEqual(9f, BuildingCapture.CaptureRadiusFor(config, baseTier: 4, cutActive: true), 0.001f,
                    "while a cut is active, the centre (tier 4) plays as tier 3 and reads tier 3's row");
                Assert.AreEqual(23f, BuildingCapture.CaptureRadiusFor(config, baseTier: 4, cutActive: false), 0.001f,
                    "with no cut active, the centre reads its own tier 4 row");
                Assert.AreEqual(9f, BuildingCapture.CaptureRadiusFor(config, baseTier: 3, cutActive: true), 0.001f,
                    "a real tier 3 tower is unaffected by cutActive - it reads its own row either way");
            }
            finally
            {
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
