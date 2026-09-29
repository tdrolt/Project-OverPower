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
    }
}
