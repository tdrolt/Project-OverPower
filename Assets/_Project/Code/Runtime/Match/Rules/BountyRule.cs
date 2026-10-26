namespace Overpower.Match
{
    /// <summary>GDD p.20 "bounce-back": a zone one team held uninterrupted for the hold time pays its
    /// bounty to the enemy team that takes it. Losing a zone and retaking it yourself pays nothing,
    /// and a contested zone that never went neutral never ended its hold.</summary>
    public static class BountyRule
    {
        public static int PayoutOnCapture(int newOwner, int lastOwner, int lastHeldMs, int bounty, int holdMs)
        {
            if (newOwner < 0 || lastOwner < 0 || newOwner == lastOwner || bounty <= 0)
                return 0;
            return lastHeldMs >= holdMs ? bounty : 0;
        }
    }
}
