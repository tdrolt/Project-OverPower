using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Vision;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Short grey speed-lines stream behind a sprinting player's feet. The sprinter tells every client when a sprint really starts
    /// and stops (phases on the ability's one RPC), so everyone who can see the player sees the lines; the sprinter's own screen uses its
    /// real state. Lines show only while the player actually moves.</summary>
    public class SprintLinesRuleTests
    {
        private const BindingFlags Any = MovementLookTestKit.Any;
        private const int SprintId = 16;

        [Test]
        public void OthersFollowTheSharedStartAndStopTheSprinterItsOwnState()
        {
            Assert.IsTrue(SprintLinesRule.Sprinting(true, true, false), "my own screen: my real sprint");
            Assert.IsFalse(SprintLinesRule.Sprinting(true, false, true), "my own screen: a press that did not start a sprint draws nothing");
            Assert.IsTrue(SprintLinesRule.Sprinting(false, false, true), "another screen: the start it heard");
            Assert.IsFalse(SprintLinesRule.Sprinting(false, true, false), "another screen never reads the owner-only state");
        }

        [Test]
        public void OtherScreensHearTheSprintWhenItReallyStartsAndStops()
        {
            Assert.IsTrue(SprintLinesRule.StartsNow(false, true), "off to on: the others are told");
            Assert.IsFalse(SprintLinesRule.StartsNow(true, true), "still running: nothing new to tell");
            Assert.IsFalse(SprintLinesRule.StartsNow(false, false), "a press that started nothing tells nobody");
            Assert.IsFalse(SprintLinesRule.StartsNow(true, false), "a stop is its own phase");
            Assert.AreNotEqual(0, SprintLinesRule.StartedPhase, "the key press's cast is not the start");
            Assert.AreNotEqual(SprintLinesRule.StoppedPhase, SprintLinesRule.StartedPhase);
            Assert.IsTrue(SprintLinesRule.AnnouncedAfter(false, SprintLinesRule.StartedPhase));
            Assert.IsFalse(SprintLinesRule.AnnouncedAfter(true, SprintLinesRule.StoppedPhase));
            Assert.IsFalse(SprintLinesRule.AnnouncedAfter(false, 0), "the key press alone may start nothing (a tap, a press during a stun)");
            Assert.IsTrue(SprintLinesRule.AnnouncedAfter(true, 0), "a press while running changes nothing until the sprint really stops");
        }

        [Test]
        public void LinesShowOnlyWhileSprintingAndMoving()
        {
            Assert.IsTrue(SprintLinesRule.Show(true, 7.5f, 1f));
            Assert.IsFalse(SprintLinesRule.Show(true, 0.2f, 1f), "holding sprint while standing still draws nothing");
            Assert.IsFalse(SprintLinesRule.Show(false, 7.5f, 1f), "a dash or a pull without a sprint draws nothing");
            Assert.IsTrue(SprintLinesRule.Show(true, 1f, 1f), "from Sprint Line Min Speed on");
        }

        [Test]
        public void SpeedIsFlatDistanceOverTimeAndSmoothedOverPhysicsSteps()
        {
            Assert.AreEqual(5f, SprintLinesRule.Speed(Vector3.zero, new Vector3(0.3f, 7f, 0.4f), 0.1f), 1e-4f, "falling or climbing is not running");
            Assert.AreEqual(0f, SprintLinesRule.Speed(Vector3.zero, Vector3.one, 0f), "no time, no speed");
            float s = 0f;
            for (int i = 0; i < 120; i++)
                s = SprintLinesRule.Smooth(s, i % 2 == 0 ? 0f : 10f, 1f / 120f, 0.1f); // a frame with a physics step, a frame without
            Assert.AreEqual(5f, s, 1.5f, "settles near the true speed instead of flickering with the physics steps");
            Assert.AreEqual(3f, SprintLinesRule.Smooth(3f, 9f, 0f, 0.1f), 1e-5f, "no time passed, nothing changes");
            Assert.AreEqual(9f, SprintLinesRule.Smooth(3f, 9f, 1f, 0f), 1e-5f, "no smoothing time: the raw speed");
        }

        [Test]
        public void LinesStreamBackFromBehindTheFeet()
        {
            for (int i = 0; i < 3; i++)
            {
                for (float t = 0f; t < 1f; t += 0.05f)
                {
                    Vector3 head = SprintLinesRule.LinePoint(i, 3, t, 3f, 0.22f, 0.35f, 0.6f, 0.7f, false);
                    Vector3 tail = SprintLinesRule.LinePoint(i, 3, t, 3f, 0.22f, 0.35f, 0.6f, 0.7f, true);
                    Assert.LessOrEqual(head.z, -0.35f + 1e-4f, "never in front of Sprint Line Behind");
                    Assert.Less(tail.z, head.z, "the tail trails further back");
                    Assert.AreEqual(head.x, tail.x, 1e-5f, "straight, along the run");
                    Assert.AreEqual(0f, head.y, 1e-5f);
                    Assert.GreaterOrEqual(SprintLinesRule.LineAlpha(i, 3, t, 3f), 0f);
                    Assert.LessOrEqual(SprintLinesRule.LineAlpha(i, 3, t, 3f), 1f);
                }
            }
            Assert.AreEqual(-0.22f, SprintLinesRule.LinePoint(0, 3, 0f, 3f, 0.22f, 0.35f, 0.6f, 0.7f, false).x, 1e-5f, "spread sideways by Sprint Line Spread");
            Assert.AreEqual(0f, SprintLinesRule.LinePoint(1, 3, 0f, 3f, 0.22f, 0.35f, 0.6f, 0.7f, false).x, 1e-5f, "about the middle");
            Assert.AreEqual(0.22f, SprintLinesRule.LinePoint(2, 3, 0f, 3f, 0.22f, 0.35f, 0.6f, 0.7f, false).x, 1e-5f);
            float near = SprintLinesRule.LinePoint(1, 3, 0.30f, 3f, 0.22f, 0.35f, 0.6f, 0.7f, false).z;
            float later = SprintLinesRule.LinePoint(1, 3, 0.32f, 3f, 0.22f, 0.35f, 0.6f, 0.7f, false).z;
            Assert.Less(later, near, "a line slides backward as time passes, so the run reads as speed");
            Assert.Less(SprintLinesRule.LineAlpha(1, 3, 0.32f, 3f), SprintLinesRule.LineAlpha(1, 3, 0.30f, 3f), "and fades as it goes");
            Assert.AreEqual(1f, SprintLinesRule.LineAlpha(0, 3, 0f, 3f), 1e-5f, "full where it starts");
            Assert.GreaterOrEqual(SprintLinesRule.LineAlpha(0, 3, 0.5f / 3f, 3f), 0.7f, "strong for most of its slide, fading only toward its end");
        }

        [Test]
        public void TheAbilityTracksTheSharedStartAndStopOnEveryClient()
        {
            var go = new GameObject("sprint phases test");
            try
            {
                SprintAbility sprint = go.AddComponent<SprintAbility>();
                Assert.IsFalse(sprint.AnnouncedSprinting);
                sprint.ExecuteCast(new CastEvent(default, 0, 2, 1, false, 0f));
                Assert.IsFalse(sprint.AnnouncedSprinting, "the key press's cast alone starts nothing on other screens");
                sprint.ExecuteCast(Heard(SprintLinesRule.StartedPhase));
                Assert.IsTrue(sprint.AnnouncedSprinting, "the start phase every client receives starts it");
                sprint.ExecuteCast(Heard(SprintLinesRule.StoppedPhase));
                Assert.IsFalse(sprint.AnnouncedSprinting, "the stop phase ends it");
                sprint.ExecuteCast(Heard(SprintLinesRule.StartedPhase));
                Assert.IsTrue(sprint.AnnouncedSprinting, "a sprint that resumes after an overheat or a stun, Shift still held, starts it again");
                sprint.Interrupt(InterruptReason.Died);
                Assert.IsFalse(sprint.AnnouncedSprinting, "a death ends it on every client");
                sprint.ExecuteCast(Heard(SprintLinesRule.StartedPhase));
                sprint.Interrupt(InterruptReason.Unequipped);
                Assert.IsFalse(sprint.AnnouncedSprinting);
                sprint.ExecuteCast(Heard(SprintLinesRule.StartedPhase));
                sprint.OnRespawned();
                Assert.IsFalse(sprint.AnnouncedSprinting, "a fresh life starts walking");
            }
            finally { Object.DestroyImmediate(go); }
        }

        private static CastEvent Heard(byte phase) => new CastEvent(default, phase, 2, 1, false, 0f);

        [Test]
        public void TheSprinterTellsOthersOnEveryRealStartThroughTheTestedRules()
        {
            MethodInfo startsNow = typeof(SprintLinesRule).GetMethod(nameof(SprintLinesRule.StartsNow));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(SprintAbility), nameof(SprintAbility.OwnerTick), startsNow),
                "every off-to-on of the sprint, a press or a resume after an overheat, a stun or a respawn, decides the start message");
            Assert.IsTrue(IlWiring.Uses(typeof(SprintAbility), nameof(SprintAbility.OwnerTick), typeof(AbilityModule).GetMethod("SendPhase", Any)),
                "the start goes out on the ability's one RPC");
            Assert.IsTrue(IlWiring.Uses(typeof(SprintAbility), nameof(SprintAbility.ExecuteCast), typeof(SprintLinesRule).GetMethod(nameof(SprintLinesRule.AnnouncedAfter))),
                "every client reads the phases through the rule");
            Assert.IsFalse(IlWiring.Uses(typeof(SprintAbility), nameof(SprintAbility.TryBuildCast), typeof(AbilityModule).GetMethod("SendPhase", Any)),
                "the key press itself announces nothing");
        }

        [Test]
        public void TheViewDrawsLookTableLinesBehindTheRunOnTheRulesAnswer()
        {
            using (var kit = new MovementLookTestKit(SprintId, LookShape.SpeedLines))
            {
                kit.Look.sprintLineCount = 3;
                kit.Look.sprintLineLength = 0.6f;
                kit.Look.sprintLineWidth = 0.05f;
                kit.Look.sprintLineSpread = 0.22f;
                kit.Look.sprintLineBehind = 0.35f;
                kit.Look.sprintLineTravel = 0.7f;
                kit.Look.sprintLineRate = 3f;
                kit.Look.sprintLineMinSpeed = 1f;
                kit.Look.sprintLineAlpha = 0.8f;
                kit.Look.sprintLineLift = 0.06f;
                GameObject player = kit.NewObject("sprinter");
                player.transform.position = new Vector3(0f, 500.5f, 0f);
                SprintAbility sprint = player.AddComponent<SprintAbility>();
                kit.GiveDefinition(sprint, SprintId);

                SprintLinesView view = SprintLinesView.TryCreate(player.transform, sprint, null, kit.Theme, 0.5f);
                Assert.IsNotNull(view);
                Assert.AreSame(player.transform, view.transform.parent, "carried by the player");
                GameObject lines = view.transform.Find(SprintLinesView.LinesChildName).gameObject;
                Assert.IsFalse(lines.activeSelf, "hidden until the rule says so");
                LineRenderer[] all = lines.GetComponentsInChildren<LineRenderer>(true);
                Assert.AreEqual(3, all.Length, "Sprint Line Count");

                sprint.ExecuteCast(Heard(SprintLinesRule.StartedPhase)); // heard as another client: not mine, started
                view.Step(Vector3.right * 7.5f * 0.1f, 0.1f, 0.25f); // ran 0.75 m toward +X in 0.1 s
                Assert.IsTrue(lines.activeSelf, "sprinting and moving");
                foreach (LineRenderer line in all)
                {
                    Assert.AreSame(kit.Fx, line.sharedMaterial);
                    Assert.AreEqual(2, line.positionCount);
                    MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, line.startColor, "the colour of the Sprint's row in the look table");
                    Vector3 a = line.GetPosition(0), b = line.GetPosition(1);
                    Assert.Less(a.x, player.transform.position.x, "behind the run");
                    Assert.Less(b.x, a.x, "streaming backward");
                    Assert.AreEqual(500.06f, a.y, 1e-3f, "just above the feet: Sprint Line Lift over 0.5 m below the root");
                }

                view.Step(Vector3.zero, 0.5f, 0.75f);
                Assert.IsFalse(lines.activeSelf, "standing still: no lines");
                sprint.ExecuteCast(Heard(SprintLinesRule.StoppedPhase));
                view.Step(Vector3.right * 0.75f, 0.1f, 0.85f);
                Assert.IsFalse(lines.activeSelf, "stopped sprinting: no lines");
            }
        }

        [Test]
        public void TheLinesAreBuiltBeforeTheFogGateAndShownOnTheRulesAnswer()
        {
            MethodInfo create = typeof(SprintLinesView).GetMethod(nameof(SprintLinesView.TryCreate));
            var attachAt = IlWiring.CallOffsets(create, typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.AttachToCaster)));
            var lineAt = IlWiring.CallOffsets(create, typeof(GameObject).GetMethods().First(m => m.Name == nameof(GameObject.AddComponent) && m.IsGenericMethod).MakeGenericMethod(typeof(LineRenderer)));
            Assert.AreEqual(1, attachAt.Count, "the sprinter's fog gate, once");
            Assert.IsNotEmpty(lineAt);
            Assert.Less(lineAt.Max(), attachAt[0], "every line exists before the gate collects the renderers");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(SprintLinesView), nameof(SprintLinesView.Step), typeof(SprintLinesRule).GetMethod(nameof(SprintLinesRule.Show))),
                "the lines switch on the rule's answer");
            Assert.IsTrue(IlWiring.Uses(typeof(SprintLinesView), nameof(SprintLinesView.Step), typeof(SprintAbility).GetProperty(nameof(SprintAbility.LooksSprinting)).GetMethod));
            Assert.IsTrue(IlWiring.Uses(typeof(SprintAbility), "get_" + nameof(SprintAbility.LooksSprinting), typeof(SprintLinesRule).GetMethod(nameof(SprintLinesRule.Sprinting))));
            Assert.IsTrue(IlWiring.Uses(typeof(SprintLinesView), "LateUpdate", typeof(SprintLinesView).GetMethod(nameof(SprintLinesView.Step))));
            Assert.IsTrue(IlWiring.Uses(typeof(SprintLinesView), "Reshape", typeof(SprintLinesRule).GetMethod(nameof(SprintLinesRule.LinePoint))));
            MethodInfo colourOf = typeof(SprintLinesView).GetMethod("ColourOf", Any);
            Assert.IsTrue(IlWiring.Uses(typeof(SprintLinesView), "ColourOf", typeof(LookColours).GetMethod(nameof(LookColours.ForAbility))));
            Assert.IsTrue(IlWiring.Uses(typeof(SprintLinesView), nameof(SprintLinesView.TryCreate), colourOf), "coloured as they are built");
            Assert.IsTrue(IlWiring.Uses(typeof(SprintLinesView), nameof(SprintLinesView.Step), colourOf), "and again as each run starts");
            Assert.IsTrue(IlWiring.Uses(typeof(SprintAbility), nameof(SprintAbility.OnEquip), create), "every client's copy of the sprint builds its lines");
        }

        [Test]
        public void DestroyFreesTheLines()
        {
            int before = MovementLookTestKit.Live(SprintLinesView.RootName);
            using (var kit = new MovementLookTestKit(SprintId, LookShape.SpeedLines))
            {
                GameObject player = kit.NewObject("sprinter");
                var module = new GameObject("Sprint");
                module.transform.SetParent(player.transform, false);
                SprintAbility sprint = module.AddComponent<SprintAbility>();
                typeof(SprintAbility).GetField("lines", Any).SetValue(sprint, SprintLinesView.TryCreate(player.transform, sprint, null, kit.Theme, 0.5f));
                Assert.AreEqual(before + 1, MovementLookTestKit.Live(SprintLinesView.RootName));
                MovementLookTestKit.DestroyLikeTheGame(module); // unequipping destroys the module; the lines hang on the player and must go with it
                Assert.AreEqual(before, MovementLookTestKit.Live(SprintLinesView.RootName), "the lines outlived their sprint");
            }
        }
    }
}
