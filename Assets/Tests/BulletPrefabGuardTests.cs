using NUnit.Framework;
using Overpower.Data;
using Overpower.UI;
using Overpower.Weapons;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Every gun that fires a projectile flies the Blender slug instead of a ball, with no collider added (hits come from the motor's
    /// sphere cast), and keeps its team trail; lasers keep their beams. The core glows the theme's Flash White: the team colour lives in the trail.</summary>
    public class BulletPrefabGuardTests
    {
        private const string SlugPath = "Assets/Gameplay/Art/BulletSlug.fbx";

        internal static WeaponDefinition[] Weapons()
        {
            string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition", new[] { "Assets/Gameplay/Weapons" });
            var weapons = new WeaponDefinition[guids.Length];
            for (int i = 0; i < guids.Length; i++)
                weapons[i] = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
            Assert.IsNotEmpty(weapons, "the shop's weapons are found");
            return weapons;
        }

        [Test]
        public void WeaponBulletsUseSlugAndAddNoCollider()
        {
            foreach (WeaponDefinition weapon in Weapons())
            {
                GameObject shot = weapon.ProjectilePrefab;
                Assert.IsNotNull(shot, weapon.name);
                if (shot.GetComponent<Hitscan>() != null)
                    continue; // a laser keeps its beam
                Mesh mesh = shot.GetComponent<MeshFilter>().sharedMesh;
                Assert.IsNotNull(mesh, weapon.name);
                Assert.AreEqual(SlugPath, AssetDatabase.GetAssetPath(mesh), weapon.name + " flies the slug from Blender, not the built-in ball");
                Assert.Less(mesh.triangles.Length / 3, 300, "a small low-poly model");
                Assert.Greater(mesh.bounds.size.z, mesh.bounds.size.x * 2f, "long along its flight (+Z), slim across");
                Assert.IsEmpty(shot.GetComponentsInChildren<Collider>(true), weapon.name + ": hits come from the motor's sphere cast, never a collider");
                Assert.IsNotNull(shot.GetComponent<ShotTeamVisuals>(), weapon.name + " keeps its team-colour trail");
            }
        }

        [Test]
        public void TheCoreGlowsFlashWhiteAndTheTrailKeepsTheTeam()
        {
            var purple = new Color(0.68f, 0.32f, 1f, 1f);
            var warmWhite = new Color(1f, 0.9f, 0.8f, 1f);
            Color glow = ShotTeamVisuals.EmissionFor(purple, warmWhite, 0.2f, 5f);
            Assert.AreEqual(5f * Mathf.Lerp(0.9f, 0.32f, 0.2f), glow.g, 1e-4f, "the glow is Flash White pulled toward the team by the tint");
            Assert.AreEqual(5f * Mathf.Lerp(0.8f, 1f, 0.2f), glow.b, 1e-4f);
            Color untinted = ShotTeamVisuals.EmissionFor(purple, warmWhite, 0f, 2f);
            Assert.AreEqual(1.8f, untinted.g, 1e-4f, "no tint: exactly Flash White, so retuning it recolours the core");
            Assert.AreEqual(1.6f, untinted.b, 1e-4f);
            Color unknown = ShotTeamVisuals.EmissionFor(new Color(1f, 1f, 1f, 0.5f), Color.white, 0.2f, 5f);
            Assert.AreEqual(2.5f, unknown.r, 1e-4f, "an unknown team's faint colour still dims the core");

            Assert.IsTrue(IlWiring.Uses(typeof(ShotTeamVisuals), "TintCore", typeof(ShotTeamVisuals).GetMethod(nameof(ShotTeamVisuals.EmissionFor))),
                "the core glow is the tested one");
            Assert.IsTrue(IlWiring.Uses(typeof(ShotTeamVisuals), "TintCore", typeof(UiTheme).GetField(nameof(UiTheme.flashWhiteColor))),
                "the core's white is the theme's Flash White");
            Assert.IsTrue(IlWiring.Uses(typeof(ShotTeamVisuals), "ConfigureTrail", typeof(UiTheme).GetMethod(nameof(UiTheme.GradientFor))),
                "the trail keeps the shooter's team colour");
        }
    }
}
