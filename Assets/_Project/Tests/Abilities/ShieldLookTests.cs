using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.UI;
using Overpower.Vision;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The Shield is the only gold thing in the game: its shell and its armed floor ring read the colour of the Shield's row in
    /// the look table (Gold = the immune bars' colour), the ring is a hexagon, the plates are opaque Lit with Cull Back so the player
    /// shows only through the seams, and the old cyan colours are gone from the prefab.</summary>
    public class ShieldLookTests
    {
        private const string LookPath = "Assets/_Project/Shared/Config/ArenaLook.asset";
        private const string PrefabPath = "Assets/_Project/Features/Abilities/Data/Invulnerability.prefab";
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const int ShieldId = 25;

        private static readonly Color Gold = new Color(0.41f, 0.42f, 0.43f, 1f);
        private static readonly Color Team1 = new Color(0.21f, 0.22f, 0.23f, 1f);

        private static MethodInfo Colour => typeof(ShieldLook).GetMethod(nameof(ShieldLook.Colour), Any);

        [Test]
        public void ShellAndRingReadGold()
        {
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            try
            {
                theme.immuneBarColor = Gold;
                theme.teamShotColors = new[] { Color.black, Team1 };
                theme.arenaLook = look;
                SetRow(look, ShieldId, LookShape.HexShell, LookFamily.Gold);
                Assert.AreEqual(Gold, ShieldLook.Colour(theme, ShieldId, 1));

                SetRow(look, ShieldId, LookShape.HexShell, LookFamily.Team);
                Assert.AreEqual(Team1, ShieldLook.Colour(theme, ShieldId, 1), "the family is read from the table, not fixed in code");

                SetRow(look, 99, LookShape.HexShell, LookFamily.Team);
                Assert.AreEqual(Gold, ShieldLook.Colour(theme, ShieldId, 1), "a table without a Shield row: the Shield still draws gold");

                theme.arenaLook = null;
                Assert.AreEqual(Gold, ShieldLook.Colour(theme, ShieldId, 1), "no table: the Shield still draws gold");
            }
            finally
            {
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }

            Assert.IsTrue(IlWiring.Uses(typeof(ShieldLook), nameof(ShieldLook.Colour), typeof(LookColours).GetMethod(nameof(LookColours.ForAbility))), "the one look-table lookup");
            Assert.IsTrue(IlWiring.Uses(typeof(InvulnerabilityAbility), "ShowShield", Colour), "the shell's gold");
            Assert.IsTrue(IlWiring.Uses(typeof(InvulnerabilityAbility), "SetRing", Colour), "the armed ring's gold");
        }

        [Test]
        public void TheArmedRingIsAHexagon()
        {
            var go = new GameObject("ring test");
            try
            {
                LineRenderer line = go.AddComponent<LineRenderer>();
                ShieldLook.DrawRing(line, 1.2f);
                Assert.AreEqual(6, line.positionCount);
                Assert.IsTrue(line.loop);
                for (int i = 0; i < 6; i++)
                    Assert.AreEqual(1.2f, line.GetPosition(i).magnitude, 1e-4f, "corners on the ring's radius");
            }
            finally { Object.DestroyImmediate(go); }
            Assert.IsTrue(IlWiring.Uses(typeof(InvulnerabilityAbility), "SetRing", typeof(ShieldLook).GetMethod(nameof(ShieldLook.DrawRing))));
        }

        [Test]
        public void TheShieldIsBuiltAsTheShellBeforeTheFogGate()
        {
            MethodInfo show = typeof(InvulnerabilityAbility).GetMethod("ShowShield", Any);
            MethodInfo attach = typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.AttachToCaster));
            MethodInfo build = typeof(ShieldShellView).GetMethod(nameof(ShieldShellView.Build));
            MethodInfo tint = typeof(ShieldShellView).GetMethod(nameof(ShieldShellView.Tint));
            var attachAt = IlWiring.CallOffsets(show, attach);
            Assert.AreEqual(1, attachAt.Count, "ShowShield attaches the fog gate exactly once");
            var buildAt = IlWiring.CallOffsets(show, build);
            var tintAt = IlWiring.CallOffsets(show, tint);
            Assert.IsNotEmpty(buildAt, "the Shield is the hex-plate shell");
            Assert.IsNotEmpty(tintAt, "the shell is tinted");
            Assert.Less(buildAt.Concat(tintAt).Max(), attachAt[0], "the gate collects the renderers once: the shell must be complete before it attaches");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(InvulnerabilityAbility), "ShowShield", typeof(GameObject).GetMethod(nameof(GameObject.CreatePrimitive))),
                "the smooth sphere is gone (lambdas included)");
            Assert.IsTrue(IlWiring.Uses(typeof(InvulnerabilityAbility), "ShowShield", typeof(ArenaLook).GetField(nameof(ArenaLook.shieldSeamShade))),
                "the seams' shade comes from ArenaLook");
        }

        [Test]
        public void TheShellIsDrawnBiggerThanTheShieldWhileTheBubbleKeepsItsSize()
        {
            Assert.AreEqual(2.64f, ShieldLook.ShellDiameter(2.2f, 1.2f), 1e-5f, "Shield Diameter times Shield Shell Scale");
            Assert.AreEqual(2f, ShieldLook.ShellDiameter(2f, 1f), 1e-6f, "a scale of 1 is the Shield Diameter itself");
            Assert.IsTrue(IlWiring.Uses(typeof(InvulnerabilityAbility), "ShowShield", typeof(ArenaLook).GetField(nameof(ArenaLook.shieldShellScale))),
                "from Arena Look's Shield Shell Scale");
            Assert.IsFalse(IlWiring.Uses(typeof(Overpower.Dominion.RespawnShield), typeof(ArenaLook).GetField(nameof(ArenaLook.shieldShellScale))),
                "the respawn bubble keeps its size from the Shield Diameter, never from the shell's look");
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.Dominion.RespawnShield), "BuildBubble", typeof(InvulnerabilityAbility).GetProperty(nameof(InvulnerabilityAbility.ShieldDiameter)).GetGetMethod()));

            // The rise starts on the armed ring the player saw: the ring is measured in the drawn shell's space, so a bigger shell keeps it in place.
            ShieldLook.ShellLayout layout = ShieldLook.Layout(2.2f, 1.2f, 2.4f, 0.05f, 0.8f);
            Assert.AreEqual(2.64f, layout.Diameter, 1e-5f, "drawn at the shell's own size");
            Assert.AreEqual(1.2f, layout.Ring.x * layout.Diameter, 1e-5f, "the plates still leave the 2.4 m ring");
            Assert.AreEqual(0.05f - 0.8f, layout.Ring.y * layout.Diameter, 1e-5f, "at the ring's own height under the shell's centre");
        }

        [Test]
        public void ShowShieldTakesTheShellSizeAndTheRingFromOneLayout()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(InvulnerabilityAbility), "ShowShield", typeof(ShieldLook).GetMethod(nameof(ShieldLook.Layout))),
                "the drawn size and the ring in its space come from one tested hand-over");
            Assert.IsFalse(IlWiring.Uses(typeof(InvulnerabilityAbility), "ShowShield", typeof(ShieldLook).GetMethod(nameof(ShieldLook.RingInShellSpace))),
                "no second measurement of the ring that could use the Shield's size instead of the drawn shell's");
            Assert.IsFalse(IlWiring.Uses(typeof(InvulnerabilityAbility), "ShowShield", typeof(ShieldLook).GetMethod(nameof(ShieldLook.ShellDiameter))),
                "no second sizing beside the layout");
            Assert.IsTrue(IlWiring.Uses(typeof(InvulnerabilityAbility), "ShowShield", typeof(ShieldLook.ShellLayout).GetField(nameof(ShieldLook.ShellLayout.Diameter))),
                "the shell is scaled to the layout's size");
            Assert.IsTrue(IlWiring.Uses(typeof(InvulnerabilityAbility), "ShowShield", typeof(ShieldLook.ShellLayout).GetField(nameof(ShieldLook.ShellLayout.Ring))),
                "the plates rise from the layout's ring");
        }

        [Test]
        public void TheRingIsHandedToTheShellInItsOwnSpace()
        {
            // Ring 2.4 m wide at 0.05 m, Shield 2.2 m wide centred 0.8 m above the feet: radius 1.2 / 2.2, height (0.05 - 0.8) / 2.2.
            Vector2 ring = ShieldLook.RingInShellSpace(2.4f, 0.05f, 2.2f, 0.8f);
            Assert.AreEqual(0.545454f, ring.x, 1e-5f);
            Assert.AreEqual(-0.340909f, ring.y, 1e-5f);
            Vector2 atFeet = ShieldLook.RingInShellSpace(1f, 0f, 2f, 0f);
            Assert.AreEqual(0.25f, atFeet.x, 1e-6f);
            Assert.AreEqual(0f, atFeet.y, 1e-6f);
        }

        [Test]
        public void ThePlatesAreOpaqueLitWithCullBack()
        {
            ArenaLook look = AssetDatabase.LoadAssetAtPath<ArenaLook>(LookPath);
            Assert.NotNull(look, LookPath);
            Material plates = look.shieldPlates;
            Assert.NotNull(plates, "ArenaLook has no Shield plates material");
            Assert.AreEqual("Universal Render Pipeline/Lit", plates.shader.name);
            Assert.AreEqual(0f, plates.GetFloat("_Surface"), "opaque: the player shows only through the seams");
            Assert.AreEqual((float)UnityEngine.Rendering.CullMode.Back, plates.GetFloat("_Cull"), "Cull Back hides the far plates' insides");
            Assert.IsTrue(plates.IsKeywordEnabled("_EMISSION"), "the gold glow is set per shell through _EmissionColor");
        }

        [Test]
        public void TheOldCyanColoursAreGoneFromThePrefab()
        {
            Assert.IsNull(typeof(InvulnerabilityAbility).GetField("shieldColor", Any));
            Assert.IsNull(typeof(InvulnerabilityAbility).GetField("armedRingColor", Any));
            string yaml = File.ReadAllText(PrefabPath);
            StringAssert.DoesNotContain("shieldColor:", yaml, "re-save the prefab so the stale key goes");
            StringAssert.DoesNotContain("armedRingColor:", yaml);
        }

        private static void SetRow(ArenaLook look, int id, LookShape shape, LookFamily family)
        {
            var so = new SerializedObject(look);
            SerializedProperty list = so.FindProperty("abilityLooks");
            list.arraySize = 1;
            SerializedProperty row = list.GetArrayElementAtIndex(0);
            row.FindPropertyRelative("abilityId").intValue = id;
            row.FindPropertyRelative("shape").enumValueIndex = (int)shape;
            row.FindPropertyRelative("family").enumValueIndex = (int)family;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
