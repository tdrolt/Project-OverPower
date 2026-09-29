using UnityEngine;

namespace Overpower.Match
{
    /// <summary>The respawn wait and what a death costs (Task 0.11a, 9b-2). PlayerLifecycle owns the state; the numbers
    /// live here so the tests can pin them.</summary>
    public static class RespawnDelayRules
    {
        /// <summary>Each death costs a bit more than the last, up to a cap (deathCount is 1 for the first).</summary>
        public static float Delay(int deathCount, float baseSeconds, float perDeathSeconds, float maxSeconds) =>
            Mathf.Min(baseSeconds + perDeathSeconds * (deathCount - 1), maxSeconds);

        /// <summary>A player who waited after their countdown already paid for that death when it started; the retake
        /// respawn must not charge a second one. A last-stand death (no countdown) is charged here, once.</summary>
        public static int DeathCountForRetake(int deathCount, bool countdownAlreadyCounted) =>
            countdownAlreadyCounted ? deathCount : deathCount + 1;

        /// <summary>Task 9g (Tudor D28, answer 8): a rejoined player keeps their death penalty. The count comes back from the deaths in
        /// their "sb" Player Property (kills, deaths, assists, damage, captures), which the room keeps through the drop. It matches the
        /// respawn count: both start at zero at go-live and both grow once per death (a last-stand death is in "sb" at once and in the
        /// respawn count at the retake, which is why a rejoin respawn never charges a second time). Missing or short: 0.</summary>
        public static int DeathCountOnRejoin(int[] scoreboard) =>
            scoreboard != null && scoreboard.Length > ScoreboardRules.DeathsIndex && scoreboard[ScoreboardRules.DeathsIndex] > 0
                ? scoreboard[ScoreboardRules.DeathsIndex] : 0;

        /// <summary>The wait after a rejoin: a flat number of seconds (GameplayConfig.RejoinRespawnSeconds), not scaled by deaths.</summary>
        public static float RejoinDelay(float flatSeconds) => Mathf.Max(0f, flatSeconds);

        /// <summary>As the two-argument form, but a rejoin respawn never charges a death: the drop's death is already in the restored count.</summary>
        public static int DeathCountForRetake(int deathCount, bool countdownAlreadyCounted, bool rejoinRespawn) =>
            rejoinRespawn ? deathCount : DeathCountForRetake(deathCount, countdownAlreadyCounted);
    }
}
