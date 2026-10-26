namespace Overpower.Combat
{
    /// <summary>The one question the damage funnel asks the Invulnerability trap. PlayerStatusEffects implements it; the
    /// tests use a fake. An interface rather than a delegate so the funnel allocates nothing per hit (burn ticks call it
    /// every frame).</summary>
    public interface IArmedShield
    {
        /// <summary>True exactly once, for the first qualifying hit while armed - and disarms as it says so.</summary>
        bool TryConsume(float damageAmount);
    }

    public enum HitVerdict
    {
        /// <summary>Your own shot or splash: no damage, not combat.</summary>
        IgnoredSelf,
        /// <summary>A teammate's: no damage, not combat.</summary>
        IgnoredTeammate,
        /// <summary>A real enemy hit the shield stopped (the armed trap, or the immunity after it): no damage, but it IS
        /// combat - no armour recharge, no shop, no regen while being shot.</summary>
        Shielded,
        /// <summary>A real enemy hit that goes on to DamageResolver.</summary>
        Lands,
    }

    /// <summary>
    /// The order of PlayerHealth.ApplyDamage's early exits, pulled out so it is tested. Self first, then
    /// teammate, then the shield - so a friendly or self hit can neither burn an armed trap nor keep anyone "in
    /// combat". An immunity already running is checked before the trap, so a hit during it never consumes a second arm.
    /// </summary>
    public static class HitVerdictRule
    {
        /// <summary>Does this kind of damage go straight through Invulnerability (a running one and an armed trap alike)? Only the Dominion sudden-death
        /// circle does (A30): it is not an enemy's hit, so it neither meets a running immunity nor springs an armed trap. Armour and damage
        /// reduction or vulnerability still apply to it afterwards, as to any damage.</summary>
        public static bool IgnoresInvulnerability(DamageSource source) => source == DamageSource.SuddenDeath;

        public static HitVerdict Classify(bool fromSelf, bool fromTeammate, bool alreadyInvulnerable,
                                          IArmedShield armedShield, float damageAmount, bool ignoresInvulnerability = false)
        {
            if (fromSelf) return HitVerdict.IgnoredSelf;
            if (fromTeammate) return HitVerdict.IgnoredTeammate;
            if (ignoresInvulnerability) return HitVerdict.Lands; // before the shield: an armed trap must not be used up by a hit it would not stop
            if (alreadyInvulnerable) return HitVerdict.Shielded;
            if (armedShield != null && armedShield.TryConsume(damageAmount)) return HitVerdict.Shielded;
            return HitVerdict.Lands;
        }

        public static bool CountsAsCombat(HitVerdict verdict) =>
            verdict == HitVerdict.Shielded || verdict == HitVerdict.Lands;
    }
}
