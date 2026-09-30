namespace Overpower.Combat
{
    /// <summary>
    /// The pure HP rule behind CoverWall's damage funnel, pulled out so it is provable without a
    /// scene or PhotonNetwork - the same reasoning as MineDetonationState. Friendly filtering is NOT
    /// this class's job: CoverWall.ApplyDamage decides that first, with FriendlyFire.IsSelfOrTeammate
    /// against the wall's own OwnerActor/OwnerTeam (already covered by FriendlyFireTests) - by the
    /// time a hit reaches this class, it has already been decided that the hit is allowed to cost HP
    /// at all. What is left, and what this class owns, is the Task 1.8 addendum's other two rules:
    /// does the SOURCE count, and how much is left before Tudor's 100 destroys it.
    /// </summary>
    public sealed class CoverDamageState
    {
        private readonly float maxHitPoints;

        /// <summary>Total HP actually absorbed so far - never exceeds MaxHitPoints.</summary>
        public float DamageAbsorbed { get; private set; }

        public bool Destroyed { get; private set; }

        public CoverDamageState(float maxHitPoints)
        {
            this.maxHitPoints = maxHitPoints;
        }

        /// <summary>Cover is about projectiles, Tudor's addendum: a Burn or Zone tick washing over
        /// it costs it nothing - this is cover to fight around, not a punching bag for a DoT.</summary>
        public static bool CountsTowardHitPoints(DamageSource source) =>
            source == DamageSource.Projectile || source == DamageSource.Splash;

        /// <summary>
        /// Absorbs amount if the source counts, clamped so DamageAbsorbed can never exceed
        /// MaxHitPoints - returns how much was actually absorbed. Once Destroyed, every further call
        /// is a no-op returning 0, the same "first call wins" shape as
        /// MineDetonationState.TryDetonate, so a frame with two hits after the wall is already at 0
        /// remaining HP cannot report a negative remainder or ask the caller to destroy it twice.
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
