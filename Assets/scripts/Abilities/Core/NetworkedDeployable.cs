using System.Collections;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Net;

namespace Overpower.Abilities
{
    /// <summary>
    /// The shared base for every ability that leaves a real networked object behind (portal, mine, cover wall, fence,
    /// zone) instead of a local RPC-rebuilt one: a player who joins while it still stands must see it, which only
    /// PhotonNetwork.Instantiate gives (see FireField's class comment, Assets/scripts/Weapons).
    /// OnPhotonInstantiate resolves OwnerActor/OwnerTeam from info.Sender, never PhotonNetwork.LocalPlayer, which in
    /// that callback is the RECEIVER. OnPlaced (every client, a late joiner's cache replay included) is the subclass hook.
    ///
    /// AGE COMES FROM instantiationData, NOT info.SentServerTime (FAIL #15): a late joiner's cached replay reads
    /// SentServerTime as "just placed", so a live 45 s mine looked freshly armed. Spawn appends the placer's
    /// PhotonNetwork.ServerTimestamp as the LAST element; OnPhotonInstantiate strips it (subclass indices unchanged)
    /// and DeployableAge.SecondsSince does the unchecked 32-bit subtraction that survives the ~49.7-day wrap.
    /// ServerTimestamp is itself 0 until the post-connect fetch lands, which a fresh join's first replay can beat:
    /// InitializeAfterServerTimeIsReady waits (bounded) for a non-zero reading.
    /// Lifetime Seconds: the OWNER alone schedules PhotonNetwork.Destroy at lifetime - Age (the single-destroyer rule,
    /// see FireField.Burn); every destroy goes through RequestDestroy.
    /// A copy that arrives already past its lifetime (IsExpired) hides renderers and colliders as a backstop, but the
    /// owner's destroy is scheduled regardless of it: an else-if once leaked a hidden, collider-less Mine or
    /// ElectricFence forever, since their FixedUpdate early-returns on IsExpired and they have no other way out.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public abstract class NetworkedDeployable : MonoBehaviourPun, IPunInstantiateMagicCallback
    {
        [SerializeField, Tooltip("Maximum seconds this object exists before it deletes itself, " +
                 "counted from the moment it was ORIGINALLY placed - a late joiner does not get extra " +
                 "time just for joining late. 0 means it never expires on its own; something else " +
                 "must destroy it (a portal is destroyed only when its owner unequips Mobility).")]
        private float lifetimeSeconds = 0f;

        /// <summary>Lifetime Seconds (0 = never expires), read-only - the shop's pop-up shows it.</summary>
        public float LifetimeSeconds => lifetimeSeconds;

        /// <summary>The actor number of whoever placed this - from the instantiate's own sender, set
        /// once in OnPhotonInstantiate and never changed after.</summary>
        public int OwnerActor { get; private set; } = -1;

        /// <summary>The owner's team at the moment this was placed, or -1 if it was not yet known.
        /// Read once rather than live (unlike AbilityOwner.TeamId): nothing here needs to track a
        /// team change mid-match, only "whose is this".</summary>
        public int OwnerTeam { get; private set; } = -1;

        /// <summary>Seconds since this was ACTUALLY placed (DeployableAge.SecondsSince on the placement timestamp in
        /// instantiationData), so a late joiner's cached replay reports the true age, never zero.</summary>
        public double Age { get; private set; }

        /// <summary>The placer's server time in ms when this was ORIGINALLY placed (the value Age is worked out from). The
        /// damage this object deals carries it, so the victim can tell a mine laid before its owner's respawn from one laid after (A26).</summary>
        public int PlacedServerTimestampMs { get; private set; }

        /// <summary>True once Age had already reached Lifetime Seconds the moment this client first received this object
        /// (the backstop in the class comment). Always false when Lifetime Seconds is 0 (Portal, AoeZone). A subclass with
        /// per-frame simulation (a mine's trigger, a fence's discovery) must check it at the top of that loop:
        /// OnPhotonInstantiate hides renderers and colliders, but only the subclass knows what its own loop must skip.</summary>
        protected bool IsExpired => lifetimeSeconds > 0f && Age >= lifetimeSeconds;

        /// <summary>True on the one machine that placed this object - the only machine allowed to
        /// PhotonNetwork.Destroy it. A subclass destroying itself early (Portal's "oldest of three")
        /// must still guard on this the same way this base does for the lifetime timer.</summary>
        protected bool IsOwnerClient => photonView.IsMine;

        // Guards PhotonNetwork.Destroy itself, not just who may call it: a mine can be ended by THREE independent paths
        // (the lifetime timer, a detonation, MineAbility.PruneOldest) that may pick the same object in the same window,
        // and Object.Destroy does not null a reference until the end of the frame, so a null check misses the second call
        // (FireField's "eight errors per cast"). Every destroy path goes through RequestDestroy.
        private bool destroyRequested;

        /// <summary>
        /// The one place this object's life ends. Safe from several paths or repeated calls: only the first call still
        /// holding IsOwnerClient true does anything. Public so MineAbility's pruning shares the same guard.
        /// </summary>
        public void RequestDestroy()
        {
            if (destroyRequested || this == null || gameObject == null)
                return;

            destroyRequested = true;

            if (IsOwnerClient)
                PhotonNetwork.Destroy(gameObject);
        }

        // Frames InitializeAfterServerTimeIsReady waits for PhotonNetwork.ServerTimestamp to leave its default 0 before
        // using whatever it reads: the post-connect fetch normally lands within a frame or two; a bound, never a hang.
        private const int MaxServerTimeCalibrationFrames = 10;

        public void OnPhotonInstantiate(PhotonMessageInfo info)
        {
            OwnerActor = info.Sender != null ? info.Sender.ActorNumber : -1;
            Teams.TryGetTeam(info.Sender, out int team);
            OwnerTeam = team;

            object[] subclassData = StripPlacedTimestamp(info.photonView.InstantiationData, out int placedServerTimestampMs);
            StartCoroutine(InitializeAfterServerTimeIsReady(placedServerTimestampMs, subclassData, info));
        }

        /// <summary>
        /// Waits, if it has to, for PhotonNetwork.ServerTimestamp to calibrate before computing Age and running
        /// everything downstream (OnPlaced, the IsExpired hide, the lifetime timer). The fetch is async over the same
        /// connection a late joiner's cached Instantiate events arrive on, so the first replay can read 0; 0 minus a
        /// large negative placement timestamp is a large positive, so a mine placed ~30 s earlier read Age ~1.6 million
        /// seconds (see DeployableAge). Polls up to MaxServerTimeCalibrationFrames, then proceeds anyway, failing open
        /// like FriendlyFire's unknown-team check rather than leaving a deployable stuck. OwnerActor/OwnerTeam are
        /// resolved synchronously in OnPhotonInstantiate because they do not depend on ServerTimestamp.
        /// </summary>
        private IEnumerator InitializeAfterServerTimeIsReady(int placedServerTimestampMs, object[] subclassData, PhotonMessageInfo info)
        {
            int waited = 0;
            while (PhotonNetwork.ServerTimestamp == 0 && waited < MaxServerTimeCalibrationFrames)
            {
                yield return null;
                waited++;
            }

            // Failed open, but loudly: silently, the "Age reads 0" symptom of FAIL #15 would return, since
            // DeployableAge.SecondsSince(placedMs, 0) with a positive placedMs also clamps to 0.
            if (PhotonNetwork.ServerTimestamp == 0)
            {
                Debug.LogWarning($"[NetworkedDeployable] {name}: PhotonNetwork.ServerTimestamp was still " +
                                  $"0 after waiting {MaxServerTimeCalibrationFrames} frames for it to " +
                                  "calibrate - Age is falling back to 0 for this object.");
            }

            PlacedServerTimestampMs = placedServerTimestampMs;
            Age = DeployableAge.SecondsSince(placedServerTimestampMs, PhotonNetwork.ServerTimestamp);

            OnPlaced(subclassData, info);

            // Visual-only views build here: after OnPlaced, so a portal's diameter has arrived, and before the IsExpired
            // hide below, so an already-expired copy hides them too.
            NotifyViews();

            if (IsExpired)
            {
                // Backstop for the cache-removal/destroy race, never the normal path. Hide and go inert; RequestDestroy
                // no-ops on non-owners, so this runs alongside the scheduling below, not instead of it.
                HideExpiredVisualAndColliders();
            }

            // Deliberately NOT an else-if against the IsExpired branch: the OWNER's own copy can compute IsExpired true
            // (Lifetime Seconds under a frame, or the calibration loop stalling past it), and skipping the schedule then
            // left a hidden, collider-less networked object forever (Mine and ElectricFence have no other path off
            // IsExpired). Scheduling on "owner with a lifetime" alone makes an expired owner's copy wait 0 s and destroy
            // next frame. DeployableLifetime.ShouldScheduleOwnerDestroy is that decision, pure and tested; it takes no
            // isExpired parameter, so the coupling cannot return by accident.
            if (DeployableLifetime.ShouldScheduleOwnerDestroy(lifetimeSeconds, IsOwnerClient))
            {
                float remaining = Mathf.Max(0f, lifetimeSeconds - (float)Age);
                StartCoroutine(DestroyAfter(remaining));
            }
        }

        /// <summary>
        /// Splits the placer's ServerTimestamp (appended by Spawn) off the end of instantiationData and returns the rest
        /// at the indices a subclass's OnPlaced reads. Missing or malformed data (a caller that bypassed Spawn, a stale
        /// build) logs an error and falls back to "placed right now" rather than throwing.
        /// </summary>
        private static object[] StripPlacedTimestamp(object[] rawData, out int placedServerTimestampMs)
        {
            if (rawData == null || rawData.Length == 0 || !(rawData[rawData.Length - 1] is int placedMs))
            {
                Debug.LogError("[NetworkedDeployable] instantiationData is missing its placement " +
                                "timestamp - falling back to Age 0 (this object was spawned by a " +
                                "caller that bypassed NetworkedDeployable.Spawn).");
                placedServerTimestampMs = PhotonNetwork.ServerTimestamp;
                return rawData;
            }

            placedServerTimestampMs = placedMs;

            var subclassData = new object[rawData.Length - 1];
            System.Array.Copy(rawData, subclassData, subclassData.Length);
            return subclassData;
        }

        /// <summary>Hands the placed object to every visual-only IDeployableView on it. A broken visual must never stop
        /// what follows here (the IsExpired hide, the owner's lifetime destroy), so each view's exception is logged and swallowed.</summary>
        private void NotifyViews()
        {
            foreach (IDeployableView view in GetComponentsInChildren<IDeployableView>(true))
            {
                try { view.OnDeployablePlaced(this); }
                catch (System.Exception e) { Debug.LogException(e, this); }
            }
        }

        /// <summary>The IsExpired backstop's visible effect: every Renderer and Collider under this object turns off, on
        /// every client that computes IsExpired true. A subclass whose per-frame logic does not go through its own Collider
        /// (Mine's and ElectricFence's Physics.OverlapSphere look at OTHER colliders) still needs its own IsExpired check
        /// at the top of that loop.</summary>
        private void HideExpiredVisualAndColliders()
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
                c.enabled = false;
        }

