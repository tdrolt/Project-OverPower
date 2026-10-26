namespace Overpower.Combat
{
    /// <summary>When a mech flashes white for damage, and how bright the flash still is. Decided on every copy of the victim that runs the
    /// damage (bullets and blasts run on every client, the fire field too; a burn, the fence and the sudden-death circle only on the victim's
    /// own client), so the flash needs no message; damage the victim's client would turn away (self, teammate, the yellow immunity, the
    /// respawn shield) is turned away here from the looks those already show. Damage over time starts a flash at most once per gap: its
    /// ticks come every frame, and refreshing on each would keep a burning mech white.</summary>
    public static class HitFlashRule
    {
        public static bool FlashesOnHit(DamageSource source, float amount, bool victimAlive, bool fromSelf, bool fromTeammate,
                                        bool victimImmune, bool victimRespawnShielded) =>
            amount > 0f && victimAlive && !fromSelf && !fromTeammate && !victimRespawnShielded
            && (!victimImmune || HitVerdictRule.IgnoresInvulnerability(source));

        /// <summary>A hit always starts the flash over; damage over time only once <paramref name="overTimeGapSeconds"/> has passed since the
        /// last flash began (hits included), and a tick inside the gap neither restarts nor stretches the one showing.</summary>
        public static bool StartsFlash(DamageSource source, float secondsSinceLastFlash, float overTimeGapSeconds) =>
            !IsOverTime(source) || secondsSinceLastFlash >= overTimeGapSeconds;

        private static bool IsOverTime(DamageSource source) =>
            source == DamageSource.Burn || source == DamageSource.Zone || source == DamageSource.SuddenDeath;

        /// <summary>1 from the hit until <paramref name="flashSeconds"/>, then 0; 0 when the flash has no length. Held, never faded: the flash
        /// swaps the glow-line map for plain white, so a dimming white would be an even grey with the team's glow lines gone.</summary>
        public static float Strength(float secondsSinceHit, float flashSeconds) =>
            flashSeconds <= 0f || secondsSinceHit < 0f || secondsSinceHit >= flashSeconds ? 0f : 1f;

        public static bool Shows(bool victimAlive, float strength) => victimAlive && strength > 0f;
    }
}
