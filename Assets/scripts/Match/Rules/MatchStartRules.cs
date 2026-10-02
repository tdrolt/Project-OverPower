using System;
using System.Collections.Generic;
using Overpower.Lobby;

namespace Overpower.Match
{
    /// <summary>Where the warm-up/countdown/live state stands for the UI, per StartStateFor.</summary>
    public enum StartState { Warmup, CountingDown, Live }

    /// <summary>What the warm-up bar should say, per WarmupMessageFor. The host's two wordings differ so that the offer to
    /// end the warm-up is only ever made while ending it is allowed.</summary>
    public enum WarmupMessage { None, Countdown, HostMayEnd, HostBlocked, WaitingForHost }

    /// <summary>
    /// Pure C# rules for 2.7b's match start (Tudor, 2026-09-18, "Going live" and answer 3): a 5-second countdown
    /// that the host starts (lobby Task 5: End warm-up, once every team of the mode has a player); "Match
    /// starts in N" shows on every screen, then the match goes live. No UnityEngine here - MatchDirector.Live.cs (step
    /// 5) is the only Photon wiring around it, reading PhotonNetwork.PlayerList/ServerTimestamp into the plain values
    /// these methods take, so a new master and every client reach the same answer with no extra state.
    /// </summary>
    public static class MatchStartRules
    {
        /// <summary>The fixed team count (CathedralBuildingIDs, TerritoryConfig.PlayersPerTeam already assume it).</summary>
        public const int TeamCount = 3;

        /// <summary>The two-team lobby mode (Tudor, 2026-09-26): the host can set the room to this before the
        /// countdown so joiners fill two teams instead of three - see LobbyModeOf. Written once when the lobby is created
        /// (the host's later switch is gone, lobby Task 4).</summary>
        public const int TwoTeams = 2;

        /// <summary>The default lobby mode - every room the host hasn't switched, spelled out for callers that read
        /// better naming the mode than TeamCount.</summary>
        public const int ThreeTeams = TeamCount;

        /// <summary>The room's lobby mode, from the raw Room Property value (Decision L1): only a boxed int 2 reads
        /// as two-team mode; anything else - absent (null), the wrong type, or any other number - reads as three,
        /// the same as a room the host never switched.</summary>
        public static int LobbyModeOf(object raw) => raw is int mode && mode == TwoTeams ? TwoTeams : ThreeTeams;

        /// <summary>The teams in the match are fixed the moment the countdown starts (Decision 4): the teams with
        /// players at that instant, ascending.</summary>
        public static int[] TeamsWithPlayers(IReadOnlyList<int> membersPerTeam)
        {
            var teams = new List<int>(membersPerTeam.Count);
            for (int i = 0; i < membersPerTeam.Count; i++)
                if (membersPerTeam[i] > 0) teams.Add(i);
            return teams.ToArray();
        }

        /// <summary>Warmup/CountingDown/Live, from the two Room Property facts (Decision 1-2): mTeams present means
        /// counting down, mPhase present means live - MatchPhase.Warmup is never stored.</summary>
        public static StartState StartStateFor(bool teamsFixed, bool phaseWritten) =>
            phaseWritten ? StartState.Live : teamsFixed ? StartState.CountingDown : StartState.Warmup;

        /// <summary>The host may end the warm-up (lobby Task 5): the teams are not fixed yet, the lobby is in the
        /// warm-up (lS = 1) and every team of the lobby's mode has at least one player present - three teams for a
        /// 3v3v3 lobby, two for a 3v3. Replaces the old "two or three teams with a player" start.</summary>
        public static bool HostMayEndWarmup(bool teamsFixed, int lobbyStage, SeatLayout layout, IReadOnlyDictionary<int, int> presentPlayersPerTeam) =>
            !teamsFixed && lobbyStage == LobbySeatRules.LobbyWarmup && LobbySeatRules.MayEndWarmup(layout, presentPlayersPerTeam);

        /// <summary>Whether a player in the room counts toward their team's present players: a dropped (inactive)
        /// player is not here, and a spectator plays for no team.</summary>
        public static bool CountsAsTeamPlayer(bool inactive, bool spectator) => PresenceRules.CountsInTheLobby(inactive) && !spectator;

        /// <summary>The teams fixed into the match when the warm-up ends: the layout's own teams, ascending.</summary>
        public static int[] TeamsOfLayout(SeatLayout layout)
        {
            int[] teams = (int[])layout.Teams.Clone();
            System.Array.Sort(teams);
            return teams;
        }

