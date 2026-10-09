using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// "Follow the caster, but freeze the moment they die or leave", for the ultimates whose prefab keeps a
    /// followsCaster checkbox (Electric Fence default off, AoE Zone default on).
    /// NOT a Transform.SetParent: Unity destroys a child with its parent, and a player leaving the room is exactly that
    /// here (PUN's room auto-cleanup removes their objects), which would delete a zone still owed ticks. A per-frame
    /// position copy just stops copying: "the zone stays where the caster was and finishes its ticks" [C].
    /// PERMANENT ONCE STOPPED: a respawned caster must not drag a finishing zone across the map to the new spawn.
    /// The caster is resolved once at placement via PlayerLookup (read only).
    /// </summary>
    public sealed class CasterFollower
    {
        private readonly PhotonView casterView;
        private readonly PlayerLifecycle casterLifecycle;
        private bool active;

        /// <summary>followsCaster false, or nobody registered yet for ownerActor, means Tick never
        /// does anything - the object simply stays exactly where NetworkedDeployable placed it.</summary>
        public CasterFollower(bool followsCaster, int ownerActor)
        {
            if (!followsCaster)
                return;

            casterView = PlayerLookup.GetPhotonViewFor(ownerActor);
            if (casterView == null)
            {
                Debug.LogWarning($"[CasterFollower] Follows Caster is checked, but no PhotonView is " +
                                  $"registered yet for actor {ownerActor} - staying where it was cast.");
                return;
            }

            casterLifecycle = casterView.GetComponent<PlayerLifecycle>();
            active = true;
        }

        /// <summary>Stops following for good - the AoE Zone's throw (D11) leaves it where it landed.</summary>
        public void Stop() => active = false;

        /// <summary>True while this still follows the caster (not stopped, dead or gone).</summary>
        public bool IsActive => active;

        /// <summary>Call every frame this object is alive. Snaps target to the caster's current
        /// position while the caster still exists and is alive; the first frame that stops being true
        /// switches this off for good, leaving target exactly where it last was.
        ///
        /// ON A NON-CASTER CLIENT, casterView.transform.position IS THE NETWORK-LERPED COPY: PlayerNetSync smooths
        /// a remote player toward their last RECEIVED position, so a follower (AoeZone) can sit a little off where the
        /// caster's own screen shows them, and a victim's client ticks damage against this copy. The same accepted
        /// victim-favours-the-defender latency tradeoff every projectile makes; not corrected here.</summary>
        public void Tick(Transform target)
        {
            if (!active)
                return;

            if (casterView == null || casterView.gameObject == null ||
                (casterLifecycle != null && !casterLifecycle.IsAlive))
            {
                active = false;
                return;
            }

            target.position = casterView.transform.position;
        }
    }
}
