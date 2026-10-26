using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Dominion;
using Overpower.UI;
using Overpower.Vision;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The respawn bubble is a team-colour hologram: its size is handed over from the Shield's diameter and the Dominion bubble
    /// scale, its colour is the owner's team shot colour through the look table, and its numbers come from ArenaLook. The bubble
    /// must be fully dressed before the fog gate collects its renderer.</summary>
    public class RespawnBubbleLookTests
    {
        private const string LookPath = "Assets/Gameplay/Config/ArenaLook.asset";
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Color Team0 = new Color(0.11f, 0.12f, 0.13f, 1f);
        private static readonly Color Team1 = new Color(0.21f, 0.22f, 0.23f, 1f);
        private static readonly Color Unknown = new Color(0.31f, 0.32f, 0.33f, 0.5f);
        private static readonly Color Gold = new Color(0.41f, 0.42f, 0.43f, 1f);

        private UiTheme theme;
        private ArenaLook look;

        [SetUp]
        public void CreateTheme()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();
            theme.teamShotColors = new[] { Team0, Team1 };
            theme.unknownTeamShotColor = Unknown;
            theme.immuneBarColor = Gold;
            look = ScriptableObject.CreateInstance<ArenaLook>();
            theme.arenaLook = look;
        }

        [TearDown]
        public void DestroyTheme()
        {
            Object.DestroyImmediate(look);
            Object.DestroyImmediate(theme);
        }

        private static MethodInfo Method(string name) => typeof(RespawnBubbleLook).GetMethod(name, Any);

        [Test]
        public void DiameterIsShieldDiameterTimesBubbleScale()
        {
            Assert.AreEqual(6f, RespawnBubbleLook.Diameter(2f, 3f), 1e-5f);
            Assert.AreEqual(1.25f, RespawnBubbleLook.Diameter(2.5f, 0.5f), 1e-5f);
        }

        [Test]
        public void TheBubbleIsSizedThroughTheHandOver()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(RespawnShield), "BuildBubble", Method(nameof(RespawnBubbleLook.Diameter))));
        }

        [Test]
        public void ColourIsTheTeamShotColour()
        {
            Assert.AreEqual(Team0, RespawnBubbleLook.ColourFor(0, theme));
            Assert.AreEqual(Team1, RespawnBubbleLook.ColourFor(1, theme));
            Assert.AreEqual(Unknown, RespawnBubbleLook.ColourFor(-1, theme), "a player with no team gets the theme's unknown colour");
        }

        [Test]
        public void ColourFollowsTheBubbleRowOfTheLookTable()
        {
            var so = new SerializedObject(look);
            so.FindProperty("respawnBubbleLook").FindPropertyRelative("family").enumValueIndex = (int)LookFamily.Gold;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual(Gold, RespawnBubbleLook.ColourFor(1, theme), "the colour must be read from the table, not fixed in code");
        }

        [Test]
        public void TheBubbleIsColouredThroughTheRule()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(RespawnShield), "BuildBubble", Method(nameof(RespawnBubbleLook.ColourFor))));
            Assert.IsTrue(IlWiring.Uses(typeof(RespawnShield), "BuildBubble", Method(nameof(RespawnBubbleLook.Dress))));
        }

        [Test]
        public void DressHandsTheLookNumbersToTheMaterial()
        {
            look.hologramRim = 0.17f;
            look.hologramScanDensity = 7.5f;
            look.hologramScanSpeed = 1.3f;
            look.hologramFlicker = 0.21f;
            look.hologramIntensity = 0.66f;
            Shader shader = Shader.Find("Overpower/Hologram");
            Assert.NotNull(shader, "Overpower/Hologram");
            var material = new Material(shader);
            try
            {
                RespawnBubbleLook.Dress(material, look, Team1, 3f);
                Assert.AreEqual(0.17f, material.GetFloat("_Rim"), 1e-5f);
                Assert.AreEqual(7.5f, material.GetFloat("_ScanDensity"), 1e-5f);
                Assert.AreEqual(1.3f, material.GetFloat("_ScanSpeed"), 1e-5f);
                Assert.AreEqual(0.21f, material.GetFloat("_Flicker"), 1e-5f);
                Assert.AreEqual(0.66f, material.GetFloat("_Intensity"), 1e-5f);
                Assert.AreEqual(3f, material.GetFloat("_Seed"), 1e-5f);
                Color c = material.GetColor("_BaseColor");
                Assert.AreEqual(Team1.r, c.r, 1e-3f);
                Assert.AreEqual(Team1.g, c.g, 1e-3f);
                Assert.AreEqual(Team1.b, c.b, 1e-3f);
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void TheHologramShaderCompilesAndIsAdditiveGlass()
        {
            ArenaLook shipped = AssetDatabase.LoadAssetAtPath<ArenaLook>(LookPath);
            Assert.NotNull(shipped, LookPath);
            Assert.NotNull(shipped.hologram, "ArenaLook has no hologram material");
            Shader shader = shipped.hologram.shader;
            Assert.AreEqual("Overpower/Hologram", shader.name);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "the hologram shader has compile errors");
            Assert.IsTrue(shader.isSupported, "the hologram shader is not supported on this pipeline");
            Assert.GreaterOrEqual(shipped.hologram.renderQueue, (int)UnityEngine.Rendering.RenderQueue.Transparent, "the bubble must draw after the opaques and the fog pass");
            Assert.AreEqual("Transparent", shipped.hologram.GetTag("RenderType", false));
            // Read as render-state lines, not anywhere in the file: the header comment names the blend too.
            string source = File.ReadAllText(AssetDatabase.GetAssetPath(shader));
            StringAssert.IsMatch(@"(?m)^\s*Blend One One\s*$", source, "additive: no sorting, and it can never hide the player inside");
            StringAssert.IsMatch(@"(?m)^\s*ZWrite Off\s*$", source);
            StringAssert.IsMatch(@"(?m)^\s*Cull Off\s*$", source, "the far side of the glass glows too");
        }

        [Test]
        public void TheBubbleNoLongerReadsTheOldBlueOrFindsAShaderByName()
        {
            Assert.IsNull(typeof(UiTheme).GetField("respawnShieldColor"), "the old blue bubble colour has no reader left and must go");
            MethodInfo find = typeof(Shader).GetMethod(nameof(Shader.Find), new[] { typeof(string) });
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(RespawnShield), find), "the bubble's material hangs off ArenaLook (lambdas and coroutines included)");
        }

        [Test]
        public void TheFogGateComesAfterTheBubbleIsDressed()
        {
            MethodInfo build = typeof(RespawnShield).GetMethod("BuildBubble", Any);
            MethodInfo attach = typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.AttachToCaster));
            var attachAt = IlWiring.CallOffsets(build, attach);
            Assert.AreEqual(1, attachAt.Count, "BuildBubble attaches the fog gate exactly once");
            MethodInfo setMaterial = typeof(Renderer).GetProperty(nameof(Renderer.sharedMaterial)).GetSetMethod();
            int lastDressing = new[] { IlWiring.CallOffsets(build, setMaterial), IlWiring.CallOffsets(build, Method(nameof(RespawnBubbleLook.Dress))) }
                .SelectMany(o => o).DefaultIfEmpty(int.MaxValue).Max();
            Assert.Less(lastDressing, attachAt[0], "the gate collects the renderers once: the bubble must be complete before it attaches");
        }
    }
}
