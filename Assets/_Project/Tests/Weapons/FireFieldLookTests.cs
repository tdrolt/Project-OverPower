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
            var coreGo = new GameObject(FlameLook.CoreChildName);
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            var soft = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            try
            {
                embersGo.transform.SetParent(root.transform, false);
                coreGo.transform.SetParent(root.transform, false);
                ParticleSystem flames = root.AddComponent<ParticleSystem>();
                ParticleSystem embers = embersGo.AddComponent<ParticleSystem>();
                ParticleSystem hearts = coreGo.AddComponent<ParticleSystem>();
                look.fireFieldFlamesPerSquareMetre = 9f;
                look.fireFieldEmbersPerSquareMetre = 3f;
                look.fireFieldFlameOpacity = 0.6f;
                look.flameJetMaterial = soft;
                look.flameLean = 8f;
                ParticleSystem.MainModule size = flames.main;
                size.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
                var body = new Gradient();
                body.SetKeys(new[] { new GradientColorKey(new Color(0.61f, 0.32f, 0.13f), 0f), new GradientColorKey(new Color(0.3f, 0.1f, 0.05f), 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                FlameLook.ApplyFloor(flames, 2.5f, new FlameLook.FireColours(body, body, Color.yellow, Color.red), look);

                float area = Mathf.PI * 2.5f * 2.5f;
                Assert.AreEqual(9f * area, flames.emission.rateOverTime.constant, 1e-3f, "Flames Per Square Metre over the whole burning area");
                Assert.AreEqual(3f * area, embers.emission.rateOverTime.constant, 1e-3f, "Embers Per Square Metre");
                Assert.AreEqual(2.5f - 0.4f, flames.shape.radius, 1e-4f, "the flames' middles keep half the biggest flame's width in from the edge");
                Assert.AreEqual(flames.shape.radius, hearts.shape.radius, 1e-4f, "their hearts burn on them");
                Assert.AreEqual(flames.shape.position, hearts.shape.position);
                Assert.AreEqual(9f * area, hearts.emission.rateOverTime.constant, 1e-3f, "one heart for every flame");
                Assert.AreEqual(2.5f, embers.shape.radius, 1e-4f, "the embers rise from all of it");
                Assert.AreEqual(new Color(1f, 1f, 1f, 0.6f), flames.main.startColor.color,
                    "every flame starts plain white (its colour is its age) and at most Flame Opacity solid, so whoever stands in the fire still shows through it");
                Color drawn = flames.colorOverLifetime.color.gradient.Evaluate(0.5f), meant = body.Evaluate(0.5f);
                Assert.AreEqual(meant.r, drawn.r, 2e-3f, "burning through the fire's colour over life");
                Assert.AreEqual(meant.g, drawn.g, 2e-3f);
                Assert.AreEqual(meant.b, drawn.b, 2e-3f);
                Assert.AreSame(soft, flames.GetComponent<ParticleSystemRenderer>().sharedMaterial, "the same fire as the Flamethrower's");
                foreach (ParticleSystem flame in new[] { flames, hearts })
                {
                    Assert.AreEqual(ParticleSystemRenderMode.Billboard, flame.GetComponent<ParticleSystemRenderer>().renderMode, flame.name + ": facing the camera, standing up on its screen");
                    Assert.AreEqual(-8f * Mathf.Deg2Rad, flame.main.startRotation.constantMin, 1e-4f, flame.name + ": straight up, leaning by up to Flame Lean");
                    Assert.AreEqual(8f * Mathf.Deg2Rad, flame.main.startRotation.constantMax, 1e-4f, flame.name);
                    Assert.IsFalse(flame.rotationOverLifetime.enabled, flame.name + ": and never spinning, so the tongues stay up");
                    Assert.IsTrue(flame.textureSheetAnimation.enabled, flame.name + ": the fire sheet's frames");
                }
                foreach (ParticleSystem system in new[] { flames, embers })
                {
                    ParticleSystem.Particle[] particles = Simulated(system, 1f);
                    Assert.Greater(particles.Length, 20, system.name + " burned");
                    float farthest = 0f;
                    float half = system == flames ? 0.4f : 0f;
                    foreach (ParticleSystem.Particle particle in particles)
                    {
                        float flat = new Vector2(particle.position.x, particle.position.z).magnitude;
                        Assert.LessOrEqual(flat + half, 2.5f + 1e-3f, $"{system.name} at {particle.position:F3} draws outside the burning ground");
                        Assert.GreaterOrEqual(particle.position.y, half - 1e-3f,
                            $"{system.name} at {particle.position:F3} sinks into the floor: facing the camera, the floor cuts it flat");
                        farthest = Mathf.Max(farthest, flat + half);
                    }
                    Assert.Greater(farthest, 2.5f * 0.85f, system.name + " covers the ground out to its edge");
                    Assert.Greater(particles.Count(p => p.position.x < -0.5f), 0, "all round, not a fan");
                    Assert.Greater(particles.Count(p => p.position.z < -0.5f), 0);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(soft);
            }
        }

        [Test]
        public void TheBurningGroundCatchesInsteadOfFlaringAllAtOnce()
        {
            var root = new GameObject("floor flames catch test");
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            try
            {
                ParticleSystem flames = root.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = flames.main;
                main.prewarm = true;
                ParticleSystem.EmissionModule emission = flames.emission;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });
                look.fireFieldFlameCatch = 0.3f;
                var ramp = new Gradient();
                ramp.SetKeys(new[] { new GradientColorKey(Color.white, 0f) }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
                FlameLook.ApplyFloor(flames, 2.5f, new FlameLook.FireColours(ramp, ramp, Color.white, Color.yellow), look);
                Assert.IsFalse(flames.main.prewarm, "never lit already burning");
                Assert.AreEqual(0, flames.emission.burstCount, "no burst: the flames come one after another");
                Gradient life = flames.colorOverLifetime.color.gradient;
                Assert.AreEqual(0f, life.Evaluate(0f).a, 1e-4f, "each flame catches from nothing");
                Assert.Less(life.Evaluate(0.15f).a, 0.75f, "swelling up over Fire Field Flame Catch of its life");
                Assert.Greater(life.Evaluate(0.3f).a, 0.95f, "and burns fully from then");
                Assert.AreEqual(0f, life.Evaluate(1f).a, 1e-4f, "fading out to nothing at the top");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(look);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Fire Field.prefab");
            GameObject field = Object.Instantiate(prefab);
            try
            {
                FireField fire = field.GetComponent<FireField>();
                typeof(FireField).GetField("radius", Any).SetValue(fire, 2.5f);
                typeof(FireField).GetMethod("ShowLook", Any).Invoke(fire, null);
                ParticleSystem flames = field.transform.Find(FireField.FlamesChildName).GetComponent<ParticleSystem>();
                flames.useAutoRandomSeed = false;
                int early = Simulated(flames, 0.1f).Length;
                int burning = Simulated(flames, 2f).Length;
                Assert.Greater(burning, 10, "the shipped field burns");
                Assert.Less(early, 0.35f * burning, $"a tenth of a second after it lights the fire is still catching ({early} of {burning} flames), not all there at once");
            }
            finally
            {
                Object.DestroyImmediate(field);
            }
        }

        // The field is destroyed the moment its burn ends, taking every flame still in the air with it: the flames stop catching early
        // enough that the longest-lived has burnt out by then, so the fire dies down instead of vanishing mid-flame.
        [Test]
        public void TheBurningGroundDiesDownBeforeTheFieldGoesOut()
        {
            var root = new GameObject("burn out test");
            var embersGo = new GameObject("Embers");
            try
            {
                embersGo.transform.SetParent(root.transform, false);
                ParticleSystem flames = root.AddComponent<ParticleSystem>();
                ParticleSystem embers = embersGo.AddComponent<ParticleSystem>();
                ParticleSystem.MainModule main = flames.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
                ParticleSystem.MainModule emberMain = embers.main;
                emberMain.startLifetime = 1.6f;
                Assert.AreEqual(3f - 1.6f, FlameLook.FloorBurnsOutAt(3f, flames), 1e-4f, "the longest-lived of the flames and embers just has time to burn out");
                emberMain.startLifetime = 0.5f;
                Assert.AreEqual(3f - 1.2f, FlameLook.FloorBurnsOutAt(3f, flames), 1e-4f);
                Assert.AreEqual(0f, FlameLook.FloorBurnsOutAt(1f, flames), 1e-4f, "a fire shorter than a flame's life stops catching at once, never before it lights");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            MethodInfo burnOut = typeof(FireField).GetMethod("BurnOut", Any);
            Assert.IsNotNull(burnOut, "FireField puts its flames out");
            Assert.IsTrue(IlWiring.Uses(typeof(FireField), nameof(FireField.OnPhotonInstantiate), burnOut), "every client puts its own copy's flames out");
            Assert.IsTrue(IlWiring.Uses(typeof(FireField), "BurnOut", typeof(FlameLook).GetMethod(nameof(FlameLook.FloorBurnsOutAt))), "when the tested rule says");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(FireField), "BurnOut",
                typeof(ParticleSystem).GetMethod(nameof(ParticleSystem.Stop), new[] { typeof(bool), typeof(ParticleSystemStopBehavior) })),
                "by stopping new flames, so those in the air burn out on their own");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Fire Field.prefab");
            GameObject field = Object.Instantiate(prefab);
            try
            {
                FireField fire = field.GetComponent<FireField>();
                float duration = (float)typeof(FireField).GetField("duration", Any).GetValue(fire);
                typeof(FireField).GetMethod("ShowLook", Any).Invoke(fire, null);
                ParticleSystem flames = field.transform.Find(FireField.FlamesChildName).GetComponent<ParticleSystem>();
                flames.useAutoRandomSeed = false;
                float outAt = FlameLook.FloorBurnsOutAt(duration, flames);
                Assert.Greater(outAt, 0.5f * duration, "the shipped fire burns fully for at least half of its burn");
                flames.Simulate(outAt, true, true);
                Assert.Greater(flames.particleCount, 10, "burning when it stops catching");
                flames.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                flames.Simulate(duration - outAt, true, false);
                foreach (ParticleSystem system in flames.GetComponentsInChildren<ParticleSystem>(true))
                    Assert.AreEqual(0, system.particleCount, system.name + ": every flame has burnt out by the time the field goes out");
            }
            finally
            {
                Object.DestroyImmediate(field);
            }
        }

        [Test]
        public void TheShippedFieldBurnsOverTheRadiusItWasSent()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Fire Field.prefab");
            GameObject field = Object.Instantiate(prefab);
            try
            {
                FireField fire = field.GetComponent<FireField>();
                typeof(FireField).GetField("radius", Any).SetValue(fire, 3.25f);
                typeof(FireField).GetMethod("ShowLook", Any).Invoke(fire, null);

                ParticleSystem flames = field.transform.Find(FireField.FlamesChildName).GetComponent<ParticleSystem>();
                foreach (ParticleSystem system in flames.GetComponentsInChildren<ParticleSystem>(true))
                {
                    float half = system.textureSheetAnimation.enabled ? 0.5f * FlameLook.BiggestFlame(flames) : 0f;
                    Assert.Greater(3.25f - half, 3.25f * 0.5f, system.name + ": its flames are small next to the ground they burn");
                    Assert.AreEqual(3.25f - half, system.shape.radius, 1e-4f, system.name + ": the radius the field was sent, less half its biggest flame");
                    Assert.AreEqual(half, system.shape.position.y, 1e-4f, system.name + ": lifted half its biggest flame off the floor");
                    Assert.AreEqual(360f, system.shape.arc, 1e-4f, system.name + ": the whole circle burns");
                    ParticleSystem.Particle[] particles = Simulated(system, 1.5f);
                    Assert.Greater(particles.Length, 0, system.name + " burned");
                    foreach (ParticleSystem.Particle particle in particles)
                        Assert.LessOrEqual(new Vector2(particle.position.x, particle.position.z).magnitude + half, 3.25f + 1e-3f,
                            $"{system.name} particle at {particle.position:F3} draws off the burning ground: its prefab moves it sideways");
                }
                UiTheme theme = (UiTheme)typeof(FireField).GetField("theme", Any).GetValue(fire);
                ParticleSystem hearts = flames.transform.Find(FlameLook.CoreChildName).GetComponent<ParticleSystem>();
                Assert.AreEqual(theme.fireBodyOverLife.Evaluate(0.7f).g, flames.colorOverLifetime.color.gradient.Evaluate(0.7f).g, 2e-3f, "the fire's own colours, not a team's");
                Assert.AreEqual(theme.fireCoreOverLife.Evaluate(0.7f).g, hearts.colorOverLifetime.color.gradient.Evaluate(0.7f).g, 2e-3f, "and its hearts'");
                Assert.AreEqual(theme.arenaLook.flameBodyGlow, Glow(flames), 1e-3f, "the burning ground's flames as bright as the jet's");
                Assert.AreEqual(theme.arenaLook.flameGlow, Glow(hearts), 1e-3f, "and their hearts glow by Flame Glow, as the jet's");

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
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Fire Field.prefab");
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
            Assert.IsTrue(IlWiring.Uses(typeof(FireField), "ShowLook", typeof(FlameLook).GetMethod(nameof(FlameLook.Colours))), "in the fire colours of the theme");
        }

        [Test]
        public void TheBurningGroundsHeartsBurnOnTheirFlames()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Resources/Fire Field.prefab");
            GameObject field = Object.Instantiate(prefab);
            try
            {
                FireField fire = field.GetComponent<FireField>();
                typeof(FireField).GetField("radius", Any).SetValue(fire, 2.5f);
                typeof(FireField).GetMethod("ShowLook", Any).Invoke(fire, null);
                ParticleSystem flames = field.transform.Find(FireField.FlamesChildName).GetComponent<ParticleSystem>();
                UiTheme theme = (UiTheme)typeof(FireField).GetField("theme", Any).GetValue(fire);
                Transform core = flames.transform.Find(FlameLook.CoreChildName);
                Assert.IsNotNull(core, "the burning ground's flames have their hearts");
                FlameLookTests.AssertHeartsOnFlames(flames, core.GetComponent<ParticleSystem>(), theme.arenaLook.flameCoreSize, 1.5f);
            }
            finally
            {
                Object.DestroyImmediate(field);
            }
        }

        private static float Glow(ParticleSystem system)
        {
            var block = new MaterialPropertyBlock();
            system.GetComponent<ParticleSystemRenderer>().GetPropertyBlock(block);
            return block.GetVector("_BaseColor").x;
        }
    }
}