        /// <summary>
        /// Runs on every client, a late joiner's room-cache replay included, right after OwnerActor/OwnerTeam/Age are
        /// set. data is instantiationData with the placement timestamp already stripped, so every index is what the
        /// subclass passed into Spawn. Still null-check before indexing (stale build, or a caller that passed none).
        /// </summary>
        protected virtual void OnPlaced(object[] data, PhotonMessageInfo info) { }

        /// <summary>
        /// Networked spawn for a deployable with no particular facing (a portal, a mine); see the rotated overload.
        /// </summary>
        public static GameObject Spawn(string prefabName, Vector3 position, object[] instantiationData)
            => Spawn(prefabName, position, Quaternion.identity, instantiationData);

        /// <summary>
        /// Networked spawn for a deployable that needs a facing at placement (Cover Wall faces the caster's aim).
        /// prefabName resolves through PUN's default pool (Resources.Load), so the prefab lives under a Resources folder
        /// (see FireField.Spawn). Returns null and logs rather than throwing, like FireField.Spawn: a missing room or a
        /// bad name refuses the cast quietly. The rotation travels in PUN's own instantiate call, a late joiner's replay included.
        /// APPENDS PhotonNetwork.ServerTimestamp AS THE LAST ELEMENT of instantiationData, after whatever a subclass put
        /// there; OnPhotonInstantiate strips it before OnPlaced, so subclass indices never move.
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

