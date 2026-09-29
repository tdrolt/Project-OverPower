namespace Overpower.Combat
{
    /// <summary>
    /// Whether a mine's model is drawn this frame while it is still arming: it blinks on and off every
    /// blinkPeriodSeconds so anyone who can see it knows it cannot go off yet, then shows steadily once armed.
    /// "Armed" is MineDetonationState.IsArmed's own threshold, so the blink stops the instant the mine can trigger.
    /// Pure numbers in, one bool out - no scene, no Photon.
    /// </summary>
    public static class MineArmingBlinkRule
    {
        public static bool IsShown(float secondsSincePlaced, float armDelaySeconds, float blinkPeriodSeconds)
        {
            if (blinkPeriodSeconds <= 0f || new MineDetonationState(armDelaySeconds).IsArmed(secondsSincePlaced))
                return true;

            // Starts shown, alternates every period.
            int step = (int)(secondsSincePlaced / blinkPeriodSeconds);
            return (step & 1) == 0;
        }
    }
}
