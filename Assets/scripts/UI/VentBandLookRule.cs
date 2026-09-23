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
    /// Pulled out of PlayerHud.UpdateOverheat so the vent band's look is a pure function of state -
    /// testable without a scene, unlike the MonoBehaviour that draws it (OverheatStateTests/
    /// VentBandLookRuleTests exercise every branch that Play Mode alone could never conveniently
    /// hit on demand).
    /// </summary>
    public static class VentBandLookRule
    {
        /// <summary>Hidden whenever not silenced (which also covers death - OverheatState.Clear
        /// drops IsSilenced along with everything else). While silenced: the outcome, once there is
        /// one, always wins over the window being open or closed (a hit or a miss is a settled fact
        /// for the rest of this silence, per PlayerHud's own "miss stays until the silence ends"
        /// rule); with no outcome yet, Bright exactly while the window is open, Dim the rest of the
        /// silence.</summary>
        public static VentBandLook Determine(bool isSilenced, bool windowOpen, VentOutcome outcome)
        {
            if (!isSilenced)
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
