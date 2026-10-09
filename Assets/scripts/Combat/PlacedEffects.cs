using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// The one place that builds the damage and status of a lasting effect (a mine, a fence, a zone, a fire field, a burn), so each
    /// carries the server time it was set up (A26). The components call these instead of building the DamageInfo inline, so a test can
    /// say "a mine's hit carries when the mine was laid" and the game cannot quietly stop doing it.
    /// </summary>
    public static class PlacedEffects
    {
        public static DamageInfo MineBlast(float amount, int actor, int team, Vector3 at, int abilityId, int placedMs) =>
            new DamageInfo(amount, actor, team, -1, DamageSource.Splash, false, at, abilityId, effectPlacedMs: placedMs);

        public static DamageInfo FenceTick(float amount, int actor, int team, Vector3 at, int abilityId, int placedMs) =>
            new DamageInfo(amount, actor, team, -1, DamageSource.Zone, false, at, abilityId, effectPlacedMs: placedMs);

        public static DamageInfo AoeZoneTick(float amount, int actor, int team, Vector3 at, int abilityId, int placedMs) =>
            new DamageInfo(amount, actor, team, -1, DamageSource.Zone, false, at, abilityId, effectPlacedMs: placedMs);

        public static DamageInfo FireFieldBurn(float amount, int actor, int team, int weaponId, Vector3 at, int placedMs) =>
            new DamageInfo(amount, actor, team, weaponId, DamageSource.Burn, false, at, -1, effectPlacedMs: placedMs);

        public static DamageInfo StatusBurn(float amount, int actor, int team, Vector3 at, int abilityId, int appliedMs) =>
            new DamageInfo(amount, actor, team, -1, DamageSource.Burn, false, at, abilityId, effectPlacedMs: appliedMs);

        /// <summary>The slow a mine or a fence puts on an enemy; it remembers when the mine or fence was set up.</summary>
        public static StatusEffectSpec PlacedSlow(float duration, float magnitude, int abilityId, int placedMs) =>
            new StatusEffectSpec { kind = StatusKind.Slow, duration = duration, magnitude = magnitude, abilityId = abilityId, effectPlacedMs = placedMs };
    }
}
