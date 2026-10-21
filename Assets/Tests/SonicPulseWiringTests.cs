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
    /// <summary>The Sonic Pulse draws its arch on every client from the cone it really hits with (its live range and angle, read through
    /// the getters), in its look-table colour, from the same cast every client already runs, gated by the caster's visibility before it shows.</summary>
    public class SonicPulseWiringTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void ExecuteCastPlaysTheViewWithLiveCone()
        {
            MethodInfo play = typeof(SonicPulseView).GetMethod(nameof(SonicPulseView.Play));
            Assert.IsNotNull(play);
            Assert.IsTrue(IlWiring.Uses(typeof(SonicPulseAbility), nameof(SonicPulseAbility.ExecuteCast), play), "every client's cast plays the arch");
            Assert.IsTrue(IlWiring.Uses(typeof(SonicPulseAbility), nameof(SonicPulseAbility.ExecuteCast),
                typeof(SonicPulseAbility).GetProperty(nameof(SonicPulseAbility.ConeRange)).GetMethod), "the live range");
            Assert.IsTrue(IlWiring.Uses(typeof(SonicPulseAbility), nameof(SonicPulseAbility.ExecuteCast),
                typeof(SonicPulseAbility).GetProperty(nameof(SonicPulseAbility.ConeAngle)).GetMethod), "the live angle");
            Assert.IsTrue(IlWiring.Uses(typeof(SonicPulseView), "Draw", typeof(AbilityVisualGeometry).GetMethod(nameof(AbilityVisualGeometry.ArcBandRadius))), "how far it has travelled");
            Assert.IsTrue(IlWiring.Uses(typeof(SonicPulseView), "Draw", typeof(AbilityVisualGeometry).GetMethod(nameof(AbilityVisualGeometry.ArchPoint))), "the arch's shape comes from the tested rule");
            Assert.IsTrue(IlWiring.Uses(typeof(SonicPulseView), "Draw", typeof(AbilityVisualGeometry).GetMethod(nameof(AbilityVisualGeometry.ArchHeight))), "and its height");
            Assert.IsTrue(IlWiring.Uses(typeof(SonicPulseView), "Draw", typeof(AbilityVisualGeometry).GetMethod(nameof(AbilityVisualGeometry.ArchStrength))), "and its fade");

            // The getters are read as Play's range and angle arguments, in that order (C# evaluates arguments left to right).
            MethodInfo cast = typeof(SonicPulseAbility).GetMethod(nameof(SonicPulseAbility.ExecuteCast));
            var pushAt = IlWiring.CallOffsets(cast, typeof(SonicPulseAbility).GetMethod("PushCasterBack", Any));
            var rangeAt = IlWiring.CallOffsets(cast, typeof(SonicPulseAbility).GetProperty(nameof(SonicPulseAbility.ConeRange)).GetMethod);
            var angleAt = IlWiring.CallOffsets(cast, typeof(SonicPulseAbility).GetProperty(nameof(SonicPulseAbility.ConeAngle)).GetMethod);
            var playAt = IlWiring.CallOffsets(cast, play);
            Assert.AreEqual(1, rangeAt.Count, "the range is read once, for the arch");
            Assert.AreEqual(1, angleAt.Count, "the angle is read once, for the arch");
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
        public void TheArchIsGatedBeforeItShows()
        {
            MethodInfo play = typeof(SonicPulseView).GetMethod(nameof(SonicPulseView.Play));
            var attachAt = IlWiring.CallOffsets(play, typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.AttachToCaster)));
            var buildAt = IlWiring.CallOffsets(play, typeof(SonicPulseView).GetMethod("Build", Any));
            var showAt = IlWiring.CallOffsets(play, typeof(GameObject).GetMethod(nameof(GameObject.SetActive)));
            Assert.AreEqual(1, attachAt.Count, "Play attaches the fog gate once");
            Assert.IsNotEmpty(buildAt, "the arch is built on the first cast");
            Assert.IsNotEmpty(showAt, "and switched on");
            Assert.Less(buildAt.Max(), attachAt[0], "every line of the arch exists before the gate collects the renderers");
            Assert.Less(attachAt[0], showAt.Min(), "the gate is on before the arch shows");

            // Built inactive, the gate's Awake would wait for the first SetActive(true) and switch every line back on from a fogged caster.
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(SonicPulseView), "Build", typeof(GameObject).GetMethod(nameof(GameObject.SetActive))),
                "the arch is built awake and empty, so the gate wakes as it attaches");
        }

        [Test]
        public void PlayDrawsAnUprightArchInTheAbilitysLookColourOverTheLiveCone()
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
                look.pulseArchCount = 2;
                look.pulseSweepSeconds = 0.2f;
                look.pulseFadeSeconds = 0.25f;
                look.pulseArchSpacing = 0.18f;
                look.pulseArchWidth = 0.3f;
                look.pulseArchHeight = 0.8f;
                look.pulseArchMaxHeight = 1.1f;
                look.pulseArchBend = 1f;
                look.pulseArchAlpha = 0.8f;
                look.pulseArchEndStrength = 0.5f;
                look.pulseTrailAlpha = 0.5f;
                look.pulseCoreWhiteness = 0.6f;
                look.pulseCoreWidth = 0.3f;
                look.pulseEdgeWidth = 0.05f;
                look.pulseEdgeStrength = 0.3f;
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
                GameObject arch = view.Arch;
                Assert.IsNotNull(arch);
                Assert.AreEqual(SonicPulseView.ArchName, arch.name);
                Assert.IsNull(arch.transform.parent, "the arch stays where the pulse went off, not on the caster who is thrown back");
                Vector3 origin = arch.transform.position;
                Assert.AreEqual(0f, origin.y, 1e-4f, "it stands on the floor under the pulse (no floor here: the pulse's own height)");
                float reach = 0f, top = 0f;
                for (int a = 0; a < 2; a++)
                {
                    LineRenderer body = arch.transform.Find("Arch " + a).GetComponent<LineRenderer>();
                    Assert.AreSame(material, body.sharedMaterial, "FX Alpha, see-through");
                    Assert.AreEqual(0.3f, body.widthMultiplier, 1e-4f, "Pulse Arch Width");
                    Assert.AreEqual(gold.r, body.startColor.r, 1f / 255f, "the colour of the Sonic Pulse's row in the look table; a line keeps 8-bit colours");
                    Assert.AreEqual(gold.g, body.startColor.g, 1f / 255f);
                    Assert.AreEqual(gold.b, body.startColor.b, 1f / 255f);
                    if (a == 0)
                        Assert.AreEqual(0.8f * 0.5f, body.startColor.a, 1f / 255f, "Pulse Arch Alpha, faded to End Strength as it reaches the end");
                    else
                        Assert.AreEqual(0.8f * 0.5f * 0.5f, body.startColor.a, 1f / 255f, "the after-image keeps Pulse Trail Alpha of the arch");

                    LineRenderer core = body.transform.Find(SonicPulseView.CoreName)?.GetComponent<LineRenderer>();
                    Assert.IsNotNull(core, body.name + " has a pale core line");
                    Assert.AreSame(material, core.sharedMaterial);
                    Assert.Greater(core.sortingOrder, body.sortingOrder, "the core draws over its tube");
                    Assert.AreEqual(0.09f, core.widthMultiplier, 1e-4f, "Pulse Core Width of the tube");
                    Assert.Greater(core.startColor.grayscale, body.startColor.grayscale + 0.1f, "lighter than its tube, so the pulse reads in value over a grey floor");
                    Assert.AreEqual(body.startColor.a, core.startColor.a, 1f / 255f, "fades with its tube");

                    LineRenderer edge = body.transform.Find(SonicPulseView.EdgeName)?.GetComponent<LineRenderer>();
                    Assert.IsNotNull(edge, body.name + " has its own edge line, the slot for the caster's team accent");
                    Assert.AreSame(material, edge.sharedMaterial);
                    Assert.Less(edge.sortingOrder, body.sortingOrder, "the edge draws under its tube, so only its rim shows");
                    Assert.AreEqual(0.3f + 2f * 0.05f, edge.widthMultiplier, 1e-4f, "Pulse Edge Width past the tube on each side");
                    Assert.AreEqual(body.startColor.a * 0.3f, edge.startColor.a, 1f / 255f,
                        "Pulse Edge Strength of its tube, so where the outline lies under the tube the arch stays see-through");

                    Assert.Greater(body.positionCount, 8);
                    Assert.AreEqual(body.positionCount, core.positionCount);
                    Assert.AreEqual(body.positionCount, edge.positionCount);
                    for (int i = 0; i < body.positionCount; i++)
                    {
                        Assert.AreEqual(body.GetPosition(i), core.GetPosition(i), "the core runs along the middle of its tube");
                        Assert.AreEqual(body.GetPosition(i), edge.GetPosition(i), "the edge runs along its tube");
                        Vector3 p = body.transform.TransformPoint(body.GetPosition(i));
                        Assert.IsTrue(ConeFilter.IsWithinCone(origin, Vector3.right, p, 3.25f + 1e-3f, 71f + 1e-2f), $"{body.name} point {i} {p:F3}");
                        if (a == 0)
                        {
                            reach = Mathf.Max(reach, new Vector2(p.x - origin.x, p.z - origin.z).magnitude);
                            top = Mathf.Max(top, p.y - origin.y);
                        }
                    }
                    Assert.AreEqual(0f, body.GetPosition(0).y, 1e-4f, body.name + " stands on its feet");
                    Assert.AreEqual(0f, body.GetPosition(body.positionCount - 1).y, 1e-4f);
                }
                Assert.AreEqual(3.25f, reach, 0.01f, "the arch reaches the live range handed to Play at the end of the sweep");
                Assert.AreEqual(Mathf.Min(0.8f * 3.25f * Mathf.Sin(35.5f * Mathf.Deg2Rad), 1.1f), top, 0.01f, "standing upright, as tall as the rule says");
            }
            finally
            {
                if (go.GetComponent<SonicPulseView>() != null && go.GetComponent<SonicPulseView>().Arch != null)
                    Object.DestroyImmediate(go.GetComponent<SonicPulseView>().Arch);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }
        }

        [Test]
        public void DestroyingTheViewFreesTheArch()
        {
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            ArenaLook look = ScriptableObject.CreateInstance<ArenaLook>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            var go = new GameObject("pulse leak test");
            int before = LiveArches();
            try
            {
                theme.arenaLook = look;
                look.fxAlpha = material;
                SonicPulseView view = go.AddComponent<SonicPulseView>();
                var so = new SerializedObject(view);
                so.FindProperty("theme").objectReferenceValue = theme;
                so.ApplyModifiedPropertiesWithoutUndo();
                view.Play(Vector3.zero, Vector3.forward, 5f, 60f, 0, null);
                view.Play(Vector3.one, Vector3.right, 5f, 60f, 0, null);
                Assert.AreEqual(before + 1, LiveArches(), "one arch per view, reused by every later pulse");

                // Edit mode never sends OnDestroy to a plain MonoBehaviour, so the test sends it the way the game's Destroy does.
                typeof(SonicPulseView).GetMethod("OnDestroy", Any).Invoke(view, null);
                Object.DestroyImmediate(go);
            }
            finally
            {
                if (go != null)
                    Object.DestroyImmediate(go);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }
            Assert.AreEqual(before, LiveArches(), "the arch leaked past its view");
        }

        private static int LiveArches() =>
            Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g != null && g.name == SonicPulseView.ArchName && g.scene.IsValid());
    }
}
