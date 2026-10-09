namespace Overpower.Combat
{
    /// <summary>
    /// The pure HP rule behind CoverWall's damage funnel, provable without a scene or PhotonNetwork.
    /// Friendly filtering is NOT this class's job: CoverWall.ApplyDamage decides that first with
    /// FriendlyFire.IsSelfOrTeammate. This class owns the other two rules: does the SOURCE count, and
    /// how much HP is left before the wall is destroyed.
    /// </summary>
    public sealed class CoverDamageState
    {
        private readonly float maxHitPoints;

        /// <summary>Total HP absorbed so far; never exceeds the maximum.</summary>
        public float DamageAbsorbed { get; private set; }

        public bool Destroyed { get; private set; }

        public CoverDamageState(float maxHitPoints)
        {
            this.maxHitPoints = maxHitPoints;
        }

        /// <summary>Cover is about projectiles: a Burn or Zone tick costs it nothing (cover to fight
        /// around, not a punching bag for a DoT).</summary>
        public static bool CountsTowardHitPoints(DamageSource source) =>
            source == DamageSource.Projectile || source == DamageSource.Splash;

        /// <summary>
        /// Absorbs amount if the source counts, clamped to the HP left; returns how much was absorbed.
        /// Once Destroyed every call returns 0 (the same first-call-wins shape as
        /// MineDetonationState.TryDetonate), so two hits in one frame cannot report a negative
        /// remainder or ask the caller to destroy the wall twice.
        /// </summary>
        public float ApplyDamage(float amount, DamageSource source)
        {
            if (Destroyed || amount <= 0f || !CountsTowardHitPoints(source))
                return 0f;

            float remaining = maxHitPoints - DamageAbsorbed;
            if (remaining <= 0f)
                return 0f;

            float absorbed = amount < remaining ? amount : remaining;
            DamageAbsorbed += absorbed;

            if (DamageAbsorbed >= maxHitPoints)
                Destroyed = true;

            return absorbed;
        }
    }
}
