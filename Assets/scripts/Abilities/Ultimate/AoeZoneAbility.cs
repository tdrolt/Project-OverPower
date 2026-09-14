using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// The AoE zone ultimate: drops a damage zone at the caster's own feet that follows them and
    /// ticks for its whole duration - Tudor's spec (2026-09-13), "self-cast radius lasting 6 seconds,
    /// dealing damage every second. Follows the caster." This module only decides WHEN and WHERE the
    /// zone goes down; everything about what it does once it exists lives on AoeZone.cs, the
    /// networked object it places - the same split ElectricFenceAbility/ElectricFence use.
    ///
    /// FIT WITH 1.0 (shared by all three ultimates): cooldownSeconds 0 / charges 1 on the base class
    /// (set on this prefab) - readiness comes entirely from UltimateCharge. IsReady reads
    /// Owner.UltimateCharge.IsFull; TryBuildCast only agrees to cast if Spend() actually returns
    /// true, and Point is ctx.Origin - the caster's own current position.
    /// </summary>
    public sealed class AoeZoneAbility : AbilityModule
    {
        [SerializeField, Tooltip("The zone that gets placed - a networked object that must live in " +
                 "Assets/Resources (PhotonNetwork.Instantiate resolves it by name). Every number the " +
                 "zone itself uses - radius, damage, duration, tick rate, whether it follows the " +
                 "caster - lives on that prefab; this ability only reads its name to spawn it.")]
        private GameObject zonePrefab;

        public override void OnEquip()
        {
            if (zonePrefab == null)
                Debug.LogError($"[AoeZoneAbility] {name}: Zone Prefab is not assigned - casting will do nothing.");
        }

        // ---- owner only ---------------------------------------------------------------------------

        public override bool IsReady => Owner.UltimateCharge != null && Owner.UltimateCharge.IsFull;

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = new CastPayload { Point = ctx.Origin };
            return Owner.UltimateCharge != null && Owner.UltimateCharge.Spend();
        }

        // ---- every client -------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            if (cast.Phase != 0 || !cast.IsCasterClient || zonePrefab == null)
                return; // Only the caster's own machine ever places the real networked object.

            NetworkedDeployable.Spawn(zonePrefab.name, cast.Payload.Point, null);
        }
    }
}
