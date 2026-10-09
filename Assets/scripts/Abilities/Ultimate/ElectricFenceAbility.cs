using UnityEngine;
using Overpower.Match;

namespace Overpower.Abilities
{
    /// <summary>
    /// The electric fence ultimate: drops a ring at the caster's feet that damages and slows enemies passing through it.
    /// This module only decides WHEN and WHERE; what the ring does lives on the networked ElectricFence (as
    /// DeployableCoverAbility / CoverWall). Shared by all three ultimates: cooldownSeconds 0 / charges 1 on the prefab,
    /// readiness comes entirely from UltimateCharge, never the base pool (IsReady reads IsFull; TryBuildCast only casts
    /// if Spend() returns true; Point is ctx.Origin, nothing the cursor picks). SELF-CAST: unlike Teleport or Deployable
    /// Cover there is no ground probe or block check; the ring appears wherever the caster stands.
    /// </summary>
    public sealed class ElectricFenceAbility : AbilityModule
    {
        [SerializeField, Tooltip("The fence that gets placed - a networked object that must live in " +
                 "Assets/Resources (PhotonNetwork.Instantiate resolves it by name). Every number the " +
                 "ring itself uses - radius, band width, damage, slow, cooldown, duration, whether it " +
                 "follows the caster - lives on that prefab; this ability only reads its name to spawn it.")]
        private GameObject fencePrefab;

        public override void OnEquip()
        {
            if (fencePrefab == null)
                Debug.LogError($"[ElectricFenceAbility] {name}: Fence Prefab is not assigned - casting will do nothing.");
        }

        // ---- owner only ---------------------------------------------------------------------------

        /// <summary>Full meter only - see the class comment. Never the base class's own charge/
        /// cooldown, which recovers the instant it is spent precisely so this is the only real gate.</summary>
        public override bool IsReady => Owner.UltimateCharge != null && Owner.UltimateCharge.IsFull;

        /// <summary>Self-cast: only the caster's position is needed. Refuses the cast (so the base SpendCharge never runs)
        /// unless UltimateCharge agrees to spend.</summary>
        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = new CastPayload { Point = ctx.Origin };
            return Owner.UltimateCharge != null && Owner.UltimateCharge.Spend();
        }

        // ---- every client -------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            if (cast.Phase != 0 || !cast.IsCasterClient || fencePrefab == null)
                return; // Only the caster's own machine ever places the real networked object.

            // This ability's id, so ElectricFence can attribute its damage passes (DamageInfo.AbilityId) for telemetry.
            NetworkedDeployable.Spawn(fencePrefab.name, cast.Payload.Point, new object[] { Definition.Id });
        }

        public override string ShopStatsText()
        {
            ElectricFence fence = fencePrefab != null ? fencePrefab.GetComponent<ElectricFence>() : null;
            if (fence == null)
                return "";
            return ShopNumberFormat.Lines(
                $"Radius {ShopNumberFormat.Compact(fence.Radius)}m · {ShopNumberFormat.Compact(fence.DamagePerPass)} damage per pass",
                $"Slows {ShopNumberFormat.Compact(fence.SlowMagnitude * 100f)}% for {ShopNumberFormat.Compact(fence.SlowSeconds)}s" +
                (fence.LifetimeSeconds > 0f ? $" · lasts {ShopNumberFormat.Compact(fence.LifetimeSeconds)}s" : ""));
        }
    }
}
