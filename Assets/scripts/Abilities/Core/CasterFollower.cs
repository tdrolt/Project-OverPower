using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// Shared "follow the caster, but stop and freeze the moment they die or leave" behaviour for
    /// the two Task 1.11b ultimates whose prefab keeps a followsCaster checkbox (Electric Fence
    /// default OFF, AoE Zone default ON - Tudor's 2026-09-13 spec: the fence stays where it is cast,
    /// the zone follows the caster).
    ///
    /// NOT A LITERAL Transform.SetParent. Unity destroys every child the instant its parent is
    /// destroyed, and a player who leaves the room is exactly that on this project (OnPlayerLeftRoom
    /// is an empty no-op in PlayerLifecycle/PlayerLoadout - it is PUN's own room auto-cleanup that
    /// actually removes a departed player's networked objects). A hard parent would silently delete a
    /// zone still owed ticks the moment its caster disconnected. A plain per-frame position copy has
    /// no such failure mode: it just stops copying once there is nothing left to read from, wherever
    /// that leaves the object - which is exactly the addendum's own decision for a caster who dies or
    /// leaves mid-cast: "the zone stays where the caster was and finishes its ticks" [C].
    ///
    /// PERMANENT ONCE STOPPED. A caster who dies and later respawns must not drag a zone that is
    /// still finishing its ticks all the way across the map to the new spawn point - so the moment
    /// following stops (death or disconnect), it never resumes, even if IsAlive later flips back on.
    ///
    /// Resolved once, at placement time, via PlayerLookup - read, never modified, per the Task 1.11b
    /// brief.
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

        /// <summary>Call every frame this object is alive. Snaps target to the caster's current
        /// position while the caster still exists and is alive; the first frame that stops being true
        /// switches this off for good, leaving target exactly where it last was.</summary>
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
