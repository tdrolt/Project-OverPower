using NUnit.Framework;
using Overpower.Dominion;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 1: the sudden-death circle and who is left standing. Literal numbers, never the tuning asset.</summary>
    public class SuddenDeathRulesTests
    {
        private const float Shrink = 10f, Start = 50f, Final = 5f;

        [Test] public void TheCircleStartsAtTheStartRadius() => Assert.AreEqual(Start, SuddenDeathRules.Radius(1000, 1000, Shrink, Start, Final), 0.001f);
        [Test] public void BeforeSuddenDeathStartsItIsTheStartRadius() => Assert.AreEqual(Start, SuddenDeathRules.Radius(1000, 200, Shrink, Start, Final), 0.001f);
        [Test] public void HalfwayThroughItIsHalfwayBetween() => Assert.AreEqual(27.5f, SuddenDeathRules.Radius(1000, 6000, Shrink, Start, Final), 0.001f);
        [Test] public void ItReachesTheFinalRadiusAtTheShrinkTimeAndStaysThere()
        {
            Assert.AreEqual(Final, SuddenDeathRules.Radius(1000, 11000, Shrink, Start, Final), 0.001f);
            Assert.AreEqual(Final, SuddenDeathRules.Radius(1000, 99000, Shrink, Start, Final), 0.001f);
        }

        [Test] public void TheCircleNeverGrowsAsTimePasses()
        {
            float last = float.MaxValue;
            for (int now = 0; now <= 20000; now += 250)
            {
                float r = SuddenDeathRules.Radius(1000, now, Shrink, Start, Final);
                Assert.LessOrEqual(r, last + 0.0001f, "grew at " + now);
                last = r;
            }
        }

        [Test] public void AFinalRadiusLargerThanTheStartDoesNotMakeItGrow() =>
            Assert.LessOrEqual(SuddenDeathRules.Radius(0, 5000, Shrink, 10f, 30f), 10f);

        [Test] public void AZeroShrinkTimeJumpsStraightToTheFinalRadius() =>
            Assert.AreEqual(Final, SuddenDeathRules.Radius(0, 100, 0f, Start, Final), 0.001f);

        [Test] public void TheRadiusWorksAcrossTheIntWrap()
        {
            int start = int.MaxValue - 4000;
            Assert.AreEqual(27.5f, SuddenDeathRules.Radius(start, unchecked(start + 5000), Shrink, Start, Final), 0.001f);
        }

        [Test] public void OnTheEdgeCountsAsInside()
        {
            Assert.IsFalse(SuddenDeathRules.IsOutside(new Vector2(3f, 4f), Vector2.zero, 5f));
            Assert.IsTrue(SuddenDeathRules.IsOutside(new Vector2(3f, 4.01f), Vector2.zero, 5f));
            Assert.IsFalse(SuddenDeathRules.IsOutside(new Vector2(10f, 10f), new Vector2(10f, 10f), 0f));
        }

        [Test] public void OnlyOneSuddenDeathTeamWithAnyoneAliveWins()
        {
            int[] alive = { 0, 2, 0 };
            Assert.AreEqual(1, SuddenDeathRules.LastTeamStanding(alive, new[] { 0, 1 }));
            SuddenDeathResult r = SuddenDeathRules.Evaluate(alive, new[] { 0, 1 });
            Assert.AreEqual(SuddenDeathState.Won, r.State);
            Assert.AreEqual(1, r.Team);
        }

        [Test] public void TwoTeamsStillAliveMeansItGoesOn()
        {
            int[] alive = { 1, 3, 0 };
            Assert.AreEqual(-1, SuddenDeathRules.LastTeamStanding(alive, new[] { 0, 1 }));
            Assert.AreEqual(SuddenDeathState.Ongoing, SuddenDeathRules.Evaluate(alive, new[] { 0, 1 }).State);
        }

        [Test] public void ATeamNotInSuddenDeathNeverWinsIt()
        {
            int[] alive = { 0, 0, 4 }; // team 2 is alive but sat this out
            Assert.AreEqual(-1, SuddenDeathRules.LastTeamStanding(alive, new[] { 0, 1 }));
            SuddenDeathResult r = SuddenDeathRules.Evaluate(alive, new[] { 0, 1 });
            Assert.AreNotEqual(SuddenDeathState.Won, r.State);
        }

        [Test] public void NobodyLeftAmongTheSuddenDeathTeamsMeansTheRoundIsReplayedNotWon()
        {
            // Tudor A8: the last players die in the same instant -> sudden death starts over; no points tie-break.
            int[] alive = { 0, 0, 3 };
            Assert.IsTrue(SuddenDeathRules.NobodyLeft(alive, new[] { 0, 1 }));
            Assert.AreEqual(-1, SuddenDeathRules.LastTeamStanding(alive, new[] { 0, 1 }));
            Assert.AreEqual(SuddenDeathState.Replay, SuddenDeathRules.Evaluate(alive, new[] { 0, 1 }).State);
        }

        [Test] public void NobodyLeftIsFalseWhileAnySuddenDeathTeamHasSomeone() =>
            Assert.IsFalse(SuddenDeathRules.NobodyLeft(new[] { 1, 0 }, new[] { 0, 1 }));

        [Test] public void AMissingOrEmptyListNeverDecidesAnything()
        {
            Assert.AreEqual(SuddenDeathState.Ongoing, SuddenDeathRules.Evaluate(null, new[] { 0, 1 }).State);
            Assert.AreEqual(SuddenDeathState.Ongoing, SuddenDeathRules.Evaluate(new[] { 1, 1 }, new int[0]).State);
        }
    }
}
