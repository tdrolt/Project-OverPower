using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Match;

namespace Overpower.Abilities
{
    /// <summary>
    /// The AoE zone ultimate: drops a damage zone at the caster's feet that follows them and ticks for its duration. This
    /// module only decides WHEN and WHERE; what the zone does lives on the networked AoeZone (as ElectricFenceAbility /
    /// ElectricFence). Shared by all three ultimates: cooldownSeconds 0 / charges 1 on the prefab, readiness comes entirely
    /// from UltimateCharge (IsReady reads IsFull; TryBuildCast only casts if Spend() returns true; Point is ctx.Origin).
    ///
    /// THE THROW (D11): while the zone is up and not yet thrown, pressing again is also "ready" and throws it to the
    /// cursor (at most Recast Range from the caster), where it stays. No meter is spent. See AoeZoneRecast and AoeZone.Throw.
    /// </summary>
    public sealed class AoeZoneAbility : AbilityModule
    {
        [SerializeField, Tooltip("The zone that gets placed - a networked object that must live in " +
                 "Assets/Resources (PhotonNetwork.Instantiate resolves it by name). Every number the " +
                 "zone itself uses - radius, damage, duration, tick rate, whether it follows the " +
                 "caster - lives on that prefab; this ability only reads its name to spawn it.")]
        private GameObject zonePrefab;

        [SerializeField, Tooltip("How far from you, in metres, the zone can be thrown. While your zone " +
                 "is up, pressing the ultimate once more throws it to your cursor - a cursor further away " +
                 "than this is cut to this distance in the same direction. The zone then stays where it " +
                 "landed and stops following you. One throw per zone, and it costs no meter.")]
        private float recastRange = 5f;

        // Owner only: the zone this player last placed. A destroyed zone reads as null, which is how
        // "the zone has ended" is known.
        private AoeZone ownZone;

        private AoeZoneRecast.Choice CurrentChoice =>
            AoeZoneRecast.Choose(Owner.UltimateCharge != null && Owner.UltimateCharge.IsFull, ownZone != null,
                                 ownZone != null && ownZone.IsFollowing, ownZone != null && ownZone.Thrown);

        // A caster who dies leaves the zone frozen where they fell; it can no longer be thrown.
        public override void Interrupt(InterruptReason reason)
        {
            if (reason == InterruptReason.Died)
                ownZone = null;
        }

        // A throw is a follow-up to the cast, not a second ultimate use (telemetry).
        internal override bool IsFollowUpCast(in CastPayload payload) => payload.IntArg == RecastIntArg;

        // The meter is the ultimate's only gate; the base class's one-charge pool (0 s cooldown) is
        // never spent, so a throw cannot touch the meter or that pool a second time.
        protected override bool SpendsChargeOnCast => false;

        protected override void OnValidate()
        {
            base.OnValidate();
            recastRange = Mathf.Max(0f, recastRange);
        }

        public override void OnEquip()
        {
            if (zonePrefab == null)
                Debug.LogError($"[AoeZoneAbility] {name}: Zone Prefab is not assigned - casting will do nothing.");
        }

        // ---- owner only ---------------------------------------------------------------------------

        public override bool IsReady => Owner.UltimateCharge != null && CurrentChoice != AoeZoneRecast.Choice.Refuse;

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            AoeZoneRecast.Choice choice = CurrentChoice;
            if (choice == AoeZoneRecast.Choice.Refuse)
            {
                payload = default;
                return false;
            }

            if (choice == AoeZoneRecast.Choice.Throw)
            {
                // The throw: no meter spent. IntArg 1 tells ExecuteCast this is the throw, not a new zone.
                payload = new CastPayload
                {
                    Point = AoeZoneRecast.LandingPoint(ctx.Origin, ctx.TargetPoint, recastRange),
                    IntArg = RecastIntArg,
                };
                return true;
            }

            payload = new CastPayload { Point = ctx.Origin };
            return Owner.UltimateCharge != null && Owner.UltimateCharge.Spend();
        }

        private const int RecastIntArg = 1;

        // ---- every client -------------------------------------------------------------------------

        public override void ExecuteCast(in CastEvent cast)
        {
            if (cast.Phase != 0 || !cast.IsCasterClient || zonePrefab == null)
                return; // Only the caster's own machine ever places the real networked object.

            if (cast.Payload.IntArg == RecastIntArg)
            {
                ThrowOwnZone(cast.Payload.Point);
                return;
            }

            ClearOldThrow();

            // This ability's id, so AoeZone can attribute its damage ticks (DamageInfo.AbilityId) for telemetry.
            GameObject placed = NetworkedDeployable.Spawn(zonePrefab.name, cast.Payload.Point, new object[] { Definition.Id });
            ownZone = placed != null ? placed.GetComponent<AoeZone>() : null;
        }

        // The throw reaches everyone through one Player Property on the caster (no RPC): every copy of
        // the zone - including one placed later on a client that joins after the throw - reads it.
        private void ThrowOwnZone(Vector3 point)
        {
            if (ownZone == null)
                return;

            ownZone.Throw(point);
            PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable
            {
                { AoeZoneRecast.PropertyKey, AoeZoneRecast.Encode(ownZone.photonView.ViewID, point) }
            });
        }

        // A new zone supersedes the last throw's value (keyed to the old zone's id, so it could never
        // grab this one anyway; removing it keeps a reused view id from ever matching).
        private static void ClearOldThrow()
        {
            if (PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey(AoeZoneRecast.PropertyKey))
                PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { AoeZoneRecast.PropertyKey, null } });
        }

        public override string ShopStatsText()
        {
            AoeZone zone = zonePrefab != null ? zonePrefab.GetComponent<AoeZone>() : null;
            if (zone == null)
                return "";
            return ShopNumberFormat.Lines(
                $"Radius {ShopNumberFormat.Compact(zone.Radius)}m · {ShopNumberFormat.Compact(zone.DamagePerTick)} damage every {ShopNumberFormat.Compact(zone.TickSeconds)}s",
                $"Lasts {ShopNumberFormat.Compact(zone.DurationSeconds)}s");
        }
    }
}
