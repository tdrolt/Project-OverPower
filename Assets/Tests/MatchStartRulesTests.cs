using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class MatchStartRulesTests
    {
        [Test]
        public void TheCountdownStartsTheMomentAllThreeTeamsHaveAPlayer()
        {
            Assert.IsFalse(MatchStartRules.StartsCountdownAutomatically(teamsFixed: false, new[] { 1, 1, 0 }));
            Assert.IsTrue(MatchStartRules.StartsCountdownAutomatically(false, new[] { 1, 1, 1 }));
            Assert.IsTrue(MatchStartRules.StartsCountdownAutomatically(false, new[] { 3, 2, 1 }));
            Assert.IsFalse(MatchStartRules.StartsCountdownAutomatically(true, new[] { 1, 1, 1 }), "a countdown or a live match already fixed the teams");
        }

        [Test]
        public void TheHostMayStartOnlyWithExactlyTwoTeams()
        {
            Assert.IsTrue(MatchStartRules.HostMayStart(teamsFixed: false, new[] { 1, 0, 2 }, playersWithoutATeam: 0));
            Assert.IsFalse(MatchStartRules.HostMayStart(false, new[] { 2, 0, 0 }, 0), "one team: nobody to play");
            Assert.IsFalse(MatchStartRules.HostMayStart(false, new[] { 1, 1, 1 }, 0), "three teams start on their own");
            Assert.IsFalse(MatchStartRules.HostMayStart(true, new[] { 1, 1, 0 }, 0), "already counting down or live");
            Assert.IsFalse(MatchStartRules.HostMayStart(false, new[] { 1, 1, 0 }, 1), "someone still joining may be the third team");
        }

        [Test]
        public void TheCountdownEndsItsLengthAfterItStartsEvenAcrossTheClockWrap()
        {
            Assert.AreEqual(15000, MatchStartRules.CountdownEndsAt(nowMs: 10000, countdownSeconds: 5f));
            Assert.AreEqual(int.MinValue + 4999, MatchStartRules.CountdownEndsAt(int.MaxValue, 5f));
            Assert.AreEqual(10000, MatchStartRules.CountdownEndsAt(10000, 0f), "0 s: live on the master's next frame");
            Assert.AreEqual(10000, MatchStartRules.CountdownEndsAt(10000, -3f), "a negative length is treated as 0");
        }

        [Test]
        public void TheMasterGoesLiveOnceItsClockReachesTheMoment()
        {
            Assert.IsFalse(MatchStartRules.HasReached(nowMs: 14999, momentMs: 15000));
            Assert.IsTrue(MatchStartRules.HasReached(15000, 15000));
            Assert.IsTrue(MatchStartRules.HasReached(15001, 15000));
            Assert.IsTrue(MatchStartRules.HasReached(int.MinValue + 10, int.MaxValue - 10), "across the wrap");
        }

        [Test]
        public void TheCountdownShowsWholeSecondsAndNeverZeroBeforeLive()
        {
            Assert.AreEqual(5, MatchStartRules.CountdownSecondsShown(nowMs: 10000, liveAtMs: 15000));
            Assert.AreEqual(5, MatchStartRules.CountdownSecondsShown(10001, 15000));
            Assert.AreEqual(1, MatchStartRules.CountdownSecondsShown(14999, 15000));
            Assert.AreEqual(1, MatchStartRules.CountdownSecondsShown(15500, 15000), "past the moment, live write not arrived yet: hold at 1");
        }

        [Test]
        public void ACountdownIsCancelledTheMomentATeamInItEmpties()
        {
            Assert.IsFalse(MatchStartRules.CountdownShouldCancel(new[] { 0, 1, 2 }, new[] { 1, 2, 1 }));
            Assert.IsTrue(MatchStartRules.CountdownShouldCancel(new[] { 0, 1, 2 }, new[] { 1, 0, 1 }));
            Assert.IsFalse(MatchStartRules.CountdownShouldCancel(new[] { 0, 1 }, new[] { 1, 1, 0 }), "the left-out team being empty is expected");
            Assert.IsTrue(MatchStartRules.CountdownShouldCancel(new[] { 0, 1 }, new[] { 0, 1, 3 }), "players on the left-out team never save a host start whose team left");
        }

        [Test]
        public void TheStartStateFollowsWhatTheRoomHolds()
        {
            Assert.AreEqual(StartState.Warmup, MatchStartRules.StartStateFor(teamsFixed: false, phaseWritten: false));
            Assert.AreEqual(StartState.CountingDown, MatchStartRules.StartStateFor(true, false));
            Assert.AreEqual(StartState.Live, MatchStartRules.StartStateFor(true, true));
        }

        [Test]
        public void TheTeamsInTheMatchAreTheTeamsWithPlayersAtThatMoment()
        {
            CollectionAssert.AreEqual(new[] { 0, 2 }, MatchStartRules.TeamsWithPlayers(new[] { 2, 0, 1 }));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, MatchStartRules.TeamsWithPlayers(new[] { 1, 1, 1 }));
        }

        [Test]
        public void OnlyTheCapitalOfATeamLeftOutOfALiveMatchIsOutOfPlay()
        {
            var inMatch = new[] { 0, 1 };
            Assert.IsFalse(MatchStartRules.IsCapitalOutOfPlay(live: false, capitalTeam: 2, inMatch), "warm-up: every capital plays");
            Assert.IsTrue(MatchStartRules.IsCapitalOutOfPlay(true, 2, inMatch));
            Assert.IsFalse(MatchStartRules.IsCapitalOutOfPlay(true, 0, inMatch));
            Assert.IsFalse(MatchStartRules.IsCapitalOutOfPlay(true, TerritoryMap.Neutral, inMatch), "not a capital");
        }

        [Test]
        public void JoinersOnlyGoToTeamsStillInTheMatch()
        {
            Assert.IsTrue(MatchStartRules.MayJoin(teamsFixed: false, inMatch: false, eliminated: false), "warm-up: any team");
            Assert.IsTrue(MatchStartRules.MayJoin(true, true, false), "during the countdown or live: a team in the match");
            Assert.IsFalse(MatchStartRules.MayJoin(true, false, false), "the left-out team of a host start, from its countdown on");
            Assert.IsFalse(MatchStartRules.MayJoin(true, true, true), "knocked out");
        }

        [Test]
        public void TheWarmupLineMatchesTheStateWhoIsHereAndWhoIsHost()
        {
            Assert.AreEqual(WarmupMessage.None, MatchStartRules.WarmupMessageFor(StartState.Live, teamsWithPlayers: 2, isHost: true));
            Assert.AreEqual(WarmupMessage.Countdown, MatchStartRules.WarmupMessageFor(StartState.CountingDown, 2, true));
            Assert.AreEqual(WarmupMessage.WaitingForTeams, MatchStartRules.WarmupMessageFor(StartState.Warmup, 1, true));
            Assert.AreEqual(WarmupMessage.HostMayStart, MatchStartRules.WarmupMessageFor(StartState.Warmup, 2, true));
            Assert.AreEqual(WarmupMessage.WaitingForHost, MatchStartRules.WarmupMessageFor(StartState.Warmup, 2, false));
        }
    }
}
