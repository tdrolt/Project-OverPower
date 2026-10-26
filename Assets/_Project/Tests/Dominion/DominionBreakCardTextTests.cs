using NUnit.Framework;
using Overpower.Dominion;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>The words on the break card: the shared-round line, the match score and MATCH POINT, and the pulse of the winners' dots.
    /// Literal formats and colours, never the theme's.</summary>
    public class DominionBreakCardTextTests
    {
        private static readonly string[] Names = { "White", "Purple", "Cyan" };
        private static readonly string[] Hex = { "F4F2ED", "9C7BE0", "4FD3DE" };
        private static readonly int[] Two = { 0, 1 };
        private static readonly int[] Three = { 0, 1, 2 };
        private const string Sep = ", ";
        private const string And = " and ";

        private static string C(string hex, string text) => "<color=#" + hex + ">" + text + "</color>";

        [TestCase(false, 3, -1, DominionHudText.PulseStep.Stop)]
        [TestCase(false, 3, 3, DominionHudText.PulseStep.Stop)]
        [TestCase(true, 3, 3, DominionHudText.PulseStep.Keep)]
        [TestCase(true, 3, -1, DominionHudText.PulseStep.Start)]
        [TestCase(true, 4, 3, DominionHudText.PulseStep.Start)]
        public void ARedrawOfTheBreakCardStopsKeepsOrStartsThePulse(bool shared, int round, int pulsedRound, DominionHudText.PulseStep expected) =>
            Assert.AreEqual(expected, DominionHudText.PulseStepOnRedraw(shared, round, pulsedRound));

        [Test] public void TheBreakCardAsksThePulseRuleOnEveryRedraw() =>
            Assert.IsTrue(IlWiring.Uses(typeof(BreakCard), "Refresh", typeof(DominionHudText).GetMethod(nameof(DominionHudText.PulseStepOnRedraw))));

        [Test] public void NoTeamListMeansNoWinnersAtTheBuzzer()
        {
            foreach (bool overtime in new[] { false, true })
            {
                DominionRules.BuzzerResult r = DominionRules.AtBuzzer(new[] { 800, 100 }, null, 100, overtime);
                Assert.AreEqual(0, r.Winners.Length);
                Assert.IsNull(r.OvertimeTeams);
            }
        }

        // ---------------------------------------------------------------- the shared-round line

        [Test] public void TwoSharedWinnersAreJoinedWithAndEachInItsColour() =>
            Assert.AreEqual(C("F4F2ED", "WHITE") + " and " + C("9C7BE0", "PURPLE") + " each get a round win",
                DominionHudText.SharedRoundLine("{0} each get a round win", Two, Names, Hex, Sep, And));

        [Test] public void ThreeSharedWinnersAreCommaSeparatedWithAndBeforeTheLast() =>
            Assert.AreEqual(C("F4F2ED", "WHITE") + ", " + C("9C7BE0", "PURPLE") + " and " + C("4FD3DE", "CYAN") + " each get a round win",
                DominionHudText.SharedRoundLine("{0} each get a round win", Three, Names, Hex, Sep, And));

        [Test] public void WithoutColoursTheNamesAreBare() =>
            Assert.AreEqual("WHITE and CYAN each get a round win",
                DominionHudText.SharedRoundLine("{0} each get a round win", new[] { 0, 2 }, Names, null, Sep, And));

        [Test] public void ABrokenSharedLineFormatFallsBackToTheRawTextNotAnException() =>
            Assert.DoesNotThrow(() => DominionHudText.SharedRoundLine("{0} each {", Two, Names, Hex, Sep, And));

        // ---------------------------------------------------------------- the match score

        [Test] public void ATwoTeamScoreReadsNameNumberDashNumberName() =>
            Assert.AreEqual(C("F4F2ED", "WHITE 1") + " - " + C("9C7BE0", "1 PURPLE"), DominionHudText.MatchScoreLine(Two, new[] { 1, 1, 0 }, Names, Hex, " - "));

        [Test] public void AThreeTeamScoreNamesEveryNumber() =>
            Assert.AreEqual(C("F4F2ED", "WHITE 1") + " - " + C("9C7BE0", "PURPLE 1") + " - " + C("4FD3DE", "CYAN 0"),
                DominionHudText.MatchScoreLine(Three, new[] { 1, 1, 0 }, Names, Hex, " - "));

        [Test] public void AScoreWithoutColoursIsPlainText() =>
            Assert.AreEqual("WHITE 2 - 0 PURPLE", DominionHudText.MatchScoreLine(Two, new[] { 2, 0, 0 }, Names, null, " - "));

        [Test] public void AWinsArrayShorterThanTheTeamsReadsMissingTeamsAsZero() =>
            Assert.AreEqual("WHITE 1 - 0 PURPLE", DominionHudText.MatchScoreLine(Two, new[] { 1 }, Names, null, " - "));

        // ---------------------------------------------------------------- match point

        [Test] public void EveryTeamOneRoundWinFromTheMatchIsOnMatchPoint()
        {
            CollectionAssert.AreEqual(new[] { 0 }, DominionHudText.MatchPointTeams(Three, new[] { 1, 0, 0 }, 2));
            CollectionAssert.AreEqual(new[] { 0, 1 }, DominionHudText.MatchPointTeams(Three, new[] { 1, 1, 0 }, 2));
        }

        [Test] public void NobodyOnMatchPointAtTheStartOrWithATeamAlreadyThere()
        {
            Assert.IsEmpty(DominionHudText.MatchPointTeams(Two, new[] { 0, 0, 0 }, 2));
            Assert.IsEmpty(DominionHudText.MatchPointTeams(Two, new[] { 2, 0, 0 }, 2), "a team at the target has won, it is not a match point");
        }

        [Test] public void MatchPointFollowsTheRoundsToWin() =>
            CollectionAssert.AreEqual(new[] { 1 }, DominionHudText.MatchPointTeams(Three, new[] { 1, 2, 0 }, 3));

        [Test] public void TheMatchPointLineNamesOneTeamOrSeveralWithCommas()
        {
            Assert.AreEqual("MATCH POINT: " + C("F4F2ED", "WHITE"), DominionHudText.MatchPointLine("MATCH POINT: {0}", new[] { 0 }, Names, Hex, ", "));
            Assert.AreEqual("MATCH POINT: " + C("F4F2ED", "WHITE") + ", " + C("9C7BE0", "PURPLE"),
                DominionHudText.MatchPointLine("MATCH POINT: {0}", new[] { 0, 1 }, Names, Hex, ", "));
        }

        [Test] public void NoTeamOnMatchPointMeansNoLine() =>
            Assert.AreEqual("", DominionHudText.MatchPointLine("MATCH POINT: {0}", new int[0], Names, Hex, ", "));

        // ---------------------------------------------------------------- the pulse

        [Test] public void ThePulseStartsAtNormalSizePeaksInTheMiddleAndEndsAtNormalSize()
        {
            Assert.AreEqual(1f, DominionHudText.PulseScale(0f, 0.6f, 1.5f), 0.001f);
            Assert.AreEqual(1.5f, DominionHudText.PulseScale(0.3f, 0.6f, 1.5f), 0.001f);
            Assert.AreEqual(1f, DominionHudText.PulseScale(0.6f, 0.6f, 1.5f), 0.001f);
        }

        [Test] public void ThePulseIsNormalSizeBeforeItAndAfterItAndWithoutALength()
        {
            Assert.AreEqual(1f, DominionHudText.PulseScale(-1f, 0.6f, 1.5f));
            Assert.AreEqual(1f, DominionHudText.PulseScale(5f, 0.6f, 1.5f));
            Assert.AreEqual(1f, DominionHudText.PulseScale(0.1f, 0f, 1.5f));
        }

        [Test] public void OnlyTheNewestWinDotOfEachSharedWinnerPulses()
        {
            int[] winners = { 0, 1 };
            Assert.IsTrue(DominionHudText.PulsesDot(winners, 0, 0, 1));
            Assert.IsTrue(DominionHudText.PulsesDot(winners, 1, 1, 2));
            Assert.IsFalse(DominionHudText.PulsesDot(winners, 1, 0, 2), "an older win");
            Assert.IsFalse(DominionHudText.PulsesDot(winners, 1, 2, 2), "an empty dot");
            Assert.IsFalse(DominionHudText.PulsesDot(winners, 2, 0, 1), "a team that did not share");
            Assert.IsFalse(DominionHudText.PulsesDot(null, 0, 0, 1));
        }

        // ---------------------------------------------------------------- the card calls the rules

        private static System.Reflection.MethodInfo Rule(string name) => typeof(DominionHudText).GetMethod(name);

        [Test] public void TheBreakCardBuildsItsLinesThroughTheTestedRules()
        {
            foreach (string rule in new[] { nameof(DominionHudText.SharedRoundLine), nameof(DominionHudText.MatchScoreLine), nameof(DominionHudText.MatchPointTeams),
                                            nameof(DominionHudText.MatchPointLine), nameof(DominionHudText.PulseScale), nameof(DominionHudText.PulsesDot) })
                Assert.IsTrue(IlWiring.Uses(typeof(BreakCard), Rule(rule)), rule);
        }
    }
}
