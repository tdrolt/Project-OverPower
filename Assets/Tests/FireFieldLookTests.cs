using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.UI;
using Overpower.Weapons;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The Rocket Cursor Fire's burning ground is floor flames in the fire colours, laid over exactly the radius every client was sent
    /// when the field lit, so the flames never show fire where it does not burn and always cover where it does.</summary>
    public class FireFieldLookTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static ParticleSystem.Particle[] Simulated(ParticleSystem system, float seconds)
        {
            system.Simulate(seconds, false, true);
            var particles = new ParticleSystem.Particle[system.main.maxParticles];
            int count = system.GetParticles(particles);
            return particles.Take(count).ToArray();
        }

        [Test]
        public void FloorFlamesStayOnTheBurningGround()
        {
            var root = new GameObject("floor flames test");
            var embersGo = new GameObject("Embers");
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            try
            {
                embersGo.transform.SetParent(root.transform, false);
                ParticleSystem flames = root.AddComponent<ParticleSystem>();
                ParticleSystem embers = embersGo.AddComponent<ParticleSystem>();
                look.fireFieldFlamesPerSquareMetre = 9f;
                look.fireFieldEmbersPerSquareMetre = 3f;
                var core = new Color(0.61f, 0.62f, 0.63f, 1f);
                var edge = new Color(0.71f, 0.72f, 0.73f, 1f);
                FlameLook.ApplyFloor(flames, 2.5f, core, edge, look);

                float area = Mathf.PI * 2.5f * 2.5f;
                Assert.AreEqual(9f * area, flames.emission.rateOverTime.constant, 1e-3f, "Flames Per Square Metre over the whole burning area");
                Assert.AreEqual(3f * area, embers.emission.rateOverTime.constant, 1e-3f, "Embers Per Square Metre");
                Assert.AreEqual(ParticleSystemGradientMode.TwoColors, flames.main.startColor.mode);
                Assert.AreEqual(core, flames.main.startColor.colorMin, "the fire's hot core");
                Assert.AreEqual(edge, flames.main.startColor.colorMax, "and its outer colour");
                foreach (ParticleSystem system in new[] { flames, embers })
                {
                    ParticleSystem.Particle[] particles = Simulated(system, 1f);
                    Assert.Greater(particles.Length, 20, system.name + " burned");
                    float farthest = 0f;
                    foreach (ParticleSystem.Particle particle in particles)
                    {
                        float flat = new Vector2(particle.position.x, particle.position.z).magnitude;
                        Assert.LessOrEqual(flat, 2.5f + 1e-3f, $"{system.name} particle at {particle.position:F3} is outside the burning ground");
                        farthest = Mathf.Max(farthest, flat);
                    }
                    Assert.Greater(farthest, 2.5f * 0.8f, system.name + " covers the ground out to its edge");
                    Assert.Greater(particles.Count(p => p.position.x < -0.5f), 0, "all round, not a fan");
                    Assert.Greater(particles.Count(p => p.position.z < -0.5f), 0);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(look);
            }
        }

        [Test]
        public void TheShippedFieldBurnsOverTheRadiusItWasSent()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Fire Field.prefab");
            GameObject field = Object.Instantiate(prefab);
            try
            {
                FireField fire = field.GetComponent<FireField>();
                typeof(FireField).GetField("radius", Any).SetValue(fire, 3.25f);
                typeof(FireField).GetMethod("ShowLook", Any).Invoke(fire, null);

                ParticleSystem flames = field.transform.Find(FireField.FlamesChildName).GetComponent<ParticleSystem>();
                foreach (ParticleSystem system in flames.GetComponentsInChildren<ParticleSystem>(true))
                {
                    Assert.AreEqual(3.25f, system.shape.radius, 1e-5f, system.name + ": the radius the field was sent");
                    Assert.AreEqual(360f, system.shape.arc, 1e-4f, system.name + ": the whole circle burns");
                    ParticleSystem.Particle[] particles = Simulated(system, 1.5f);
                    Assert.Greater(particles.Length, 0, system.name + " burned");
                    foreach (ParticleSystem.Particle particle in particles)
                        Assert.LessOrEqual(new Vector2(particle.position.x, particle.position.z).magnitude, 3.25f + 1e-3f,
                            $"{system.name} particle at {particle.position:F3} drifted off the burning ground: its prefab moves it sideways");
                }
                UiTheme theme = (UiTheme)typeof(FireField).GetField("theme", Any).GetValue(fire);
                Assert.AreEqual(LookColours.FireCore(theme), flames.main.startColor.colorMin, "the fire's own colours, not a team's");
                Assert.AreEqual(LookColours.For(LookFamily.Fire, -1, theme), flames.main.startColor.colorMax);
                var glow = new MaterialPropertyBlock();
                flames.GetComponent<ParticleSystemRenderer>().GetPropertyBlock(glow);
                Assert.AreEqual(theme.arenaLook.flameGlow, glow.GetColor("_BaseColor").r, 1e-3f, "the burning ground glows by Flame Glow, as the jet");

                LineRenderer rim = field.transform.Find("Rim").GetComponent<LineRenderer>();
                Assert.AreEqual(3.25f, new Vector2(rim.GetPosition(0).x, rim.GetPosition(0).y).magnitude, 1e-3f, "the rim still marks the true edge");
            }
            finally
            {
                Object.DestroyImmediate(field);
            }
        }

        [Test]
        public void WithoutAnArenaLookTheFieldShowsNoFlames()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Fire Field.prefab");
            GameObject field = Object.Instantiate(prefab);
            try
            {
                FireField fire = field.GetComponent<FireField>();
                typeof(FireField).GetField("radius", Any).SetValue(fire, 3.25f);
                typeof(FireField).GetField("theme", Any).SetValue(fire, null);
                typeof(FireField).GetMethod("ShowLook", Any).Invoke(fire, null);
                Assert.IsFalse(field.transform.Find(FireField.FlamesChildName).gameObject.activeSelf,
                    "flames never laid over the radius would burn the prefab's own shape, not the ground that burns");
                LineRenderer rim = field.transform.Find("Rim").GetComponent<LineRenderer>();
                Assert.AreEqual(3.25f, new Vector2(rim.GetPosition(0).x, rim.GetPosition(0).y).magnitude, 1e-3f, "the rim still marks the true edge");
            }
            finally
            {
                Object.DestroyImmediate(field);
            }
        }

        [Test]
        public void TheFieldLightsItsFlamesFromTheRadiusItWasSent()
        {
            MethodInfo lit = typeof(FireField).GetMethod(nameof(FireField.OnPhotonInstantiate));
            MethodInfo show = typeof(FireField).GetMethod("ShowLook", Any);
            Assert.IsNotNull(show);
            FieldInfo radius = typeof(FireField).GetField("radius", Any);
            int showAt = IlWiring.CallOffsets(lit, show).Single();
            Assert.IsNotEmpty(IlWiring.FieldStoreOffsets(lit, radius), "the radius arrives in the instantiation data");
            Assert.Less(IlWiring.FieldStoreOffsets(lit, radius).Max(), showAt, "the flames are laid out after the sent radius is stored, so every client burns the same ground");
            Assert.IsTrue(IlWiring.Uses(typeof(FireField), "ShowLook", typeof(FlameLook).GetMethod(nameof(FlameLook.ApplyFloor))), "laid out by the tested rule");
            int applyAt = IlWiring.CallOffsets(show, typeof(FlameLook).GetMethod(nameof(FlameLook.ApplyFloor))).Single();
            Assert.IsNotEmpty(IlWiring.FieldLoadOffsets(show, radius).Where(at => at < applyAt), "with the field's radius");
            Assert.IsTrue(IlWiring.Uses(typeof(FireField), "ShowLook", typeof(LookColours).GetMethod(nameof(LookColours.FireCore))), "in the fire colours of the theme");
        }
    }
}
