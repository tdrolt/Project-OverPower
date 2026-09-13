using System.Collections;
using Photon.Pun;
using UnityEngine;
using Overpower.Net;

namespace Overpower.Abilities
{
    /// <summary>
    /// The shared base for every ability that leaves a real networked object behind rather than a
    /// local, RPC-rebuilt one - a teleport portal today (Task 1.7a), mines, deployable cover, a
    /// fence and a zone later (Tasks 1.8/1.11). See FireField's own class comment
    /// (Assets/scripts/Weapons) for why something that OUTLIVES a single frame needs
    /// PhotonNetwork.Instantiate at all rather than the RPC_FireWeapon-style local rebuild every
    /// projectile uses: a player who joins the match while it is still standing there has to see it,
    /// which only a real networked object gives.
    ///
    /// WHAT THIS BASE OWNS: who placed it, and when. OnPhotonInstantiate resolves OwnerActor and
    /// OwnerTeam from info.Sender - never from PhotonNetwork.LocalPlayer, which inside this callback
    /// is whichever machine is RECEIVING the spawn, not who sent it (the same "local means the
    /// receiver" trap WeaponFiring's and AbilityRunner's own class comments call out). Age is
    /// PhotonNetwork.Time minus info.SentServerTime - the moment the OWNER's client actually sent
    /// the instantiate call. For a late joiner, PUN replays the room's cached instantiations with
    /// their ORIGINAL SentServerTime intact, so Age already reads as "how long ago this was really
    /// placed" for them too, not "since I joined". A subclass that wants a maximum lifetime sets
    /// Lifetime Seconds; this base schedules PhotonNetwork.Destroy at lifetime - Age, owner only -
    /// the same single-destroyer rule FireField.Burn documents, where every non-owner calling
    /// PhotonNetwork.Destroy on the same object logs one error each.
    ///
    /// WHAT THIS BASE DOES NOT OWN: anything about what the object looks like, does, or how its own
    /// numbers arrive. A subclass overrides OnPlaced to unpack its own instantiationData and do
    /// whatever "being placed" means for it - Portal registers itself and sizes its ring; a future
    /// mine would arm itself. OnPlaced runs for EVERY client, including a late joiner replaying this
    /// from the room's cache, which matters for Portal: the pairing registry has to exist there too,
    /// even though only the owner's own client ever queries it (see Portal's class comment).
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public abstract class NetworkedDeployable : MonoBehaviourPun, IPunInstantiateMagicCallback
    {
        [SerializeField, Tooltip("Maximum seconds this object exists before it deletes itself, " +
                 "counted from the moment it was ORIGINALLY placed - a late joiner does not get extra " +
                 "time just for joining late. 0 means it never expires on its own; something else " +
                 "must destroy it (a portal is destroyed only when its owner unequips Mobility).")]
        private float lifetimeSeconds = 0f;

        /// <summary>The actor number of whoever placed this - from the instantiate's own sender, set
        /// once in OnPhotonInstantiate and never changed after.</summary>
        public int OwnerActor { get; private set; } = -1;

        /// <summary>The owner's team at the moment this was placed, or -1 if it was not yet known.
        /// Read once rather than live (unlike AbilityOwner.TeamId): nothing here needs to track a
        /// team change mid-match, only "whose is this".</summary>
        public int OwnerTeam { get; private set; } = -1;

        /// <summary>Seconds since this was ACTUALLY placed - PhotonNetwork.Time minus the
        /// instantiate's own SentServerTime - so a late joiner's cached replay reports the true age,
        /// never zero.</summary>
        public double Age { get; private set; }

        /// <summary>True on the one machine that placed this object - the only machine allowed to
        /// PhotonNetwork.Destroy it. A subclass destroying itself early (Portal's "oldest of three")
        /// must still guard on this the same way this base does for the lifetime timer.</summary>
        protected bool IsOwnerClient => photonView.IsMine;

        // Guards PhotonNetwork.Destroy itself, not just who may call it: a mine can be ended by
        // THREE independent paths that know nothing of each other - this base's own lifetime timer,
        // a detonation, and a caller pruning the oldest of a capped set (MineAbility.PruneOldest) -
        // and nothing stops two of them from deciding to destroy the same object in the same window.
        // Object.Destroy does not null a reference until the end of the frame, so a same-frame second
        // call would not be caught by a plain "is this null yet" check; a same-frame issue is exactly
        // the "eight errors per cast" FireField's class comment warns about. Every destroy path must
        // go through RequestDestroy below instead of calling PhotonNetwork.Destroy directly.
        private bool destroyRequested;

        /// <summary>
        /// The one place this object's life actually ends. Safe to call from more than one path, or
        /// more than once from the same path - only the first call still holding IsOwnerClient true
        /// does anything. Public so a caller outside this hierarchy (MineAbility pruning its own
        /// oldest mine) shares the same guard as this base's own lifetime timer and a subclass's own
        /// detonation, rather than keeping a second, unguarded PhotonNetwork.Destroy of its own.
        /// </summary>
        public void RequestDestroy()
        {
            if (destroyRequested || this == null || gameObject == null)
                return;

            destroyRequested = true;

            if (IsOwnerClient)
                PhotonNetwork.Destroy(gameObject);
        }

        public void OnPhotonInstantiate(PhotonMessageInfo info)
        {
            OwnerActor = info.Sender != null ? info.Sender.ActorNumber : -1;
            Teams.TryGetTeam(info.Sender, out int team);
            OwnerTeam = team;
            Age = System.Math.Max(0.0, PhotonNetwork.Time - info.SentServerTime);

            OnPlaced(info.photonView.InstantiationData, info);

            if (lifetimeSeconds > 0f && IsOwnerClient)
            {
                float remaining = Mathf.Max(0f, lifetimeSeconds - (float)Age);
                StartCoroutine(DestroyAfter(remaining));
            }
        }

        /// <summary>
        /// Runs on every client - including a late joiner replaying this from the room cache - right
        /// after OwnerActor/OwnerTeam/Age are set. data is the raw instantiationData; null-check it
        /// before indexing (a stale build with fewer elements, or a caller that spawned this with
        /// none), the same defensive read FireField.OnPhotonInstantiate uses.
        /// </summary>
        protected virtual void OnPlaced(object[] data, PhotonMessageInfo info) { }

        /// <summary>
        /// Networked spawn for any deployable placed with no particular facing - a portal, a mine.
        /// Identical to the rotated overload below except for that; see its own doc for everything
        /// else (resolution, the null/not-in-room refusals, the late-joiner replay guarantee).
        /// </summary>
        public static GameObject Spawn(string prefabName, Vector3 position, object[] instantiationData)
            => Spawn(prefabName, position, Quaternion.identity, instantiationData);

        /// <summary>
        /// Networked spawn for a deployable that needs a facing at the moment it is placed - Cover
        /// Wall (Task 1.8b), rotated to face the caster's aim. prefabName resolves through PUN's
        /// default pool (Resources.Load), so the prefab must live under a Resources folder - see
        /// FireField.Spawn's own doc for why that folder is otherwise kept minimal. Returns null
        /// (and logs why) rather than throwing, the same contract FireField.Spawn already has: a
        /// missing room or a bad name should refuse the cast quietly, not crash a client mid-match.
        /// The rotation travels as part of PUN's own instantiate call, exactly like position does -
        /// no extra instantiationData needed for it, and a late joiner replaying this from the room
        /// cache gets the identical facing along with everything else NetworkedDeployable restores.
        /// </summary>
        public static GameObject Spawn(string prefabName, Vector3 position, Quaternion rotation, object[] instantiationData)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                Debug.LogError("[NetworkedDeployable] Spawn called with no prefab name - nothing was placed.");
                return null;
            }

            if (!PhotonNetwork.InRoom)
            {
                Debug.LogWarning($"[NetworkedDeployable] not in a Photon room, so '{prefabName}' was not placed.");
                return null;
            }

            return PhotonNetwork.Instantiate(prefabName, position, rotation, 0, instantiationData);
        }

        private IEnumerator DestroyAfter(float seconds)
        {
            if (seconds > 0f)
                yield return new WaitForSeconds(seconds);

            // RequestDestroy re-checks whether this is still here and not already ended by something
            // else (a pruning rule, a detonation, an Interrupt) during the wait - see its own comment.
            RequestDestroy();
        }
    }
}
