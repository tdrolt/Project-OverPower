using System.Reflection;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>
    /// Tudor, 30-31 Oct: ultimate charge packs at set spots on every map - the health pack's pad with a floating yellow icosahedron (the ult
    /// meter's yellow), +30% of the meter, back after 30 s, never taken with a full meter; and on the Dominion 2v2 map two health packs that
    /// heal 25. Both are "spot packs": placed from a list per map in HealthPackConfig, taken through the same room values as the zone packs.
    /// Literal numbers here are test values.
    /// </summary>
    public class PackSpotsTests
    {
        [TestCase(true, false, true, true, true, TestName = "alive, meter not full, ready: take it")]
        [TestCase(true, true, true, true, false, TestName = "a full meter leaves it for the others")]
        [TestCase(false, false, true, true, false, TestName = "a knocked-out player cannot take it")]
        [TestCase(true, false, false, true, false, TestName = "not back yet")]
        [TestCase(true, false, true, false, false, TestName = "out of play")]
        public void AnUltimatePackIsTakenOnlyWithRoomInTheMeter(bool alive, bool meterFull, bool available, bool inPlay, bool expected) =>
            Assert.AreEqual(expected, HealthPackRules.MayTakeUltimate(alive, meterFull, available, inPlay));

        [Test]
        public void APackAddsAShareOfTheWholeMeter()
        {
            var meter = new UltimateChargeState(1000f, 1f, 0.5f, 150f, 75f);
            meter.AddFraction(0.3f);
            Assert.AreEqual(300f, meter.Current, 1e-3f);
        }

        [Test]
        public void APackNeverFillsPastTheTop()
        {
            var meter = new UltimateChargeState(1000f, 1f, 0.5f, 150f, 75f);
            meter.AddDamageDealt(900f);
            meter.AddFraction(0.3f);
            Assert.AreEqual(1000f, meter.Current, 1e-3f);
        }

        [Test]
        public void EachMapFindsItsOwnSpots()
        {
            var scenes = new[] { "Game Scene", "Dominion 2v2" };
            Assert.AreEqual(1, HealthPackRules.SpotSetIndex(scenes, "Dominion 2v2"));
            Assert.AreEqual(0, HealthPackRules.SpotSetIndex(scenes, "Game Scene"));
            Assert.AreEqual(-1, HealthPackRules.SpotSetIndex(scenes, "Test Range"), "a map with no list has no spot packs");
        }

        [Test]
        public void SpotPacksNeverShareAnIdWithAZoneOrTheRecessPack()
        {
            Assert.Greater(HealthPackRules.SpotPackId(0), 64, "far above any zone count (and the recess pack, indexed right after the zones)");
            Assert.AreEqual(HealthPackRules.SpotPackId(0) + 3, HealthPackRules.SpotPackId(3));
        }

        [Test]
        public void TheIcosahedronHasTwentyFacesOnOneSphere()
        {
            UnityEngine.Mesh mesh = IcosahedronMesh.Create(0.6f);
            try
            {
                Assert.AreEqual(60, mesh.triangles.Length, "20 triangles");
                foreach (UnityEngine.Vector3 v in mesh.vertices)
                    Assert.AreEqual(0.6f, v.magnitude, 1e-4f, "every corner on the sphere of that radius");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        // ---- the asset: the lists Tudor asked for (how many spots per map; the numbers are his)

        [Test]
        public void TheConfigListsTheTriangleAndTheTwoVersusTwoMaps()
        {
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<HealthPackConfig>("Assets/_Project/Design/Settings/HealthPackConfig.asset");
            Assert.NotNull(config);
            int triangle = HealthPackRules.SpotSetIndex(config.SpotSceneNames, "Game Scene");
            int lanes = HealthPackRules.SpotSetIndex(config.SpotSceneNames, "Dominion 2v2");
            Assert.GreaterOrEqual(triangle, 0, "the triangle map (Conquest and Dominion 3v3v3)");
            Assert.GreaterOrEqual(lanes, 0, "the Dominion 2v2 map");
            Assert.AreEqual(3, config.SpotSet(triangle).UltimateSpots.Length, "three ult packs round the centre");
            Assert.AreEqual(0, config.SpotSet(triangle).HealthSpots.Length);
            Assert.AreEqual(2, config.SpotSet(lanes).UltimateSpots.Length, "two by the centre cross");
            Assert.AreEqual(2, config.SpotSet(lanes).HealthSpots.Length, "two health packs");
            Assert.Less(config.SpotSet(lanes).HealAmount, config.HealAmount, "2v2's health packs heal less than the others");
        }

        // ---- the game asks the rules

        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        [Test]
        public void TheManagerAsksBeforeAskingForAnUltimatePack() =>
            Assert.IsTrue(IlWiring.Uses(typeof(HealthPackManager), "TryRequestLocal", typeof(HealthPackRules).GetMethod(nameof(HealthPackRules.MayTakeUltimate))));

        [Test]
        public void TheEchoOfAnUltimatePackFillsTheMeter() =>
            Assert.IsTrue(IlWiring.Uses(typeof(HealthPackManager), nameof(HealthPackManager.OnRoomPropertiesUpdate),
                typeof(UltimateCharge).GetMethod(nameof(UltimateCharge.AddFraction))));

        [Test]
        public void TheManagerBuildsTheSpotsOfThisMap() =>
            Assert.IsTrue(IlWiring.Uses(typeof(HealthPackManager), "EnsureSpotPacks", typeof(HealthPackRules).GetMethod(nameof(HealthPackRules.SpotSetIndex))));

        [Test]
        public void AnUltimatePackFloatsAnIcosahedron() =>
            Assert.IsTrue(IlWiring.Uses(typeof(HealthPackView), nameof(HealthPackView.Create), typeof(IcosahedronMesh).GetMethod(nameof(IcosahedronMesh.Create))));

        [Test]
        public void TheMeterAddsTheShareThroughItsState() =>
            Assert.IsTrue(IlWiring.Uses(typeof(UltimateCharge), nameof(UltimateCharge.AddFraction), typeof(UltimateChargeState).GetMethod(nameof(UltimateChargeState.AddFraction))));
    }
}
