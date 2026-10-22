using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Combat;
using Overpower.UI;
using Overpower.Weapons;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The Flamethrower's flames jet out of the gun and spread over the cone it really burns (its live range and angle), and never
    /// leave it, so the fire never claims more ground than it hurts; the flat fan stays as a faint marker of that ground.</summary>
    public class FlameLookTests
    {
        // The Flamethrower's cone as Flamethrower.prefab ships it, and the gun's muzzle as Multiplayer Player.prefab holds it (from the
        // player's feet); literal here, both may be retuned freely.
        private const float Range = 6f;
        private const float Angle = 60f;
        private static readonly Vector3 Muzzle = new Vector3(0.07f, 1.49f, 0.68f);

        private static ParticleSystem[] SimulatedSystems(ParticleSystem flames, float seconds)
        {
            ParticleSystem[] systems = flames.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem system in systems)
                system.Simulate(seconds, false, true);
            return systems;
        }

        private static ParticleSystem.Particle[] ParticlesOf(ParticleSystem system)
        {
            var particles = new ParticleSystem.Particle[system.main.maxParticles];
            int count = system.GetParticles(particles);
            return particles.Take(count).ToArray();
        }

        // The jet's look numbers, literal here (the shipped values may be retuned freely).
        private static ArenaLook JetLook()
        {
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            look.flameEmbersPerSecond = 60f;
            look.flameJetReach = 0.95f;
            look.flameJetSpread = 0.5f;
            look.flameStretch = 2f;
            look.flameJetWidth = 0.9f;
            look.flameJetStartSize = 0.1f;
            look.flameJetOverlap = 1.5f;
            look.flameJetLandHeight = 0.1f;
            return look;
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        private static float Bearing(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;

        // The shipped cone, sprayed with the look and placed with its feet at the origin, aiming +Z, the gun at Muzzle.
        private static (GameObject cone, ParticleSystem tongues) SprayedCone(UiTheme theme, float angle)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Abilities/Flamethrower Cone.prefab");
            GameObject cone = Object.Instantiate(prefab);
            var visual = cone.GetComponent<FlameConeVisual>();
            visual.Configure(Range, angle, Color.red, Color.red, true);
            visual.ConfigureFire(Range, angle, theme, 21);
            ParticleSystem tongues = cone.transform.Find(FlameConeVisual.TonguesChildName).GetComponent<ParticleSystem>();
            PinTuning(tongues);
            visual.Place(Vector3.zero, Vector3.forward, Muzzle);
            Seeded(tongues);
            return (cone, tongues);
        }

        // Stopped and cleared first: a playing system takes no new seed.
        private static void Seeded(ParticleSystem tongues)
        {
            foreach (ParticleSystem system in tongues.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.useAutoRandomSeed = false;
                system.randomSeed = 7;
            }
        }

        // The prefab's lifetimes, caps and fall are tuning: literal ones here (today's), so retuning the prefab never fails a rule test.
        private static void PinTuning(ParticleSystem tongues)
        {
            foreach (ParticleSystem system in tongues.GetComponentsInChildren<ParticleSystem>(true))
            {
                bool jet = system == tongues;
                ParticleSystem.MainModule main = system.main;
                main.startLifetime = jet ? 0.4f : 0.9f;
                main.maxParticles = jet ? 200 : 40;
                main.gravityModifier = jet ? -2.25f : 0f;
            }
        }

        private static (GameObject root, ParticleSystem flames) NewFlames()
        {
            var root = new GameObject("flame jet test");
            var embersGo = new GameObject("Embers");
            embersGo.transform.SetParent(root.transform, false);
            ParticleSystem flames = root.AddComponent<ParticleSystem>();
            embersGo.AddComponent<ParticleSystem>();
            return (root, flames);
        }

        [Test]
        public void FlameGlowRidesInTheFlamesBaseColour()
        {
            (GameObject root, ParticleSystem flames) = NewFlames();
            ArenaLook look = JetLook();
            try
            {
                look.flameGlow = 2.5f;
                FlameLook.Apply(flames, Range, Angle, Color.white, Color.yellow, look);
                var block = new MaterialPropertyBlock();
                flames.GetComponent<ParticleSystemRenderer>().GetPropertyBlock(block);
                Color glow = block.GetColor("_BaseColor");
                Assert.AreEqual(2.5f, glow.r, 1e-3f, "a particle's colour stops at 1, so the glow that blooms rides in the renderer's base colour");
                Assert.AreEqual(glow.r, glow.g, 1e-5f, "white, so the flames keep their own colours");
                Assert.AreEqual(glow.r, glow.b, 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(look);
            }
        }

        [Test]
        public void TheJetStaysInsideTheHitFan()
        {
            (GameObject root, ParticleSystem flames) = NewFlames();
            ArenaLook look = JetLook();
            try
            {
                look.flameJetReach = 1f;
                ParticleSystem.MainModule main = flames.main;
                main.startLifetime = 0.4f; // a flame's life, so the jet is fast and dense like the shipped one
                foreach ((float angle, Vector3 muzzle) in new[] { (Angle, Muzzle), (20f, Muzzle), (Angle, new Vector3(1.5f, 1.4f, 0.3f)), (Angle, new Vector3(0f, 1.4f, -0.4f)) })
                {
                    FlameLook.Apply(flames, Range, angle, Color.yellow, Color.red, look);
                    FlameLook.AimJet(flames.GetComponentsInChildren<ParticleSystem>(true), muzzle, Range, angle, look);
                    foreach (ParticleSystem system in SimulatedSystems(flames, 0.6f))
                    {
                        ParticleSystem.Particle[] particles = ParticlesOf(system);
                        Assert.Greater(particles.Length, 10, system.name + " emitted");
                        foreach (ParticleSystem.Particle particle in particles)
                            Assert.IsTrue(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, particle.position, Range, angle),
                                $"{angle} degrees, gun at {muzzle:F2}: {system.name} particle at {particle.position:F3} is outside the fan the flame really burns");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(look);
            }
        }

        [Test]
        public void TheJetLeavesFromTheGunPulledInsideTheCone()
        {
            Assert.AreEqual(Muzzle, FlameLook.JetFrom(Muzzle, Range, Angle), "a gun inside the cone is where the flames leave from");
            Vector3 aside = FlameLook.JetFrom(new Vector3(1.5f, 1.4f, 0.3f), Range, Angle);
            Assert.AreEqual(1.4f, aside.y, 1e-5f, "at the gun's height");
            Assert.IsTrue(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, aside, Range, Angle), "a gun poking out of the cone is pulled into it");
            Assert.Greater(aside.x, 0f, "on the gun's side");
            Vector3 behind = FlameLook.JetFrom(new Vector3(0f, 1.4f, -0.4f), Range, Angle);
            Assert.Greater(behind.z, 0f, "a gun behind the caster's feet is moved in front of them");
            Vector3 far = FlameLook.JetFrom(new Vector3(0f, 1.4f, 5.9f), Range, Angle);
            Assert.LessOrEqual(new Vector2(far.x, far.z).magnitude, Range * 0.5f + 1e-4f, "never so far out that the flames have no room to fly");

            Assert.AreEqual(0.9f * (Range - 0.6f), FlameLook.JetTravel(new Vector3(0f, 1.4f, 0.6f), Range, 0.9f), 1e-4f,
                "Jet Reach of the room left between the gun and the end of the cone");
            Assert.AreEqual(Range - 0.5f, FlameLook.JetTravel(new Vector3(0.3f, 1.4f, 0.4f), Range, 1f), 1e-4f, "measured flat, from the caster's feet");
            Assert.AreEqual(0f, FlameLook.JetTravel(new Vector3(0f, 1f, 9f), Range, 1f), 1e-5f, "never backwards");
        }

        [Test]
        public void TheShippedJetLeavesTheGunAndSpreadsOverTheCone()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Abilities/Flamethrower Cone.prefab");
            GameObject cone = Object.Instantiate(prefab);
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            ArenaLook look = JetLook();
            try
            {
                theme.arenaLook = look;
                look.flameJetReach = 1f;
                var visual = cone.GetComponent<FlameConeVisual>();
                visual.Configure(Range, Angle, Color.red, Color.red, true);
                visual.ConfigureFire(Range, Angle, theme, 21);
                ParticleSystem tongues = cone.transform.Find(FlameConeVisual.TonguesChildName).GetComponent<ParticleSystem>();
                PinTuning(tongues);
                visual.Place(new Vector3(0f, 0.05f, 0f), Vector3.forward, new Vector3(0f, 0.05f, 0f) + Muzzle);
                Seeded(tongues);
                Vector3 muzzleLocal = tongues.transform.InverseTransformPoint(new Vector3(0f, 0.05f, 0f) + Muzzle);

                foreach (ParticleSystem system in SimulatedSystems(tongues, 0.02f))
                    foreach (ParticleSystem.Particle particle in ParticlesOf(system))
                        Assert.Less(Vector3.Distance(muzzleLocal, particle.position), 0.5f, $"{system.name}: a fresh flame at {particle.position:F2} is at the gun");
                Assert.Greater(ParticlesOf(tongues).Length, 0, "the jet started at once");

                foreach (ParticleSystem system in SimulatedSystems(tongues, 1.5f))
                {
                    ParticleSystem.Particle[] particles = ParticlesOf(system);
                    Assert.Greater(particles.Length, 5, system.name + " emitted");
                    foreach (ParticleSystem.Particle particle in particles)
                        Assert.IsTrue(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, particle.position, Range, Angle),
                            $"{system.name} particle at {particle.position:F3} drifted out of the fan: its prefab moves it sideways");
                }
                ParticleSystem.Particle[] flames = ParticlesOf(tongues);
                Assert.Greater(flames.Max(p => new Vector2(p.position.x, p.position.z).magnitude), Range * 0.6f, "the flames fly well out over the cone");
                float spread = FlameLook.JetSpread(Angle, look.flameJetSpread);
                Assert.Less(flames.Min(p => Bearing(p.position)), -spread * 0.3f, "and spread to its left");
                Assert.Greater(flames.Max(p => Bearing(p.position)), spread * 0.3f, "and to its right");
                Assert.Greater(flames.Where(p => p.remainingLifetime > 0.85f * p.startLifetime).Average(p => p.position.y), 1f,
                    "a jet leaving the gun at chest height, not flames rising from the floor");
            }
            finally
            {
                Object.DestroyImmediate(cone);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void TheFlamesAndEmbersAreDrawnWithTheArenaLooksMaterials()
        {
            (GameObject root, ParticleSystem flames) = NewFlames();
            ArenaLook look = JetLook();
            var jet = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            var alpha = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            var additive = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            try
            {
                look.fxAlpha = alpha;
                look.fxAdditive = additive;
                look.flameStretch = 3.5f;
                FlameLook.Apply(flames, Range, Angle, Color.yellow, Color.red, look);
                var renderer = root.GetComponent<ParticleSystemRenderer>();
                Assert.AreSame(alpha, renderer.sharedMaterial, "no Flame Jet material: the flames keep the see-through Fx Alpha");
                Assert.AreSame(additive, root.transform.Find("Embers").GetComponent<ParticleSystemRenderer>().sharedMaterial, "the embers: Arena Look > Fx Additive");

                look.flameJetMaterial = jet;
                // A prefab edit that would break the cone rule: rolled flames, extra paths, longer while the camera moves, pushed round.
                ParticleSystem.MainModule main = flames.main;
                main.startRotation = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                ParticleSystem.ShapeModule paths = flames.shape;
                paths.arcSpread = 0.5f;
                renderer.cameraVelocityScale = 1f;
                ParticleSystem.VelocityOverLifetimeModule push = flames.velocityOverLifetime;
                push.orbitalY = 2f;
                push.radial = 1f;
                push.speedModifier = 2f;
                FlameLook.Apply(flames, Range, Angle, Color.yellow, Color.red, look);
                Assert.AreSame(jet, renderer.sharedMaterial, "the flames: Arena Look > Flame Jet Material, the pointed flame shape");
                Assert.AreEqual(ParticleSystemRenderMode.Stretch, renderer.renderMode, "each flame is stretched along its flight, so the jet points at the target");
                Assert.AreEqual(3.5f, renderer.lengthScale, 1e-5f, "Flame Stretch times longer than wide");
                Assert.AreEqual(0f, renderer.velocityScale, "never longer for flying faster: the cone rule counts on the length alone");
                Assert.AreEqual(0f, renderer.cameraVelocityScale, "nor while the camera follows a running caster");
                Assert.AreEqual(ParticleSystemCurveMode.Constant, flames.main.startRotation.mode, "each flame points along its own flight, never rolled off it");
                Assert.AreEqual(0f, flames.main.startRotation.constant);
                Assert.AreEqual(0f, flames.shape.arcSpread, "paths anywhere on the spread, not on a few fixed lines");
                Assert.AreEqual(0f, flames.velocityOverLifetime.orbitalY.constant, "never swung round the caster");
                Assert.AreEqual(0f, flames.velocityOverLifetime.radial.constant, "nor pushed out from the aim");
                Assert.AreEqual(1f, flames.velocityOverLifetime.speedModifier.constant, "nor sped up past Jet Travel");
                Assert.AreEqual(ParticleSystemShapeMultiModeValue.Loop, flames.shape.arcMode, "the flames take their paths in turn");
                Assert.AreEqual(ParticleSystemShapeMultiModeValue.Random, root.transform.Find("Embers").GetComponent<ParticleSystem>().shape.arcMode, "the embers fly anywhere");
                Assert.AreEqual(ParticleSystemRenderMode.Billboard, root.transform.Find("Embers").GetComponent<ParticleSystemRenderer>().renderMode, "the embers stay sparks");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(jet);
                Object.DestroyImmediate(alpha);
                Object.DestroyImmediate(additive);
            }
        }

        [Test]
        public void TheJetsFlamesStayInsideTheConeWhereverTheyAre()
        {
            // A flame is a wedge: widest at its head (its particle), narrowing to a point on its own path back toward the gun, so its
            // head corners are the nearest the cone's edge. On the jet's outermost path (half the spread off the aim) they touch it.
            const float spread = 30f;
            Assert.AreEqual(30f, FlameLook.JetSpread(Angle, 0.5f), 1e-4f, "Flame Jet Spread of the cone's angle");
            Assert.AreEqual(Angle, FlameLook.JetSpread(Angle, 3f), 1e-4f, "never wider than the cone");
            foreach (float head in new[] { 0.7f, 2f, 5.7f })
            {
                float width = FlameLook.JetWidth(head, Angle, spread);
                Assert.Greater(width, 0f);
                Quaternion path = Quaternion.Euler(0f, spread * 0.5f, 0f);
                Vector3 corner = path * new Vector3(width * 0.4995f, 0f, head);
                Assert.IsTrue(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, corner, Range, Angle), $"a flame {width:F2} m wide with its head {head} m out stays inside");
                Vector3 wider = path * new Vector3(width * 0.55f, 0f, head);
                Assert.IsFalse(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, wider, Range, Angle), "control: 10% wider pokes out of the cone");
            }
            Assert.AreEqual(2f * FlameLook.JetWidth(2f, Angle, spread), FlameLook.JetWidth(4f, Angle, spread), 1e-4f,
                "twice as far from the caster, twice as wide: the jet widens as it flies");
            Assert.Greater(FlameLook.JetWidth(4f, Angle, 10f), FlameLook.JetWidth(4f, Angle, spread), "a tighter stream leaves its flames more room to grow");
            Assert.AreEqual(0f, FlameLook.JetWidth(4f, Angle, Angle), 1e-5f, "flames that already fly along the cone's edge have no room");

            Vector3 gun = new Vector3(0.1f, 1.4f, 0.6f);
            Assert.AreEqual(0.6083f + 0.9f * (Range - 0.6083f), FlameLook.JetTipReach(gun, Range, 0.9f), 1e-3f, "the heads stop Jet Reach of the way from the gun to the end");
        }

        [Test]
        public void TheYoungFlamesNeverTrailOutOfTheConeBehindTheGun()
        {
            // A flame just out of the gun trails back past it, on its own path; the gun sits close to the cone's narrow tip.
            const float spread = 30f;
            var gun = new Vector3(0.07f, 1.4f, 0.68f);
            float room = FlameLook.JetTailRoom(gun, Angle, spread);
            Assert.Greater(room, 0f);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 path = Quaternion.Euler(0f, side * spread * 0.5f, 0f) * Vector3.forward;
                Assert.IsTrue(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, gun - path * room * 0.99f, Range, Angle), "a tail Jet Tail Room behind the gun is inside");
                if (side < 0f)
                    Assert.IsFalse(ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, gun - path * room * 1.05f, Range, Angle), "control: a little farther leaves the cone");
            }
            Assert.Greater(FlameLook.JetTailRoom(new Vector3(0f, 1.4f, 2f), Angle, spread), room, "farther out, more room behind");
            Assert.AreEqual(0f, FlameLook.JetTailRoom(new Vector3(0.5f, 1.4f, 0.3f), Angle, spread), 1e-5f, "none for a gun on the cone's edge");

            ArenaLook look = JetLook();
            try
            {
                look.flameJetWidth = 1f;
                float widest = FlameLook.JetWidest(gun, Range, Angle, look);
                Assert.LessOrEqual(widest * look.flameJetStartSize * look.flameStretch, room + 1e-4f, "the biggest flame, just out of the gun, trails no farther than the room");
                Assert.LessOrEqual(widest, FlameLook.JetWidth(FlameLook.JetTipReach(gun, Range, look.flameJetReach), Angle, spread) + 1e-4f, "nor is wider than the cone where it lands");
                Assert.LessOrEqual(widest * look.flameJetStartSize, FlameLook.JetWidth(new Vector2(gun.x, gun.z).magnitude, Angle, spread) + 1e-4f, "nor at the gun");
                look.flameJetStartSize = 0.02f;
                look.flameStretch = 1f;
                float landing = FlameLook.JetWidest(gun, Range, Angle, look);
                Assert.AreEqual(FlameLook.JetWidth(FlameLook.JetTipReach(gun, Range, look.flameJetReach), Angle, spread), landing, 1e-3f,
                    "flames small at the gun grow to fill the cone's room where they land");
                look.flameJetWidth = 0.5f;
                Assert.AreEqual(0.5f * landing, FlameLook.JetWidest(gun, Range, Angle, look), 1e-4f, "Flame Jet Width of it");
            }
            finally
            {
                Object.DestroyImmediate(look);
            }
        }

        [Test]
        public void TheShippedFlameShapeNeverDrawsOutsideItsWedge()
        {
            // JetWidth counts on it: a flame draws only inside the wedge from its full-width head (u 0) to a point at its tail (u 1).
            var look = UnityEditor.AssetDatabase.LoadAssetAtPath<ArenaLook>("Assets/Gameplay/Config/ArenaLook.asset");
            Texture shape = look.flameJetMaterial.GetTexture("_BaseMap");
            var pixels = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(pixels.LoadImage(System.IO.File.ReadAllBytes(UnityEditor.AssetDatabase.GetAssetPath(shape))));
                Assert.AreEqual(TextureWrapMode.Clamp, shape.wrapMode, "a flame's edge never wraps round to the other side");
                int drawn = 0;
                for (int y = 0; y < pixels.height; y++)
                    for (int x = 0; x < pixels.width; x++)
                    {
                        float u = (x + 0.5f) / pixels.width, across = Mathf.Abs((y + 0.5f) / pixels.height - 0.5f);
                        if (pixels.GetPixel(x, y).a < 0.02f) continue;
                        drawn++;
                        Assert.LessOrEqual(across, 0.5f * (1f - u) + 1f / pixels.height, $"pixel {x},{y} draws outside the wedge");
                    }
                Assert.Greater(drawn, 0, "the shape draws a flame");
            }
            finally
            {
                Object.DestroyImmediate(pixels);
            }
        }

        [Test]
        public void TheJetDipsFromTheGunToLandAtItsHeight()
        {
            float dip = FlameLook.JetDip(1.5f, 0.1f, 5f, 0.4f, 0f);
            Assert.AreEqual(1.4f / 5f, Mathf.Tan(dip * Mathf.Deg2Rad), 1e-4f, "a straight jet aimed down from the gun to the landing height at the end of its flight");
            const float fall = 6f;
            dip = FlameLook.JetDip(1.5f, 0.1f, 5f, 0.4f, fall);
            float landing = 1.5f - 5f * Mathf.Tan(dip * Mathf.Deg2Rad) - 0.5f * fall * 0.4f * 0.4f;
            Assert.AreEqual(0.1f, landing, 1e-4f, "the flames' own fall counts: aimed down less, they land at the same height");
            Assert.Less(FlameLook.JetDip(1.5f, 0.1f, 5f, 0.4f, 30f), 0f, "a fall steeper than the drop is aimed up instead");
            dip = FlameLook.JetDip(1.5f, 0.1f, 5f, 0.4f, -fall);
            landing = 1.5f - 5f * Mathf.Tan(dip * Mathf.Deg2Rad) + 0.5f * fall * 0.4f * 0.4f;
            Assert.AreEqual(0.1f, landing, 1e-4f, "flames that rise as they fly are aimed down more, so they still land at the same height");
            Assert.AreEqual(0f, FlameLook.JetDip(1.5f, 1.5f, 5f, 0.4f, 0f), 1e-4f, "level when it lands at the gun's height");
            Assert.AreEqual(0f, FlameLook.JetDip(1.5f, 0.1f, 0f, 0.4f, 0f), 1e-4f, "no flight, no dip");
        }

        [Test]
        public void TheJetRateLeavesNoGapAlongTheJet()
        {
            // A flame flies Speed; the next one leaves 1 / Rate later, so the heads are Speed / Rate apart. The shortest flame
            // (a small one, just out of the gun) must reach back to the next one's head, Overlap times over.
            float rate = FlameLook.JetRate(12f, 0.3f, 1f, 0.4f, 1000f);
            Assert.AreEqual(0.3f, 12f / rate, 1e-4f, "one overlap: the heads are exactly the shortest flame's length apart");
            Assert.AreEqual(2f * rate, FlameLook.JetRate(12f, 0.3f, 2f, 0.4f, 1000f), 1e-3f, "Overlap 2: twice as many");
            Assert.AreEqual(2f * rate, FlameLook.JetRate(24f, 0.3f, 1f, 0.4f, 1000f), 1e-3f, "a faster jet needs more flames");
            Assert.AreEqual(0f, FlameLook.JetRate(12f, 0f, 1f, 0.4f, 1000f), "no flame to draw, none thrown");
            Assert.AreEqual(100f / 0.4f, FlameLook.JetRate(12f, 0.003f, 1f, 0.4f, 100f), 1e-2f,
                "tiny flames would ask for 4000 a second: never more than Flame Jet Most Flames alive at once (rate x life)");
        }

        [Test]
        public void AJetWiderThanItsFlamesThrowsEnoughToCoverItsWidthToo()
        {
            // At flight f the paths span 2 f tan(spread / 2) across and a flame is w(f) = w0 + k f wide, so a point sees w / across of
            // them; that share times the one-path need peaks where w = 2 k f, at tan(spread / 2) / (2 k) times it.
            float k = 0.6f * (1f - 0.06f) / 5f;
            Assert.AreEqual(Mathf.Tan(22.5f * Mathf.Deg2Rad) / (2f * k), FlameLook.JetAcross(45f, 5f, 0.6f, 0.06f), 1e-4f,
                "a wide spread of thin flames needs that many times more");
            Assert.Greater(FlameLook.JetAcross(45f, 5f, 0.6f, 0.06f), 1.5f);
            Assert.AreEqual(1f, FlameLook.JetAcross(10f, 5f, 3f, 0.06f), 1e-5f, "flames wider than the spread at every point need no more than one path does");
            Assert.AreEqual(1f, FlameLook.JetAcross(0f, 5f, 0.6f, 0.06f), 1e-5f, "one path: the along-the-jet rule alone");
            Assert.Greater(FlameLook.JetAcross(60f, 5f, 0.6f, 0.06f), FlameLook.JetAcross(45f, 5f, 0.6f, 0.06f), "a wider spread needs more");
            Assert.Greater(FlameLook.JetAcross(45f, 5f, 0.4f, 0.06f), FlameLook.JetAcross(45f, 5f, 0.6f, 0.06f), "thinner flames need more");
        }

        [Test]
        public void EachFlameLeavesAGoldenShareOfTheSpreadRoundFromTheLast()
        {
            // The emitter's arc speed is in whole turns (360 degrees) a second, wrapped into the arc; each flame leaves 1 / Rate after the
            // last, so it starts Speed x 360 / Rate degrees further round. A golden share of the spread never repeats and never clumps.
            float sweep = FlameLook.JetSweep(131f, 45f);
            Assert.AreEqual(0.618034f * 45f, sweep * 360f / 131f, 1e-3f, "the next flame's path 0.618 of the spread round from the last");
            Assert.AreEqual(2f * sweep, FlameLook.JetSweep(262f, 45f), 1e-3f, "twice the flames, twice as fast round, the same step");
            Assert.AreEqual(0f, FlameLook.JetSweep(131f, 0f), "no spread, nothing to sweep");
            Assert.AreEqual(0f, FlameLook.JetSweep(0f, 45f), "no flames, nothing to sweep");
        }

        [Test]
        public void AJetPressedAgainstAWallNeverKeepsMoreThanItsMostFlamesAlive()
        {
            // A wall pulls the gun's muzzle back toward the cone's tip, where the flames must start tiny to stay inside it.
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Abilities/Flamethrower Cone.prefab");
            GameObject cone = Object.Instantiate(prefab);
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            ArenaLook look = JetLook();
            try
            {
                theme.arenaLook = look;
                look.flameJetMostFlames = 80;
                var visual = cone.GetComponent<FlameConeVisual>();
                visual.Configure(Range, Angle, Color.red, Color.red, true);
                visual.ConfigureFire(Range, Angle, theme, 21);
                ParticleSystem tongues = cone.transform.Find(FlameConeVisual.TonguesChildName).GetComponent<ParticleSystem>();
                float life = tongues.main.startLifetime.constantMax;
                visual.Place(Vector3.zero, Vector3.forward, Muzzle);
                float open = tongues.emission.rateOverTime.constant;
                Assert.Less(open * life, 80f, "in the open the jet needs fewer, so the limit never thins it there");
                foreach (Vector3 muzzle in new[] { new Vector3(0f, 1.49f, 0.05f), new Vector3(2f, 1.49f, 0.05f), new Vector3(0.3f, 1.49f, 0.1f) })
                {
                    visual.Place(Vector3.zero, Vector3.forward, muzzle);
                    Assert.Greater(tongues.emission.rateOverTime.constant, open, $"gun at {muzzle:F2}: smaller flames, more of them");
                    Assert.LessOrEqual(tongues.emission.rateOverTime.constant * life, 80f + 1e-3f, $"gun at {muzzle:F2}: no more than Flame Jet Most Flames alive");
                    Assert.LessOrEqual(tongues.main.maxParticles, 81, $"gun at {muzzle:F2}: nor room kept for more, even after spraying in the open");
                    tongues.Simulate(1.2f, false, true);
                    Assert.LessOrEqual(tongues.particleCount, 81, $"gun at {muzzle:F2}: flames alive");
                }
            }
            finally
            {
                Object.DestroyImmediate(cone);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void TheShippedJetIsOneUnbrokenStreamAlongTheAim()
        {
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            ArenaLook look = JetLook();
            (GameObject cone, ParticleSystem tongues) = (null, null);
            try
            {
                theme.arenaLook = look;
                look.flameJetSpread = 0.75f; // paths over three quarters of the cone, so the far flames reach toward its sides
                (cone, tongues) = SprayedCone(theme, Angle);
                ParticleSystem.MainModule main = tongues.main;
                Assert.AreEqual(ParticleSystemCurveMode.Constant, main.startLifetime.mode, "every flame lives as long, so its size says how far it has flown");
                tongues.Simulate(1.2f, false, true);
                var renderer = tongues.GetComponent<ParticleSystemRenderer>();
                ParticleSystem.Particle[] flames = ParticlesOf(tongues);
                Assert.Greater(flames.Length, 20, "a stream, not a few sparks");
                Assert.Less(flames.Length, main.maxParticles, "never capped, which would break the stream");

                Vector3 gun = FlameLook.JetFrom(Muzzle, Range, Angle);
                float tip = FlameLook.JetTipReach(gun, Range, look.flameJetReach);
                float spread = FlameLook.JetSpread(Angle, look.flameJetSpread);
                float widest = FlameLook.JetWidest(gun, Range, Angle, look);
                var near = new System.Collections.Generic.List<float>();
                var far = new System.Collections.Generic.List<float>();
                float bearings = 0f;
                foreach (ParticleSystem.Particle flame in flames)
                {
                    float bearing = Bearing(flame.velocity);
                    Assert.LessOrEqual(Mathf.Abs(bearing), spread * 0.5f + 0.5f, $"a flame flying {bearing:F1} degrees off the aim");
                    bearings += bearing;
                    float size = flame.GetCurrentSize(tongues);
                    Assert.LessOrEqual(size, widest * 1.001f, "never wider than the jet rule allows");
                    float flown = Flat(flame.position - gun);
                    if (flown < 1f) near.Add(size);
                    if (flown > tip - gun.z - 1.5f) far.Add(size);
                }
                Assert.AreEqual(0f, bearings / flames.Length, 4f, "the stream as a whole runs along the aim");
                Assert.Greater(near.Count, 3);
                Assert.Greater(far.Count, 3);
                Assert.Greater(far.Average(), 3f * near.Average(), "small at the gun, wide where it lands: the jet widens as it flies");
                Assert.Greater(far.Max(), 0.55f * widest, "the far flames grow toward the widest the cone allows");

                ParticleSystem.Particle[] landing = flames.Where(f => f.remainingLifetime < 0.15f * f.startLifetime).ToArray();
                ParticleSystem.Particle[] leaving = flames.Where(f => f.remainingLifetime > 0.85f * f.startLifetime).ToArray();
                Assert.Greater(landing.Length, 1);
                Assert.Greater(leaving.Length, 1);
                foreach (ParticleSystem.Particle flame in landing)
                    Assert.That(flame.position.y, Is.InRange(look.flameJetLandHeight - 0.1f, look.flameJetLandHeight + 0.35f),
                        "the jet licks the floor where it lands, lined up with the fan it burns");
                Assert.Greater(leaving.Average(f => f.position.y), 1f, "and leaves the gun at chest height");

                Assert.IsNull(FirstGap(cone, tongues, gun, tip, spread), "one unbroken stream along the aim and across its spread, reaching toward the cone's sides");
                ParticleSystem.ShapeModule paths = tongues.shape;
                paths.arcSpread = 1f;
                tongues.Simulate(1.2f, false, true);
                Assert.IsNotNull(FirstGap(cone, tongues, gun, tip, spread), "control: the same flames thrown along only the spread's two edges leave holes");
            }
            finally
            {
                Object.DestroyImmediate(cone);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }
        }

        // Seen from straight above, where the drawn fire lies flat on the ground it burns: a point is on fire when some flame's quad covers
        // it with its shape's alpha times the flame's own fade over Drawn. Points: along the aim from the gun to 80% of the flight, 80% of
        // the way to the spread's edges from a metre out (nearer, the flames are still a thin nozzle inside their own spread), and
        // toward the cone's sides (60% of its half angle) over the far part of the jet.
        private const float Drawn = 0.15f;

        private static string FirstGap(GameObject cone, ParticleSystem tongues, Vector3 gun, float tip, float spread)
        {
            var eye = new GameObject("flame jet coverage camera");
            var mesh = new Mesh();
            var shape = new Texture2D(2, 2);
            try
            {
                var renderer = tongues.GetComponent<ParticleSystemRenderer>();
                Texture source = renderer.sharedMaterial.GetTexture("_BaseMap");
                Assert.IsTrue(shape.LoadImage(System.IO.File.ReadAllBytes(UnityEditor.AssetDatabase.GetAssetPath(source))));
                Camera camera = eye.AddComponent<Camera>();
                camera.enabled = false;
                eye.transform.SetPositionAndRotation(new Vector3(0f, 40f, Range * 0.5f), Quaternion.Euler(90f, 0f, 0f));
                renderer.BakeMesh(mesh, camera, ParticleSystemBakeMeshOptions.Default);
                Vector3[] vertices = mesh.vertices;
                Color32[] colours = mesh.colors32;
                var uv = new System.Collections.Generic.List<Vector2>();
                mesh.GetUVs(0, uv);
                var quads = new System.Collections.Generic.List<(Vector2 o, Vector2 a, Vector2 b, Vector2 uo, Vector2 ua, Vector2 ub, float alpha)>();
                for (int q = 0; q + 3 < vertices.Length; q += 4)
                {
                    // Corners by their texture corner: o the (0,0) one, a its neighbour along u, b along v.
                    int o = q, a = q, b = q;
                    for (int k = q; k < q + 4; k++)
                    {
                        if (uv[k].x + uv[k].y < uv[o].x + uv[o].y) o = k;
                    }
                    for (int k = q; k < q + 4; k++)
                    {
                        Vector2 d = uv[k] - uv[o];
                        if (Mathf.Abs(d.x) > 0.5f && Mathf.Abs(d.y) < 0.5f) a = k;
                        if (Mathf.Abs(d.y) > 0.5f && Mathf.Abs(d.x) < 0.5f) b = k;
                    }
                    Vector3 wo = tongues.transform.TransformPoint(vertices[o]), wa = tongues.transform.TransformPoint(vertices[a]), wb = tongues.transform.TransformPoint(vertices[b]);
                    quads.Add((new Vector2(wo.x, wo.z), new Vector2(wa.x, wa.z), new Vector2(wb.x, wb.z), uv[o], uv[a], uv[b], colours[o].a / 255f));
                }
                Vector2 from = new Vector2(gun.x, gun.z);
                float flight = tip - from.magnitude;
                var points = new System.Collections.Generic.List<(Vector2 at, string where)>();
                for (float f = 0.3f; f <= 0.8f * flight; f += 0.2f)
                    foreach (float bearing in f < 1f ? new[] { 0f } : new[] { 0f, -0.4f * spread, 0.4f * spread })
                        points.Add((from + Flat2(bearing) * f, $"{f:F1} m from the gun, {bearing:F0} degrees off the aim"));
                for (float d = 0.6f * tip; d <= 0.8f * tip; d += 0.2f)
                    foreach (float bearing in new[] { -0.3f * Angle, 0.3f * Angle })
                        points.Add((Flat2(bearing) * d, $"{d:F1} m from the caster, {bearing:F0} degrees toward the cone's side"));
                foreach ((Vector2 at, string where) in points)
                {
                    bool lit = false;
                    foreach (var quad in quads)
                    {
                        Vector2 eu = quad.a - quad.o, ev = quad.b - quad.o, x = at - quad.o;
                        float det = eu.x * ev.y - eu.y * ev.x;
                        if (Mathf.Abs(det) < 1e-8f) continue;
                        float s = (x.x * ev.y - x.y * ev.x) / det, t = (eu.x * x.y - eu.y * x.x) / det;
                        if (s < 0f || s > 1f || t < 0f || t > 1f) continue;
                        Vector2 texel = quad.uo + s * (quad.ua - quad.uo) + t * (quad.ub - quad.uo);
                        if (shape.GetPixelBilinear(texel.x, texel.y).a * quad.alpha >= Drawn) { lit = true; break; }
                    }
                    if (!lit)
                        return "a gap in the jet " + where;
                }
                return null;
            }
            finally
            {
                Object.DestroyImmediate(eye);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(shape);
            }
        }

        private static Vector2 Flat2(float bearing) => new Vector2(Mathf.Sin(bearing * Mathf.Deg2Rad), Mathf.Cos(bearing * Mathf.Deg2Rad));

        [Test]
        public void EveryDrawnFlameStaysInsideTheHitCone()
        {
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            ArenaLook look = JetLook();
            var eye = new GameObject("flame jet test camera");
            var mesh = new Mesh();
            GameObject cone = null;
            try
            {
                theme.arenaLook = look;
                Camera camera = eye.AddComponent<Camera>();
                camera.enabled = false;
                // Top-down (each flame lies flat, its full width on the floor) and the game camera's 63 degrees with the jet aimed up,
                // right and down the screen.
                var views = new[] { new Vector3(0f, 14f, 3f), new Vector3(0f, 10f, -5f), new Vector3(5f, 10f, 3f), new Vector3(0f, 10f, 8f) };
                foreach ((float angle, float width, bool inside) in new[] { (Angle, 1f, true), (20f, 1f, true), (Angle, 1.6f, false) })
                {
                    look.flameJetWidth = width;
                    if (cone != null) Object.DestroyImmediate(cone);
                    ParticleSystem tongues;
                    (cone, tongues) = SprayedCone(theme, angle);
                    tongues.Simulate(1.2f, false, true);
                    var renderer = tongues.GetComponent<ParticleSystemRenderer>();
                    bool allInside = true;
                    string outside = "";
                    foreach (Vector3 view in views)
                    {
                        eye.transform.position = view;
                        eye.transform.LookAt(new Vector3(0f, 0.5f, 3f));
                        renderer.BakeMesh(mesh, camera, ParticleSystemBakeMeshOptions.Default);
                        Assert.Greater(mesh.vertexCount, 40, "the flames were drawn");
                        // Each flame is a quad: its two head corners (u 0) and the middle of its tail edge (u 1) span the wedge it draws
                        // (TheShippedFlameShapeNeverDrawsOutsideItsWedge).
                        Vector3[] vertices = mesh.vertices;
                        var uv = new System.Collections.Generic.List<Vector2>();
                        mesh.GetUVs(0, uv);
                        for (int quad = 0; quad + 3 < vertices.Length; quad += 4)
                        {
                            Vector3 tail = Vector3.zero;
                            var drawn = new System.Collections.Generic.List<Vector3>();
                            for (int k = quad; k < quad + 4; k++)
                                if (uv[k].x < 0.5f) drawn.Add(vertices[k]);
                                else tail += vertices[k] * 0.5f;
                            drawn.Add(tail);
                            foreach (Vector3 vertex in drawn)
                            {
                                Vector3 local = cone.transform.InverseTransformPoint(tongues.transform.TransformPoint(vertex));
                                if (!ConeFilter.IsWithinCone(Vector3.zero, Vector3.forward, local, Range + 0.01f, angle + 0.2f))
                                {
                                    allInside = false;
                                    outside = $"seen from {view}: a flame's edge at {local:F2}";
                                }
                            }
                        }
                    }
                    if (inside)
                        Assert.IsTrue(allInside, $"{angle} degrees: {outside} is outside the fan the flame really burns");
                    else
                        Assert.IsFalse(allInside, "control: flames wider than the jet rule allows poke out of the cone");
                }
            }
            finally
            {
                if (cone != null) Object.DestroyImmediate(cone);
                Object.DestroyImmediate(eye);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void EachSprayHandsTheLiveConeToTheFlames()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Abilities/Flamethrower Cone.prefab");
            GameObject cone = Object.Instantiate(prefab);
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            ArenaLook look = JetLook();
            try
            {
                theme.arenaLook = look;
                look.flameJetSpread = 0.4f;
                look.flameJetStartSize = 0.15f;
                cone.GetComponent<FlameConeVisual>().ConfigureFire(3.25f, 71f, theme, 21);
                ParticleSystem tongues = cone.transform.Find(FlameConeVisual.TonguesChildName).GetComponent<ParticleSystem>();
                foreach (ParticleSystem system in tongues.GetComponentsInChildren<ParticleSystem>(true))
                {
                    float arc = system == tongues ? FlameLook.JetSpread(71f, 0.4f) : 71f;
                    Assert.AreEqual(arc, system.shape.arc, 1e-4f, system.name + ": the angle handed in (the flames: Flame Jet Spread of it; the embers: all of it)");
                    Assert.AreEqual(FlameLook.Sector(arc).y, system.shape.rotation.z, 1e-4f, system.name + ": centred on the aim");
                    Assert.AreEqual(ParticleSystemSimulationSpace.Local, system.main.simulationSpace, system.name + ": turns with the aim, so it stays in the cone");
                }
                Assert.AreEqual(ParticleSystemCurveMode.Constant, tongues.main.startLifetime.mode, "every flame lives as long, so its size says how far it has flown");
                ParticleSystem.MinMaxCurve growth = tongues.sizeOverLifetime.size;
                Assert.IsTrue(tongues.sizeOverLifetime.enabled);
                Assert.AreEqual(0.15f, growth.Evaluate(0f), 1e-3f, "Flame Jet Start Size of its full size as it leaves the gun");
                Assert.AreEqual(1f, growth.Evaluate(1f), 1e-3f, "full size where it lands");
                Assert.AreEqual(0.575f, growth.Evaluate(0.5f), 1e-3f, "growing evenly with the way it has flown");
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
            int fireAt = IlWiring.CallOffsets(show, typeof(FlameConeVisual).GetMethod(nameof(FlameConeVisual.ConfigureFire))).Single();
            Assert.Less(fanAt, fireAt);
            int[] rangeAt = IlWiring.FieldLoadOffsets(show, typeof(FlamethrowerAbility).GetField("coneRange", any)).Where(at => at > fanAt && at < fireAt).ToArray();
            int[] angleAt = IlWiring.FieldLoadOffsets(show, typeof(FlamethrowerAbility).GetField("coneAngle", any)).Where(at => at > fanAt && at < fireAt).ToArray();
            Assert.AreEqual(1, rangeAt.Length, "the live Cone Range goes to the flames");
            Assert.AreEqual(1, angleAt.Length, "the live Cone Angle goes to the flames");
            Assert.Less(rangeAt[0], angleAt[0], "range as the range, angle as the angle");
        }

        [Test]
        public void EveryPhysicsStepJetsTheFlamesFromTheGun()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Abilities/Flamethrower Cone.prefab");
            GameObject cone = Object.Instantiate(prefab);
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            ArenaLook look = JetLook();
            try
            {
                theme.arenaLook = look;
                look.flameJetReach = 0.5f;
                look.flameJetSpread = 0.4f;
                look.flameStretch = 3f;
                look.flameJetWidth = 0.7f;
                look.flameJetStartSize = 0.2f;
                look.flameJetOverlap = 2.5f;
                look.flameJetLandHeight = 0.3f;
                var visual = cone.GetComponent<FlameConeVisual>();
                ParticleSystem tongues = cone.transform.Find(FlameConeVisual.TonguesChildName).GetComponent<ParticleSystem>();
                float shareOfWidest = tongues.main.startSize.constantMin / tongues.main.startSize.constantMax;
                visual.Configure(Range, Angle, Color.red, Color.red, true);
                visual.ConfigureFire(Range, Angle, theme, 21);
                var apex = new Vector3(10f, 0f, -4f);
                visual.Place(apex, Vector3.right, apex + new Vector3(0.68f, 1.49f, -0.07f));
                Vector3 expected = FlameLook.JetFrom(cone.transform.InverseTransformPoint(apex + new Vector3(0.68f, 1.49f, -0.07f)), Range, Angle);
                float travel = FlameLook.JetTravel(expected, Range, 0.5f) - FlameLook.JetRadius;
                foreach (ParticleSystem system in tongues.GetComponentsInChildren<ParticleSystem>(true))
                {
                    Assert.That(Vector3.Distance(expected, system.shape.position), Is.LessThan(1e-4f), $"{system.name} leaves from the gun: {system.shape.position:F3}");
                    Assert.AreEqual(travel / system.main.startLifetime.constantMax, system.main.startSpeed.constant, 1e-4f,
                        $"{system.name} flies the Arena Look's Flame Jet Reach of the way, no farther");
                }
                float life = tongues.main.startLifetime.constantMax;
                float speed = travel / life;
                float dip = FlameLook.JetDip(expected.y, 0.3f, travel, life, -Physics.gravity.y * tongues.main.gravityModifier.constant);
                ParticleSystem.VelocityOverLifetimeModule sink = tongues.velocityOverLifetime;
                Assert.IsTrue(sink.enabled);
                Assert.AreEqual(ParticleSystemSimulationSpace.Local, sink.space);
                Assert.AreEqual(-speed * Mathf.Tan(dip * Mathf.Deg2Rad), sink.y.constant, 1e-4f, "the flames sink as they fly, to land at Flame Jet Land Height");
                Assert.AreEqual(0f, sink.x.constant, "never pushed sideways");
                Assert.AreEqual(0f, sink.z.constant, "nor along the aim, so their paths over the floor stay on the jet's spread");

                float widest = FlameLook.JetWidest(expected, Range, Angle, look);
                ParticleSystem.MainModule main = tongues.main;
                Assert.AreEqual(widest, main.startSize.constantMax, 1e-4f, "the flames grow to the widest the jet rule allows");
                Assert.AreEqual(shareOfWidest, main.startSize.constantMin / main.startSize.constantMax, 1e-4f, "the prefab's smallest-to-biggest share kept");
                float across = FlameLook.JetAcross(FlameLook.JetSpread(Angle, 0.4f), travel, widest * shareOfWidest, 0.2f);
                float rate = FlameLook.JetRate(new Vector2(speed, sink.y.constant).magnitude, widest * shareOfWidest * 0.2f * 3f, 2.5f * across, life, look.flameJetMostFlames);
                Assert.AreEqual(rate, tongues.emission.rateOverTime.constant, 1e-2f, "as many flames as an unbroken jet needs: Flame Jet Overlap, the smallest flame's length as it leaves the gun");
                Assert.GreaterOrEqual(main.maxParticles, Mathf.CeilToInt(rate * main.startLifetime.constantMax), "room for every flame alive at once");
                Assert.AreEqual(ParticleSystemShapeMultiModeValue.Loop, tongues.shape.arcMode, "the flames' paths go round the spread in turn, never by chance");
                Assert.AreEqual(FlameLook.JetSweep(rate, tongues.shape.arc), tongues.shape.arcSpeed.constant, 1e-3f, "each a golden share of it from the last, at this rate");
            }
            finally
            {
                Object.DestroyImmediate(cone);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }

            const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            MethodInfo place = typeof(FlameConeVisual).GetMethod(nameof(FlameConeVisual.Place));
            Assert.AreEqual(3, place.GetParameters().Length, "Place takes the gun's muzzle");
            Assert.IsTrue(IlWiring.Uses(typeof(FlameConeVisual), nameof(FlameConeVisual.Place), typeof(FlameLook).GetMethod(nameof(FlameLook.AimJet))), "the jet is aimed by the tested rule");
            Assert.IsTrue(IlWiring.Uses(typeof(FlameLook), nameof(FlameLook.AimJet), typeof(FlameLook).GetMethod("SizeJet", any | BindingFlags.Static)), "each step sizes the flames");
            Assert.IsTrue(IlWiring.Uses(typeof(FlameLook), "SizeJet", typeof(FlameLook).GetMethod(nameof(FlameLook.JetWidest))), "by the cone rule");
            Assert.IsTrue(IlWiring.Uses(typeof(FlameLook), nameof(FlameLook.JetWidest), typeof(FlameLook).GetMethod(nameof(FlameLook.JetTipReach))),
                "the widest a head may be is measured where the tested rule says the heads land");
            Assert.IsTrue(IlWiring.Uses(typeof(FlameLook), "SizeJet", typeof(FlameLook).GetMethod(nameof(FlameLook.JetRate))), "and throws them as often as the no-gap rule asks");
            Assert.IsTrue(IlWiring.Uses(typeof(FlameLook), "SizeJet", typeof(FlameLook).GetMethod(nameof(FlameLook.JetSweep))), "on paths spread evenly over the jet");
            Assert.IsTrue(IlWiring.Uses(typeof(FlameLook), "SizeJet", typeof(FlameLook).GetMethod(nameof(FlameLook.JetAcross))), "enough to cover its width too");
            Assert.IsTrue(IlWiring.Uses(typeof(FlameLook), nameof(FlameLook.AimJet), typeof(FlameLook).GetMethod(nameof(FlameLook.JetDip))), "and aims them down to lick the floor");
            Assert.IsTrue(IlWiring.Uses(typeof(FlameLook), nameof(FlameLook.Apply), typeof(FlameLook).GetMethod(nameof(FlameLook.JetSpread))), "and fanned over the jet's spread");
            Assert.IsTrue(IlWiring.Uses(typeof(FlamethrowerAbility), "PositionVfx", place), "each physics step places the fire with the gun");
            MethodInfo tick = typeof(FlamethrowerAbility).GetMethod("TickCone", any);
            int muzzleAt = IlWiring.CallOffsets(tick, typeof(WeaponFiring).GetProperty(nameof(WeaponFiring.SafeMuzzlePosition)).GetMethod).Single();
            int placeAt = IlWiring.CallOffsets(tick, typeof(FlamethrowerAbility).GetMethod("PositionVfx", any)).Single();
            Assert.Less(muzzleAt, placeAt);
            byte[] il = tick.GetMethodBody().GetILAsByteArray();
            int stored = StoredLocal(il, muzzleAt + 5);
            Assert.GreaterOrEqual(stored, 0, "the muzzle is kept in a local");
            Assert.AreEqual(stored, LoadedLocalJustBefore(il, placeAt), "the muzzle the gun already has (the one the wall check uses) goes to the fire as its last argument");
        }

        // stloc.0-3 / stloc.s right at the offset (the end of a ?: lands on its store), else -1.
        private static int StoredLocal(byte[] il, int at)
        {
            if (il[at] >= 0x0A && il[at] <= 0x0D) return il[at] - 0x0A;
            if (il[at] == 0x13) return il[at + 1];
            return -1;
        }

        // ldloc.0-3 / ldloc.s ending right before the call, else -1.
        private static int LoadedLocalJustBefore(byte[] il, int callAt)
        {
            if (il[callAt - 1] >= 0x06 && il[callAt - 1] <= 0x09) return il[callAt - 1] - 0x06;
            if (il[callAt - 2] == 0x11) return il[callAt - 1];
            return -2;
        }

        [Test]
        public void TheFanIsLeftAFaintMarkerInTheFireColour()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Abilities/Flamethrower Cone.prefab");
            GameObject cone = Object.Instantiate(prefab);
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            try
            {
                theme.arenaLook = look;
                var visual = cone.GetComponent<FlameConeVisual>();
                visual.Configure(Range, Angle, Color.red, Color.red, true);
                byte full = cone.transform.Find("Fan").GetComponent<MeshFilter>().sharedMesh.colors32.Max(c => c.a);
                look.flameFanStrength = 0.4f;
                visual.ConfigureFire(Range, Angle, theme, 21);
                byte faint = cone.transform.Find("Fan").GetComponent<MeshFilter>().sharedMesh.colors32.Max(c => c.a);
                Assert.Greater(full, 20, "the fan drew before");
                Assert.AreEqual(full * 0.4f, faint, 1.5f, "Flame Fan Strength of what it was");
            }
            finally
            {
                Object.DestroyImmediate(cone);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }

            const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            MethodInfo show = typeof(FlamethrowerAbility).GetMethod("ShowVfx", any);
            var edgeAt = IlWiring.CallOffsets(show, typeof(FlameLook).GetMethod(nameof(FlameLook.Edge)));
            int fanAt = IlWiring.CallOffsets(show, typeof(FlameConeVisual).GetMethod(nameof(FlameConeVisual.Configure))).Single();
            Assert.AreEqual(1, edgeAt.Count, "the fan takes the fire colour of the look table, so one colour change recolours the whole Flamethrower");
            Assert.Less(edgeAt[0], fanAt, "read before the fan is coloured");
            byte[] il = show.GetMethodBody().GetILAsByteArray();
            int fire = StoredLocal(il, edgeAt[0] + 5);
            Assert.GreaterOrEqual(fire, 0, "the fire colour is kept in a local");
            int angleAt = IlWiring.FieldLoadOffsets(show, typeof(FlamethrowerAbility).GetField("coneAngle", any)).Where(at => at < fanAt).Max();
            Assert.AreEqual(fire, LoadedLocalAt(il, angleAt + 5), "and that fire colour is the one the fan is coloured with, right after the cone angle");
        }

        [Test]
        public void AimingTheJetEveryPhysicsStepAllocatesNothing()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Abilities/Flamethrower Cone.prefab");
            GameObject cone = Object.Instantiate(prefab);
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            try
            {
                theme.arenaLook = look;
                var visual = cone.GetComponent<FlameConeVisual>();
                visual.Configure(Range, Angle, Color.red, Color.red, true);
                visual.ConfigureFire(Range, Angle, theme, 21);
                // Far from any collider in the open scene, so only the jet's own work is measured. GC.GetAllocatedBytesForCurrentThread
                // reads 0 in the Editor's Mono whatever is allocated; the profiler's GC.Alloc samples do count.
                var apex = new Vector3(5000f, 0f, 5000f);
                visual.Place(apex, Vector3.right, apex + Muzzle);
                UnityEngine.Profiling.Recorder allocations = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
                allocations.enabled = false;
                allocations.FilterToCurrentThread();
                allocations.enabled = true;
                try
                {
                    for (int step = 0; step < 50; step++)
                        visual.Place(apex, Vector3.right, apex + Muzzle);
                }
                finally
                {
                    allocations.enabled = false;
                    allocations.CollectFromAllThreads();
                }
                Assert.AreEqual(0, allocations.sampleBlockCount, "a second of spraying allocates nothing on any client");
            }
            finally
            {
                Object.DestroyImmediate(cone);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }
        }

        // ldloc.0-3 / ldloc.s starting at the offset, else -2.
        private static int LoadedLocalAt(byte[] il, int at)
        {
            if (il[at] >= 0x06 && il[at] <= 0x09) return il[at] - 0x06;
            if (il[at] == 0x11) return il[at + 1];
            return -2;
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

            Assert.IsTrue(IlWiring.Uses(typeof(FlameConeVisual), nameof(FlameConeVisual.ConfigureFire), typeof(FlameLook).GetMethod(nameof(FlameLook.Apply))));
            Assert.IsTrue(IlWiring.Uses(typeof(FlamethrowerAbility), "ShowVfx", typeof(FlameConeVisual).GetMethod(nameof(FlameConeVisual.ConfigureFire))),
                "each spray sizes the flames to the live cone");
        }
    }
}
