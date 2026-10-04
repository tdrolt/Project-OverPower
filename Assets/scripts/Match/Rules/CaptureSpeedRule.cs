using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>
    /// How fast a zone is captured, or a neutral zone's claim pushed down, with n players of one team in it, as a multiple of one player's speed
    /// (lobby Task 14, Tudor: helping a teammate should pay, but three players stacking on a zone should not speed-run it; 1, 1.5, 1.75 instead of
    /// 1, 2, 3). The list is the TerritoryConfig's captureSpeedByPlayers: entry n-1 for n players, the last entry past its end. An empty or missing
    /// list falls back to n (the old rule). Pure: the capture step, the fade rule and the published rate every client extrapolates all read it here,
    /// so they cannot disagree.
    /// </summary>
    public static class CaptureSpeedRule
    {
        public static float For(int players, IReadOnlyList<float> list)
        {
            if (players <= 0) return 0f;
            if (list == null || list.Count == 0) return players;
            return list[players <= list.Count ? players - 1 : list.Count - 1];
        }
    }
}