        /// <summary>Decision 1: mLiveAt = now + the countdown length, computed wrap-safe. 0 (or a negative length,
        /// treated as 0) means live on the master's very next frame.</summary>
        public static int CountdownEndsAt(int nowMs, float countdownSeconds) =>
            unchecked(nowMs + (int)Math.Round(Math.Max(0, countdownSeconds) * 1000));

        /// <summary>The master goes live the frame its own clock reaches the moment - wrap-safe, same maths as
        /// TerritorySnapshot's hold timers.</summary>
        public static bool HasReached(int nowMs, int momentMs) => unchecked(nowMs - momentMs) >= 0;

        /// <summary>"Match starts in N" (Decision 22): whole seconds, rounded up, and never 0 - it holds at 1 until the
        /// master's live write actually arrives, even a little past the moment.
        /// Review fix: nowMs reads 0 for a joiner's first few frames, before PhotonNetwork.ServerTimestamp has synced
        /// (BuildingManager's own "FAIL #15" comment already records this elsewhere) - liveAtMs - 0 would read as a
        /// nonsense huge number of seconds, so while the clock hasn't synced this shows countdownSecondsIfUnsynced
        /// instead (MatchDirector.Live.cs's getter passes the local player's own configured countdown length),
        /// rounded up and floored at 1 the same way as the normal path.</summary>
        public static int CountdownSecondsShown(int nowMs, int liveAtMs, float countdownSecondsIfUnsynced = 0f) =>
            nowMs == 0
                ? Math.Max(1, (int)Math.Ceiling(Math.Max(0, countdownSecondsIfUnsynced)))
                : Math.Max(1, (int)Math.Ceiling(unchecked(liveAtMs - nowMs) / 1000.0));

        /// <summary>Decision 22: a team fixed into the countdown emptying cancels it - back to the warm-up.</summary>
        public static bool CountdownShouldCancel(IReadOnlyList<int> teamsInMatch, IReadOnlyList<int> membersPerTeam)
        {
            foreach (int team in teamsInMatch)
                if (team >= 0 && team < membersPerTeam.Count && membersPerTeam[team] == 0)
                    return true;
            return false;
        }

        /// <summary>Decision 8: the cut capital is out of play from LIVE, not from the countdown - the countdown is
        /// still warm-up (Decision 3), and the cut capital only goes neutral in the live reset.</summary>
        public static bool IsCapitalOutOfPlay(bool live, int capitalTeam, IReadOnlyList<int> teamsInMatch) =>
            live && capitalTeam >= 0 && teamsInMatch != null && !Contains(teamsInMatch, capitalTeam);

        /// <summary>Decision 4/17: before the teams are fixed, any team; from the countdown on, only a team in the
        /// match and not knocked out.</summary>
        public static bool MayJoin(bool teamsFixed, bool inMatch, bool eliminated) =>
            !teamsFixed || (inMatch && !eliminated);

        /// <summary>Decision L2: in two-team mode, only teams 0 and 1 are open before the countdown - team 2 is
        /// closed so a joiner never lands on the third, disappearing team. Mode 3 leaves every team open (the
        /// countdown-or-live rule takes over once teamsFixed, in MayJoin below).</summary>
        public static bool IsTeamOpen(int mode, int team) => mode != TwoTeams || team < TwoTeams;

        /// <summary>Decision L2: before the countdown, a joiner may only pick an open team (IsTeamOpen); from the
        /// countdown on, the existing rule above wins regardless of teamOpen (a two-team room stays two teams for
        /// its whole match, but a rejoin still needs to be in the match and not knocked out).</summary>
        public static bool MayJoin(bool teamsFixed, bool inMatch, bool eliminated, bool teamOpen) =>
            teamsFixed ? MayJoin(teamsFixed, inMatch, eliminated) : teamOpen;

        /// <summary>The warm-up bar's wording: nothing once live, the countdown while counting down, else the host's line
        /// (the offer to end the warm-up only while HostMayEndWarmup holds, otherwise the blocked wording) or everyone
        /// else's line naming the host.</summary>
        public static WarmupMessage WarmupMessageFor(StartState state, bool isHost, bool hostMayEnd)
        {
            if (state == StartState.Live) return WarmupMessage.None;
            if (state == StartState.CountingDown) return WarmupMessage.Countdown;
            if (!isHost) return WarmupMessage.WaitingForHost;
            return hostMayEnd ? WarmupMessage.HostMayEnd : WarmupMessage.HostBlocked;
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return true;
            return false;
        }
    }
}
