using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.UI;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>A stunned player crackles with electric-blue arcs for exactly as long as their STUNNED label shows, on every client: the arcs
    /// follow the label (stun beats slow), their shape comes only from the server clock and the player, and the fog gate is on before they show.</summary>
    public class StunArcsRuleTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void ShowOnlyWhileStunned()
        {
            Assert.IsTrue(StunArcsRule.Show(StatusLabel.Stunned));
            Assert.IsFalse(StunArcsRule.Show(StatusLabel.Slowed), "slowed is not stunned");
            Assert.IsFalse(StunArcsRule.Show(StatusLabel.None));
            Assert.IsTrue(StunArcsRule.Show(StatusLabelRule.Choose(1.5f, 3f)), "stunned and slowed at once: stunned beats slowed, so the arcs show");

            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(StunArcsView), "LateUpdate", typeof(StunArcsRule).GetMethod(nameof(StunArcsRule.Show))),
                "the view switches its arcs on the rule's answer");
        }

        [Test]
        public void TheSameMomentAndPlayerDrawTheSameArcsOnEveryClient()
        {
            int seed = StunArcsRule.Seed(123456, 3, 15f);
            Assert.AreEqual(seed, StunArcsRule.Seed(123456, 3, 15f));
            Assert.AreEqual(seed, StunArcsRule.Seed(123456 + 20, 3, 15f), "20 ms later, still inside the same 1/15 s step");
            Assert.AreNotEqual(seed, StunArcsRule.Seed(123456 + 70, 3, 15f), "the next step re-shapes the arcs");
            Assert.AreNotEqual(seed, StunArcsRule.Seed(123456, 4, 15f), "another player crackles differently");
            Assert.AreNotEqual(StunArcsRule.Seed(-1, 3, 15f), StunArcsRule.Seed(0, 3, 15f), "the server clock wrapping through zero starts a new step");
            Assert.AreEqual(StunArcsRule.Seed(-1, 3, 15f), StunArcsRule.Seed(-60, 3, 15f), "the step just below zero is as long as any other (67 ms)");

            for (int k = 0; k < 7; k++)
                Assert.AreEqual(StunArcsRule.ArcPoint(seed, 1, k, 7, 0.8f, 0.7f, 0.25f, 60f, 110f), StunArcsRule.ArcPoint(seed, 1, k, 7, 0.8f, 0.7f, 0.25f, 60f, 110f));

            int differing = 0;
            for (int k = 0; k < 7; k++)
                if (StunArcsRule.ArcPoint(seed, 1, k, 7, 0.8f, 0.7f, 0.25f, 60f, 110f) != StunArcsRule.ArcPoint(seed + 1, 1, k, 7, 0.8f, 0.7f, 0.25f, 60f, 110f))
                    differing++;
            Assert.Greater(differing, 3, "a new seed is a new shape");
        }

        [Test]
        public void EveryClientShapesTheArcsFromTheServerClockAndThePlayer()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(StunArcsView), "Reshape", typeof(StunArcsRule).GetMethod(nameof(StunArcsRule.Seed))), "the shape's seed comes from the rule");
            Assert.IsTrue(IlWiring.Uses(typeof(StunArcsView), "Reshape", typeof(StunArcsRule).GetMethod(nameof(StunArcsRule.ArcPoint))), "the points come from the rule");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(StunArcsView), "Reshape",
                typeof(Photon.Pun.PhotonNetwork).GetProperty(nameof(Photon.Pun.PhotonNetwork.ServerTimestamp)).GetMethod), "the seed's moment is the shared server clock");
            Assert.IsTrue(IlWiring.CallsAcrossAssemblies(typeof(StunArcsView), "Reshape",
                typeof(Photon.Pun.PhotonView).GetProperty(nameof(Photon.Pun.PhotonView.OwnerActorNr)).GetMethod), "and the stunned player's actor number");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(StunArcsView), "Reshape", typeof(Time).GetProperty(nameof(Time.time)).GetMethod),
                "never the local clock, which differs on every client");
        }

        [Test]
        public void ArcsHugTheBodyAndAreJagged()
        {
            const float radius = 0.8f, halfHeight = 0.7f, jitter = 0.25f, shortest = 60f, longest = 110f;
            for (int seed = -40; seed < 40; seed++)
                for (int arc = 0; arc < 3; arc++)
                {
                    float turn = 0f;
                    for (int k = 0; k < 7; k++)
                    {
                        Vector3 p = StunArcsRule.ArcPoint(seed, arc, k, 7, radius, halfHeight, jitter, shortest, longest);
                        float flat = new Vector2(p.x, p.z).magnitude;
                        Assert.That(flat, Is.InRange(radius * (1f - jitter) - 1e-4f, radius * (1f + jitter) + 1e-4f), $"seed {seed} arc {arc} point {k}: on the shell round the body");
                        Assert.That(Mathf.Abs(p.y), Is.LessThanOrEqualTo(halfHeight * (1f + jitter) + 1e-4f), "within the body's height");
                        if (k > 0)
                        {
                            Vector3 before = StunArcsRule.ArcPoint(seed, arc, k - 1, 7, radius, halfHeight, jitter, shortest, longest);
                            turn += Vector2.SignedAngle(new Vector2(before.x, before.z), new Vector2(p.x, p.z));
                        }
                    }
                    Assert.That(Mathf.Abs(turn), Is.InRange(shortest - 1e-2f, longest + 1e-2f), $"seed {seed} arc {arc}: runs between the shortest and longest sweep round the body");
                }

            Vector3 a = StunArcsRule.ArcPoint(7, 0, 0, 7, radius, halfHeight, 0f, shortest, longest);
            Assert.AreEqual(radius, new Vector2(a.x, a.z).magnitude, 1e-4f, "without jitter every point sits on the radius");
        }

        [Test]
        public void EveryPlayerBuildsHiddenElectricBlueArcsBesideItsLabel()
        {
            var stunned = new Color(0.41f, 0.42f, 0.43f, 1f);
            UiTheme theme = ScriptableObject.CreateInstance<UiTheme>();
            var look = ScriptableObject.CreateInstance<Overpower.Arena.ArenaLook>();
            var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            var player = new GameObject("arcs test player");
            try
            {
                theme.statusStunnedColor = stunned;
                theme.arenaLook = look;
                look.fxAlpha = material;
                look.stunArcCount = 3;
                StunArcsView view = StunArcsView.TryCreate(player.transform, null, null, theme, 1.3f);
                Assert.IsNotNull(view);
                Assert.AreSame(player.transform, view.transform.parent);
                Assert.AreEqual(1.3f, view.transform.localPosition.y, 1e-5f, "centred on the body, not the feet");
                Transform arcs = view.transform.Find(StunArcsView.ArcsChildName);
                Assert.IsNotNull(arcs);
                Assert.IsFalse(arcs.gameObject.activeSelf, "nobody is stunned yet");
                LineRenderer[] lines = arcs.GetComponentsInChildren<LineRenderer>(true);
                Assert.AreEqual(3, lines.Length);
                foreach (LineRenderer line in lines)
                {
                    Assert.AreSame(material, line.sharedMaterial, "FX Alpha, shared: the blue stays blue over a grey floor");
                    foreach (Color c in new[] { line.startColor, line.endColor })
                    {
                        Assert.AreEqual(stunned.r, c.r, 1f / 255f, "the STUNNED label's colour (a line keeps 8-bit colours)");
                        Assert.AreEqual(stunned.g, c.g, 1f / 255f);
                        Assert.AreEqual(stunned.b, c.b, 1f / 255f);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(player);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(look);
                Object.DestroyImmediate(theme);
            }

            Assert.IsTrue(IlWiring.Uses(typeof(PlayerStatusEffects), "Start", typeof(StunArcsView).GetMethod(nameof(StunArcsView.TryCreate))),
                "every copy of every player builds its arcs, so every client can show them");
        }

        [Test]
        public void TheArcsAreBuiltBeforeTheFogGateAndShownOnlyAfterIt()
        {
            MethodInfo attach = typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.AttachToCaster));
            MethodInfo addLine = typeof(GameObject).GetMethods().First(m => m.Name == nameof(GameObject.AddComponent) && m.IsGenericMethod)
                .MakeGenericMethod(typeof(LineRenderer));
            MethodInfo setActive = typeof(GameObject).GetMethod(nameof(GameObject.SetActive));

            MethodInfo build = typeof(StunArcsView).GetMethod(nameof(StunArcsView.TryCreate), Any);
            var attachAt = IlWiring.CallOffsets(build, attach);
            var linesAt = IlWiring.CallOffsets(build, addLine);
            Assert.AreEqual(1, attachAt.Count, "TryCreate attaches the fog gate once");
            Assert.IsNotEmpty(linesAt, "the arcs are LineRenderers");
            Assert.Less(linesAt.Max(), attachAt[0], "the gate collects renderers once: every arc must exist before it attaches");

            MethodInfo late = typeof(StunArcsView).GetMethod("LateUpdate", Any);
            var lateAttach = IlWiring.CallOffsets(late, attach);
            var lateShow = IlWiring.CallOffsets(late, setActive);
            Assert.IsNotEmpty(lateAttach, "a player built before the local sight existed is gated on the first stun");
            Assert.IsNotEmpty(lateShow, "the view switches its child, never itself");
            Assert.Less(lateAttach.Min(), lateShow.Min(), "the gate is on before the arcs are switched on");
        }
    }
}
