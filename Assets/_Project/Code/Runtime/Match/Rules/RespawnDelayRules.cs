using UnityEngine;

namespace Overpower.Match
{
    /// <summary>The respawn wait and what a death costs. PlayerLifecycle owns the state; the numbers
    /// live here so the tests can pin them.</summary>
    public static class RespawnDelayRules
    {
        /// <summary>Each death costs a bit more than the last, up to a cap (deathCount is 1 for the first).</summary>
        public static float Delay(int deathCount, float baseSeconds, float perDeathSeconds, float maxSeconds) =>
            Mathf.Min(baseSeconds + perDeathSeconds * (deathCount - 1), maxSeconds);

        /// <summary>The wait before coming back. In Dominion it is the match size's fixed seconds however many times the player
        /// has died; in Conquest it grows with each death as Delay says.</summary>
        public static float DelayFor(bool dominion, float dominionFixedSeconds, int deathCount, float baseSeconds, float perDeathSeconds, float maxSeconds) =>
            dominion ? Mathf.Max(0f, dominionFixedSeconds) : Delay(deathCount, baseSeconds, perDeathSeconds, maxSeconds);

        /// <summary>The one decision behind PlayerLifecycle.NextRespawnDelay: how long this death waits. A rejoiner waits the rejoin time, anyone else
        /// the death-scaled one; in a live Dominion match both are the fixed seconds instead (dominionLive false = Conquest or the Dominion warm-up).</summary>
        public static float WaitFor(bool dominionLive, bool rejoin, float dominionFixedSeconds, int deathCount, float baseSeconds, float perDeathSeconds,
                                    float maxSeconds, float flatRejoinSeconds) =>
            rejoin ? RejoinDelayFor(dominionLive, dominionFixedSeconds, flatRejoinSeconds)
                   : DelayFor(dominionLive, dominionFixedSeconds, deathCount, baseSeconds, perDeathSeconds, maxSeconds);

        /// <summary>A rejoiner in Dominion waits the same fixed time as everyone; in Conquest the flat rejoin seconds (A13).</summary>
        public static float RejoinDelayFor(bool dominion, float dominionFixedSeconds, float flatSeconds) =>
            dominion ? Mathf.Max(0f, dominionFixedSeconds) : RejoinDelay(flatSeconds);

        /// <summary>A player who waited after their countdown already paid for that death when it started; the retake
        /// respawn must not charge a second one. A last-stand death (no countdown) is charged here, once.</summary>
        public static int DeathCountForRetake(int deathCount, bool countdownAlreadyCounted) =>
            countdownAlreadyCounted ? deathCount : deathCount + 1;

        /// <summary>A rejoined player keeps their death penalty (D28). The count comes back from the deaths in
        /// their "sb" Player Property (kills, deaths, assists, damage, captures), which the room keeps through the drop. It matches the
        /// respawn count: both start at zero at go-live and both grow once per death (a last-stand death is in "sb" at once and in the
        /// respawn count at the retake, which is why a rejoin respawn never charges a second time). Missing or short: 0.</summary>
        public static int DeathCountOnRejoin(int[] scoreboard) =>
            scoreboard != null && scoreboard.Length > ScoreboardRules.DeathsIndex && scoreboard[ScoreboardRules.DeathsIndex] > 0
                ? scoreboard[ScoreboardRules.DeathsIndex] : 0;

        /// <summary>The wait after a rejoin: a flat number of seconds (GameplayConfig.RejoinRespawnSeconds), not scaled by deaths.</summary>
        public static float RejoinDelay(float flatSeconds) => Mathf.Max(0f, flatSeconds);

        /// <summary>As the two-argument form, but a rejoin respawn never charges a death (a drop is not a death): the count is what the room's sb held.</summary>
        public static int DeathCountForRetake(int deathCount, bool countdownAlreadyCounted, bool rejoinRespawn) =>
            rejoinRespawn ? deathCount : DeathCountForRetake(deathCount, countdownAlreadyCounted);
    }
}
