using System.Collections;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
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
    /// receiver" trap WeaponFiring's and AbilityRunner's own class comments call out).
    ///
    /// AGE COMES FROM instantiationData, NOT FROM info.SentServerTime (fixed 2026-09-15, FAIL #15).
    /// The old code trusted PUN to hand a late joiner replaying a cached PhotonNetwork.Instantiate
    /// event the ORIGINAL SentServerTime, the same way it hands back the original position. Measured
    /// on a genuinely fresh late joiner (two-client-harness.md ss7): it does not - info.SentServerTime
    /// read back as though the object had just been placed, even ~23 real seconds after it actually
    /// was, so a still-alive Mine (Lifetime Seconds 45) reported Age 0.00 and a full 45s still ahead
    /// of it - visually a freshly-placed trap. Spawn below now appends the placer's own
    /// PhotonNetwork.ServerTimestamp as the LAST element of instantiationData; OnPhotonInstantiate
    /// strips it back off (every existing parameter index a subclass reads from OnPlaced's own data
    /// is unchanged) and hands both raw ints to DeployableAge.SecondsSince (Combat/DeployableAge.cs),
    /// which does the unchecked 32-bit subtraction that survives ServerTimestamp's own ~49.7-day
    /// wrap. A subclass that wants a maximum lifetime sets Lifetime Seconds; this base schedules
    /// PhotonNetwork.Destroy at lifetime - Age, owner only - the same single-destroyer rule
    /// FireField.Burn documents, where every non-owner calling PhotonNetwork.Destroy on the same
    /// object logs one error each.
    ///
    /// A COPY THAT ARRIVES ALREADY PAST ITS LIFETIME (IsExpired below) hides its renderers and disables
    /// its colliders rather than looking freshly armed - a defensive backstop for the cache-removal/
    /// destroy race described above, not the primary fix (the primary fix is simply computing Age
    /// correctly, so a copy that is genuinely still alive reports its true remaining life instead of a
    /// fresh Lifetime Seconds). Hiding is NOT this object's only fate, though: the lifetime-destroy
    /// schedule below runs unconditionally whenever this is the owner's own copy and Lifetime Seconds is
    /// set, regardless of whether IsExpired is also true - code review finding, Task 2.1a-era pass. It
    /// used to be an else-if against the IsExpired hide, which meant an OWNER whose own copy computed
    /// IsExpired true (a Lifetime Seconds under one frame, or this very initialization stalling past it)
    /// never scheduled its own destroy at all - Mine and ElectricFence have no other path off IsExpired
    /// (their own FixedUpdate also early-returns on it), so that hidden, collider-less object leaked
    /// forever. Scheduling on "am I the owner with a lifetime" rather than "is this read of Age not
    /// already expired" means an already-expired owner's copy just gets a zero-second wait and destroys
    /// itself on the next frame instead of never.
    ///
    /// A SECOND BUG FOUND WHILE VERIFYING THE FIRST ONE: PhotonNetwork.ServerTimestamp itself is not
    /// trustworthy the INSTANT a late joiner's cached replay first fires - it is fetched from the
    /// server once, asynchronously, right after connecting, and a fresh join's first OnPhotonInstantiate
    /// can beat that fetch home, reading ServerTimestamp as its un-set default of 0. Subtracting a real
    /// placement timestamp from a wrongly-zero "now" does not read as Age 0 the way the first bug did -
    /// it swings however the sign happens to fall, including a multi-million-second Age. See
    /// InitializeAfterServerTimeIsReady below for the fix (wait for a non-zero reading, bounded, with a
    /// one-time warning if the wait ever actually runs out - see that coroutine's own comment).
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

        /// <summary>Seconds since this was ACTUALLY placed - DeployableAge.SecondsSince applied to
        /// the placement timestamp carried in instantiationData and PhotonNetwork.ServerTimestamp at
        /// the moment THIS client learned about it - so a late joiner's cached replay reports the
        /// true age, never zero. See the class comment for why this is no longer read from
        /// info.SentServerTime.</summary>
        public double Age { get; private set; }

        /// <summary>True once Age has already reached or passed Lifetime Seconds the moment this
        /// client first placed/received this object - the defensive backstop the class comment
        /// describes. Always false when Lifetime Seconds is 0 (Portal, AoeZone - something else
        /// entirely decides when those end). A subclass with per-frame simulation (a mine's trigger,
        /// a fence's discovery) must check this at the top of that loop; OnPhotonInstantiate already
        /// hides renderers and disables colliders for it, but only a subclass knows what its own
        /// per-frame logic must skip.</summary>
        protected bool IsExpired => lifetimeSeconds > 0f && Age >= lifetimeSeconds;

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

        // How many frames InitializeAfterServerTimeIsReady will wait for PhotonNetwork.ServerTimestamp
        // to leave its uninitialized-int default of 0 before giving up and using whatever it reads -
        // see that coroutine's own comment. 10 frames is generous slack (well over 100ms even at 60fps)
        // for a one-time fetch-after-connect that normally lands within the very next frame or two;
        // never a hang, just a bound on how long a wrong Age can theoretically be trusted.
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
        /// Waits, if it has to, for PhotonNetwork.ServerTimestamp to look calibrated before computing
        /// Age and running everything downstream of it (OnPlaced, the IsExpired hide, the lifetime
        /// destroy timer) - fixed 2026-09-15, found while verifying the FAIL #15 fix above.
        ///
        /// PhotonPeer.ServerTimeInMilliSeconds - what PhotonNetwork.ServerTimestamp reads - is "fetched
        /// after connecting (once)" per its own XML doc, asynchronously, over the SAME connection a
        /// late joiner's cached PhotonNetwork.Instantiate events arrive on. Measured: a genuinely fresh
        /// join's very first OnPhotonInstantiate for a cached replay can fire before that fetch lands,
        /// reading ServerTimestamp as its un-set default, 0 - which, subtracted from a real (possibly
        /// negative, see DeployableAge's own class comment) placement timestamp, does not read as "age
        /// 0" the way the OLD info.SentServerTime bug did. It swings the OTHER way just as easily: a
        /// mine placed ~30s earlier read Age as roughly 1.6 MILLION seconds - the placement timestamp's
        /// own magnitude divided by 1000, because 0 minus a large negative IS a large positive. Either
        /// direction is wrong for the same reason: "now" was not really 0, calibration just had not
        /// landed yet on THIS client's THIS connection.
        ///
        /// A flat one-frame yield is not quite enough of a guarantee (the fetch is a real round trip,
        /// not just a local computation), so this polls ServerTimestamp != 0 for up to
        /// MaxServerTimeCalibrationFrames frames and then proceeds regardless - failing open, the same
        /// call FriendlyFire's own unknown-team check makes, rather than ever leaving a deployable stuck
        /// mid-initialization. OwnerActor/OwnerTeam are resolved synchronously in OnPhotonInstantiate,
        /// above, because they do not depend on ServerTimestamp at all and every other client's own
        /// early logic (a mine's registry, say) may want them the instant this object exists.
        /// </summary>
        private IEnumerator InitializeAfterServerTimeIsReady(int placedServerTimestampMs, object[] subclassData, PhotonMessageInfo info)
        {
            int waited = 0;
            while (PhotonNetwork.ServerTimestamp == 0 && waited < MaxServerTimeCalibrationFrames)
            {
                yield return null;
                waited++;
            }

            // Failed open (see this coroutine's own comment) rather than waiting forever - but failing
            // open here silently would recreate the exact "Age reads 0" symptom FAIL #15 was about, one
            // level down: DeployableAge.SecondsSince(placedMs, 0) with a positive placedMs also clamps
            // to 0. Loud rather than silent, so a genuinely stuck calibration (never observed, but the
            // whole point of a bounded wait instead of an infinite one) shows up in the console instead
            // of quietly masquerading as "placed 0 seconds ago" again.
            if (PhotonNetwork.ServerTimestamp == 0)
            {
                Debug.LogWarning($"[NetworkedDeployable] {name}: PhotonNetwork.ServerTimestamp was still " +
                                  $"0 after waiting {MaxServerTimeCalibrationFrames} frames for it to " +
                                  "calibrate - Age is falling back to 0 for this object.");
            }

            Age = DeployableAge.SecondsSince(placedServerTimestampMs, PhotonNetwork.ServerTimestamp);

            OnPlaced(subclassData, info);

            // Ability visuals step 2: visual-only views build here - after OnPlaced, so a portal's diameter has
            // arrived, and before the IsExpired hide below, so an already-expired copy hides them too.
            NotifyViews();

            if (IsExpired)
            {
                // Defensive backstop for the cache-removal/destroy race the class comment describes -
                // never the normal path. Hide and go inert; RequestDestroy() below (when this is also
                // the owner) or the real owner's own destroy (already in flight, or about to be) is
                // what actually removes this - RequestDestroy() already no-ops on every non-owner
                // client, so this is safe to run alongside the scheduling below rather than instead of
                // it (see that branch's own comment for why it must not be "instead of").
                HideExpiredVisualAndColliders();
            }

            // Deliberately NOT an "else if" against the IsExpired branch above (code review finding,
            // Task 2.1a-era pass): the OWNER's own copy can itself compute IsExpired true - a Lifetime
            // Seconds under one frame's worth of real time, or this very coroutine stalling past it
            // while it waited on InitializeAfterServerTimeIsReady's calibration loop above. Skipping
            // the destroy schedule whenever IsExpired was true used to leave a hidden, collider-less,
            // never-destroyed networked object behind forever on exactly the owner's own machine -
            // Mine and ElectricFence have no OTHER path off IsExpired (their own FixedUpdate also
            // early-returns on it, so neither ever ticks its way to a detonation or a discovery hit
            // either); CoverWall only escaped it by accident, and only if someone actually shot it
            // first. Scheduling this unconditionally on "am I the owner and do I have a lifetime at
            // all" - never on whether THIS particular read of Age happened to already clear it -
            // means an already-expired owner's copy gets Mathf.Max(0f, ...) = 0 here and destroys
            // itself on the very next frame instead of never. DeployableLifetime.ShouldScheduleOwnerDestroy
            // (Combat/DeployableLifetime.cs) is that decision pulled out pure and tested: its own
            // signature has no isExpired parameter at all, so there is nothing here that could
            // reintroduce the coupling by accident.
            if (DeployableLifetime.ShouldScheduleOwnerDestroy(lifetimeSeconds, IsOwnerClient))
            {
                float remaining = Mathf.Max(0f, lifetimeSeconds - (float)Age);
                StartCoroutine(DestroyAfter(remaining));
            }
        }

        /// <summary>
        /// Splits the placer's own PhotonNetwork.ServerTimestamp (appended by Spawn below) off the
        /// end of instantiationData and returns the rest, unchanged, at the same indices a subclass's
        /// own OnPlaced has always read them at. Missing or malformed data (a caller that bypassed
        /// Spawn, or a stale build with fewer elements - the same defensive read FireField.
        /// OnPhotonInstantiate uses) falls back to "placed right now" rather than throwing, loudly,
        /// exactly like the null-data fallbacks OnPlaced's own subclasses already use.
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

        /// <summary>Ability visuals step 2: hands the placed object to every visual-only IDeployableView on it. A broken
        /// visual must never stop what follows it here (the IsExpired hide, the owner's lifetime destroy), so each
        /// view's exception is logged and swallowed.</summary>
        private void NotifyViews()
        {
            foreach (IDeployableView view in GetComponentsInChildren<IDeployableView>(true))
            {
                try { view.OnDeployablePlaced(this); }
                catch (System.Exception e) { Debug.LogException(e, this); }
            }
        }

        /// <summary>The IsExpired backstop's only visible effect: every Renderer and Collider under
        /// this object turns off, on every client that computes IsExpired true, regardless of which
        /// subclass this is - a mine's model, a cover wall's box, a fence's ring. A subclass whose own
        /// per-frame logic does not go through a Collider (Mine's and ElectricFence's own
        /// Physics.OverlapSphere queries look at OTHER colliders, never their own) still needs its own
        /// IsExpired check at the top of that loop - this alone is not enough for those two.</summary>
        private void HideExpiredVisualAndColliders()
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
            foreach (Collider c in GetComponentsInChildren<Collider>(true))
                c.enabled = false;
        }

        /// <summary>
        /// Runs on every client - including a late joiner replaying this from the room cache - right
        /// after OwnerActor/OwnerTeam/Age are set. data is instantiationData with the placement
        /// timestamp already stripped back off (see StripPlacedTimestamp) - every index a subclass
        /// reads here is exactly what it passed into Spawn, unchanged. Still null-check before
        /// indexing (a stale build with fewer elements, or a caller that spawned this with none), the
        /// same defensive read FireField.OnPhotonInstantiate uses.
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
        ///
        /// APPENDS PhotonNetwork.ServerTimestamp AS THE LAST ELEMENT of instantiationData (FAIL #15
        /// fix) - the placer's own "right now", boxed as an int, after whatever a subclass already
        /// put there (Mine's Seq, Portal's diameter+Seq, or nothing at all). OnPhotonInstantiate on
        /// every receiving client strips that same last element back off before handing the rest to
        /// OnPlaced, so a subclass never sees it and every existing parameter index it already reads
        /// stays exactly where it was.
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
