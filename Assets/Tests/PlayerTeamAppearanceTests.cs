using System.IO;
using System.Reflection;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.UI;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Every team's mech wears a runtime Mech Team material from the template, never one of the three PBR team assets (the
    /// first is the source character's own). The assignment goes through MechMaterials.For.</summary>
    public class PlayerTeamAppearanceTests
    {
        private static readonly Color[] TeamColours =
        {
            new Color(0.93f, 0.97f, 1f, 1f), new Color(0.68f, 0.32f, 1f, 1f), new Color(0.15f, 0.95f, 1f, 1f),
        };

        private UiTheme theme;
        private ArenaLook look;
        private Material template;
        private Material prefabLook;
        private GameObject player;
        private SkinnedMeshRenderer[] renderers;

        [SetUp]
        public void Create()
        {
            MechMaterials.Free();
            theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.teamShotColors = TeamColours;
            look = ScriptableObject.CreateInstance<ArenaLook>();
            template = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "test template" };
            look.mechTemplate = template;
            theme.arenaLook = look;

            prefabLook = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "PBR team 1" };
            player = new GameObject("appearance test");
            renderers = new SkinnedMeshRenderer[2];
            for (int i = 0; i < renderers.Length; i++)
            {
                var part = new GameObject("part " + i);
                part.transform.SetParent(player.transform);
                renderers[i] = part.AddComponent<SkinnedMeshRenderer>();
                renderers[i].sharedMaterial = prefabLook;
            }
        }

        [TearDown]
        public void Destroy()
        {
            MechMaterials.Free();
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(prefabLook);
            Object.DestroyImmediate(template);
            Object.DestroyImmediate(look);
            Object.DestroyImmediate(theme);
        }

        [Test]
        public void NeverAssignsPbrTeam1()
        {
            var appearance = player.AddComponent<PlayerTeamAppearance>();
            for (int team = 0; team < 3; team++)
            {
                typeof(PlayerTeamAppearance).GetField("appliedTeam", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(appearance, PlayerTeam.NoTeam);
                appearance.ApplyTeam(team, theme);
                foreach (SkinnedMeshRenderer r in renderers)
                {
                    Assert.IsFalse(r.sharedMaterial.name.StartsWith("PBR team"), $"team {team} still wears {r.sharedMaterial.name}");
                    Assert.AreEqual("Mech Team " + team, r.sharedMaterial.name);
                }
            }
        }

        [Test]
        public void PurpleIsNotBlackAndEveryTeamGlowsInItsOwnColour()
        {
            var appearance = player.AddComponent<PlayerTeamAppearance>();
            appearance.ApplyTeam(1, theme);
            Color body = renderers[0].sharedMaterial.GetColor("_BaseColor");
            Assert.Greater(Mathf.Max(body.r, Mathf.Max(body.g, body.b)), 0.2f, "the purple team's mech must not be black: " + body);
            Color glow = renderers[0].sharedMaterial.GetColor("_EmissionColor");
            Assert.Greater(glow.b, glow.g, "purple glows purple");
        }

        [Test]
        public void TheSameTeamAppliedAgainChangesNothing()
        {
            var appearance = player.AddComponent<PlayerTeamAppearance>();
            appearance.ApplyTeam(2, theme);
            Material first = renderers[0].sharedMaterial;
            renderers[0].sharedMaterial = prefabLook;
            appearance.ApplyTeam(2, theme);
            Assert.AreSame(prefabLook, renderers[0].sharedMaterial, "an already applied team is skipped");
            Assert.AreSame(first, renderers[1].sharedMaterial);
        }

        [Test]
        public void ATeamNotKnownYetKeepsThePrefabLook()
        {
            var appearance = player.AddComponent<PlayerTeamAppearance>();
            appearance.ApplyTeam(PlayerTeam.NoTeam, theme);
            Assert.AreSame(prefabLook, renderers[0].sharedMaterial);
        }

        [Test]
        public void ATeamThatIsNoLongerKnownKeepsTheLastLook()
        {
            var appearance = player.AddComponent<PlayerTeamAppearance>();
            appearance.ApplyTeam(1, theme);
            Material last = renderers[0].sharedMaterial;
            appearance.ApplyTeam(PlayerTeam.NoTeam, theme);
            Assert.AreSame(last, renderers[0].sharedMaterial);
            Assert.AreEqual("Mech Team 1", renderers[1].sharedMaterial.name);
        }

        [Test]
        public void ApplyTeamAssignsThroughMechMaterials()
        {
            MethodInfo forMethod = typeof(MechMaterials).GetMethod(nameof(MechMaterials.For));
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerTeamAppearance), "ApplyTeam", forMethod), "the assignment is MechMaterials.For");
        }

        [Test]
        public void ApplyHandsTheTeamAndThemeToApplyTeam()
        {
            MethodInfo applyTeam = typeof(PlayerTeamAppearance).GetMethod("ApplyTeam", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerTeamAppearance), "Apply", applyTeam));
        }

        [Test]
        public void ApplyReadsThePlayersOwnTheme()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerTeamAppearance), "Apply", typeof(PlayerHealth).GetProperty(nameof(PlayerHealth.Theme)).GetMethod));
        }

        [Test]
        public void ApplyReadsThePlayersOwnTeam()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(PlayerTeamAppearance), "Apply", typeof(PlayerTeam).GetProperty(nameof(PlayerTeam.teamID)).GetMethod));
        }

        [Test]
        public void TheOldTeamMaterialsArrayIsRetiredFromCodeAndPrefab()
        {
            Assert.IsNull(typeof(PlayerTeamAppearance).GetField("teamMaterials"), "nothing reads the PBR team array any more");
            string prefab = File.ReadAllText("Assets/Resources/Multiplayer Player.prefab");
            StringAssert.DoesNotContain("teamMaterials:", prefab);
            StringAssert.DoesNotContain("1666fb81395f51e4293b01cca8f58481", prefab, "the player prefab no longer references PBR team 1");
            StringAssert.DoesNotContain("93954767c5da02742b700c59b05ae323", prefab, "or the black purple material");
        }
    }
}
