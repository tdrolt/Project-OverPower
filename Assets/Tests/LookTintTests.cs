using NUnit.Framework;
using Overpower.Arena;
using Overpower.UI;
using Overpower.Weapons;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>An ability's shot takes the colour of its row in the look table, put on each renderer through a property block so the shared
    /// material file is never changed while playing; a row-less id falls back to the family the effect was built for.</summary>
    public class LookTintTests
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color Stunned = new Color(0.41f, 0.42f, 0.43f, 1f);
        private static readonly Color Team1 = new Color(0.21f, 0.22f, 0.23f, 1f);
        private const int StunGunId = 22;

        private UiTheme theme;
        private ArenaLook look;

        [SetUp]
        public void CreateTheme()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();
            look = ScriptableObject.CreateInstance<ArenaLook>();
            theme.statusStunnedColor = Stunned;
            theme.teamShotColors = new[] { Color.black, Team1 };
            theme.arenaLook = look;
            look.abilityShotGlow = 2f;
            look.abilitySparkGlow = 3f;
        }

        [TearDown]
        public void DestroyTheme()
        {
            Object.DestroyImmediate(look);
            Object.DestroyImmediate(theme);
        }

        private void SetRow(int id, LookShape shape, LookFamily family)
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

        [Test]
        public void ForAbilityReadsTheRowElseTheFallbackFamily()
        {
            SetRow(StunGunId, LookShape.Bolt, LookFamily.ElectricBlue);
            Assert.AreEqual(Stunned, LookColours.ForAbility(theme, StunGunId, 1, LookFamily.Gold));
            SetRow(StunGunId, LookShape.Bolt, LookFamily.Team);
            Assert.AreEqual(Team1, LookColours.ForAbility(theme, StunGunId, 1, LookFamily.Gold), "the family is read from the table");
            SetRow(99, LookShape.Bolt, LookFamily.Team);
            Assert.AreEqual(Stunned, LookColours.ForAbility(theme, StunGunId, 1, LookFamily.ElectricBlue), "no row: the effect's own family");
            theme.arenaLook = null;
            Assert.AreEqual(Stunned, LookColours.ForAbility(theme, StunGunId, 1, LookFamily.ElectricBlue), "no table: the effect's own family");
        }

        [Test]
        public void ApplyPutsTheLookColourOnEveryRendererThroughAPropertyBlock()
        {
            SetRow(StunGunId, LookShape.Bolt, LookFamily.ElectricBlue);
            var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var fx = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            var root = new GameObject("tint test");
            var sparks = new GameObject("sparks");
            try
            {
                sparks.transform.SetParent(root.transform, false);
                root.AddComponent<MeshFilter>();
                MeshRenderer core = root.AddComponent<MeshRenderer>();
                core.sharedMaterial = lit;
                ParticleSystem system = sparks.AddComponent<ParticleSystem>();
                ParticleSystemRenderer sparkRenderer = sparks.GetComponent<ParticleSystemRenderer>();
                sparkRenderer.sharedMaterial = fx;
                LookTint tint = root.AddComponent<LookTint>();
                var so = new SerializedObject(tint);
                so.FindProperty("theme").objectReferenceValue = theme;
                so.FindProperty("abilityId").intValue = StunGunId;
                so.ApplyModifiedPropertiesWithoutUndo();

                Assert.IsTrue(tint.Apply(-1));

                var block = new MaterialPropertyBlock();
                core.GetPropertyBlock(block);
                Color coreColour = block.GetColor(BaseColorId);
                Assert.AreEqual(Stunned.r, coreColour.r, 1e-4f, "the core is painted the look colour");
                Assert.AreEqual(Stunned.b, coreColour.b, 1e-4f);
                Assert.AreEqual(Stunned.b * 2f, block.GetColor(EmissionColorId).b, 1e-4f, "and glows by Ability Shot Glow");
                sparkRenderer.GetPropertyBlock(block);
                Assert.AreEqual(Stunned.b * 3f, block.GetColor(BaseColorId).b, 1e-4f, "a spark carries Ability Spark Glow in its colour");
                Assert.AreEqual(Color.white, lit.GetColor(BaseColorId), "the shared material file is untouched");
                Assert.AreEqual(Color.white, fx.GetColor(BaseColorId));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(lit);
                Object.DestroyImmediate(fx);
            }

            Assert.IsTrue(IlWiring.Uses(typeof(LookTint), nameof(LookTint.Apply), typeof(LookColours).GetMethod(nameof(LookColours.ForAbility))));
            Assert.IsTrue(IlWiring.Uses(typeof(LookTint), nameof(LookTint.OnSpawned), typeof(LookTint).GetMethod(nameof(LookTint.Apply))),
                "every bolt is tinted as it spawns");
        }
    }
}
