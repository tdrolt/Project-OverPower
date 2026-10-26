using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.UI;
using Overpower.Vision;
using Overpower.Weapons;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The few shot lights go to the shots the viewer can see, nearest to the camera first, never more than the cap. A Light is not a
    /// Renderer, so the sight gate does not hide it: a light on a shot hidden in the fog would show where the shooter is.</summary>
    public class ShotLightRuleTests
    {
        private static ShotCandidate Seen(float sqrDistance) => new ShotCandidate(true, sqrDistance);
        private static ShotCandidate Hidden(float sqrDistance) => new ShotCandidate(false, sqrDistance);

        [Test]
        public void NeverMoreThanCap()
        {
            var shots = new List<ShotCandidate>();
            for (int i = 0; i < 40; i++)
                shots.Add(Seen(i * 3f % 17f));
            var chosen = new List<int> { 99 };
            for (int cap = 0; cap <= 6; cap++)
            {
                ShotLightRule.Pick(shots, cap, chosen);
                Assert.AreEqual(cap, chosen.Count, "every light used while enough shots are seen, never more");
                Assert.AreEqual(chosen.Count, new HashSet<int>(chosen).Count, "no shot gets two lights");
            }
            ShotLightRule.Pick(new List<ShotCandidate> { Seen(1f), Seen(2f) }, 4, chosen);
            Assert.AreEqual(2, chosen.Count, "fewer shots than lights: one light each");
            ShotLightRule.Pick(shots, -1, chosen);
            Assert.IsEmpty(chosen);
        }

        [Test]
        public void OnlyShownShotsQualify()
        {
            var shots = new List<ShotCandidate> { Hidden(0.1f), Seen(50f), Hidden(0.2f), Hidden(0.3f), Seen(60f) };
            var chosen = new List<int>();
            ShotLightRule.Pick(shots, 4, chosen);
            CollectionAssert.AreEquivalent(new[] { 1, 4 }, chosen, "the near shots in the fog get no light, however close");

            Assert.IsTrue(ShotLightRule.Shown(true, true, true), "a shot my team sees");
            Assert.IsFalse(ShotLightRule.Shown(true, false, true), "a shot in the fog");
            Assert.IsFalse(ShotLightRule.Shown(false, false, true), "a shot not gated yet while there is a sight: hidden until its gate says so");
            Assert.IsTrue(ShotLightRule.Shown(false, false, false), "no sight at all (no fog): every shot is drawn, so it may be lit");
        }

        [Test]
        public void NearestToCameraFirst()
        {
            var shots = new List<ShotCandidate> { Seen(9f), Seen(1f), Hidden(0f), Seen(25f), Seen(4f), Seen(16f) };
            var chosen = new List<int>();
            ShotLightRule.Pick(shots, 3, chosen);
            CollectionAssert.AreEqual(new[] { 1, 4, 0 }, chosen, "the three nearest seen shots, nearest first");
        }

        [Test]
        public void ThePoolAsksTheRuleEveryFrame()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(ShotLightPool), "LateUpdate", typeof(ShotLightRule).GetMethod(nameof(ShotLightRule.Pick))),
                "the lights are re-chosen every frame by the tested rule");
            Assert.IsTrue(IlWiring.Uses(typeof(ShotLightPool), "LateUpdate", typeof(ShotLightRule).GetMethod(nameof(ShotLightRule.Shown))),
                "whether a shot is seen comes from the tested rule");
        }

        [Test]
        public void ThePoolReadsTheSightGatesFreshAnswer()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(ShotLightPool), "LateUpdate", typeof(VisibleWhenSeen).GetProperty(nameof(VisibleWhenSeen.Shown)).GetGetMethod()),
                "the sight gate's own answer, read every frame");
        }

        [Test]
        public void ThePoolRunsAfterTheSightGates()
        {
            var order = (DefaultExecutionOrder)System.Attribute.GetCustomAttribute(typeof(ShotLightPool), typeof(DefaultExecutionOrder));
            var gateOrder = (DefaultExecutionOrder)System.Attribute.GetCustomAttribute(typeof(VisibleWhenSeen), typeof(DefaultExecutionOrder));
            Assert.IsNotNull(order);
            Assert.Greater(order.order, gateOrder != null ? gateOrder.order : 0,
                "after the sight gates' LateUpdate, so a shot hidden this frame loses its light this frame");
        }

        [Test]
        public void EveryShotOffersItselfForALightAsItSpawns()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(ShotTeamVisuals), "OnSpawned", typeof(ShotLightPool).GetMethod(nameof(ShotLightPool.Register))));
        }

        [Test]
        public void AShotIsWithdrawnWhenItIsGone()
        {
            MethodInfo unregister = typeof(ShotLightPool).GetMethod(nameof(ShotLightPool.Unregister));
            Assert.IsTrue(IlWiring.Uses(typeof(ShotTeamVisuals), "OnExpired", unregister), "when the shot ends");
            Assert.IsTrue(IlWiring.Uses(typeof(ShotTeamVisuals), "OnDestroy", unregister), "and when it is destroyed without ending (a scene change)");
        }

        [Test]
        public void TheLightSitsLowOverTheFloorUnderTheShot()
        {
            Assert.AreEqual(new Vector3(3f, 0.5f, -2f), ShotLightRule.LightAt(new Vector3(3f, 1.6f, -2f), 0.1f, 0.4f),
                "straight under the shot, Shot Light Height above the floor, so the glow lands under the slug on screen");
            Assert.AreEqual(0.3f, ShotLightRule.LightAt(new Vector3(0f, 0.3f, 0f), 0.1f, 0.4f).y, 1e-5f, "never above the shot itself");
            Assert.IsTrue(IlWiring.Uses(typeof(ShotLightPool), "LateUpdate", typeof(ShotLightRule).GetMethod(nameof(ShotLightRule.LightAt))),
                "every light is placed by the tested rule");
            Assert.IsTrue(IlWiring.Uses(typeof(ShotLightPool), "LateUpdate", typeof(ArenaLook).GetField(nameof(ArenaLook.shotLightHeight))),
                "at the Arena Look's height");
            Assert.IsTrue(IlWiring.Uses(typeof(ShotLightPool), "LateUpdate",
                typeof(GroundSnap).GetMethod(nameof(GroundSnap.TryFindGroundY), new[] { typeof(Vector3), typeof(float).MakeByRefType() })),
                "over the real floor under the shot");
            Assert.IsTrue(IlWiring.Uses(typeof(ShotLightPool), "LateUpdate", typeof(ShotTeamVisuals).GetProperty(nameof(ShotTeamVisuals.LightColour)).GetGetMethod()),
                "in the shooter's team colour");
        }

        [Test]
        public void MatchSystemsCarriesTheLightPoolWithTheTheme()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Features/Match/Prefabs/MatchSystems.prefab");
            var pool = prefab.GetComponentInChildren<ShotLightPool>(true);
            Assert.IsNotNull(pool, "both arena scenes hold MatchSystems, so neither scene file changes");
            var theme = new SerializedObject(pool).FindProperty("theme").objectReferenceValue;
            Assert.AreEqual("Assets/_Project/Shared/Config/UiTheme.asset", AssetDatabase.GetAssetPath(theme));
            Assert.IsEmpty(prefab.GetComponentsInChildren<Light>(true), "the lights are made once at runtime, never saved in the prefab");
        }

        [Test]
        public void TheLightsAreMadeOnceShadowlessAndOff()
        {
            var theme = ScriptableObject.CreateInstance<UiTheme>();
            var look = ScriptableObject.CreateInstance<ArenaLook>();
            theme.arenaLook = look;
            look.shotLightCap = 3;
            look.shotLightRange = 2.5f;
            look.shotLightIntensity = 0.7f;
            var go = new GameObject("Shot Light Pool Test");
            try
            {
                var pool = go.AddComponent<ShotLightPool>();
                pool.Build(theme);
                pool.Build(theme);
                Light[] lights = go.GetComponentsInChildren<Light>(true);
                Assert.AreEqual(3, lights.Length, "Shot Light Cap lights, made once");
                foreach (Light light in lights)
                {
                    Assert.AreEqual(LightType.Point, light.type);
                    Assert.AreEqual(LightShadows.None, light.shadows);
                    Assert.IsFalse(light.enabled, "off until a seen shot takes it");
                    Assert.AreEqual(2.5f, light.range, 1e-5f, "Shot Light Range");
                    Assert.AreEqual(0.7f, light.intensity, 1e-5f, "Shot Light Intensity");
                    Assert.AreEqual(ShotLightPool.LightName, light.name);
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }
        }
    }
}
