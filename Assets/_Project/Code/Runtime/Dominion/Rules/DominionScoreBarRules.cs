using System;

namespace Overpower.Dominion
{
    /// <summary>
    /// The score bars at the top right (A52): one per team, the leading team's full, the others filled by their share of the leader's points, each
    /// followed by the team's round-win dots. Pure, so the drawing code in UI/Dominion/ScoreBars only places what these return.
    /// </summary>
    public static class DominionScoreBarRules
    {
        /// <summary>The bars and their points show while the stage plays for points - a round and its overtime - and nowhere else (the break card says
        /// the points in the break; sudden death is not decided by points).</summary>
        public static bool ShownIn(DominionStage stage) => DominionRules.IsRoundPlay(stage);

        /// <summary>The rows' round-win dots show in a round, its overtime and sudden death (there alone, without the bars); the break card has its own.</summary>
        public static bool DotsShownIn(DominionStage stage) => DominionRules.IsRoundPlay(stage) || stage == DominionStage.SuddenDeath;

        /// <summary>The top score among the match's teams (0 when nobody has any). Only the listed teams count: a 2v2 room keeps a third slot in its points.</summary>
        public static int Leader(int[] teams, int[] points)
        {
            int leader = 0;
            if (teams == null) return leader;
            for (int i = 0; i < teams.Length; i++) leader = Math.Max(leader, PointsOf(points, teams[i]));
            return leader;
        }

        /// <summary>How full one bar is, 0 to 1: its points over the leader's. Nobody on points = every bar empty (not a 0/0).</summary>
        public static float Fill(int points, int leaderPoints)
        {
            if (leaderPoints <= 0 || points <= 0) return 0f;
            return Math.Min(1f, points / (float)leaderPoints);
        }

        /// <summary>Fills for each listed team written into a buffer the caller keeps (the bars are refreshed every frame, so they allocate nothing). The leader is
        /// found once. Entries past the teams' count are left as they were.</summary>
        public static void FillsInto(int[] teams, int[] points, float[] buffer)
        {
            if (teams == null || buffer == null) return;
            int leader = Leader(teams, points);
            for (int i = 0; i < teams.Length && i < buffer.Length; i++) buffer[i] = Fill(PointsOf(points, teams[i]), leader);
        }

        /// <summary>A won round-win dot is the team's colour; an empty one its text colour faded by emptyAlpha (0 to 1), so in sudden death, with
        /// only the dots on screen, each row still says whose it is.</summary>
        public static UnityEngine.Color DotColour(bool won, UnityEngine.Color teamColour, UnityEngine.Color teamTextColour, float emptyAlpha)
        {
            if (won) return teamColour;
            teamTextColour.a *= Math.Max(0f, Math.Min(1f, emptyAlpha));
            return teamTextColour;
        }

        /// <summary>The points as shown in a bar (never negative).</summary>
        public static int PointsOf(int[] points, int team) =>
            points != null && team >= 0 && team < points.Length ? Math.Max(0, points[team]) : 0;

        /// <summary>The gold readout's height over the Loadout (P) button: two lines of body text and the padding. PlayerHud sizes the readout with this.</summary>
        public static float GoldReadoutHeight(float bodyTextSize) => 2f * bodyTextSize + GoldReadoutPadding;

        /// <summary>The extra height PlayerHud gives the gold readout beyond its two lines of text.</summary>
        public const float GoldReadoutPadding = 10f;
    }
}
