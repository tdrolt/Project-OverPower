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
    /// <summary>Where a Stun Gun bolt strikes a player, every client that can see the spot shows a flash of electric spikes, a blue light and a
    /// crackling shock ring spreading over the floor; a bolt that hits a wall shows only its usual burst.</summary>
    public class StunImpactLookTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void TheMarkShowsOnlyWhereABoltStrikesSomeoneISee()
        {
            Assert.IsTrue(StunImpactLook.ShowsMark(true, true));
            Assert.IsFalse(StunImpactLook.ShowsMark(false, true), "a wall takes no stun, so it gets only the usual burst");
            Assert.IsFalse(StunImpactLook.ShowsMark(true, false), "an enemy's hit in the fog is not drawn, like its burst");

            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(StunImpactOnHit), nameof(StunImpactOnHit.OnHit),
                typeof(StunImpactLook).GetMethod(nameof(StunImpactLook.ShowsMark))), "the bolt plays the mark on the rule's answer");
            Assert.IsTrue(IlWiring.Uses(typeof(StunImpactOnHit), nameof(StunImpactOnHit.OnHit), typeof(TeamSight).GetMethod(nameof(TeamSight.ShotShownAt))),
                "with the same sight check as every shot's burst");
            Assert.IsTrue(IlWiring.Uses(typeof(StunImpactOnHit), nameof(StunImpactOnHit.OnHit), typeof(StunImpactView).GetMethod(nameof(StunImpactView.Play))),
                "and plays the mark");
        }

        [Test]
        public void TheSpikesShootOutFastThenStop()
        {
            const float seconds = 0.2f, reach = 1f;
            Assert.AreEqual(0f, StunImpactLook.RayReach(0f, seconds, reach), 1e-5f, "they start at the point of impact");
            Assert.Greater(StunImpactLook.RayReach(seconds / 3f, seconds, reach), reach / 2f, "most of the way out in the first third");
            float before = 0f;
            for (float t = 0.01f; t <= seconds; t += 0.01f)
            {
                float now = StunImpactLook.RayReach(t, seconds, reach);
                Assert.GreaterOrEqual(now, before, "never pulled back in");
                before = now;
            }
            Assert.AreEqual(reach, StunImpactLook.RayReach(seconds, seconds, reach), 1e-5f);
            Assert.AreEqual(reach, StunImpactLook.RayReach(seconds * 3f, seconds, reach), 1e-5f, "held at full reach afterwards");
        }

        [Test]
        public void TheSpikesFanOutEvenlyAcrossTheScreen()
        {
            Vector3 right = Vector3.right, up = Vector3.forward;
            const int count = 8;
            for (int i = 0; i < count; i++)
            {
                Vector3 d = StunImpactLook.RayDirection(i, count, 10f, right, up);
                Assert.AreEqual(1f, d.magnitude, 1e-4f);
                Assert.AreEqual(0f, d.y, 1e-4f, "in the plane facing the camera, so no spike points into the screen");
                Vector3 next = StunImpactLook.RayDirection((i + 1) % count, count, 10f, right, up);
                Assert.AreEqual(360f / count, Vector3.Angle(d, next), 1e-2f, "evenly spread round the point");
            }
            Assert.AreEqual(10f, Vector3.Angle(right, StunImpactLook.RayDirection(0, count, 10f, right, up)), 1e-2f, "the whole fan turned by the given angle");
        }

        [Test]
        public void TheShockRingSpreadsFromTheBoltOutward()
        {
            const float seconds = 0.5f, start = 0.4f, end = 2f;
            Assert.AreEqual(start, StunImpactLook.RingRadius(0f, seconds, start, end), 1e-5f, "it appears at the bolt's own size");
            float before = start;
            for (float t = 0.02f; t <= seconds; t += 0.02f)
            {
                float now = StunImpactLook.RingRadius(t, seconds, start, end);
                Assert.Greater(now, before, "always spreading");
                before = now;
            }
            Assert.Greater(StunImpactLook.RingRadius(seconds / 3f, seconds, start, end), (start + end) / 2f, "fast at first, like a shock wave");
            Assert.AreEqual(end, StunImpactLook.RingRadius(seconds, seconds, start, end), 1e-5f);
        }

        [Test]
        public void TheShockRingCracklesRoundItsRadius()
        {
            const float radius = 1.5f, jitter = 0.2f;
            const int n = 24;
            int off = 0;
            for (int k = 0; k < n; k++)
            {
                Vector3 p = StunImpactLook.RingPoint(5, k, n, radius, jitter);
                Assert.AreEqual(0f, p.y, 1e-5f, "flat on the floor");
                float r = new Vector2(p.x, p.z).magnitude;
                Assert.That(r, Is.InRange(radius * (1f - jitter) - 1e-4f, radius * (1f + jitter) + 1e-4f));
                if (Mathf.Abs(r - radius) > radius * 0.02f)
                    off++;
                Assert.AreEqual(p, StunImpactLook.RingPoint(5, k, n, radius, jitter), "the same seed, the same shape");
                Vector3 slot = Overpower.Combat.AbilityVisualGeometry.CirclePoint(1f, k * 360f / n);
                Assert.AreEqual(0f, Vector2.Angle(new Vector2(slot.x, slot.z), new Vector2(p.x, p.z)), 0.5f, "points go round the ring in order");
            }
            Assert.Greater(off, n / 3, "jagged, not a smooth circle");
            Assert.AreEqual(radius, new Vector2(StunImpactLook.RingPoint(5, 3, n, radius, 0f).x, StunImpactLook.RingPoint(5, 3, n, radius, 0f).z).magnitude, 1e-4f,
                "without jitter, a plain circle");
        }

        [Test]
        public void TheLightFlashesAndDiesFast()
        {
            const float seconds = 0.2f, peak = 3f;
            Assert.AreEqual(peak, StunImpactLook.LightIntensity(0f, seconds, peak), 1e-5f);
            Assert.Less(StunImpactLook.LightIntensity(seconds / 2f, seconds, peak), peak / 2f, "gone faster than a straight fade");
            Assert.AreEqual(0f, StunImpactLook.LightIntensity(seconds, seconds, peak), 1e-5f);
            Assert.AreEqual(0f, StunImpactLook.LightIntensity(seconds * 2f, seconds, peak), 1e-5f);
        }

        [Test]
        public void TheMarkIsElectricBlueSpikesARingAndALight()
        {
            var stunned = new Color(0.1f, 0.3f, 0.9f, 1f);
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            var look = ScriptableObject.CreateInstance<ArenaLook>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            StunImpactView view = null;
            try
            {
                theme.statusStunnedColor = stunned;
                theme.arenaLook = look;
                look.fxAlpha = material;
                look.stunFlashRayCount = 5;
                look.stunFlashGlow = 4f;
                look.stunFlashLightIntensity = 2f;
                view = StunImpactView.Play(new Vector3(3f, 1f, 4f), theme);
                Assert.IsNotNull(view);
                Assert.IsNull(view.transform.parent, "left where the bolt struck, not on the bolt that is about to go");

                Transform rays = view.transform.Find(StunImpactView.RaysName);
                Assert.IsNotNull(rays);
                LineRenderer[] spikes = rays.GetComponentsInChildren<LineRenderer>(true);
                Assert.AreEqual(5, spikes.Length);
                var block = new MaterialPropertyBlock();
                Color glow = TowerLookRules.GlowKeepingHue(stunned, 4f);
                foreach (LineRenderer spike in spikes)
                {
                    Assert.AreSame(material, spike.sharedMaterial, "FX Alpha: blended, so the blue never turns pale cyan");
                    spike.GetPropertyBlock(block);
                    Color c = block.GetColor("_BaseColor");
                    Assert.AreEqual(glow.r, c.r, 1e-3f, "the stun's blue, brightened without leaving its hue");
                    Assert.AreEqual(glow.g, c.g, 1e-3f);
                    Assert.AreEqual(glow.b, c.b, 1e-3f);
                }

                Transform ring = view.transform.Find(StunImpactView.RingName);
                Assert.IsNotNull(ring);
                LineRenderer band = ring.GetComponentInChildren<LineRenderer>(true);
                Assert.IsNotNull(band);
                Assert.IsTrue(band.loop, "a closed ring");

                Light light = view.GetComponentInChildren<Light>(true);
                Assert.IsNotNull(light, "a blue light flashes on the floor around it");
                Assert.AreEqual(LightType.Point, light.type);
                Assert.AreEqual(LightShadows.None, light.shadows);

                view.Draw(look.stunRingSeconds * 0.5f);
                Assert.Greater(band.positionCount, 8);
            }
            finally
            {
                if (view != null) Object.DestroyImmediate(view.gameObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }

            Assert.IsTrue(IlWiring.Uses(typeof(StunImpactView), nameof(StunImpactView.Draw), typeof(StunImpactLook).GetMethod(nameof(StunImpactLook.RayReach))));
            Assert.IsTrue(IlWiring.Uses(typeof(StunImpactView), nameof(StunImpactView.Draw), typeof(StunImpactLook).GetMethod(nameof(StunImpactLook.RingRadius))));
            Assert.IsTrue(IlWiring.Uses(typeof(StunImpactView), nameof(StunImpactView.Draw), typeof(StunImpactLook).GetMethod(nameof(StunImpactLook.RingPoint))));
            Assert.IsTrue(IlWiring.Uses(typeof(StunImpactView), nameof(StunImpactView.Draw), typeof(StunImpactLook).GetMethod(nameof(StunImpactLook.LightIntensity))));
        }

        [Test]
        public void NoLightWhenItsStrengthIsZero()
        {
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            var look = ScriptableObject.CreateInstance<ArenaLook>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            StunImpactView view = null;
            try
            {
                theme.arenaLook = look;
                look.fxAlpha = material;
                look.stunFlashLightIntensity = 0f;
                view = StunImpactView.Play(Vector3.zero, theme);
                Assert.IsNotNull(view);
                Assert.IsNull(view.GetComponentInChildren<Light>(true));
            }
            finally
            {
                if (view != null) Object.DestroyImmediate(view.gameObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void TheStunGunBoltCarriesTheMark()
        {
            var bolt = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Features/Weapons/Projectiles/Stun Gun Bullet.prefab");
            Assert.IsNotNull(bolt);
            StunImpactOnHit mark = bolt.GetComponent<StunImpactOnHit>();
            Assert.IsNotNull(mark, "the bolt plays the mark where it strikes");
            var theme = (UiTheme)typeof(StunImpactOnHit).GetField("theme", Any).GetValue(mark);
            Assert.IsNotNull(theme, "with the theme its look comes from");
        }
    }
}
