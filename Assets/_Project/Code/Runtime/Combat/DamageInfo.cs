using UnityEngine;

namespace Overpower.Combat
{
    public enum DamageSource
    {
        Projectile, Splash, Burn, Zone, Contact,
        /// <summary>The Dominion sudden-death circle: no attacker, and it ignores Invulnerability (A30). Appended last: the numbers are logged, so never renumber.</summary>
        SuddenDeath,
    }

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
        /// Id of the ability that dealt this damage, or -1 when it did not come from an ability (a
        /// weapon shot, or a source with no ability to credit). Mutually exclusive with WeaponId in
        /// practice (a hit is a weapon's or an ability's, never both), but nothing here enforces that.
        /// </summary>
        public readonly int AbilityId;

        /// <summary>Seconds a mark from this hit's weapon lasts, straight off the firing weapon's stat
        /// block (WeaponDefinition.MarkWindowSeconds), read on the victim's client since that is the one
        /// place a hit's mark is decided. 0 means this source does not mark: MarkLedger.OnLandedHit
        /// treats 0 (or less) as "never touch marks", so a non-marking hit neither places nor cashes
        /// one.</summary>
        public readonly float MarkWindowSeconds;

        /// <summary>This hit's damage as a multiple of Damage, IF it cashes an existing mark
        /// (MarkLedger.ScaledAmount applies it only on a Cashed outcome). Off the firing weapon's stat
        /// block (WeaponDefinition.MarkedDamageMultiplier). Defaults to 1 (no change).</summary>
        public readonly float MarkedDamageMultiplier;

        /// <summary>The server time in ms when the lasting effect behind this hit was set up - a mine, a fire field, an
        /// electric fence, a zone, a burn. The victim compares it with the attacker's respawn shield to tell an effect from before the respawn
        /// (it must not end the attacker's new bubble) from one set up after it (A26). 0 = a direct hit (a shot, a blast) with nothing set up
        /// earlier.</summary>
        public readonly int EffectPlacedMs;

        public DamageInfo(float amount, int sourceActorNumber, int sourceTeamId, int weaponId,
                          DamageSource source, bool ignoresArmor, Vector3 hitPoint, int abilityId = -1,
                          float markWindowSeconds = 0f, float markedDamageMultiplier = 1f, int effectPlacedMs = 0)
        {
            Amount = amount;
            SourceActorNumber = sourceActorNumber;
            SourceTeamId = sourceTeamId;
            WeaponId = weaponId;
            Source = source;
            IgnoresArmor = ignoresArmor;
            HitPoint = hitPoint;
            AbilityId = abilityId;
            MarkWindowSeconds = markWindowSeconds;
            MarkedDamageMultiplier = markedDamageMultiplier;
            EffectPlacedMs = effectPlacedMs;
        }

        /// <summary>A copy of this hit with only Amount changed: PlayerHealth/DummyTarget use it to scale
        /// a cashed hit's damage up before resolving it, leaving everything else about the hit (its mark
        /// fields included) as it was.</summary>
        public DamageInfo WithAmount(float amount) =>
            new DamageInfo(amount, SourceActorNumber, SourceTeamId, WeaponId, Source, IgnoresArmor,
                           HitPoint, AbilityId, MarkWindowSeconds, MarkedDamageMultiplier, EffectPlacedMs);
    }

    public readonly struct DamageResult
    {
        public readonly float ArmorAbsorbed;
        public readonly float HealthLost;
        public readonly bool ArmorBroke;
        public readonly bool Lethal;

        /// <summary>What this hit did to the attacker's mark on the victim: None for a source that does
        /// not mark (its DamageInfo's MarkWindowSeconds was 0). Read by the credit path and telemetry.</summary>
        public readonly MarkOutcome Mark;

        public DamageResult(float armorAbsorbed, float healthLost, bool armorBroke, bool lethal, MarkOutcome mark = MarkOutcome.None)
        {
            ArmorAbsorbed = armorAbsorbed;
            HealthLost = healthLost;
            ArmorBroke = armorBroke;
            Lethal = lethal;
            Mark = mark;
        }

        public float Total => ArmorAbsorbed + HealthLost;

        /// <summary>A copy of this result with only Mark changed: the funnel builds the plain damage
        /// result first (DamageResolver knows nothing about marks) and stamps the outcome on once
        /// MarkLedger has decided it.</summary>
        public DamageResult WithMark(MarkOutcome mark) => new DamageResult(ArmorAbsorbed, HealthLost, ArmorBroke, Lethal, mark);
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
        /// True only on the machine that owns this target, the same machine ApplyDamage requires IsMine
        /// on for a real player. A mine reads it so only the victim's own client decides to trigger, the
        /// same "only the owner acts, every client calls" rule as the rest of this interface; a test
        /// dummy has no owner to defer to, so it is always locally authoritative over itself.
        /// </summary>
        bool HasLocalAuthority { get; }
    }
}
