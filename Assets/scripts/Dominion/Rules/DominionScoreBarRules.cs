using System;

namespace Overpower.Dominion
{
    /// <summary>
    /// Dominion Task 17 (Tudor A52), the score bars in the bottom-right corner: one per team, the leading team's full, the others filled by their share
    /// of the leader's points. Pure, so the drawing code in UI/Dominion/ScoreBars only places what these return.
    /// </summary>
    public static class DominionScoreBarRules
    {
        /// <summary>The bars show while the stage plays for points - a round and its overtime - and nowhere else (default: the break, sudden death and the
        /// result hide them; the round bar and the break card already say the points there).</summary>
        public static bool ShownIn(DominionStage stage) => DominionRules.IsRoundPlay(stage);

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

        /// <summary>The fill of each listed team's bar, in the order of <paramref name="teams"/>. A missing points entry reads as 0.</summary>
        public static float[] Fills(int[] teams, int[] points)
        {
            if (teams == null) return new float[0];
            int leader = Leader(teams, points);
            var fills = new float[teams.Length];
            for (int i = 0; i < teams.Length; i++) fills[i] = Fill(PointsOf(points, teams[i]), leader);
            return fills;
        }

        /// <summary>The points as shown in a bar (never negative).</summary>
        public static int PointsOf(int[] points, int team) =>
            points != null && team >= 0 && team < points.Length ? Math.Max(0, points[team]) : 0;

        /// <summary>How far above the screen's bottom edge the gold readout over the "Loadout (P)" button ends, in canvas units: the button's margin, the
        /// button, the gap, the readout's two lines of body text and their padding (the same numbers PlayerHud.BuildGoldCorner uses), all scaled by the HUD
        /// scale. The score bars sit above this line, so they overlap neither.</summary>
        public static float GoldReadoutTop(float margin, float buttonHeight, float goldGap, float bodyTextSize, float hudScale) =>
            (margin + buttonHeight + goldGap + 2f * bodyTextSize + GoldReadoutPadding) * hudScale;

        /// <summary>The extra height PlayerHud gives the gold readout beyond its two lines of text.</summary>
        public const float GoldReadoutPadding = 10f;
    }
}
