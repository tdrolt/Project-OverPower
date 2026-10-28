using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Data;
using Overpower.UI;
using Overpower.Vision;
using Overpower.Weapons;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Every gun flash (impacts, the muzzle flash, the ricochet chevron) is the theme's Flash White times the Arena Look's Gun Flash
    /// Glow, so a hit blooms above the arena's stripes and retuning the white recolours them all. A flash wider than half a metre is an
    /// outline, so the player it lands on still shows through it. The Bounce shot shows a chevron on each wall it bounces off.</summary>
    public class GunFlashTests
    {
        private const string ThemePath = "Assets/_Project/Design/Settings/UiTheme.asset";
        private const string MuzzlePath = "Assets/_Project/Shared/FX/FX Muzzle Flash.prefab";
        private const string ChevronPath = "Assets/_Project/Shared/FX/Impacts/FX Ricochet Chevron.prefab";
        private const string BouncePath = "Assets/_Project/Design/Weapons/Projectiles/Burst Bullet - Bounce.prefab";

        private static IEnumerable<string> FlashPaths()
        {
            var paths = new SortedSet<string> { MuzzlePath, ChevronPath };
            foreach (WeaponDefinition weapon in BulletPrefabGuardTests.Weapons())
                paths.Add(AssetDatabase.GetAssetPath(weapon.ImpactVfx));
            return paths;
        }

        [Test]
        public void AGunFlashIsFlashWhiteTimesItsGlow()
        {
            Color c = FlashTint.ColourFor(new Color(1f, 0.9f, 0.8f, 1f), 3f);
            Assert.AreEqual(3f, c.r, 1e-5f);
            Assert.AreEqual(2.7f, c.g, 1e-5f);
            Assert.AreEqual(2.4f, c.b, 1e-5f);
            Assert.AreEqual(1f, c.a, 1e-5f, "the glow brightens, it never makes the flash more solid");
        }

        [Test]
        public void EveryGunFlashTakesItsWhiteAndGlowFromTheTheme()
        {
            foreach (string path in FlashPaths())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path);
                var tint = prefab.GetComponent<FlashTint>();
                Assert.IsNotNull(tint, path + " is tinted from the theme");
                Assert.AreEqual(ThemePath, AssetDatabase.GetAssetPath(new SerializedObject(tint).FindProperty("theme").objectReferenceValue), path);
            }
            Assert.IsTrue(IlWiring.Uses(typeof(FlashTint), "Apply", typeof(FlashTint).GetMethod(nameof(FlashTint.ColourFor))), "the tested colour");
            Assert.IsTrue(IlWiring.Uses(typeof(FlashTint), "Apply", typeof(UiTheme).GetField(nameof(UiTheme.flashWhiteColor))), "from Flash White");
            Assert.IsTrue(IlWiring.Uses(typeof(FlashTint), "Apply", typeof(ArenaLook).GetField(nameof(ArenaLook.gunFlashGlow))), "times Gun Flash Glow");
            Assert.IsTrue(IlWiring.Uses(typeof(FlashTint), "Awake", typeof(FlashTint).GetMethod(nameof(FlashTint.Apply))),
                "once per copy as it is made (VFXManager reuses copies, so nothing is set per flash)");
        }

        [Test]
        public void AFlashWiderThanHalfAMetreIsAnOutlineSoThePlayerShowsThrough()
        {
            foreach (string path in FlashPaths())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path);
                foreach (ParticleSystem system in prefab.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var renderer = system.GetComponent<ParticleSystemRenderer>();
                    if (renderer.renderMode != ParticleSystemRenderMode.Mesh || system.main.startSize.constantMax <= 0.5f)
                        continue;
                    Mesh mesh = renderer.mesh;
                    Assert.IsNotNull(mesh, path + "/" + system.name);
                    Assert.LessOrEqual(Coverage(mesh), 0.3f, path + "/" + system.name + " is " + system.main.startSize.constantMax +
                        " m wide: an outline or a thin star, never a filled plate over the player it hit");
                }
            }
        }

        // The share of the mesh's flat bounding box its triangles fill (a filled hexagon is 0.75, a thin outline far less).
        private static float Coverage(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            int[] t = mesh.triangles;
            float area = 0f;
            for (int i = 0; i < t.Length; i += 3)
                area += 0.5f * Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]).magnitude;
            var size = new List<float> { mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z };
            size.Sort();
            return area / (size[1] * size[2]);
        }

        [Test]
        public void AChevronOnlyOnAHitThatBounced()
        {
            Assert.IsTrue(RicochetRule.Bounced(0, 1), "the first bounce");
            Assert.IsTrue(RicochetRule.Bounced(2, 3), "the last bounce still bounces");
            Assert.IsFalse(RicochetRule.Bounced(3, 3), "the wall that ends the shot: its impact, no chevron");
            Assert.IsFalse(RicochetRule.Bounced(1, 1), "a player hit ends the shot without a bounce");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(RicochetChevronView), "OnHit", typeof(RicochetRule).GetMethod(nameof(RicochetRule.Bounced))),
                "the view shows the chevron only when the tested rule says the shot bounced");
            Assert.IsTrue(IlWiring.Uses(typeof(RicochetChevronView), "OnHit", typeof(BounceOffWalls).GetProperty(nameof(BounceOffWalls.Bounces)).GetGetMethod()),
                "from the bounce count the shot really has");
            Assert.IsTrue(IlWiring.Uses(typeof(RicochetChevronView), "OnHit", typeof(TeamSight).GetMethod(nameof(TeamSight.ShotShownAt))),
                "gated like every other shot effect: a bounce in the fog shows nothing");
            Assert.IsTrue(IlWiring.Uses(typeof(RicochetChevronView), "OnHit",
                typeof(VFXManager).GetMethod(nameof(VFXManager.PlayVFX), new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion) })),
                "pooled like the impacts, pointing away from the wall");

            Quaternion away = RicochetRule.Facing(new Vector3(-1f, 0.2f, 0f));
            Assert.Less(Vector3.Angle(away * Vector3.forward, Vector3.left), 0.01f, "the chevron lies level and points away from the wall it bounced off");
            Assert.AreEqual(Quaternion.identity, RicochetRule.Facing(Vector3.up), "a floor or ceiling normal has no level direction: drawn unturned");
        }

        [Test]
        public void TheBounceShotCarriesTheChevronAfterItsBounce()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BouncePath);
            var behaviours = new List<IProjectileBehaviour>(prefab.GetComponents<IProjectileBehaviour>());
            var view = prefab.GetComponent<RicochetChevronView>();
            Assert.IsNotNull(view, "the Bounce upgrade shows a ricochet chevron");
            Assert.Greater(behaviours.IndexOf(view), behaviours.IndexOf(prefab.GetComponent<BounceOffWalls>()),
                "it reads the bounce count after BounceOffWalls has counted this hit");
            Assert.AreEqual(ChevronPath, AssetDatabase.GetAssetPath(new SerializedObject(view).FindProperty("chevron").objectReferenceValue));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Design/Weapons/Projectiles/Burst Bullet.prefab").GetComponent<RicochetChevronView>(),
                "only the shot that bounces");
            var chevron = AssetDatabase.LoadAssetAtPath<GameObject>(ChevronPath);
            Assert.IsNotNull(chevron, ChevronPath);
            Assert.IsEmpty(chevron.GetComponentsInChildren<Collider>(true));
            Assert.IsEmpty(chevron.GetComponentsInChildren<Light>(true));
            foreach (ParticleSystem system in chevron.GetComponentsInChildren<ParticleSystem>(true))
                Assert.IsFalse(system.main.loop, system.name + " ends on its own");
        }
    }
}
