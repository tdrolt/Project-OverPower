using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;
using Overpower.Net;
using Overpower.Telemetry;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Lobby Task 6: the pure rules behind a spectator seat - the Q / E / Space camera, and every guard that keeps a
    /// body-less, team-less player out of the counts.</summary>
    public class SpectatorSeatTests
    {
        private static readonly int[] Three = { 2, 4, 7 };

        // ---- Q / E over everyone with a body, in actor order

        [Test]
        public void NextActorMovesToTheNextHigherActorAndWrapsAround()
        {
            Assert.AreEqual(4, SpectateRules.NextActor(Three, 2));
            Assert.AreEqual(7, SpectateRules.NextActor(Three, 4));
            Assert.AreEqual(2, SpectateRules.NextActor(Three, 7));
        }

        [Test]
        public void PreviousActorMovesToTheNextLowerActorAndWrapsAround()
        {
            Assert.AreEqual(4, SpectateRules.PreviousActor(Three, 7));
            Assert.AreEqual(2, SpectateRules.PreviousActor(Three, 4));
            Assert.AreEqual(7, SpectateRules.PreviousActor(Three, 2));
        }

        [Test]
        public void WithNoCurrentTargetNextIsTheFirstAndPreviousIsTheLast()
        {
            Assert.AreEqual(2, SpectateRules.NextActor(Three, SpectateRules.None));
            Assert.AreEqual(7, SpectateRules.PreviousActor(Three, SpectateRules.None));
        }

        [TestCase(1, 2, 7)]  // before everyone
        [TestCase(3, 4, 2)]  // between 2 and 4
        [TestCase(5, 7, 4)]  // between 4 and 7
        [TestCase(9, 2, 7)]  // after everyone
        public void ACurrentActorWhoLeftNextIsTheFirstAfterItAndPreviousTheLastBeforeIt(int gone, int expectedNext, int expectedPrevious)
        {
            Assert.AreEqual(expectedNext, SpectateRules.NextActor(Three, gone));
            Assert.AreEqual(expectedPrevious, SpectateRules.PreviousActor(Three, gone));
        }

        [Test]
        public void OneWatchablePlayerIsTheirOwnNextAndPrevious()
        {
            Assert.AreEqual(5, SpectateRules.NextActor(new[] { 5 }, 5));
            Assert.AreEqual(5, SpectateRules.PreviousActor(new[] { 5 }, 5));
        }

        [Test]
        public void NobodyToWatchGivesNone()
        {
            Assert.AreEqual(SpectateRules.None, SpectateRules.NextActor(new int[0], 3));
            Assert.AreEqual(SpectateRules.None, SpectateRules.PreviousActor(new int[0], 3));
            Assert.AreEqual(SpectateRules.None, SpectateRules.NextActor(null, 3));
            Assert.AreEqual(SpectateRules.None, SpectateRules.PreviousActor(null, 3));
        }

        [Test]
        public void TheWatchableListIsInActorOrderWhateverTheTeam()
        {
            var living = new[] { new SpectateCandidate(9, 0), new SpectateCandidate(3, 2), new SpectateCandidate(5, 1) };
            CollectionAssert.AreEqual(new[] { 3, 5, 9 }, SpectateRules.SortedActors(living));
            Assert.AreEqual(0, SpectateRules.SortedActors(null).Length);
        }

        // ---- Space: the whole map

        [Test]
        public void TheWholeMapIsCentredOnTheOutlineAndReachesItsFarthestPoint()
        {
            var outline = new List<Vector2> { new Vector2(0, 0), new Vector2(40, 0), new Vector2(40, 20), new Vector2(0, 20) };
            SpectateRules.WholeMapFrame(outline, out Vector2 centre, out float radius);
            Assert.AreEqual(20f, centre.x, 0.001f);
            Assert.AreEqual(10f, centre.y, 0.001f);
            Assert.AreEqual(Mathf.Sqrt(20f * 20f + 10f * 10f), radius, 0.001f);
        }

        [Test]
        public void NoOutlineFramesNothing()
        {
            SpectateRules.WholeMapFrame(null, out Vector2 centre, out float radius);
            Assert.AreEqual(Vector2.zero, centre);
            Assert.AreEqual(0f, radius);
        }

        // A three-lobe arena like the real one: wide at the bottom, a long arm up the middle.
        private static readonly List<Vector2> Lobes = new List<Vector2>
        {
            new Vector2(-60, -40), new Vector2(60, -40), new Vector2(20, 10), new Vector2(8, 90), new Vector2(-8, 90), new Vector2(-20, 10),
        };

        // Puts a real camera where the framing says (tilted down, yaw 0: behind the bottom edge, looking up the map) and projects the outline.
        private static void ProjectOutline(IReadOnlyList<Vector2> outline, float fov, float aspect, float tilt, Vector2 aim, float distance,
            out float minY, out float maxY, out float minX, out float maxX)
        {
            var go = new GameObject("framing test camera");
            try
            {
                Camera cam = go.AddComponent<Camera>();
                cam.fieldOfView = fov;
                cam.aspect = aspect;
                Vector3 target = new Vector3(aim.x, 0f, aim.y);
                float t = tilt * Mathf.Deg2Rad;
                cam.transform.position = target + distance * new Vector3(0f, Mathf.Sin(t), -Mathf.Cos(t));
                cam.transform.LookAt(target);
                minY = minX = float.MaxValue;
                maxY = maxX = float.MinValue;
                foreach (Vector2 p in outline)
                {
                    Vector3 v = cam.WorldToViewportPoint(new Vector3(p.x, 0f, p.y));
                    minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
                    minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        [TestCase(0.88f)]
        [TestCase(0.7f)]
        public void TheWholeMapFillsTheAskedShareOfTheScreenHeightAndIsCentred(float fill)
        {
            SpectateRules.WholeMapFraming(Lobes, 60f, 16f / 9f, 63.4349f, fill, out Vector2 aim, out float distance);
            ProjectOutline(Lobes, 60f, 16f / 9f, 63.4349f, aim, distance, out float minY, out float maxY, out float minX, out float maxX);
            Assert.AreEqual(fill, maxY - minY, 0.01f, "share of the screen height");
            Assert.AreEqual(0.5f, (maxY + minY) * 0.5f, 0.01f, "centred up and down");
            Assert.Greater(minX, 0f);
            Assert.Less(maxX, 1f);
        }

        [Test]
        public void ANarrowWindowKeepsTheWholeWidthOnScreenInsteadOfFillingTheHeight()
        {
            SpectateRules.WholeMapFraming(Lobes, 60f, 0.5f, 63.4349f, 0.88f, out Vector2 aim, out float distance);
            ProjectOutline(Lobes, 60f, 0.5f, 63.4349f, aim, distance, out float minY, out float maxY, out float minX, out float maxX);
            Assert.GreaterOrEqual(minX, 0f);
            Assert.LessOrEqual(maxX, 1f);
            Assert.Less(maxY - minY, 0.88f);
        }

        [Test]
        public void NoOutlineNeedsNoFraming()
        {
            SpectateRules.WholeMapFraming(null, 60f, 16f / 9f, 63.4349f, 0.88f, out Vector2 aim, out float distance);
            Assert.AreEqual(Vector2.zero, aim);
            Assert.AreEqual(0f, distance);
        }

        // ---- the guards: nobody else's game counts a spectator

        [TestCase(false, true, true)]   // a player on a team plays for it
        [TestCase(true, true, false)]   // a spectator never does, even with a stale team
        [TestCase(false, false, false)] // no team yet (seatless, or just arrived)
        [TestCase(true, false, false)]
        public void OnlyAPlayerWithATeamPlaysForIt(bool spectator, bool teamKnown, bool expected) =>
            Assert.AreEqual(expected, Teams.PlaysForTeam(spectator, teamKnown));

        [TestCase(true, false, true)]   // fog on, a player: fog applies
        [TestCase(true, true, false)]   // fog on, a seat spectator: sees everything
        [TestCase(false, false, false)] // fog off in the config: none
        [TestCase(false, true, false)]
        public void ASeatSpectatorSeesEverythingWithNoFog(bool fogSwitchedOn, bool spectator, bool expected) =>
            Assert.AreEqual(expected, VisionRules.FogApplies(fogSwitchedOn, spectator));

        // spectator, onATeam, isMaster, lobbyStage, expected
        [TestCase(false, true, false, 1, true)]    // a player on a team logs
        [TestCase(false, true, false, 2, true)]
        [TestCase(true, false, true, 1, true)]     // a spectator who is the host logs: the master-only lines are the territory timeline
        [TestCase(true, true, true, 2, true)]      // even with a stale team
        [TestCase(true, false, false, 2, false)]   // a spectator who is not the host writes nothing
        [TestCase(true, true, false, 2, false)]    // ... whatever stale team it carries
        [TestCase(false, false, false, 1, false)]  // no role yet
        [TestCase(false, false, true, 1, false)]   // the host with no seat and no team
        [TestCase(false, true, false, 0, false)]   // still in the lobby (stage 0): no file yet
        [TestCase(true, false, true, 0, false)]
        public void WhoOpensATelemetryFile(bool spectator, bool onATeam, bool isMaster, int lobbyStage, bool expected) =>
            Assert.AreEqual(expected, TelemetryRoleRule.MayOpenFile(spectator, onATeam, isMaster, lobbyStage));
    }
}
