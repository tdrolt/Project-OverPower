using Overpower.Combat;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>How high a standing player's root is above the floor, read once from the local player's own
    /// CapsuleCollider - the derivation TestRangeSpawner.Grounded uses. Visual only: for any visual that must match
    /// where a standing player's body really reaches (A6, Tudor 2026-09-17 evening: rockets stay at their real
    /// height now, so this no longer sizes a floor ring - kept for whatever else needs "how tall is a player" without
    /// retyping the number).</summary>
    public static class StandingBody
    {
        private static float cached = -1f;

        /// <summary>0 until a local player exists. A ring cut at the floor instead is a little smaller, never larger,
        /// than the real reach.</summary>
        public static float RootAboveFeet()
        {
            if (cached >= 0f)
                return cached;

            PhotonView view = PhotonNetwork.LocalPlayer != null ? PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber) : null;
            CapsuleCollider capsule = view != null ? view.GetComponent<CapsuleCollider>() : null;
            if (capsule == null)
                return 0f;

            cached = Mathf.Max(0f, AbilityVisualGeometry.RootAboveFeet(capsule.center.y, capsule.height, capsule.transform.lossyScale.y));
            return cached;
        }
    }
}
