using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.UI;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The three per-team mech materials: runtime copies of one template (never an edit of a shared asset), cached so a spawn
    /// adds none, freed as a set, swept after a domain reload, and dressed through the mech colour rules. The template asset itself
    /// is URP Lit with a greyscale glow map, so tinting the glow colour gives the team's hue.</summary>
    public class MechMaterialsTests
    {
        private const string LookPath = "Assets/_Project/Design/Settings/ArenaLook.asset";
        private const string BakedGlowMapFolder = "Assets/_Project/Shared/Art/";
        private static readonly Color Team0 = new Color(0.93f, 0.97f, 1f, 1f);
        private static readonly Color Team1 = new Color(0.68f, 0.32f, 1f, 1f);
        private static readonly Color Team2 = new Color(0.15f, 0.95f, 1f, 1f);

        private UiTheme theme;
        private ArenaLook look;
        private Material template;

        [SetUp]
        public void Create()
        {
            MechMaterials.Free();
            theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.teamShotColors = new[] { Team0, Team1, Team2 };
            look = ScriptableObject.CreateInstance<ArenaLook>();
            look.mechBaseMix = 0.5f;
            look.mechGlowStrength = 1.5f;
            template = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "test template" };
            look.mechTemplate = template;
            theme.arenaLook = look;
        }

        [TearDown]
        public void Destroy()
        {
            MechMaterials.Free();
            Object.DestroyImmediate(template);
            Object.DestroyImmediate(look);
            Object.DestroyImmediate(theme);
        }

        private static int LiveMechMaterials() =>
            Resources.FindObjectsOfTypeAll<Material>().Count(m => m != null && m.name.StartsWith("Mech Team ") && !EditorUtility.IsPersistent(m));

        [Test]
        public void ThreeCachedInstancesNamedByTeamAndNeverTheTemplate()
        {
            Material a = MechMaterials.For(0, theme, look);
            Material b = MechMaterials.For(1, theme, look);
            Material c = MechMaterials.For(2, theme, look);
            Assert.AreEqual("Mech Team 0", a.name);
            Assert.AreEqual("Mech Team 1", b.name);
            Assert.AreEqual("Mech Team 2", c.name);
            Assert.AreNotSame(template, a);
            Assert.AreNotSame(a, b);
            Assert.AreSame(a, MechMaterials.For(0, theme, look), "the same team gets the same instance back");
            Assert.AreSame(c, MechMaterials.For(2, theme, look));
            Assert.AreSame(template.shader, a.shader, "same shader as the template");
        }

        [Test]
        public void ARuntimeCopyIsNeverWrittenIntoASceneOrAsset()
        {
            for (int team = 0; team < 3; team++)
                Assert.AreEqual(HideFlags.DontSave, MechMaterials.For(team, theme, look).hideFlags & HideFlags.DontSave, $"team {team}");
        }

        [Test]
        public void TheTemplateIsNeverWritten()
        {
            Color before = template.GetColor("_BaseColor");
            MechMaterials.For(1, theme, look);
            Assert.AreEqual(before, template.GetColor("_BaseColor"));
        }

        [Test]
        public void ATeamNotKnownYetGivesNoMaterial()
        {
            Assert.IsNull(MechMaterials.For(PlayerTeam.NoTeam, theme, look));
        }

        [Test]
        public void NoTemplateGivesNoMaterial()
        {
            look.mechTemplate = null;
            Assert.IsNull(MechMaterials.For(0, theme, look));
        }

        [Test]
        public void NoThemeOrNoLookGivesNoMaterial()
        {
            Assert.IsNull(MechMaterials.For(0, null, look));
            Assert.IsNull(MechMaterials.For(0, theme, null));
        }

        [Test]
        public void SpawningManyPlayersLeaksNothingAndFreeRemovesTheSet()
        {
            int before = LiveMechMaterials();
            for (int spawn = 0; spawn < 12; spawn++)
                MechMaterials.For(spawn % 3, theme, look);
            Assert.AreEqual(before + 3, LiveMechMaterials(), "twelve spawns across three teams make exactly three materials");
            MechMaterials.Free();
            Assert.AreEqual(before, LiveMechMaterials(), "Free removed every runtime mech material");
        }

        [Test]
        public void TheSweepAfterADomainReloadRemovesOrphansButNotAssets()
        {
            int before = LiveMechMaterials();
            var orphan = new Material(template) { name = "Mech Team 2", hideFlags = HideFlags.DontSave };
            try
            {
                Assert.AreEqual(before + 1, LiveMechMaterials());
                typeof(MechMaterials).GetMethod("FreeLeftovers", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                Assert.IsTrue(orphan == null, "the orphan is destroyed");
                Assert.AreEqual(0, LiveMechMaterials(), "and nothing named like it is left");
            }
            finally
            {
                if (orphan != null)
                    Object.DestroyImmediate(orphan);
            }
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Shared/FX/Materials/Mech Team.mat"), "the persistent template is untouched");
        }

        [Test]
        public void DressHandsTheMixAndStrengthToTheMaterial()
        {
            look.mechBaseMix = 0.7f;
            look.mechGlowStrength = 2.5f;
            var target = new Material(template);
            try
            {
                MechMaterials.Dress(target, Team1, look);
                Color expectedBase = MechLook.Base(Team1, 0.7f);
                Color body = target.GetColor("_BaseColor");
                Assert.AreEqual(expectedBase.r, body.r, 2e-3f);
                Assert.AreEqual(expectedBase.b, body.b, 2e-3f);
                Color glow = target.GetColor("_EmissionColor");
                Assert.Greater(glow.b, glow.g, "a purple team glows purple");
                Color expectedGlow = MechLook.Glow(Team1.linear, 2.5f);
                Assert.AreEqual(expectedGlow.g, glow.g, 1e-3f, "the glow is handed over as linear light, the way an HDR colour is read");
                Assert.AreEqual(expectedGlow.b, glow.b, 1e-3f);
                Assert.IsTrue(target.IsKeywordEnabled("_EMISSION"), "the glow is switched on");
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void EveryTeamsGlowAsHandedToTheShaderShowsTheTeamHue()
        {
            foreach (var pair in new[] { (0, Team0), (1, Team1), (2, Team2) })
            {
                Color shaderSees = MechMaterials.For(pair.Item1, theme, look).GetColor("_EmissionColor");
                float peak = Mathf.Max(shaderSees.r, Mathf.Max(shaderSees.g, shaderSees.b));
                Color.RGBToHSV(new Color(shaderSees.r / peak, shaderSees.g / peak, shaderSees.b / peak, 1f).gamma, out float shown, out _, out _);
                Color.RGBToHSV(pair.Item2, out float wanted, out _, out _);
                float gap = Mathf.Abs(shown - wanted);
                Assert.Less(Mathf.Min(gap, 1f - gap), 0.01f, $"team {pair.Item1} glows {shaderSees}, which shows hue {shown}, not {wanted}");
            }
        }

        [Test]
        public void TheMaterialsAreDressedThroughTheMechLookRules()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(MechMaterials), "Dress", typeof(MechLook).GetMethod(nameof(MechLook.Base))));
            Assert.IsTrue(IlWiring.Uses(typeof(MechMaterials), "Dress", typeof(MechLook).GetMethod(nameof(MechLook.Glow))));
            Assert.IsTrue(IlWiring.Uses(typeof(MechMaterials), "For", typeof(MechMaterials).GetMethod(nameof(MechMaterials.Dress), BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)));
        }

        [Test]
        public void TheTeamColourIsTheSharedLookColourOfTheTeamFamily()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(MechMaterials), "For", typeof(LookColours).GetMethod(nameof(LookColours.For))), "the same source as the shots' and the respawn bubble's team colour");
            Color glow = MechMaterials.For(2, theme, look).GetColor("_EmissionColor");
            Assert.Greater(glow.g, glow.r, "team 2 is the cyan team and glows cyan");
        }

        [Test]
        public void ARetuningShowsOnTheNextCall()
        {
            Material m = MechMaterials.For(1, theme, look);
            float before = m.GetColor("_EmissionColor").b;
            look.mechGlowStrength = 4f;
            Assert.AreSame(m, MechMaterials.For(1, theme, look));
            Assert.Greater(m.GetColor("_EmissionColor").b, before, "the cached copy is re-dressed with the current numbers");
        }

        [Test]
        public void TheTemplateAssetIsUrpLitWithAGreyscaleGlowMap()
        {
            ArenaLook real = AssetDatabase.LoadAssetAtPath<ArenaLook>(LookPath);
            Assert.IsNotNull(real.mechTemplate, "ArenaLook has no mech template material");
            Assert.AreEqual("Mech Team", real.mechTemplate.name);
            Assert.AreEqual("Universal Render Pipeline/Lit", real.mechTemplate.shader.name);
            Assert.IsTrue(real.mechTemplate.IsKeywordEnabled("_EMISSION"));
            Shader urpLit = real.mechTemplate.shader;
            Assert.IsTrue((urpLit.GetPropertyFlags(urpLit.FindPropertyIndex("_EmissionColor")) & UnityEngine.Rendering.ShaderPropertyFlags.HDR) != 0,
                "an HDR colour reaches the shader as it is, which is why the glow is handed over as linear light");
            Texture glowMap = real.mechTemplate.GetTexture("_EmissionMap");
            Assert.IsNotNull(glowMap, "the template keeps the model's glow-line map");
            string path = AssetDatabase.GetAssetPath(glowMap);
            Assert.IsTrue(path.StartsWith(BakedGlowMapFolder), $"the original glow map is orange, so the template uses a greyscale copy: {path}");

            var pixels = new Texture2D(2, 2);
            Assert.IsTrue(ImageConversion.LoadImage(pixels, File.ReadAllBytes(path)));
            int lit = 0, tinted = 0;
            foreach (Color32 p in pixels.GetPixels32())
            {
                int top = Mathf.Max(p.r, Mathf.Max(p.g, p.b));
                if (top <= 8) continue;
                lit++;
                if (top - Mathf.Min(p.r, Mathf.Min(p.g, p.b)) > 2) tinted++;
            }
            Object.DestroyImmediate(pixels);
            Assert.Greater(lit, 1000, "the glow lines are in the map");
            Assert.AreEqual(0, tinted, "no pixel of the glow map carries a colour of its own");
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.AreEqual(TextureImporterCompression.CompressedHQ, importer.textureCompression,
                "the map reaches the shader through a compression that keeps a grey grey (the default one tints it green)");
        }

        [Test]
        public void TheTemplateKeepsTheTeamMaterialsMapsAndSurface()
        {
            Material template = AssetDatabase.LoadAssetAtPath<ArenaLook>(LookPath).mechTemplate;
            Material team1 = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Shared/Materials/PBR team 1.mat");
            foreach (string map in new[] { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" })
                Assert.AreSame(team1.GetTexture(map), template.GetTexture(map), map + " is the model's own");
            Assert.AreEqual(team1.GetFloat("_Smoothness"), template.GetFloat("_Smoothness"), 1e-5f);
            Assert.AreEqual(team1.GetFloat("_Metallic"), template.GetFloat("_Metallic"), 1e-5f);
            Assert.AreEqual(team1.shader, template.shader);
        }
    }
}
