using System;
using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>Where the warm-up/countdown/live state stands for the UI, per StartStateFor.</summary>
    public enum StartState { Warmup, CountingDown, Live }

    /// <summary>What the warm-up line should say, per WarmupMessageFor.</summary>
    public enum WarmupMessage { None, Countdown, WaitingForTeams, HostMayStart, WaitingForHost }

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

        /// <summary>Tudor: with only two teams, the host gets a Start button. Refused while anyone in the room has no
        /// team yet (Decision 17): they might be the third team.</summary>
        public static bool HostMayStart(bool teamsFixed, IReadOnlyList<int> membersPerTeam, int playersWithoutATeam) =>
            !teamsFixed && playersWithoutATeam == 0 && CountTeamsWithPlayers(membersPerTeam) == 2;

        /// <summary>Decision 1: mLiveAt = now + the countdown length, computed wrap-safe. 0 (or a negative length,
        /// treated as 0) means live on the master's very next frame.</summary>
        public static int CountdownEndsAt(int nowMs, float countdownSeconds) =>
            unchecked(nowMs + (int)Math.Round(Math.Max(0, countdownSeconds) * 1000));

        /// <summary>The master goes live the frame its own clock reaches the moment - wrap-safe, same maths as
        /// TerritorySnapshot's hold timers.</summary>
        public static bool HasReached(int nowMs, int momentMs) => unchecked(nowMs - momentMs) >= 0;

        /// <summary>"Match starts in N" (Decision 22): whole seconds, rounded up, and never 0 - it holds at 1 until the
        /// master's live write actually arrives, even a little past the moment.</summary>
        public static int CountdownSecondsShown(int nowMs, int liveAtMs) =>
            Math.Max(1, (int)Math.Ceiling(unchecked(liveAtMs - nowMs) / 1000.0));

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

        /// <summary>The warm-up line's wording (Open for Tudor #5: default text, unchanged) - None once live, the
        /// countdown while counting down, else who is here and who is host.</summary>
        public static WarmupMessage WarmupMessageFor(StartState state, int teamsWithPlayers, bool isHost)
        {
            if (state == StartState.Live) return WarmupMessage.None;
            if (state == StartState.CountingDown) return WarmupMessage.Countdown;
            if (teamsWithPlayers == 2) return isHost ? WarmupMessage.HostMayStart : WarmupMessage.WaitingForHost;
            return WarmupMessage.WaitingForTeams;
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return true;
            return false;
        }
    }
}
