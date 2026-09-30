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
            // Tudor, 2026-09-26: never automatic - the host presses Start.
            Assert.IsFalse(MatchStartRules.StartsCountdownAutomatically(false, new[] { 1, 1, 1 }));
            Assert.IsFalse(MatchStartRules.StartsCountdownAutomatically(false, new[] { 3, 2, 1 }));
            Assert.IsFalse(MatchStartRules.StartsCountdownAutomatically(true, new[] { 1, 1, 1 }), "a countdown or a live match already fixed the teams");
        }

        [Test]
        public void TheHostMayStartOnlyWithExactlyTwoTeams()
        {
            Assert.IsTrue(MatchStartRules.HostMayStart(teamsFixed: false, new[] { 1, 0, 2 }, playersWithoutATeam: 0));
            Assert.IsFalse(MatchStartRules.HostMayStart(false, new[] { 2, 0, 0 }, 0), "one team: nobody to play");
            Assert.IsTrue(MatchStartRules.HostMayStart(false, new[] { 1, 1, 1 }, 0), "three teams: the host starts it too (Tudor, 2026-09-26)");
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
        public void ReviewFix2_AnUnsyncedClockShowsTheConfiguredCountdownLengthInstead()
        {
            // Review fix 2: nowMs == 0 means PhotonNetwork.ServerTimestamp has not synced yet (a joiner's first
            // frames) - liveAtMs - 0 would otherwise read as a nonsense huge number of seconds.
            Assert.AreEqual(5, MatchStartRules.CountdownSecondsShown(nowMs: 0, liveAtMs: 999999999, countdownSecondsIfUnsynced: 5f));
            Assert.AreEqual(6, MatchStartRules.CountdownSecondsShown(0, 999999999, 5.2f), "rounds up, same as the normal path");
            Assert.AreEqual(1, MatchStartRules.CountdownSecondsShown(0, 999999999, 0f), "never 0, same floor as the normal path");
            Assert.AreEqual(5, MatchStartRules.CountdownSecondsShown(10000, 15000), "nowMs != 0: unaffected, fallback defaults to unused");
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

        // --- Two-team lobby (Tudor, 2026-09-26): the host can set the room to two teams before the countdown. ---

        [Test]
        public void ModeReadsTwoOnlyWhenStoredAsTwo()
        {
            Assert.AreEqual(2, MatchStartRules.LobbyModeOf(2));
            Assert.AreEqual(3, MatchStartRules.LobbyModeOf(3));
            Assert.AreEqual(3, MatchStartRules.LobbyModeOf(null), "absent reads as three");
            Assert.AreEqual(3, MatchStartRules.LobbyModeOf("x"), "not an int");
            Assert.AreEqual(3, MatchStartRules.LobbyModeOf(5), "not a mode anyone can write");
        }

        [Test]
        public void TwoTeamModeOpensOnlyTheFirstTwoTeamsBeforeTheCountdown()
        {
            Assert.IsTrue(MatchStartRules.IsTeamOpen(2, 0));
            Assert.IsTrue(MatchStartRules.IsTeamOpen(2, 1));
            Assert.IsFalse(MatchStartRules.IsTeamOpen(2, 2), "the third team is closed in two-team mode");
            Assert.IsTrue(MatchStartRules.IsTeamOpen(3, 2));
            Assert.IsFalse(MatchStartRules.MayJoin(teamsFixed: false, inMatch: false, eliminated: false, teamOpen: false));
            Assert.IsTrue(MatchStartRules.MayJoin(true, true, false, false), "after the countdown the existing rule wins");
        }

        [Test]
        public void TwoTeamModeNeverStartsItself()
        {
            Assert.IsFalse(MatchStartRules.StartsCountdownAutomatically(false, new[] { 1, 1, 1 }, mode: 2));
            Assert.IsFalse(MatchStartRules.StartsCountdownAutomatically(false, new[] { 1, 1, 1 }, mode: 3));
        }

        [Test]
        public void TheHostStartsATwoTeamMatchWhenBothTeamsHaveSomeone()
        {
            Assert.IsTrue(MatchStartRules.HostMayStart(false, new[] { 1, 1, 0 }, 0, mode: 2));
            Assert.IsFalse(MatchStartRules.HostMayStart(false, new[] { 2, 0, 0 }, 0, mode: 2));
            Assert.IsFalse(MatchStartRules.HostMayStart(false, new[] { 1, 1, 1 }, 0, mode: 2), "someone still on the third team");
            Assert.IsFalse(MatchStartRules.HostMayStart(false, new[] { 1, 1, 0 }, 1, mode: 2), "a player without a team");
            Assert.IsFalse(MatchStartRules.HostMayStart(true, new[] { 1, 1, 0 }, 0, mode: 2), "already counting down or live");
            // Mode 3 keeps the old answers.
            Assert.IsTrue(MatchStartRules.HostMayStart(false, new[] { 1, 0, 2 }, 0, mode: 3));
            Assert.IsTrue(MatchStartRules.HostMayStart(false, new[] { 1, 1, 1 }, 0, mode: 3), "three teams: the host starts it");
        }

        [Test]
        public void TheSwitchToTwoTeamsNeedsSixOrFewer()
        {
            Assert.IsTrue(MatchStartRules.MaySwitchToTwoTeams(false, 6, teamSize: 3));
            Assert.IsFalse(MatchStartRules.MaySwitchToTwoTeams(false, 7, 3));
            Assert.IsFalse(MatchStartRules.MaySwitchToTwoTeams(true, 6, 3), "teams already fixed");
            Assert.IsTrue(MatchStartRules.MaySwitchToThreeTeams(false));
            Assert.IsFalse(MatchStartRules.MaySwitchToThreeTeams(true));
        }

        [Test]
        public void TheRoomHoldsModeTimesTeamSize()
        {
            Assert.AreEqual(6, MatchStartRules.MaxPlayersFor(2, 3));
            Assert.AreEqual(9, MatchStartRules.MaxPlayersFor(3, 3));
        }

        [Test]
        public void TheWarmupLineSaysTwoTeams()
        {
            Assert.AreEqual(WarmupMessage.TwoTeamsHostMayStart, MatchStartRules.WarmupMessageFor(StartState.Warmup, teamsWithPlayers: 2, isHost: true, mode: 2));
            Assert.AreEqual(WarmupMessage.TwoTeamsWaitingForHost, MatchStartRules.WarmupMessageFor(StartState.Warmup, 2, false, 2));
            Assert.AreEqual(WarmupMessage.TwoTeamsWaitingForPlayers, MatchStartRules.WarmupMessageFor(StartState.Warmup, 1, true, 2));
            Assert.AreEqual(WarmupMessage.Countdown, MatchStartRules.WarmupMessageFor(StartState.CountingDown, 2, true, 2));
            Assert.AreEqual(WarmupMessage.None, MatchStartRules.WarmupMessageFor(StartState.Live, 2, true, 2));
            // Mode 3: the old answers.
            Assert.AreEqual(WarmupMessage.HostMayStart, MatchStartRules.WarmupMessageFor(StartState.Warmup, 2, true, 3));
        }

        // --- Review fix 3 (2026-09-26): several players on a team the switch just closed must not all re-pick
        // the same open team from the same counts - each walks the list in the SAME order, filling as it goes. ---

        [Test]
        public void ReseatingSeveralClosedPlayersSpreadsThemOverTheOpenTeams()
        {
            // 0/0/{2 players}: the lower actor gets 0, the higher gets 1 - not both onto 0.
            Assert.AreEqual(0, MatchStartRules.ReseatTeamFor(mode: 2, myActor: 5,
                closedActorsAscending: new[] { 5, 9 }, openCounts: new[] { 0, 0, 0 }, teamSize: 3));
            Assert.AreEqual(1, MatchStartRules.ReseatTeamFor(2, 9, new[] { 5, 9 }, new[] { 0, 0, 0 }, 3));

            // 1/0/{1}: the one team with room wins.
            Assert.AreEqual(1, MatchStartRules.ReseatTeamFor(2, 7, new[] { 7 }, new[] { 1, 0, 0 }, 3));

            // 3/3/{1}: both open teams are already at the cap.
            Assert.AreEqual(-1, MatchStartRules.ReseatTeamFor(2, 4, new[] { 4 }, new[] { 3, 3, 0 }, 3));

            // 1/1/{3 players}, actor order: 0, 1, 0 - not all three onto the same team.
            Assert.AreEqual(0, MatchStartRules.ReseatTeamFor(2, 1, new[] { 1, 2, 3 }, new[] { 1, 1, 0 }, 3));
            Assert.AreEqual(1, MatchStartRules.ReseatTeamFor(2, 2, new[] { 1, 2, 3 }, new[] { 1, 1, 0 }, 3));
            Assert.AreEqual(0, MatchStartRules.ReseatTeamFor(2, 3, new[] { 1, 2, 3 }, new[] { 1, 1, 0 }, 3));

            // myActor never on the closed list: -1.
            Assert.AreEqual(-1, MatchStartRules.ReseatTeamFor(2, 99, new[] { 5, 9 }, new[] { 0, 0, 0 }, 3));

            // Mode 3 never has a closed team - a call with it just returns the smallest by the same walk.
            Assert.AreEqual(0, MatchStartRules.ReseatTeamFor(3, 5, new[] { 5, 9 }, new[] { 0, 0, 0 }, 3));
            Assert.AreEqual(1, MatchStartRules.ReseatTeamFor(3, 9, new[] { 5, 9 }, new[] { 0, 0, 0 }, 3));
        }
    }
}
