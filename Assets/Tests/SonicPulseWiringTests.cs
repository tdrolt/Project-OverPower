using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Combat;
using Overpower.Data;
using Overpower.UI;
using Overpower.Vision;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The Sonic Pulse draws its bands on every client from the cone it really hits with (its live range and angle, read through
    /// the getters), in its look-table colour, from the same cast every client already runs, gated by the caster's visibility before the bands show.</summary>
    public class SonicPulseWiringTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void ExecuteCastPlaysTheViewWithLiveCone()
        {
            MethodInfo play = typeof(SonicPulseView).GetMethod(nameof(SonicPulseView.Play));
            Assert.IsNotNull(play);
            Assert.IsTrue(IlWiring.Uses(typeof(SonicPulseAbility), nameof(SonicPulseAbility.ExecuteCast), play), "every client's cast plays the bands");
            Assert.IsTrue(IlWiring.Uses(typeof(SonicPulseAbility), nameof(SonicPulseAbility.ExecuteCast),
                typeof(SonicPulseAbility).GetProperty(nameof(SonicPulseAbility.ConeRange)).GetMethod), "the live range");
            Assert.IsTrue(IlWiring.Uses(typeof(SonicPulseAbility), nameof(SonicPulseAbility.ExecuteCast),
                typeof(SonicPulseAbility).GetProperty(nameof(SonicPulseAbility.ConeAngle)).GetMethod), "the live angle");
            Assert.IsTrue(IlWiring.Uses(typeof(SonicPulseView), "Draw", typeof(AbilityVisualGeometry).GetMethod(nameof(AbilityVisualGeometry.ArcBandRadius))));
            Assert.IsTrue(IlWiring.Uses(typeof(SonicPulseView), "Draw", typeof(AbilityVisualGeometry).GetMethod(nameof(AbilityVisualGeometry.ArcPoint))));

            // The getters are read as Play's range and angle arguments, in that order (C# evaluates arguments left to right).
            MethodInfo cast = typeof(SonicPulseAbility).GetMethod(nameof(SonicPulseAbility.ExecuteCast));
            var pushAt = IlWiring.CallOffsets(cast, typeof(SonicPulseAbility).GetMethod("PushCasterBack", Any));
            var rangeAt = IlWiring.CallOffsets(cast, typeof(SonicPulseAbility).GetProperty(nameof(SonicPulseAbility.ConeRange)).GetMethod);
            var angleAt = IlWiring.CallOffsets(cast, typeof(SonicPulseAbility).GetProperty(nameof(SonicPulseAbility.ConeAngle)).GetMethod);
            var playAt = IlWiring.CallOffsets(cast, play);
            Assert.AreEqual(1, rangeAt.Count, "the range is read once, for the bands");
            Assert.AreEqual(1, angleAt.Count, "the angle is read once, for the bands");
            Assert.AreEqual(1, playAt.Count);
            Assert.IsNotEmpty(pushAt);
            Assert.Less(pushAt.Max(), rangeAt[0], "nothing between the getters and Play but its own arguments");
            Assert.Less(rangeAt[0], angleAt[0], "the live range goes in as the range, the live angle as the angle");
            Assert.Less(angleAt[0], playAt[0]);
        }

        [Test]
        public void TheGettersReadTheTunedCone()
        {
            var go = new GameObject("pulse getters test");
            try
            {
                SonicPulseAbility pulse = go.AddComponent<SonicPulseAbility>();
                var so = new SerializedObject(pulse);
                so.FindProperty("coneRange").floatValue = 3.25f;
                so.FindProperty("coneAngle").floatValue = 71f;
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.AreEqual(3.25f, pulse.ConeRange);
                Assert.AreEqual(71f, pulse.ConeAngle);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheBandsAreGatedBeforeTheyShow()
        {
            MethodInfo play = typeof(SonicPulseView).GetMethod(nameof(SonicPulseView.Play));
            var attachAt = IlWiring.CallOffsets(play, typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.AttachToCaster)));
            var buildAt = IlWiring.CallOffsets(play, typeof(SonicPulseView).GetMethod("Build", Any));
            var showAt = IlWiring.CallOffsets(play, typeof(GameObject).GetMethod(nameof(GameObject.SetActive)));
            Assert.AreEqual(1, attachAt.Count, "Play attaches the fog gate once");
            Assert.IsNotEmpty(buildAt, "the bands are built on the first cast");
            Assert.IsNotEmpty(showAt, "and switched on");
            Assert.Less(buildAt.Max(), attachAt[0], "every band exists before the gate collects the renderers");
            Assert.Less(attachAt[0], showAt.Min(), "the gate is on before the bands show");

            // Built inactive, the gate's Awake would wait for the first SetActive(true) and switch every band back on from a fogged caster.
            MethodInfo build = typeof(SonicPulseView).GetMethod("Build", Any);
            Assert.IsEmpty(IlWiring.CallOffsets(build, typeof(GameObject).GetMethod(nameof(GameObject.SetActive))),
                "the bands are built awake and empty, so the gate wakes as it attaches");
        }

        [Test]
        public void PlayDrawsTheAbilitysLookColourOverTheLiveCone()
        {
            const int testId = 9901;
            var gold = new Color(0.81f, 0.52f, 0.23f, 1f);
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            AbilityDefinition definition = ScriptableObject.CreateInstance<AbilityDefinition>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            var go = new GameObject("pulse view test");
            try
            {
                theme.immuneBarColor = gold;
                theme.arenaLook = look;
                look.fxAlpha = material;
                look.pulseBandCount = 3;
                look.pulseSweepSeconds = 0.2f;
                look.pulseBandSpacing = 0.18f;
                look.pulseBandHeight = 0f;
                look.pulseBandWidth = 0.3f;
                look.pulseBandAlpha = 0.8f;
                look.pulseTrailAlpha = 0.5f;
                look.pulseCoreWhiteness = 0.6f;
                look.pulseCoreWidth = 0.3f;
                ((List<AbilityLook>)typeof(ArenaLook).GetField("abilityLooks", Any).GetValue(look)).Add(new AbilityLook(testId, LookShape.ArcBands, LookFamily.Gold));
                typeof(AbilityDefinition).GetField("id", Any).SetValue(definition, testId); // not through SerializedObject: its OnValidate wants a whole ability
                SonicPulseAbility pulse = go.AddComponent<SonicPulseAbility>();
                typeof(AbilityModule).GetProperty(nameof(AbilityModule.Definition)).GetSetMethod(true).Invoke(pulse, new object[] { definition });
                SonicPulseView view = go.AddComponent<SonicPulseView>();
                var so = new SerializedObject(view);
                so.FindProperty("theme").objectReferenceValue = theme;
                so.ApplyModifiedPropertiesWithoutUndo();

                view.Play(new Vector3(2f, 0f, 1f), Vector3.right, 3.25f, 71f, 1, null);
                view.Draw(0.2f);
                GameObject bands = view.Bands;
                Assert.IsNotNull(bands);
                Assert.IsNull(bands.transform.parent, "the bands stay where the pulse went off, not on the caster who is thrown back");
                Vector3 origin = bands.transform.position;
                float reach = 0f;
                for (int b = 0; b < 3; b++)
                {
                    LineRenderer band = bands.transform.Find("Band " + b).GetComponent<LineRenderer>();
                    Assert.AreSame(material, band.sharedMaterial, "FX Alpha, see-through");
                    Assert.AreEqual(gold.r, band.startColor.r, 1f / 255f, "the colour of the Sonic Pulse's row in the look table; a line keeps 8-bit colours");
                    Assert.AreEqual(gold.g, band.startColor.g, 1f / 255f);
                    Assert.AreEqual(gold.b, band.startColor.b, 1f / 255f);
                    if (b == 0)
                        Assert.AreEqual(0.8f, band.startColor.a, 1f / 255f, "the leading band as solid as Pulse Band Alpha");
                    if (b == 2)
                        Assert.AreEqual(0.4f, band.startColor.a, 1f / 255f, "the last band keeps Pulse Trail Alpha of the leading band's strength");

                    LineRenderer core = band.transform.Find(SonicPulseView.CoreName)?.GetComponent<LineRenderer>();
                    Assert.IsNotNull(core, band.name + " has a pale core line");
                    Assert.AreSame(material, core.sharedMaterial);
                    Assert.Greater(core.sortingOrder, band.sortingOrder, "the core draws over its band");
                    Assert.AreEqual(0.09f, core.widthMultiplier, 1e-4f, "Pulse Core Width of the band");
                    Assert.Greater(core.startColor.grayscale, band.startColor.grayscale + 0.1f, "lighter than its band, so the pulse reads in value over a grey floor");
                    Assert.AreEqual(band.startColor.a, core.startColor.a, 1f / 255f, "fades with its band");
                    Assert.AreEqual(band.positionCount, core.positionCount);

                    for (int i = 0; i < band.positionCount; i++)
                    {
                        Assert.AreEqual(band.GetPosition(i), core.GetPosition(i), "the core runs along the middle of its band");
                        Vector3 p = band.transform.TransformPoint(band.GetPosition(i));
                        Assert.IsTrue(ConeFilter.IsWithinCone(origin, Vector3.right, p, 3.25f + 1e-3f, 71f + 1e-2f), $"{band.name} point {i} {p:F3}");
                        if (b == 0)
                            reach = Mathf.Max(reach, Vector3.Distance(origin, p));
                    }
                }
                Assert.AreEqual(3.25f, reach, 0.01f, "the leading band reaches the live range handed to Play at the end of the sweep");
            }
            finally
            {
                if (go.GetComponent<SonicPulseView>() != null && go.GetComponent<SonicPulseView>().Bands != null)
                    Object.DestroyImmediate(go.GetComponent<SonicPulseView>().Bands);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }
        }
    }
}
