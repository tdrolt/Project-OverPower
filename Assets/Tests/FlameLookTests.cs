using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Combat;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The Flamethrower's upright flames and embers rise only from ground its fan really burns: they are spawned inside the hit
    /// cone (its live range and angle) and never drift sideways out of it, so the fire never claims more ground than it hurts.</summary>
    public class FlameLookTests
    {
        // The Flamethrower's cone as Flamethrower.prefab ships it; literal here, the prefab may be retuned freely.
        private const float Range = 6f;
        private const float Angle = 60f;

        [Test]
        public void TonguesStayInsideTheHitFan()
        {
            var root = new GameObject("flame tongues test");
            var embersGo = new GameObject("Embers");
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            try
            {
                embersGo.transform.SetParent(root.transform, false);
                ParticleSystem tongues = root.AddComponent<ParticleSystem>();
                ParticleSystem embers = embersGo.AddComponent<ParticleSystem>();
                look.flameTonguesPerMetre = 40f;
                look.flameEmbersPerSecond = 60f;
                look.flameTongueStart = 0.15f;
                FlameLook.Apply(tongues, Range, Angle, Color.yellow, Color.red, look);

                foreach (ParticleSystem system in new[] { tongues, embers })
                {
                    system.Simulate(0.6f, false, true);
                    var particles = new ParticleSystem.Particle[system.main.maxParticles];
                    int count = system.GetParticles(particles);
                    Assert.Greater(count, 10, system.name + " emitted");
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 p = particles[i].position;
                        Assert.IsTrue(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, p, Range, Angle),
                            $"{system.name} particle {i} at {p:F3} is outside the fan the flame really burns");
                        Assert.GreaterOrEqual(new Vector2(p.x, p.z).magnitude, Range * 0.15f - 1e-3f, "no flame inside the caster");
                    }
                }

                FlameLook.Apply(tongues, Range, 20f, Color.yellow, Color.red, look);
                tongues.Simulate(0.6f, false, true);
                var narrow = new ParticleSystem.Particle[tongues.main.maxParticles];
                int narrowCount = tongues.GetParticles(narrow);
                bool anyOutsideNarrow = false;
                for (int i = 0; i < narrowCount; i++)
                    anyOutsideNarrow |= !ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, narrow[i].position, Range, 20f);
                Assert.IsFalse(anyOutsideNarrow, "a retuned, narrower cone narrows the flames with it");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(look);
            }
        }

        [Test]
        public void TheShippedTonguesStayInsideTheHitFan()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Abilities/Flamethrower Cone.prefab");
            GameObject cone = Object.Instantiate(prefab);
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            try
            {
                ParticleSystem tongues = cone.transform.Find(FlameConeVisual.TonguesChildName).GetComponent<ParticleSystem>();
                look.flameTonguesPerMetre = 40f;
                look.flameEmbersPerSecond = 60f;
                FlameLook.Apply(tongues, Range, Angle, Color.yellow, Color.red, look);
                foreach (ParticleSystem system in tongues.GetComponentsInChildren<ParticleSystem>(true))
                {
                    system.Simulate(1.5f, false, true);
                    var particles = new ParticleSystem.Particle[system.main.maxParticles];
                    int count = system.GetParticles(particles);
                    Assert.Greater(count, 5, system.name + " emitted");
                    for (int i = 0; i < count; i++)
                        Assert.IsTrue(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, particles[i].position, Range, Angle),
                            $"{system.name} particle {i} at {particles[i].position:F3} drifted out of the fan: its prefab moves it sideways");
                }
            }
            finally
            {
                Object.DestroyImmediate(cone);
                Object.DestroyImmediate(look);
            }
        }

        [Test]
        public void TheFlamesAndEmbersAreDrawnWithTheArenaLooksMaterials()
        {
            var root = new GameObject("flame materials test");
            var embersGo = new GameObject("Embers");
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            var alpha = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            var additive = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            try
            {
                embersGo.transform.SetParent(root.transform, false);
                ParticleSystem tongues = root.AddComponent<ParticleSystem>();
                embersGo.AddComponent<ParticleSystem>();
                look.fxAlpha = alpha;
                look.fxAdditive = additive;
                FlameLook.Apply(tongues, Range, Angle, Color.yellow, Color.red, look);
                Assert.AreSame(alpha, root.GetComponent<ParticleSystemRenderer>().sharedMaterial, "the flames: Arena Look > Fx Alpha, so their colour reads over any floor");
                Assert.AreSame(additive, embersGo.GetComponent<ParticleSystemRenderer>().sharedMaterial, "the embers: Arena Look > Fx Additive");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(alpha);
                Object.DestroyImmediate(additive);
            }
        }

        [Test]
        public void EachSprayHandsTheLiveConeToTheFlames()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Abilities/Flamethrower Cone.prefab");
            GameObject cone = Object.Instantiate(prefab);
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            try
            {
                theme.arenaLook = look;
                cone.GetComponent<FlameConeVisual>().ConfigureTongues(3.25f, 71f, theme, 21);
                ParticleSystem tongues = cone.transform.Find(FlameConeVisual.TonguesChildName).GetComponent<ParticleSystem>();
                foreach (ParticleSystem system in tongues.GetComponentsInChildren<ParticleSystem>(true))
                {
                    Assert.AreEqual(3.25f, system.shape.radius, 1e-5f, system.name + ": the range handed in");
                    Assert.AreEqual(71f, system.shape.arc, 1e-4f, system.name + ": the angle handed in");
                    Assert.AreEqual(FlameLook.Sector(71f).y, system.shape.rotation.z, 1e-4f, system.name + ": centred on the aim");
                }
            }
            finally
            {
                Object.DestroyImmediate(cone);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }

            // ShowVfx hands the ability's own cone fields to the flames, range then angle, after sizing the fan with the same two.
            const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            MethodInfo show = typeof(FlamethrowerAbility).GetMethod("ShowVfx", any);
            int fanAt = IlWiring.CallOffsets(show, typeof(FlameConeVisual).GetMethod(nameof(FlameConeVisual.Configure))).Single();
            int tonguesAt = IlWiring.CallOffsets(show, typeof(FlameConeVisual).GetMethod(nameof(FlameConeVisual.ConfigureTongues))).Single();
            Assert.Less(fanAt, tonguesAt);
            int[] rangeAt = IlWiring.FieldLoadOffsets(show, typeof(FlamethrowerAbility).GetField("coneRange", any)).Where(at => at > fanAt && at < tonguesAt).ToArray();
            int[] angleAt = IlWiring.FieldLoadOffsets(show, typeof(FlamethrowerAbility).GetField("coneAngle", any)).Where(at => at > fanAt && at < tonguesAt).ToArray();
            Assert.AreEqual(1, rangeAt.Length, "the live Cone Range goes to the flames");
            Assert.AreEqual(1, angleAt.Length, "the live Cone Angle goes to the flames");
            Assert.Less(rangeAt[0], angleAt[0], "range as the range, angle as the angle");
        }

        [Test]
        public void TheSectorIsTheConeAngleCentredOnTheAim()
        {
            Assert.AreEqual(new Vector2(60f, 60f), FlameLook.Sector(60f));
            Assert.AreEqual(new Vector2(90f, 45f), FlameLook.Sector(90f));
            Assert.AreEqual(360f, FlameLook.Sector(400f).x, "never more than a full circle");
        }

        [Test]
        public void TheFlamesBurnFromTheYellowCoreToTheOrangeEdgeOfTheFireLook()
        {
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            var go = new GameObject("flame colours test");
            try
            {
                var core = new Color(0.61f, 0.62f, 0.63f, 1f);
                var edge = new Color(0.71f, 0.72f, 0.73f, 1f);
                theme.fireCoreColor = core;
                theme.fireEdgeColor = edge;
                theme.arenaLook = look;
                ParticleSystem tongues = go.AddComponent<ParticleSystem>();
                FlameLook.Apply(tongues, Range, Angle, FlameLook.Core(theme), FlameLook.Edge(theme, 21), look);
                ParticleSystem.MinMaxGradient colour = tongues.main.startColor;
                Assert.AreEqual(ParticleSystemGradientMode.TwoColors, colour.mode);
                Assert.AreEqual(core, colour.colorMin);
                Assert.AreEqual(edge, colour.colorMax, "no Flamethrower row: the Fire family's orange");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }

            Assert.IsTrue(IlWiring.Uses(typeof(FlameConeVisual), nameof(FlameConeVisual.ConfigureTongues), typeof(FlameLook).GetMethod(nameof(FlameLook.Apply))));
            Assert.IsTrue(IlWiring.Uses(typeof(FlamethrowerAbility), "ShowVfx", typeof(FlameConeVisual).GetMethod(nameof(FlameConeVisual.ConfigureTongues))),
                "each spray sizes the flames to the live cone");
        }
    }
}
