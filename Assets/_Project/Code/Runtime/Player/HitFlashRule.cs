namespace Overpower.Combat
{
    /// <summary>When a mech flashes white for a hit, and how bright the flash still is. Decided on every client's own copy of the victim
    /// from what that copy already knows (every client simulates every shot), so the flash needs no message; a hit the victim's own client
    /// would turn away (self, teammate, the yellow immunity, the respawn shield) is turned away here from the looks those already show.
    /// Damage over time does not flash: its ticks would keep a burning mech white, and the flames, fence arcs and circle already show it.</summary>
    public static class HitFlashRule
    {
        public static bool FlashesOnHit(DamageSource source, float amount, bool victimAlive, bool fromSelf, bool fromTeammate,
                                        bool victimImmune, bool victimRespawnShielded) =>
            IsAHit(source) && amount > 0f && victimAlive && !fromSelf && !fromTeammate && !victimImmune && !victimRespawnShielded;

        private static bool IsAHit(DamageSource source) =>
            source == DamageSource.Projectile || source == DamageSource.Splash || source == DamageSource.Contact;

        /// <summary>1 from the hit until <paramref name="flashSeconds"/>, then 0; 0 when the flash has no length. Held, never faded: the flash
        /// swaps the glow-line map for plain white, so a dimming white would be an even grey with the team's glow lines gone.</summary>
        public static float Strength(float secondsSinceHit, float flashSeconds) =>
            flashSeconds <= 0f || secondsSinceHit < 0f || secondsSinceHit >= flashSeconds ? 0f : 1f;

        public static bool Shows(bool victimAlive, float strength) => victimAlive && strength > 0f;
    }
}
