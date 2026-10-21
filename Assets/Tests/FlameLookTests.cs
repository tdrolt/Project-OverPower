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
        public void TheJetStaysInsideTheHitFan()
        {
            (GameObject root, ParticleSystem flames) = NewFlames();
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            try
            {
                look.flameTonguesPerMetre = 40f;
                look.flameEmbersPerSecond = 60f;
                foreach ((float angle, Vector3 muzzle) in new[] { (Angle, Muzzle), (20f, Muzzle), (Angle, new Vector3(1.5f, 1.4f, 0.3f)), (Angle, new Vector3(0f, 1.4f, -0.4f)) })
                {
                    FlameLook.Apply(flames, Range, angle, Color.yellow, Color.red, look);
                    FlameLook.AimJet(flames.GetComponentsInChildren<ParticleSystem>(true), muzzle, Range, angle, 1f);
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
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            try
            {
                theme.arenaLook = look;
                look.flameTonguesPerMetre = 40f;
                look.flameEmbersPerSecond = 60f;
                look.flameJetReach = 1f;
                var visual = cone.GetComponent<FlameConeVisual>();
                visual.Configure(Range, Angle, Color.red, true);
                visual.ConfigureFire(Range, Angle, theme, 21);
                visual.Place(new Vector3(0f, 0.05f, 0f), Vector3.forward, new Vector3(0f, 0.05f, 0f) + Muzzle);
                ParticleSystem tongues = cone.transform.Find(FlameConeVisual.TonguesChildName).GetComponent<ParticleSystem>();
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
                Assert.Less(flames.Min(p => Mathf.Atan2(p.position.x, p.position.z) * Mathf.Rad2Deg), -Angle * 0.25f, "and spread to its left");
                Assert.Greater(flames.Max(p => Mathf.Atan2(p.position.x, p.position.z) * Mathf.Rad2Deg), Angle * 0.25f, "and to its right");
                Assert.Greater(flames.Average(p => p.position.y), 0.8f, "a jet at chest height, not flames rising from the floor");
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
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            var alpha = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            var additive = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            try
            {
                look.fxAlpha = alpha;
                look.fxAdditive = additive;
                FlameLook.Apply(flames, Range, Angle, Color.yellow, Color.red, look);
                Assert.AreSame(alpha, root.GetComponent<ParticleSystemRenderer>().sharedMaterial, "the flames: Arena Look > Fx Alpha, so their colour reads over any floor");
                Assert.AreSame(additive, root.transform.Find("Embers").GetComponent<ParticleSystemRenderer>().sharedMaterial, "the embers: Arena Look > Fx Additive");
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
                cone.GetComponent<FlameConeVisual>().ConfigureFire(3.25f, 71f, theme, 21);
                ParticleSystem tongues = cone.transform.Find(FlameConeVisual.TonguesChildName).GetComponent<ParticleSystem>();
                foreach (ParticleSystem system in tongues.GetComponentsInChildren<ParticleSystem>(true))
                {
                    Assert.AreEqual(71f, system.shape.arc, 1e-4f, system.name + ": the angle handed in");
                    Assert.AreEqual(FlameLook.Sector(71f).y, system.shape.rotation.z, 1e-4f, system.name + ": centred on the aim");
                    Assert.AreEqual(ParticleSystemSimulationSpace.Local, system.main.simulationSpace, system.name + ": turns with the aim, so it stays in the cone");
                }
                Assert.AreEqual(look.flameTonguesPerMetre * 3.25f, tongues.emission.rateOverTime.constant, 1e-3f, "as many flames as the range handed in calls for");
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
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            try
            {
                theme.arenaLook = look;
                look.flameJetReach = 0.5f;
                var visual = cone.GetComponent<FlameConeVisual>();
                visual.Configure(Range, Angle, Color.red, true);
                visual.ConfigureFire(Range, Angle, theme, 21);
                var apex = new Vector3(10f, 0f, -4f);
                visual.Place(apex, Vector3.right, apex + new Vector3(0.68f, 1.49f, -0.07f));
                ParticleSystem tongues = cone.transform.Find(FlameConeVisual.TonguesChildName).GetComponent<ParticleSystem>();
                Vector3 expected = FlameLook.JetFrom(cone.transform.InverseTransformPoint(apex + new Vector3(0.68f, 1.49f, -0.07f)), Range, Angle);
                float travel = FlameLook.JetTravel(expected, Range, 0.5f) - FlameLook.JetRadius;
                foreach (ParticleSystem system in tongues.GetComponentsInChildren<ParticleSystem>(true))
                {
                    Assert.That(Vector3.Distance(expected, system.shape.position), Is.LessThan(1e-4f), $"{system.name} leaves from the gun: {system.shape.position:F3}");
                    Assert.AreEqual(travel / system.main.startLifetime.constantMax, system.main.startSpeed.constant, 1e-4f,
                        $"{system.name} flies the Arena Look's Flame Jet Reach of the way, no farther");
                }
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
                visual.Configure(Range, Angle, Color.red, true);
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
                visual.Configure(Range, Angle, Color.red, true);
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
