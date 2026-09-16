using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The electric fence ultimate: drops a ring at the caster's own feet that damages and slows any
    /// enemy passing through it - Tudor's spec (2026-09-13), "stays where it is cast". This module
    /// only decides WHEN and WHERE the ring goes down; everything about what the ring actually does
    /// once it exists lives on ElectricFence.cs, the networked object it places - the same split
    /// DeployableCoverAbility/CoverWall already use.
    ///
    /// FIT WITH 1.0 (the addendum's own wording, shared by all three ultimates): cooldownSeconds 0 /
    /// charges 1 on the base class (set on this prefab) - readiness comes entirely from
    /// UltimateCharge, never the base pool. IsReady reads Owner.UltimateCharge.IsFull; TryBuildCast
    /// only agrees to cast if Owner.UltimateCharge.Spend() actually returns true, and Point is
    /// ctx.Origin - the caster's own current position, nothing the cursor picks.
    ///
    /// SELF-CAST, NO TARGET TO VALIDATE - unlike Teleport or Deployable Cover there is no ground
    /// probe or block check: the ring appears wherever the caster is standing, full stop.
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

        /// <summary>Self-cast: the only thing this ability needs from ctx is where the caster is
        /// standing right now. Refuses the cast (so the base class's own SpendCharge never runs
        /// either) unless UltimateCharge itself agrees to spend.</summary>
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

            // Task T3 (telemetry): this ability's own id, so ElectricFence can attribute its own
            // damage passes to it (DamageInfo.AbilityId) - see ElectricFence.OnPlaced.
            NetworkedDeployable.Spawn(fencePrefab.name, cast.Payload.Point, new object[] { Definition.Id });
        }
    }
}
