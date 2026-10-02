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

        [Test]
        public void TheWholeMapDistanceKeepsTheNearEdgeOfTheMapOnScreen()
        {
            // 50 m radius, 60 degree vertical view, 16:9, looking down 63.4 degrees (the camera's 10 up, 5 back): the tilted
            // near edge of the circle is the tight one (about 99.8 m), not the width (about 48.7 m).
            float distance = SpectateRules.WholeMapDistance(50f, 60f, 16f / 9f, 63.4349f, 1f);
            Assert.AreEqual(99.82f, distance, 0.05f);
        }

        [Test]
        public void ANarrowWindowNeedsTheWidthToFit()
        {
            float wide = SpectateRules.WholeMapDistance(50f, 60f, 16f / 9f, 63.4349f, 1f);
            float narrow = SpectateRules.WholeMapDistance(50f, 60f, 0.4f, 63.4349f, 1f);
            Assert.Greater(narrow, wide);
        }

        [Test]
        public void TheMarginScalesTheDistanceAndNoMapNeedsNoDistance()
        {
            float plain = SpectateRules.WholeMapDistance(50f, 60f, 16f / 9f, 63.4349f, 1f);
            Assert.AreEqual(plain * 1.2f, SpectateRules.WholeMapDistance(50f, 60f, 16f / 9f, 63.4349f, 1.2f), 0.01f);
            Assert.AreEqual(0f, SpectateRules.WholeMapDistance(0f, 60f, 16f / 9f, 63.4349f, 1.1f));
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

        [TestCase(false, true, true)]    // a player on a team logs
        [TestCase(true, true, false)]    // a spectator logs no player row
        [TestCase(false, false, false)]  // still in the lobby: no role yet, no row
        public void OnlyAPlayerOnATeamOpensATelemetryFile(bool spectator, bool onATeam, bool expected) =>
            Assert.AreEqual(expected, TelemetryRoleRule.MayOpenFile(spectator, onATeam));
    }
}
