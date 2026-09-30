namespace Overpower.UI
{
    /// <summary>What the HUD's Ultimate slot shows.</summary>
    public enum UltimateSlotState { NoUltimate, Filling, Ready, Throw }

    /// <summary>Pure rule for the Ultimate slot's display. No ultimate equipped wins over everything
    /// (the meter keeps filling underneath, it just is not shown); otherwise a full meter is Ready, an
    /// empty meter with a usable slot is the AoE Zone's Throw, else the meter is filling.</summary>
    public static class UltimateSlotRule
    {
        public static UltimateSlotState StateFor(bool equipped, bool meterFull, bool slotUsable)
        {
            if (!equipped) return UltimateSlotState.NoUltimate;
            if (meterFull) return UltimateSlotState.Ready;
            return slotUsable ? UltimateSlotState.Throw : UltimateSlotState.Filling;
        }
    }
}
