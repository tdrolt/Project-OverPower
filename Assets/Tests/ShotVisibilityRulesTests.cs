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
    }
}
