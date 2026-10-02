using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Lobby;
using Overpower.Match;

namespace Overpower.Tests
{
    public class MatchStartRulesTests
    {
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
        public void TheHostsLineOnlyOffersEndingTheWarmupWhileItIsAllowed()
        {
            Assert.AreEqual(WarmupMessage.HostMayEnd, MatchStartRules.WarmupMessageFor(StartState.Warmup, isHost: true, hostMayEnd: true));
            Assert.AreEqual(WarmupMessage.HostBlocked, MatchStartRules.WarmupMessageFor(StartState.Warmup, isHost: true, hostMayEnd: false),
                "no 'end it when ready' line while a team has nobody");
        }

        [Test]
        public void TheWarmupLineOfEveryoneElseNamesTheHostWhateverTheRule()
        {
            Assert.AreEqual(WarmupMessage.WaitingForHost, MatchStartRules.WarmupMessageFor(StartState.Warmup, isHost: false, hostMayEnd: false));
            Assert.AreEqual(WarmupMessage.WaitingForHost, MatchStartRules.WarmupMessageFor(StartState.Warmup, isHost: false, hostMayEnd: true));
        }

        [Test]
        public void TheCountdownAndGoingLiveReadTheSameForEveryone()
        {
            Assert.AreEqual(WarmupMessage.Countdown, MatchStartRules.WarmupMessageFor(StartState.CountingDown, isHost: true, hostMayEnd: false));
            Assert.AreEqual(WarmupMessage.Countdown, MatchStartRules.WarmupMessageFor(StartState.CountingDown, isHost: false, hostMayEnd: false));
            Assert.AreEqual(WarmupMessage.None, MatchStartRules.WarmupMessageFor(StartState.Live, isHost: true, hostMayEnd: true));
            Assert.AreEqual(WarmupMessage.None, MatchStartRules.WarmupMessageFor(StartState.Live, isHost: false, hostMayEnd: false));
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

        private static readonly SeatLayout ThreeTeamLobby = new SeatLayout(new[] { 0, 1, 2 }, 3, 2);
        private static readonly SeatLayout TwoTeamLobby = new SeatLayout(new[] { 0, 1 }, 3, 2);

        private static Dictionary<int, int> Present(params (int team, int n)[] entries)
        {
            var d = new Dictionary<int, int>();
            foreach (var e in entries) d[e.team] = e.n;
            return d;
        }

        [Test]
        public void TheHostEndsTheWarmupOnlyWhenEveryTeamOfTheModeHasAPlayer()
        {
            Assert.IsFalse(MatchStartRules.HostMayEndWarmup(false, 1, ThreeTeamLobby, Present((0, 1), (1, 1))), "a 3v3v3 lobby with two teams present");
            Assert.IsFalse(MatchStartRules.HostMayEndWarmup(false, 1, ThreeTeamLobby, Present((0, 2), (1, 0), (2, 1))), "team 1 has nobody left");
            Assert.IsTrue(MatchStartRules.HostMayEndWarmup(false, 1, ThreeTeamLobby, Present((0, 1), (1, 1), (2, 1))));
            Assert.IsTrue(MatchStartRules.HostMayEndWarmup(false, 1, TwoTeamLobby, Present((0, 1), (1, 3))), "a 3v3 never asks about team 2");
            Assert.IsFalse(MatchStartRules.HostMayEndWarmup(false, 1, TwoTeamLobby, Present((0, 1))), "a 3v3 with one team");
        }

        [Test]
        public void TheWarmupCanOnlyEndWhileItIsTheWarmup()
        {
            var all = Present((0, 1), (1, 1), (2, 1));
            Assert.IsFalse(MatchStartRules.HostMayEndWarmup(true, 1, ThreeTeamLobby, all), "a countdown or a live match already fixed the teams");
            Assert.IsFalse(MatchStartRules.HostMayEndWarmup(false, 0, ThreeTeamLobby, all), "still the lobby: that is Start game");
            Assert.IsFalse(MatchStartRules.HostMayEndWarmup(false, 2, ThreeTeamLobby, all), "already in the match");
        }

        [Test]
        public void ADroppedPlayerAndASpectatorCountForNoTeam()
        {
            Assert.IsTrue(MatchStartRules.CountsAsTeamPlayer(inactive: false, spectator: false));
            Assert.IsFalse(MatchStartRules.CountsAsTeamPlayer(inactive: true, spectator: false), "a dropped player is not present");
            Assert.IsFalse(MatchStartRules.CountsAsTeamPlayer(inactive: false, spectator: true), "a spectator plays for no team");
        }

        [Test]
        public void TheMatchIsFixedToTheTeamsOfTheLayout()
        {
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, MatchStartRules.TeamsOfLayout(ThreeTeamLobby));
            CollectionAssert.AreEqual(new[] { 0, 1 }, MatchStartRules.TeamsOfLayout(TwoTeamLobby));
        }

        [Test]
        public void TheLiveWriteMarksTheLobbyInMatch()
        {
            var props = MatchDirector.LiveProperties(new[] { 0, 1 }, MatchPhase.TwoTeams);
            Assert.AreEqual(2, props[LobbyKeys.Stage], "the list shows In match");
            Assert.AreEqual((int)MatchPhase.TwoTeams, props[MatchDirector.PhaseKey]);
            CollectionAssert.AreEqual(new[] { 0, 1 }, (int[])props[MatchDirector.TeamsInMatchKey]);
            Assert.AreEqual(-1, props[MatchDirector.WinnerKey]);
            Assert.AreEqual(0, ((int[])props[MatchDirector.EliminatedKey]).Length);
        }

    }
}
