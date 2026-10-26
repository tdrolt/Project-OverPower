using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Tests
{
    public class VisionRulesTests
    {
        // Literal test values, not the game's tuning.
        static readonly SightShape Shape = new SightShape(90f, 20f, 5f);
        static Eye EyeFacingForward() => new Eye(Vector2.zero, Vector2.up, Shape);
        static bool Clear(Vector2 a, Vector2 b) => true;
        static bool Blocked(Vector2 a, Vector2 b) => false;

        [Test] public void InShape_PointAheadWithinLength_IsSeen() => Assert.IsTrue(VisionRules.InShape(EyeFacingForward(), new Vector2(0, 19.9f)));
        [Test] public void InShape_PointAheadPastLength_IsNotSeen() => Assert.IsFalse(VisionRules.InShape(EyeFacingForward(), new Vector2(0, 20.1f)));

        [Test] public void InShape_JustInsideHalfAngle_IsSeen()
        {
            float a = 44f * Mathf.Deg2Rad;
            Assert.IsTrue(VisionRules.InShape(EyeFacingForward(), new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * 10f));
        }

        [Test] public void InShape_JustOutsideHalfAngle_IsNotSeen()
        {
            float a = 46f * Mathf.Deg2Rad;
            Assert.IsFalse(VisionRules.InShape(EyeFacingForward(), new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * 10f));
        }

        [Test] public void InShape_BehindInsideCircle_IsSeen() => Assert.IsTrue(VisionRules.InShape(EyeFacingForward(), new Vector2(0, -4.9f)));
        [Test] public void InShape_BehindOutsideCircle_IsNotSeen() => Assert.IsFalse(VisionRules.InShape(EyeFacingForward(), new Vector2(0, -5.1f)));

        [Test] public void InShape_ZeroFacing_SeesOnlyTheCircle()
        {
            var eye = new Eye(Vector2.zero, Vector2.zero, Shape);
            Assert.IsTrue(VisionRules.InShape(eye, new Vector2(0, 4.9f)));
            Assert.IsFalse(VisionRules.InShape(eye, new Vector2(0, 10f)));
        }

        [Test] public void ShapeFor_Unscoped_UsesNormalValues()
        {
            var s = VisionRules.ShapeFor(90f, 20f, 5f, false, 30f, 40f, -2f);
            Assert.AreEqual(90f, s.ConeAngleDegrees); Assert.AreEqual(20f, s.ConeLength); Assert.AreEqual(5f, s.CircleRadius);
        }

        [Test] public void ShapeFor_Scoped_IsNarrowerLongerAndSmallerCircle()
        {
            var n = VisionRules.ShapeFor(90f, 20f, 5f, false, 30f, 40f, -2f);
            var s = VisionRules.ShapeFor(90f, 20f, 5f, true, 30f, 40f, -2f);
            Assert.Less(s.ConeAngleDegrees, n.ConeAngleDegrees);
            Assert.Greater(s.ConeLength, n.ConeLength);
            Assert.Less(s.CircleRadius, n.CircleRadius);
            Assert.AreEqual(30f, s.ConeAngleDegrees);
            Assert.AreEqual(40f, s.ConeLength);
            Assert.AreEqual(3f, s.CircleRadius);
        }

        [Test] public void ShapeFor_CircleNeverGoesNegative() =>
            Assert.AreEqual(0f, VisionRules.ShapeFor(90f, 20f, 5f, true, 30f, 40f, -50f).CircleRadius);

        [Test] public void TeamSees_BlockedLine_HidesPointInCone() =>
            Assert.IsFalse(VisionRules.TeamSees(new[] { EyeFacingForward() }, new Vector2(0, 10), Blocked));

        [Test] public void TeamSees_ClearLineInCone_Sees() =>
            Assert.IsTrue(VisionRules.TeamSees(new[] { EyeFacingForward() }, new Vector2(0, 10), Clear));

        [Test] public void TeamSees_ClearLineButPointOutsideEveryShape_DoesNotSee() =>
            Assert.IsFalse(VisionRules.TeamSees(new[] { EyeFacingForward() }, new Vector2(0, -30), Clear));

        [Test] public void TeamSees_AnyOneEyeIsEnough()
        {
            var far = new Eye(new Vector2(100, 100), Vector2.up, Shape);
            Assert.IsTrue(VisionRules.TeamSees(new List<Eye> { far, EyeFacingForward() }, new Vector2(0, 10), Clear));
        }

        [Test] public void TeamSees_NoEyes_SeesNothing() =>
            Assert.IsFalse(VisionRules.TeamSees(new List<Eye>(), Vector2.zero, Clear));

        [Test] public void IsEye_Alive_MeAndLivingTeammates()
        {
            Assert.IsTrue(VisionRules.IsEye(0, ViewerMode.Alive, -1, 0, true, true));
            Assert.IsTrue(VisionRules.IsEye(0, ViewerMode.Alive, -1, 0, true, false));
        }
        [Test] public void IsEye_Alive_EnemyNever() => Assert.IsFalse(VisionRules.IsEye(0, ViewerMode.Alive, -1, 1, true, false));
        [Test] public void IsEye_Alive_DeadTeammateNever() => Assert.IsFalse(VisionRules.IsEye(0, ViewerMode.Alive, -1, 0, false, false));
        [Test] public void IsEye_Dead_TeammatesButNotMe()
        {
            Assert.IsTrue(VisionRules.IsEye(0, ViewerMode.Dead, -1, 0, true, false));
            Assert.IsFalse(VisionRules.IsEye(0, ViewerMode.Dead, -1, 0, false, true));
            Assert.IsFalse(VisionRules.IsEye(0, ViewerMode.Dead, -1, 0, true, true));
        }
        [Test] public void IsEye_Spectating_WatchedTeamLivingOnly()
        {
            Assert.IsTrue(VisionRules.IsEye(0, ViewerMode.Spectating, 1, 1, true, false));
            Assert.IsFalse(VisionRules.IsEye(0, ViewerMode.Spectating, 1, 1, false, false));
            Assert.IsFalse(VisionRules.IsEye(0, ViewerMode.Spectating, 1, 0, true, false));
        }
        [Test] public void IsEye_TeamUnknown_OnlyMe()
        {
            Assert.IsTrue(VisionRules.IsEye(-1, ViewerMode.Alive, -1, -1, true, true));
            Assert.IsFalse(VisionRules.IsEye(-1, ViewerMode.Alive, -1, 0, true, false));
            Assert.IsFalse(VisionRules.IsEye(-1, ViewerMode.Alive, -1, -1, false, true));
            Assert.IsFalse(VisionRules.IsEye(-1, ViewerMode.Alive, -1, -1, true, false));
        }
        [Test] public void IsEye_Spectating_UnknownOwnTeam_StillSeesThroughWatchedTeam() =>
            Assert.IsTrue(VisionRules.IsEye(-1, ViewerMode.Spectating, 1, 1, true, false));
        [Test] public void IsEye_Spectating_NoWatchedTeam_Nobody()
        {
            Assert.IsFalse(VisionRules.IsEye(0, ViewerMode.Spectating, -1, 0, true, false));
            Assert.IsFalse(VisionRules.IsEye(-1, ViewerMode.Spectating, -1, -1, true, false));
        }
        [Test] public void IsEye_Dead_UnknownOwnTeam_Nobody()
        {
            Assert.IsFalse(VisionRules.IsEye(-1, ViewerMode.Dead, -1, -1, true, false));
            Assert.IsFalse(VisionRules.IsEye(-1, ViewerMode.Dead, -1, -1, true, true));
        }
    }
}
