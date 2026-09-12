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

        public DamageInfo(float amount, int sourceActorNumber, int sourceTeamId, int weaponId,
                          DamageSource source, bool ignoresArmor, Vector3 hitPoint)
        {
            Amount = amount;
            SourceActorNumber = sourceActorNumber;
            SourceTeamId = sourceTeamId;
            WeaponId = weaponId;
            Source = source;
            IgnoresArmor = ignoresArmor;
            HitPoint = hitPoint;
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
    }
}
