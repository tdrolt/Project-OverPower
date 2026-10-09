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
    /// Pure C# rules for the match start: a countdown that the host starts (End warm-up, once every team of the mode
    /// has a player); "Match starts in N" shows on every screen, then the match goes live. No UnityEngine here -
    /// MatchDirector.Live.cs is the only Photon wiring around it, reading PhotonNetwork.PlayerList/ServerTimestamp
    /// into the plain values these methods take, so a new master and every client reach the same answer with no extra state.
    /// </summary>
    public static class MatchStartRules
    {
        /// <summary>The fixed team count (CathedralBuildingIDs, TerritoryConfig.PlayersPerTeam already assume it).</summary>
        public const int TeamCount = 3;

        /// <summary>The two-team lobby mode: joiners fill two teams instead of three - see LobbyModeOf. Written once
        /// when the lobby is created.</summary>
        public const int TwoTeams = 2;

        /// <summary>The default lobby mode, spelled out for callers that read better naming the mode than TeamCount.</summary>
        public const int ThreeTeams = TeamCount;

        /// <summary>The room's lobby mode, from the raw Room Property value (L1): only a boxed int 2 reads
        /// as two-team mode; anything else - absent (null), the wrong type, or any other number - reads as three,
        /// the default.</summary>
        public static int LobbyModeOf(object raw) => raw is int mode && mode == TwoTeams ? TwoTeams : ThreeTeams;

        /// <summary>Warmup/CountingDown/Live, from the two Room Property facts: mTeams present means
        /// counting down, mPhase present means live - MatchPhase.Warmup is never stored.</summary>
        public static StartState StartStateFor(bool teamsFixed, bool phaseWritten) =>
            phaseWritten ? StartState.Live : teamsFixed ? StartState.CountingDown : StartState.Warmup;

        /// <summary>The host may end the warm-up: the teams are not fixed yet, the lobby is in the warm-up (lS = 1)
        /// and every team of the lobby's mode has at least one player present - three teams for a 3v3v3 lobby, two for a 3v3.</summary>
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

        /// <summary>mLiveAt = now + the countdown length, computed wrap-safe. 0 (or a negative length,
        /// treated as 0) means live on the master's very next frame.</summary>
        public static int CountdownEndsAt(int nowMs, float countdownSeconds) =>
            unchecked(nowMs + (int)Math.Round(Math.Max(0, countdownSeconds) * 1000));

        /// <summary>The master goes live the frame its own clock reaches the moment - wrap-safe, same maths as
        /// TerritorySnapshot's hold timers.</summary>
        public static bool HasReached(int nowMs, int momentMs) => unchecked(nowMs - momentMs) >= 0;

        /// <summary>"Match starts in N": whole seconds, rounded up, and never 0 - it holds at 1 until the master's live
        /// write actually arrives, even a little past the moment. nowMs reads 0 for a joiner's first few frames, before
        /// PhotonNetwork.ServerTimestamp has synced - liveAtMs - 0 would read as a nonsense huge number of seconds, so
        /// while the clock hasn't synced this shows countdownSecondsIfUnsynced instead (MatchDirector.Live.cs's getter
        /// passes the local player's own configured countdown length), rounded up and floored at 1 the same way.</summary>
        public static int CountdownSecondsShown(int nowMs, int liveAtMs, float countdownSecondsIfUnsynced = 0f) =>
            nowMs == 0
                ? Math.Max(1, (int)Math.Ceiling(Math.Max(0, countdownSecondsIfUnsynced)))
                : Math.Max(1, (int)Math.Ceiling(unchecked(liveAtMs - nowMs) / 1000.0));

        /// <summary>A team fixed into the countdown emptying cancels it - back to the warm-up (Decision 22).</summary>
        public static bool CountdownShouldCancel(IReadOnlyList<int> teamsInMatch, IReadOnlyList<int> membersPerTeam)
        {
            foreach (int team in teamsInMatch)
                if (team >= 0 && team < membersPerTeam.Count && membersPerTeam[team] == 0)
                    return true;
            return false;
        }

        /// <summary>The cut capital is out of play from LIVE, not from the countdown - the countdown is still
        /// warm-up, and the cut capital only goes neutral in the live reset (Decisions 3, 8).</summary>
        public static bool IsCapitalOutOfPlay(bool live, int capitalTeam, IReadOnlyList<int> teamsInMatch) =>
            live && capitalTeam >= 0 && teamsInMatch != null && !Contains(teamsInMatch, capitalTeam);

        /// <summary>Before the teams are fixed, any team; from the countdown on, only a team in the
        /// match and not knocked out (Decisions 4, 17).</summary>
        public static bool MayJoin(bool teamsFixed, bool inMatch, bool eliminated) =>
            !teamsFixed || (inMatch && !eliminated);

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
