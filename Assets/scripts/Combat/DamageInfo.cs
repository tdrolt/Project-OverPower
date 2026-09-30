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

        /// <summary>Mark plan step 3: seconds a mark from this hit's weapon lasts, straight off the
        /// firing weapon's own stat block (WeaponDefinition.MarkWindowSeconds), read on the victim's
        /// client since that is the one place a hit's mark is ever decided. 0 means this hit's source
        /// does not mark at all - MarkLedger.OnLandedHit treats 0 (or less) as "never touch marks",
        /// so a non-marking hit neither places nor cashes one. Appended after AbilityId, following
        /// that field's own precedent, so every existing call site keeps compiling unchanged.</summary>
        public readonly float MarkWindowSeconds;

        /// <summary>Mark plan step 3: this hit's damage, as a multiple of Damage, IF it turns out to
        /// cash an existing mark (MarkLedger.ScaledAmount only ever applies it on a Cashed outcome).
        /// Also off the firing weapon's own stat block (WeaponDefinition.MarkedDamageMultiplier).
        /// Defaults to 1 (no change) for every call site that never mentions marks at all.</summary>
        public readonly float MarkedDamageMultiplier;

        public DamageInfo(float amount, int sourceActorNumber, int sourceTeamId, int weaponId,
                          DamageSource source, bool ignoresArmor, Vector3 hitPoint, int abilityId = -1,
                          float markWindowSeconds = 0f, float markedDamageMultiplier = 1f)
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
        }

        /// <summary>Mark plan step 3: a copy of this hit with only Amount changed - PlayerHealth/
        /// DummyTarget use this to scale a cashed hit's damage up before resolving it, without
        /// disturbing anything else about where the hit came from or what it can still do (its own
        /// mark fields included, so a scaled DamageInfo still reports truthfully what marked it).</summary>
        public DamageInfo WithAmount(float amount) =>
            new DamageInfo(amount, SourceActorNumber, SourceTeamId, WeaponId, Source, IgnoresArmor,
                           HitPoint, AbilityId, MarkWindowSeconds, MarkedDamageMultiplier);
    }

    public readonly struct DamageResult
    {
        public readonly float ArmorAbsorbed;
        public readonly float HealthLost;
        public readonly bool ArmorBroke;
        public readonly bool Lethal;

        /// <summary>Mark plan step 3: what this hit did to the attacker's mark on the victim - None
        /// for every damage source that predates marks, or that simply doesn't mark (its DamageInfo's
        /// MarkWindowSeconds was 0). Read by the credit path (mark step 4) and telemetry (mark step 6).</summary>
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

        /// <summary>Mark plan step 3: a copy of this result with only Mark changed - the funnel builds
        /// the plain damage result first (DamageResolver knows nothing about marks) and stamps the
        /// outcome on afterward, once MarkLedger has decided it.</summary>
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
        /// True only on the machine that owns this target - the same machine ApplyDamage already
        /// requires IsMine on for a real player. A mine (Task 1.8) reads this so only the victim's
        /// own client decides to trigger it, the same "only the owner acts, every client calls"
        /// rule the rest of this interface already follows; a test dummy has no owner to defer to,
        /// so it is always locally authoritative over itself.
        /// </summary>
        bool HasLocalAuthority { get; }
    }
}
