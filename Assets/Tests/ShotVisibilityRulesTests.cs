using NUnit.Framework;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Tests
{
    public class ShotVisibilityRulesTests
    {
        // Sight along the x axis between lo and hi (a window), nothing elsewhere.
        static System.Func<Vector3, bool> Window(float lo, float hi) => p => p.x >= lo && p.x <= hi;

        [Test] public void Friendly_IsAlwaysShown() => Assert.IsTrue(ShotVisibilityRules.ShowOwnTeamOrSeen(true, false));
        [Test] public void EnemyUnseenPoint_IsHidden() => Assert.IsFalse(ShotVisibilityRules.ShowOwnTeamOrSeen(false, false));
        [Test] public void EnemySeenPoint_IsShown() => Assert.IsTrue(ShotVisibilityRules.ShowOwnTeamOrSeen(false, true));

        [Test] public void UnknownTeamShooter_CountsAsEnemy() => Assert.IsFalse(ShotVisibilityRules.IsFriendlyTeam(-1, -1));
        [Test] public void NoFriendlyTeam_NobodyIsFriendly() => Assert.IsFalse(ShotVisibilityRules.IsFriendlyTeam(2, -1));
        [Test] public void SameTeam_IsFriendly() => Assert.IsTrue(ShotVisibilityRules.IsFriendlyTeam(2, 2));
        [Test] public void OtherTeam_IsNotFriendly() => Assert.IsFalse(ShotVisibilityRules.IsFriendlyTeam(1, 2));

        [Test]
        public void Line_MiddleCrossesSight_IsSeenEvenIfBothEndsAreNot()
        {
            Assert.IsTrue(ShotVisibilityRules.LineSeen(new Vector3(0, 0, 0), new Vector3(10, 0, 0), 1f, Window(4f, 6f)));
        }

        [Test]
        public void Line_EntirelyInFog_IsNotSeen()
        {
            Assert.IsFalse(ShotVisibilityRules.LineSeen(new Vector3(0, 0, 0), new Vector3(10, 0, 0), 1f, Window(20f, 30f)));
        }

        [Test]
        public void Line_SeenOnlyAtTheEndPoint_IsSeen()
        {
            Assert.IsTrue(ShotVisibilityRules.LineSeen(new Vector3(0, 0, 0), new Vector3(10, 0, 0), 1f, Window(9.9f, 11f)));
        }

        [Test]
        public void Line_SeenOnlyAtTheStartPoint_IsSeen()
        {
            Assert.IsTrue(ShotVisibilityRules.LineSeen(new Vector3(0, 0, 0), new Vector3(10, 0, 0), 1f, Window(-1f, 0.1f)));
        }

        [Test]
        public void Line_SpacingIsRespected_NarrowWindowCaughtOnlyByFineSpacing()
        {
            var window = Window(4.1f, 4.6f); // 0.5 m wide, between the 1 m samples (4 and 5)
            Assert.IsFalse(ShotVisibilityRules.LineSeen(new Vector3(0, 0, 0), new Vector3(10, 0, 0), 1f, window));
            Assert.IsTrue(ShotVisibilityRules.LineSeen(new Vector3(0, 0, 0), new Vector3(10, 0, 0), 0.25f, window));
        }

        [Test]
        public void Line_ZeroLength_IsOneSample()
        {
            int calls = 0;
            bool seen = ShotVisibilityRules.LineSeen(Vector3.one, Vector3.one, 1f, p => { calls++; return true; });
            Assert.IsTrue(seen);
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Line_StopsAtTheFirstSeenSample()
        {
            int calls = 0;
            ShotVisibilityRules.LineSeen(Vector3.zero, new Vector3(10, 0, 0), 1f, p => { calls++; return true; });
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Line_NonPositiveSpacing_DoesNotHangAndStillSamplesBothEnds()
        {
            Assert.IsTrue(ShotVisibilityRules.LineSeen(Vector3.zero, new Vector3(10, 0, 0), 0f, Window(9.9f, 11f)));
        }

        [Test] public void Blast_Friendly_IsShown() => Assert.IsTrue(ShotVisibilityRules.BlastShown(true, false, false));
        [Test] public void Blast_EnemyCentreSeen_IsShown() => Assert.IsTrue(ShotVisibilityRules.BlastShown(false, true, false));
        [Test] public void Blast_EnemyUnseenAndNobodyReached_IsHidden() => Assert.IsFalse(ShotVisibilityRules.BlastShown(false, false, false));
        [Test] public void Blast_EnemyUnseenButReachesMyTeam_IsShown() => Assert.IsTrue(ShotVisibilityRules.BlastShown(false, false, true));

        [Test]
        public void ReachesAny_OneTeammateInside_IsTrue()
        {
            var team = new System.Collections.Generic.List<Vector3> { new Vector3(50, 0, 0), new Vector3(2, 0, 0) };
            Assert.IsTrue(ShotVisibilityRules.ReachesAny(Vector3.zero, 3f, team));
        }

        [Test]
        public void ReachesAny_AllOutside_IsFalse()
        {
            var team = new System.Collections.Generic.List<Vector3> { new Vector3(50, 0, 0), new Vector3(10, 0, 0) };
            Assert.IsFalse(ShotVisibilityRules.ReachesAny(Vector3.zero, 3f, team));
        }

        [Test] public void ReachesAny_ExactlyAtTheRim_IsTrue() =>
            Assert.IsTrue(ShotVisibilityRules.ReachesAny(Vector3.zero, 3f, new[] { new Vector3(3, 0, 0) }));
        [Test] public void ReachesAny_NoOne_IsFalse() =>
            Assert.IsFalse(ShotVisibilityRules.ReachesAny(Vector3.zero, 3f, new Vector3[0]));

        // A shot is already in the air, so its wall test aims at the eye's own height, not lifted again.
        [Test] public void ShotTarget_IsTheEyesOwnHeight() => Assert.AreEqual(1.5f, ShotVisibilityRules.TargetHeight(true, 1.5f, 2f, 1f), 1e-5f);
        [Test] public void GroundTarget_IsLiftedByEyeHeight() => Assert.AreEqual(3f, ShotVisibilityRules.TargetHeight(false, 1.5f, 2f, 1f), 1e-5f);

        // ---- disc (zones, fire fields), body reach, cone reach (Task 5 review fixes) ----

        [Test] public void Disc_CentreSeen_IsSeen() =>
            Assert.IsTrue(ShotVisibilityRules.DiscSeen(new Vector3(5, 0, 0), 3f, 8, p => Vector3.Distance(p, new Vector3(5, 0, 0)) < 0.01f));

        [Test]
        public void Disc_OnlyTheRimOverlapsSight_IsSeen()
        {
            // Centre at x=10 is outside the window (x<=8), but the rim point at x=7 is inside it.
            Assert.IsTrue(ShotVisibilityRules.DiscSeen(new Vector3(10, 0, 0), 3f, 8, Window(float.NegativeInfinity, 8f)));
        }

        [Test] public void Disc_NothingSeen_IsHidden() =>
            Assert.IsFalse(ShotVisibilityRules.DiscSeen(new Vector3(10, 0, 0), 3f, 8, p => false));

        [Test]
        public void Disc_StopsAtTheFirstSeenSample()
        {
            int calls = 0;
            ShotVisibilityRules.DiscSeen(new Vector3(0, 0, 0), 3f, 8, p => { calls++; return true; });
            Assert.AreEqual(1, calls, "the centre is tried first and seen");
        }

        [Test]
        public void Disc_RimPointsLieOnTheRimAtTheCentresHeight()
        {
            var points = new System.Collections.Generic.List<Vector3>();
            ShotVisibilityRules.DiscSeen(new Vector3(1, 2, 3), 4f, 8, p => { points.Add(p); return false; });
            Assert.AreEqual(9, points.Count, "centre plus 8 rim points");
            for (int i = 1; i < points.Count; i++)
            {
                Assert.AreEqual(2f, points[i].y, 1e-4f);
                Assert.AreEqual(4f, Vector2.Distance(new Vector2(1, 3), new Vector2(points[i].x, points[i].z)), 1e-3f);
            }
        }

        [Test] public void Disc_ZeroRadius_IsJustTheCentre()
        {
            int calls = 0;
            Assert.IsFalse(ShotVisibilityRules.DiscSeen(new Vector3(0, 0, 0), 0f, 8, p => { calls++; return false; }));
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void Body_BurstAtChestHeight_ReachesATeammateWhoseFeetAreOutOfRange()
        {
            // Burst 2 m up, radius 1.5: the feet (2 m away) are out, the chest at 1.4 m is 0.6 m away.
            var feet = new[] { new Vector3(0, 0, 0) };
            Assert.IsFalse(ShotVisibilityRules.ReachesAny(new Vector3(0, 2, 0), 1.5f, feet));
            Assert.IsTrue(ShotVisibilityRules.ReachesAnyBody(new Vector3(0, 2, 0), 1.5f, feet, 1.4f));
        }

        [Test]
        public void Body_BurstFarAway_DoesNotReach() =>
            Assert.IsFalse(ShotVisibilityRules.ReachesAnyBody(new Vector3(10, 1, 0), 2f, new[] { new Vector3(0, 0, 0) }, 1.4f));

        [Test]
        public void Body_BurstBelowTheFeet_MeasuresToTheFeet() =>
            Assert.IsFalse(ShotVisibilityRules.ReachesAnyBody(new Vector3(0, -3, 0), 2f, new[] { new Vector3(0, 0, 0) }, 1.4f));

        [Test]
        public void Body_RimCounts() =>
            Assert.IsTrue(ShotVisibilityRules.ReachesAnyBody(new Vector3(2, 0.5f, 0), 2f, new[] { new Vector3(0, 0, 0) }, 1.4f));

        [Test] public void Cone_PointInFrontInsideRangeAndAngle_IsReached() =>
            Assert.IsTrue(ShotVisibilityRules.ConeReaches(Vector3.zero, Vector3.forward, 7f, 45f, new Vector3(0.5f, 0, 5f)));

        [Test] public void Cone_PointBeyondRange_IsNotReached() =>
            Assert.IsFalse(ShotVisibilityRules.ConeReaches(Vector3.zero, Vector3.forward, 7f, 45f, new Vector3(0, 0, 8f)));

        [Test] public void Cone_PointOutsideTheAngle_IsNotReached() =>
            Assert.IsFalse(ShotVisibilityRules.ConeReaches(Vector3.zero, Vector3.forward, 7f, 45f, new Vector3(5f, 0, 5f)));

        [Test] public void Cone_PointBehind_IsNotReached() =>
            Assert.IsFalse(ShotVisibilityRules.ConeReaches(Vector3.zero, Vector3.forward, 7f, 45f, new Vector3(0, 0, -1f)));

        [Test] public void Cone_PointAtTheApex_IsReached() =>
            Assert.IsTrue(ShotVisibilityRules.ConeReaches(Vector3.zero, Vector3.forward, 7f, 45f, new Vector3(0, 1f, 0)));

        [Test] public void Cone_HeightIsIgnored() =>
            Assert.IsTrue(ShotVisibilityRules.ConeReaches(Vector3.zero, Vector3.forward, 7f, 45f, new Vector3(0, 9f, 3f)));
    }
}
