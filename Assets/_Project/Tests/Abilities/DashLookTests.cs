using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Combat;
using Overpower.Vision;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The Dash draws a straight silver streak along its path and flat chevrons on the floor where it began, pointing the way it
    /// went, for the players who saw the dash (the other clients, behind the dasher's fog gate), then fades. No cyan trail: it read as the
    /// cyan team.</summary>
    public class DashLookTests
    {
        private const BindingFlags Any = MovementLookTestKit.Any;
        private const int DashId = 14;
        private const string DashPrefab = "Assets/_Project/Features/Abilities/Data/Dash.prefab";

        [Test]
        public void ChevronsStartAtTheLaunchAndStayInsideTheDash()
        {
            Assert.AreEqual(0f, DashLook.ChevronDistance(0, 3, 0.7f, 3f), 1e-5f, "the first chevron sits where the dash began");
            Assert.AreEqual(0.7f, DashLook.ChevronDistance(1, 3, 0.7f, 3f), 1e-5f, "one spacing apart");
            Assert.AreEqual(1.4f, DashLook.ChevronDistance(2, 3, 0.7f, 3f), 1e-5f);
            for (int i = 0; i < 3; i++)
                Assert.Less(DashLook.ChevronDistance(i, 3, 2f, 1.5f), 1.5f, "a short dash squeezes them together instead of drawing past its end");
            Assert.Less(DashLook.ChevronDistance(1, 3, 2f, 1.5f), DashLook.ChevronDistance(2, 3, 2f, 1.5f), "still in order along the dash");
            Assert.AreEqual(0f, DashLook.ChevronDistance(2, 3, -1f, 3f), 1e-5f, "no negative spacing");
        }

        [Test]
        public void ChevronsPointTheWayTheDashWent()
        {
            Vector3 tip = new Vector3(2f, 0f, 1f);
            Vector3 left = DashLook.ChevronPoint(tip, Vector3.right, 0.5f, 0.2f, 0);
            Vector3 mid = DashLook.ChevronPoint(tip, Vector3.right, 0.5f, 0.2f, 1);
            Vector3 right = DashLook.ChevronPoint(tip, Vector3.right, 0.5f, 0.2f, 2);
            Assert.AreEqual(tip, mid, "the middle point is the tip");
            Assert.AreEqual(1.8f, left.x, 1e-5f, "the wings trail the tip by the chevron's depth");
            Assert.AreEqual(1.8f, right.x, 1e-5f);
            Assert.AreEqual(0.5f, Vector3.Distance(left, right), 1e-5f, "the wings span the chevron's size");
            Assert.AreEqual(1f, (left.z + right.z) * 0.5f, 1e-5f, "symmetric about the dash line");
            Assert.AreEqual(0f, left.y, 1e-5f, "flat");
        }

        [Test]
        public void TheStreakRunsStraightFromTheLaunchToTheDasherNeverPastTheDash()
        {
            Vector3 start = new Vector3(1f, 0.5f, 1f);
            Assert.AreEqual(1.2f, DashLook.StreakReach(start, Vector3.right, new Vector3(2.2f, 0.5f, 1.3f), 3f), 1e-5f,
                "the dasher's progress along the dash line, whatever their sideways wobble on a remote screen");
            Assert.AreEqual(3f, DashLook.StreakReach(start, Vector3.right, new Vector3(9f, 0.5f, 1f), 3f), 1e-5f, "never past the dash's length");
            Assert.AreEqual(0f, DashLook.StreakReach(start, Vector3.right, new Vector3(0f, 0.5f, 1f), 3f), 1e-5f, "never behind the launch");
            Assert.AreEqual(1f, DashLook.StreakReach(start, Vector3.right, new Vector3(2f, 4f, 1f), 3f), 1e-5f, "height plays no part");
        }

        [Test]
        public void TheStreakStopsShortOfTheDasherAndChevronsOnlyWhereTheDashGot()
        {
            Assert.AreEqual(0.7f, DashLook.HeadReach(1.2f, 0.5f), 1e-5f, "the wide end stops Dash Streak Gap behind the dasher, off their legs");
            Assert.AreEqual(0f, DashLook.HeadReach(0.3f, 0.5f), 1e-5f, "nothing yet while the dasher is closer to the launch than the gap");
            Assert.AreEqual(1.2f, DashLook.HeadReach(1.2f, -1f), 1e-5f, "a negative gap never pushes the streak past the dasher");
            Assert.IsTrue(DashLook.ChevronShown(0f, 0f), "the first chevron marks the launch at once");
            Assert.IsTrue(DashLook.ChevronShown(0.7f, 1.2f), "the dasher has passed it");
            Assert.IsFalse(DashLook.ChevronShown(1.4f, 0.5f), "a dash stopped short by a wall draws no chevron past where it got");
        }

        [Test]
        public void TheLookHoldsWhileTravellingThenFades()
        {
            Assert.AreEqual(1f, DashLook.Alpha(0f, 0.2f, 0.25f), 1e-5f);
            Assert.AreEqual(1f, DashLook.Alpha(0.2f, 0.2f, 0.25f), 1e-5f, "solid for the whole dash");
            Assert.AreEqual(0.5f, DashLook.Alpha(0.325f, 0.2f, 0.25f), 1e-4f, "then fades over the fade time");
            Assert.AreEqual(0f, DashLook.Alpha(0.5f, 0.2f, 0.25f), 1e-5f);
        }

        [Test]
        public void PlayDrawsTheLookTableColourAsAStraightStreakAndFloorChevrons()
        {
            using (var kit = new MovementLookTestKit(DashId, LookShape.Streak))
            {
                kit.Look.dashStreakWidth = 0.4f;
                kit.Look.dashStreakHeight = 0.6f;
                kit.Look.dashStreakAlpha = 0.8f;
                kit.Look.dashFadeSeconds = 0.25f;
                kit.Look.dashChevronCount = 3;
                kit.Look.dashChevronSpacing = 0.7f;
                kit.Look.dashChevronSize = 0.5f;
                kit.Look.dashChevronWidth = 0.07f;
                kit.Look.dashStreakGap = 0.5f;
                GameObject go = kit.NewObject("dash view test");
                kit.GiveDefinition(go.AddComponent<DashAbility>(), DashId);
                DashStreakView view = go.AddComponent<DashStreakView>();
                MovementLookTestKit.SetTheme(view, kit.Theme);
                Vector3 start = new Vector3(2f, 500f, 1f); // far above the arena: no floor under it, so the launch height is the floor
                Transform body = kit.NewObject("dasher").transform;
                body.position = start + new Vector3(1.2f, 0f, 0.3f);

                view.Play(start, Vector3.right, 3f, 0.2f, body, 1, null);
                kit.Make(view.Root);
                view.Draw(0.1f);

                Assert.IsNotNull(view.Root);
                Assert.IsNull(view.Root.transform.parent, "the streak stays where the dash happened, not on the dasher");
                LineRenderer streak = view.Root.transform.Find(DashStreakView.StreakName).GetComponent<LineRenderer>();
                Assert.AreSame(kit.Fx, streak.sharedMaterial, "FX Alpha");
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, streak.startColor, "the colour of the Dash's row in the look table");
                Assert.AreEqual(0.8f, streak.startColor.a, 1f / 255f, "Dash Streak Alpha while travelling");
                Assert.AreEqual(2, streak.positionCount, "one straight piece");
                Vector3 tail = streak.GetPosition(0), head = streak.GetPosition(1);
                Assert.AreEqual(2f, tail.x, 1e-4f, "from the launch");
                Assert.AreEqual(2.7f, head.x, 1e-4f, "to the dasher's progress along the dash, Dash Streak Gap short of the dasher");
                Assert.AreEqual(1f, tail.z, 1e-4f, "straight along the dash line");
                Assert.AreEqual(1f, head.z, 1e-4f);
                Assert.AreEqual(500.6f, head.y, 1e-3f, "Dash Streak Height over the floor");
                Assert.AreEqual(0f, streak.widthCurve.Evaluate(0f), 1e-3f, "sharp where the dash began");
                Assert.AreEqual(0.4f, streak.widthMultiplier * streak.widthCurve.Evaluate(1f), 1e-3f, "Dash Streak Width at the dasher");

                LineRenderer farChevron = view.Root.transform.Find(DashStreakView.ChevronName + 2).GetComponent<LineRenderer>();
                Assert.AreEqual(0, farChevron.positionCount, "the dasher is 1.2 m out: the chevron at 1.4 m waits until they pass it");
                for (int i = 0; i < 2; i++)
                {
                    LineRenderer chevron = view.Root.transform.Find(DashStreakView.ChevronName + i).GetComponent<LineRenderer>();
                    Assert.AreSame(kit.Fx, chevron.sharedMaterial);
                    Assert.AreEqual(3, chevron.positionCount, "a V");
                    Assert.AreEqual(LineAlignment.TransformZ, chevron.alignment, "lying flat");
                    Vector3 tip = chevron.GetPosition(1);
                    Assert.AreEqual(2f + DashLook.ChevronDistance(i, 3, 0.7f, 3f), tip.x, 1e-4f, "spaced along the dash from the launch");
                    Assert.Greater(tip.x, chevron.GetPosition(0).x, "pointing the way the dash went");
                    Assert.Less(tip.y, 500.1f, "on the floor");
                    MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, chevron.startColor, "chevron colour");
                }

                body.position = start + new Vector3(2.6f, 0f, 0f);
                view.Draw(0.3f);
                Assert.AreEqual(4.1f, streak.GetPosition(1).x, 1e-4f, "after the travel time the head still follows: another screen's copy of the dasher arrives late");
                Assert.AreEqual(3, farChevron.positionCount, "passed: every chevron shows");
                Assert.AreEqual(3.4f, farChevron.GetPosition(1).x, 1e-4f);
                body.position = start + new Vector3(0.5f, 0f, 0f);
                view.Draw(0.35f);
                Assert.AreEqual(4.1f, streak.GetPosition(1).x, 1e-4f, "and never draws back");

                view.Draw(0.2f + 0.25f + 0.01f);
                Assert.AreEqual(0f, streak.startColor.a, 1e-3f, "faded out after the dash plus Dash Fade Seconds");
            }
        }

        [Test]
        public void TheStreakStartsWhereTheDasherWasFirstSeen()
        {
            Assert.Less(DashLook.SeenFrom(DashLook.NotSeenYet, false, 1.5f), 0f, "hidden in the fog: nothing seen yet");
            Assert.AreEqual(0f, DashLook.SeenFrom(DashLook.NotSeenYet, true, 0f), "seen at the launch: the whole dash shows");
            Assert.AreEqual(2f, DashLook.SeenFrom(DashLook.NotSeenYet, true, 2f), "first seen 2 m out: the streak starts there");
            Assert.AreEqual(2f, DashLook.SeenFrom(2f, true, 2.8f), "and stays there as the dasher goes on");
            Assert.AreEqual(2f, DashLook.SeenFrom(2f, false, 2.8f), "hidden again later: what was seen stays where it began");
        }

        [Test]
        public void AnEnemyDashOutOfTheFogShowsOnlyThePartItsTeamSaw()
        {
            using (var kit = new MovementLookTestKit(DashId, LookShape.Streak))
            {
                kit.Look.dashStreakGap = 0.5f;
                kit.Look.dashChevronCount = 3;
                kit.Look.dashChevronSpacing = 0.7f;
                kit.Look.dashFadeSeconds = 0.25f;
                GameObject go = kit.NewObject("dash view fog test");
                kit.GiveDefinition(go.AddComponent<DashAbility>(), DashId);
                DashStreakView view = go.AddComponent<DashStreakView>();
                MovementLookTestKit.SetTheme(view, kit.Theme);
                Vector3 start = new Vector3(2f, 500f, 1f);
                Transform body = kit.NewObject("dasher").transform;
                body.position = start;
                view.Play(start, Vector3.right, 3f, 0.2f, body, 1, null); // builds the streak once
                kit.Make(view.Root);
                VisibleWhenSeen gate = view.Root.AddComponent<VisibleWhenSeen>(); // what TeamSight's gate is on another team's screen
                gate.SetVisible(false);

                view.Play(start, Vector3.right, 3f, 0.2f, body, 1, null); // an enemy dash that begins in the fog
                LineRenderer streak = view.Root.transform.Find(DashStreakView.StreakName).GetComponent<LineRenderer>();
                body.position = start + new Vector3(1.5f, 0f, 0f);
                view.Draw(0.1f);
                Assert.AreEqual(0, streak.positionCount, "still in the fog: nothing is drawn");

                gate.SetVisible(true); // the dasher steps out of the fog 1.5 m along the dash
                body.position = start + new Vector3(1.6f, 0f, 0f);
                view.Draw(0.12f);
                body.position = start + new Vector3(3f, 0f, 0f);
                view.Draw(0.2f);
                Assert.AreEqual(2, streak.positionCount);
                Assert.AreEqual(3.6f, streak.GetPosition(0).x, 1e-4f, "the streak begins where the dasher was first seen, never back in the fog");
                Assert.AreEqual(4.5f, streak.GetPosition(1).x, 1e-4f, "and still runs to just behind the dasher");
                for (int i = 0; i < 3; i++)
                {
                    LineRenderer chevron = view.Root.transform.Find(DashStreakView.ChevronName + i).GetComponent<LineRenderer>();
                    bool inFog = DashLook.ChevronDistance(i, 3, 0.7f, 3f) < 1.6f;
                    Assert.AreEqual(inFog ? 0 : 3, chevron.positionCount, "chevron " + i + (inFog ? " lies in the fog: hidden" : " was seen: drawn"));
                }

                gate.SetVisible(false);
                view.Play(start, Vector3.right, 3f, 0.2f, body, 1, null);
                body.position = start + new Vector3(3f, 0f, 0f); // a dasher who walks into sight only after the dash is over
                view.Draw(0.1f);
                gate.SetVisible(true);
                view.Draw(0.3f);
                Assert.IsTrue(streak.positionCount == 0 || Mathf.Approximately(streak.GetPosition(0).x, streak.GetPosition(1).x),
                    "nothing of the path behind them, which their enemies never saw");
            }
        }

        [Test]
        public void TheStreakIsBuiltBeforeTheFogGateAndShownOnlyAfterIt()
        {
            MethodInfo play = typeof(DashStreakView).GetMethod(nameof(DashStreakView.Play));
            var attachAt = IlWiring.CallOffsets(play, typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.AttachToCaster)));
            var buildAt = IlWiring.CallOffsets(play, typeof(DashStreakView).GetMethod("Build", Any));
            var showAt = IlWiring.CallOffsets(play, typeof(GameObject).GetMethod(nameof(GameObject.SetActive)));
            Assert.AreEqual(1, attachAt.Count, "Play attaches the dasher's fog gate once");
            Assert.IsNotEmpty(buildAt);
            Assert.IsNotEmpty(showAt);
            Assert.Less(buildAt.Max(), attachAt[0], "every line exists before the gate collects the renderers");
            Assert.Less(attachAt[0], showAt.Min(), "the gate is on before the streak shows");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(typeof(DashStreakView), "Build", typeof(GameObject).GetMethod(nameof(GameObject.SetActive))),
                "built awake and empty, so the gate wakes as it attaches");
        }

        [Test]
        public void OnlyTheOtherClientsPlayTheStreakThroughTheTestedRules()
        {
            MethodInfo play = typeof(DashStreakView).GetMethod(nameof(DashStreakView.Play));
            Assert.IsTrue(IlWiring.Uses(typeof(DashAbility), "PlayRemoteStreak", play), "the remote branch plays the streak");
            Assert.IsFalse(IlWiring.Uses(typeof(DashAbility), "StartTravel", play), "the dasher's own screen draws no streak");
            Assert.IsTrue(IlWiring.Uses(typeof(DashAbility), nameof(DashAbility.ExecuteCast), typeof(DashAbility).GetMethod("PlayRemoteStreak", Any)));
            Assert.IsTrue(IlWiring.Uses(typeof(DashStreakView), "Draw", typeof(DashLook).GetMethod(nameof(DashLook.StreakReach))));
            Assert.IsTrue(IlWiring.Uses(typeof(DashStreakView), "Draw", typeof(DashLook).GetMethod(nameof(DashLook.HeadReach))));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(DashStreakView), "Draw", typeof(DashLook).GetMethod(nameof(DashLook.ChevronShown))));
            Assert.IsTrue(IlWiring.Uses(typeof(DashStreakView), "Draw", typeof(DashLook).GetMethod(nameof(DashLook.ChevronDistance))));
            Assert.IsTrue(IlWiring.Uses(typeof(DashStreakView), "Draw", typeof(DashLook).GetMethod(nameof(DashLook.ChevronPoint))));
            Assert.IsTrue(IlWiring.Uses(typeof(DashStreakView), "Draw", typeof(DashLook).GetMethod(nameof(DashLook.Alpha))));
            Assert.IsTrue(IlWiring.Uses(typeof(DashStreakView), "Draw", typeof(DashLook).GetMethod(nameof(DashLook.SeenFrom))),
                "the streak starts where the dasher's enemies first saw them");
            Assert.IsTrue(IlWiring.Uses(typeof(DashStreakView), "Draw", typeof(VisibleWhenSeen).GetProperty(nameof(VisibleWhenSeen.Shown)).GetGetMethod()),
                "asked of the dasher's own fog gate");
            Assert.IsTrue(IlWiring.Uses(typeof(DashStreakView), nameof(DashStreakView.Play), typeof(LookColours).GetMethod(nameof(LookColours.ForAbility))));
        }

        [Test]
        public void TheDashersOwnScreenDrawsNoStreakAndEveryOtherScreenDoes()
        {
            using (var kit = new MovementLookTestKit(DashId, LookShape.Streak))
            {
                GameObject root = kit.NewObject("dasher root"); // no PlayerDisplacement: the dasher's own travel does nothing here
                root.transform.position = new Vector3(0f, 500f, 0f);
                GameObject go = kit.NewObject("dash module");
                DashAbility dash = go.AddComponent<DashAbility>();
                DashStreakView view = go.AddComponent<DashStreakView>();
                MovementLookTestKit.SetTheme(view, kit.Theme);
                kit.GiveDefinition(dash, DashId);
                dash.Bind(null, new AbilityOwner(root), dash.Definition);
                typeof(DashAbility).GetField("streak", Any).SetValue(dash, view);
                var payload = new CastPayload { Origin = root.transform.position, Direction = Vector3.right };

                dash.ExecuteCast(new CastEvent(payload, 0, 2, 1, true, 0f));
                Assert.IsNull(view.Root, "the dasher's own screen draws no streak");
                dash.ExecuteCast(new CastEvent(payload, 0, 2, 1, false, 0f));
                Assert.IsNotNull(view.Root, "every other screen plays it");
                kit.Make(view.Root);
            }
        }

        [Test]
        public void DestroyFreesTheStreak()
        {
            int before = MovementLookTestKit.Live(DashStreakView.RootName);
            using (var kit = new MovementLookTestKit(DashId, LookShape.Streak))
            {
                GameObject go = new GameObject("dash leak test");
                DashStreakView view = go.AddComponent<DashStreakView>();
                MovementLookTestKit.SetTheme(view, kit.Theme);
                view.Play(new Vector3(0f, 500f, 0f), Vector3.forward, 3f, 0.2f, go.transform, 0, null);
                Assert.AreEqual(before + 1, MovementLookTestKit.Live(DashStreakView.RootName), "one streak object per view");
                MovementLookTestKit.DestroyLikeTheGame(go);
            }
            Assert.AreEqual(before, MovementLookTestKit.Live(DashStreakView.RootName), "the streak leaked past its view");
        }

        [Test]
        public void DashHasTheStreakViewAndNoCyanTrail()
        {
            GameObject dash = AssetDatabase.LoadAssetAtPath<GameObject>(DashPrefab);
            Assert.IsNotNull(dash);
            Assert.IsEmpty(dash.GetComponentsInChildren<TrailRenderer>(true), "the cyan trail read as the cyan team");
            foreach (Renderer r in dash.GetComponentsInChildren<Renderer>(true))
                foreach (Material m in r.sharedMaterials)
                    Assert.AreNotEqual("Dash Trail", m != null ? m.name : "", "Dash Trail.mat is not used");
            Assert.IsFalse(System.IO.File.ReadAllText(DashPrefab).Contains("f6fe46bab45b4034e9ac2662f30ba5d8"), "no reference to Dash Trail.mat left in the prefab");
            DashStreakView view = dash.GetComponent<DashStreakView>();
            Assert.IsNotNull(view, "Dash.prefab carries the streak view");
            Assert.AreEqual("Assets/_Project/Shared/Config/UiTheme.asset", AssetDatabase.GetAssetPath(new SerializedObject(view).FindProperty("theme").objectReferenceValue));
            Assert.IsNull(typeof(DashAbility).GetField("remoteTrail", Any), "no trail field");
        }
    }
}
