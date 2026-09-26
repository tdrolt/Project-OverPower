using System;
using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>Where the warm-up/countdown/live state stands for the UI, per StartStateFor.</summary>
    public enum StartState { Warmup, CountingDown, Live }

    /// <summary>What the warm-up line should say, per WarmupMessageFor. The three "TwoTeams..." values are the
    /// two-team lobby's own wording (Tudor, 2026-09-26; Decision L7) - a room in two-team mode never returns one of
    /// the older values while in the warm-up.</summary>
    public enum WarmupMessage
    {
        None, Countdown, WaitingForTeams, HostMayStart, WaitingForHost,
        TwoTeamsWaitingForPlayers, TwoTeamsHostMayStart, TwoTeamsWaitingForHost
    }

    /// <summary>
    /// Pure C# rules for 2.7b's match start (Tudor, 2026-09-18, "Going live" and answer 3): a 5-second countdown
    /// starts automatically once all three teams have a player, or the host starts it with exactly two teams; "Match
    /// starts in N" shows on every screen, then the match goes live. No UnityEngine here - MatchDirector.Live.cs (step
    /// 5) is the only Photon wiring around it, reading PhotonNetwork.PlayerList/ServerTimestamp into the plain values
    /// these methods take, so a new master and every client reach the same answer with no extra state.
    /// </summary>
    public static class MatchStartRules
    {
        /// <summary>The fixed team count (CathedralBuildingIDs, TerritoryConfig.PlayersPerTeam already assume it).</summary>
        public const int TeamCount = 3;

        /// <summary>The two-team lobby mode (Tudor, 2026-09-26): the host can set the room to this before the
        /// countdown so joiners fill two teams instead of three - see LobbyModeOf, MaySwitchToTwoTeams.</summary>
        public const int TwoTeams = 2;

        /// <summary>The default lobby mode - every room the host hasn't switched, spelled out for callers that read
        /// better naming the mode than TeamCount.</summary>
        public const int ThreeTeams = TeamCount;

        /// <summary>The room's lobby mode, from the raw Room Property value (Decision L1): only a boxed int 2 reads
        /// as two-team mode; anything else - absent (null), the wrong type, or any other number - reads as three,
        /// the same as a room the host never switched.</summary>
        public static int LobbyModeOf(object raw) => raw is int mode && mode == TwoTeams ? TwoTeams : ThreeTeams;

        /// <summary>Allocation-free - the per-frame UI (host button, countdown line) calls this every frame.</summary>
        public static int CountTeamsWithPlayers(IReadOnlyList<int> membersPerTeam)
        {
            int count = 0;
            for (int i = 0; i < membersPerTeam.Count; i++)
                if (membersPerTeam[i] > 0) count++;
            return count;
        }

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

        /// <summary>Tudor, "Going live": the match goes live automatically once three teams have at least one player.</summary>
        public static bool StartsCountdownAutomatically(bool teamsFixed, IReadOnlyList<int> membersPerTeam) =>
            !teamsFixed && CountTeamsWithPlayers(membersPerTeam) >= TeamCount;

        /// <summary>Decision L3: the two-team lobby never starts itself, whatever fills the room - the host presses
        /// Start (HostMayStart). Mode 3 keeps the answer above.</summary>
        public static bool StartsCountdownAutomatically(bool teamsFixed, IReadOnlyList<int> membersPerTeam, int mode) =>
            mode != TwoTeams && StartsCountdownAutomatically(teamsFixed, membersPerTeam);

        /// <summary>Tudor: with only two teams, the host gets a Start button. Refused while anyone in the room has no
        /// team yet (Decision 17): they might be the third team.</summary>
        public static bool HostMayStart(bool teamsFixed, IReadOnlyList<int> membersPerTeam, int playersWithoutATeam) =>
            !teamsFixed && playersWithoutATeam == 0 && CountTeamsWithPlayers(membersPerTeam) == 2;

        /// <summary>The two-team lobby (Tudor, 2026-09-26; Decision L3): mode 3 keeps the answer above unchanged. In
        /// two-team mode the host may start once teams 0 and 1 both have a player, team 2 (closed - see IsTeamOpen)
        /// has nobody, and nobody in the room is still without a team.</summary>
        public static bool HostMayStart(bool teamsFixed, IReadOnlyList<int> membersPerTeam, int playersWithoutATeam, int mode)
        {
            if (mode != TwoTeams)
                return HostMayStart(teamsFixed, membersPerTeam, playersWithoutATeam);
            return !teamsFixed && playersWithoutATeam == 0
                && membersPerTeam[0] > 0 && membersPerTeam[1] > 0 && membersPerTeam[TwoTeams] == 0;
        }

        /// <summary>Decision L4: the host may switch to two teams while the teams aren't fixed and at most 2 ×
        /// teamSize players are already in the room (a 7th would have nowhere open to join once MaxPlayersFor
        /// shrinks the room - the panel greys the button and says why).</summary>
        public static bool MaySwitchToTwoTeams(bool teamsFixed, int playersInRoom, int teamSize) =>
            !teamsFixed && playersInRoom <= TwoTeams * teamSize;

        /// <summary>Decision L4: switching back to three teams needs only that the teams aren't fixed yet - nobody
        /// moves (team 2 simply reopens, IsTeamOpen).</summary>
        public static bool MaySwitchToThreeTeams(bool teamsFixed) => !teamsFixed;

        /// <summary>Decision L1: the same write that sets the lobby mode sets the room's MaxPlayers to this, so a
        /// two-team room actually refuses a 7th player instead of merely hiding the option.</summary>
        public static int MaxPlayersFor(int mode, int teamSize) => mode * teamSize;

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

        /// <summary>The warm-up line's wording (Open for Tudor #5: default text, unchanged) - None once live, the
        /// countdown while counting down, else who is here and who is host.</summary>
        public static WarmupMessage WarmupMessageFor(StartState state, int teamsWithPlayers, bool isHost)
        {
            if (state == StartState.Live) return WarmupMessage.None;
            if (state == StartState.CountingDown) return WarmupMessage.Countdown;
            if (teamsWithPlayers == 2) return isHost ? WarmupMessage.HostMayStart : WarmupMessage.WaitingForHost;
            return WarmupMessage.WaitingForTeams;
        }

        /// <summary>Decision L7: the two-team lobby's own wording (Tudor, 2026-09-26). Mode 3 keeps the answer
        /// above; live and counting down read the same regardless of mode - only the warm-up wording differs.</summary>
        public static WarmupMessage WarmupMessageFor(StartState state, int teamsWithPlayers, bool isHost, int mode)
        {
            if (mode != TwoTeams) return WarmupMessageFor(state, teamsWithPlayers, isHost);
            if (state == StartState.Live) return WarmupMessage.None;
            if (state == StartState.CountingDown) return WarmupMessage.Countdown;
            if (teamsWithPlayers == TwoTeams)
                return isHost ? WarmupMessage.TwoTeamsHostMayStart : WarmupMessage.TwoTeamsWaitingForHost;
            return WarmupMessage.TwoTeamsWaitingForPlayers;
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return true;
            return false;
        }
    }
}
