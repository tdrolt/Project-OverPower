using Overpower.Combat;

namespace Overpower.UI
{
    /// <summary>What the vent band on the overheat bar should look like right now. Hidden/Dim/
    /// Bright/Hit/Miss rather than a raw colour, so PlayerHud is the only place that ever touches a
    /// UiTheme colour for it.</summary>
    public enum VentBandLook
    {
        Hidden,
        Dim,
        Bright,
        Hit,
        Miss
    }

    /// <summary>
    /// The vent band's look as a pure function of state, tested without a scene (VentBandLookRuleTests,
    /// OverheatStateTests).
    /// </summary>
    public static class VentBandLookRule
    {
        /// <summary>Hidden whenever not silenced (which also covers death - OverheatState.Clear drops IsSilenced)
        /// or whenever ventEnabled is false: ventWindow &lt;= 0 means Vent is off (GameplayConfig's "0 = off"), so
        /// not even a Missed band may appear. OverheatState.Outcome reads None throughout then, but windowOpen alone
        /// can't be told from "silenced, window not open yet", hence the flag. While silenced and enabled the
        /// outcome, once there is one, wins over the window (a hit or miss is settled for the rest of the silence;
        /// a miss stays until it ends); with no outcome, Bright exactly while the window is open, else Dim.</summary>
        public static VentBandLook Determine(bool isSilenced, bool windowOpen, VentOutcome outcome, bool ventEnabled = true)
        {
            if (!isSilenced || !ventEnabled)
                return VentBandLook.Hidden;

            switch (outcome)
            {
                case VentOutcome.Hit:
                    return VentBandLook.Hit;
                case VentOutcome.Missed:
                    return VentBandLook.Miss;
                default:
                    return windowOpen ? VentBandLook.Bright : VentBandLook.Dim;
            }
        }
    }
}
