using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// Whether a mine's model is drawn this frame while it is still arming: it blinks on and off every
    /// blinkPeriodSeconds so anyone who can see it knows it cannot go off yet, then shows steadily once armed.
    /// The caller passes "armed" from the mine's own MineDetonationState.IsArmed, so the blink stops the instant
    /// the mine can trigger and there is one threshold, one source. Pure numbers in, one bool out.
    /// </summary>
    public static class MineArmingBlinkRule
    {
        public static bool IsShown(float secondsSincePlaced, bool armed, float blinkPeriodSeconds)
        {
            if (armed || blinkPeriodSeconds <= 0f)
                return true;

            // Starts shown, alternates every period. A negative age reads as the start.
            int step = (int)(Mathf.Max(0f, secondsSincePlaced) / blinkPeriodSeconds);
            return (step & 1) == 0;
        }
    }
}
