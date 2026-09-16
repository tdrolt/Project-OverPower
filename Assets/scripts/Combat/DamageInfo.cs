using UnityEngine;

namespace Overpower.Combat
{
    public enum DamageSource { Projectile, Splash, Burn, Zone, Contact }

    /// <summary>
    /// Everything the damage funnel needs to resolve one hit. Immutable on purpose: a hit is a
    /// fact, and letting callers mutate it mid-flight is how the two divergent damage paths this
    /// replaces came about.
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;

        /// <summary>
        /// ActorNumber of whoever fired. Inside a Photon RPC this MUST come from
        /// PhotonMessageInfo.Sender, never PhotonNetwork.LocalPlayer, which resolves to the
        /// receiver. Getting this wrong gives kill credit to the victim.
        /// </summary>
        public readonly int SourceActorNumber;

        public readonly int SourceTeamId;
        public readonly int WeaponId;          // -1 when the damage did not come from a weapon
        public readonly DamageSource Source;
        public readonly bool IgnoresArmor;
        public readonly Vector3 HitPoint;

        /// <summary>
        /// Id of the ability that dealt this damage, or -1 when it did not come from an ability
        /// (a weapon shot, or a source with no ability to credit). Task T3 (telemetry): appended
        /// LAST with a default so every existing call site keeps compiling unchanged. Mutually
        /// exclusive with WeaponId in practice - a hit is either a weapon's or an ability's, never
        /// both - but nothing here enforces that; each call site simply passes whichever it has.
        /// </summary>
        public readonly int AbilityId;

        public DamageInfo(float amount, int sourceActorNumber, int sourceTeamId, int weaponId,
                          DamageSource source, bool ignoresArmor, Vector3 hitPoint, int abilityId = -1)
        {
            Amount = amount;
            SourceActorNumber = sourceActorNumber;
            SourceTeamId = sourceTeamId;
            WeaponId = weaponId;
            Source = source;
            IgnoresArmor = ignoresArmor;
            HitPoint = hitPoint;
            AbilityId = abilityId;
        }
    }

    public readonly struct DamageResult
    {
        public readonly float ArmorAbsorbed;
        public readonly float HealthLost;
        public readonly bool ArmorBroke;
        public readonly bool Lethal;

        public DamageResult(float armorAbsorbed, float healthLost, bool armorBroke, bool lethal)
        {
            ArmorAbsorbed = armorAbsorbed;
            HealthLost = healthLost;
            ArmorBroke = armorBroke;
            Lethal = lethal;
        }

        public float Total => ArmorAbsorbed + HealthLost;
    }

    public interface IDamageable
    {
        /// <summary>
        /// Call only on the victim's own client. The victim is the sole authority on its health;
        /// every other client learns the result through serialization.
        /// </summary>
        DamageResult ApplyDamage(in DamageInfo info);

        bool IsAlive { get; }
        int TeamId { get; }
        int ActorNumber { get; }

        /// <summary>
        /// True only on the machine that owns this target - the same machine ApplyDamage already
        /// requires IsMine on for a real player. A mine (Task 1.8) reads this so only the victim's
        /// own client decides to trigger it, the same "only the owner acts, every client calls"
        /// rule the rest of this interface already follows; a test dummy has no owner to defer to,
        /// so it is always locally authoritative over itself.
        /// </summary>
        bool HasLocalAuthority { get; }
    }
}