            int existingCount = instantiationData != null ? instantiationData.Length : 0;
            var dataWithPlacedMs = new object[existingCount + 1];
            if (existingCount > 0)
                System.Array.Copy(instantiationData, dataWithPlacedMs, existingCount);
            dataWithPlacedMs[existingCount] = PhotonNetwork.ServerTimestamp;

            return PhotonNetwork.Instantiate(prefabName, position, rotation, 0, dataWithPlacedMs);
        }

        /// <summary>The fresh start at match-live destroys every deployable THIS client placed (a mine, cover wall,
        /// portal, fence, AoE zone) through RequestDestroy, the single-destroyer path. Fire fields are left alone: Decision 6
        /// gives already-in-flight fire the same few seconds' grace as any other projectile.</summary>
        public static void DestroyAllPlacedByLocalPlayer()
        {
            foreach (NetworkedDeployable deployable in FindObjectsByType<NetworkedDeployable>(FindObjectsSortMode.None))
            {
                if (deployable.IsOwnerClient)
                    deployable.RequestDestroy();
            }
        }

        /// <summary>A deployable that moves with its caster (the AoE zone; a fence set to follow) is never "left
        /// behind" anywhere, so a pass by position skips it.</summary>
        protected virtual bool FollowsCaster => false;

        /// <summary>When a corner closes, whatever THIS client placed behind the new wall goes, through RequestDestroy
        /// (owner only, so every client can run the same pass and only the owner's copy acts; no RPC). If any of this
        /// client's portals is behind the wall, all of them go: a pair with one end in the closed corner would be a way through.</summary>
        public static void DestroyOwnedWhere(System.Func<Vector3, bool> isGone)
        {
            if (isGone == null)
                return;
            bool aPortalWentBehind = false;
            foreach (NetworkedDeployable deployable in FindObjectsByType<NetworkedDeployable>(FindObjectsSortMode.None))
            {
                if (!deployable.IsOwnerClient || deployable.FollowsCaster || !isGone(deployable.transform.position))
                    continue;
                if (deployable is Portal)
                    aPortalWentBehind = true;
                deployable.RequestDestroy();
            }
            if (!aPortalWentBehind)
                return;
            foreach (NetworkedDeployable deployable in FindObjectsByType<NetworkedDeployable>(FindObjectsSortMode.None))
                if (deployable.IsOwnerClient && deployable is Portal)
                    deployable.RequestDestroy();
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
