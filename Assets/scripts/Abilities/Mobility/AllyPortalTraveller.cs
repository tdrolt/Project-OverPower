using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Abilities
{
    /// <summary>
    /// Lets a player travel through TEAMMATES' teleport portals (D15); only the local player's copy acts. Traveller half:
    /// channel on a teammate's portal (same PortalChannelState, can-act and exit rules as the owner's TeleportAbility, gated
    /// by the owner's ReadyKey), then move via PlayerDisplacement.TeleportTo and write UseKey on yourself. Owner half: on a
    /// teammate's UseKey, spend one charge through your own TeleportAbility. No RPC: charges live only on the owner's machine,
    /// so both halves talk through Player Properties. Group travel: UseKey's third value is the departure portal's Seq, and
    /// each client moves only its own player (JoinGroupTrip). Accepted race: two trips finishing on the last charge both happen.
    /// </summary>
    public sealed class AllyPortalTraveller : MonoBehaviourPun, IInRoomCallbacks
    {
        /// <summary>Player Property, bool: this player (a portal owner) currently has a portal charge to spend.
        /// Written by TeleportAbility on change; read by teammates before they channel. Absent = false.</summary>
        public const string ReadyKey = "tpRdy";

        /// <summary>Player Property, int[3]: { owner actor whose portal was used, trip counter, Seq of the departure portal }. Written by the
        /// TRAVELLER on themselves when a trip finishes; the counter makes every trip a new value.</summary>
        public const string UseKey = "tpUse";

        // Counts this client's finished trips. Static so it survives the player object being rebuilt: the portal
        // owners remember the last counter they saw from this actor, and a restarted count would be ignored.
        private static int tripCounter;

        /// <summary>This client's own player's copy of this component (null until it exists). Lets a portal owner's
        /// "trip completed" message reach the local player so it can join a group trip.</summary>
        public static AllyPortalTraveller Local { get; private set; }

        private PlayerLifecycle lifecycle;
        private PlayerStatusEffects status;
        private PlayerOverheat overheat;
        private PlayerDisplacement displacement;
        private CapsuleCollider capsule;
        private Rigidbody body;
        private AbilityRunner runner;

        // The channel timer is retuned to the portal's own seconds every tick; this first value is never used.
        private readonly PortalChannelState channelState = new PortalChannelState(1f);

        // Owner half: the highest trip counter already handled, per teammate actor.
        private readonly Dictionary<int, int> lastSeenCounter = new Dictionary<int, int>();

        private void Awake()
        {
            lifecycle = GetComponent<PlayerLifecycle>();
            status = GetComponent<PlayerStatusEffects>();
            overheat = GetComponent<PlayerOverheat>();
            displacement = GetComponent<PlayerDisplacement>();
            capsule = GetComponent<CapsuleCollider>();
            body = GetComponent<Rigidbody>();
            runner = GetComponent<AbilityRunner>();

            // The static counter survives a rebuilt body but not a restarted game, while the portal owners still remember
            // the last counter this actor wrote ("tpUse" is a Player Property the room kept): count on from there, or the
            // first trips after a restart would be ignored as old.
            if (photonView.IsMine && PhotonNetwork.LocalPlayer != null
                && PhotonNetwork.LocalPlayer.CustomProperties.TryGetValue(UseKey, out object useRaw)
                && useRaw is int[] use && use.Length >= 2)
                tripCounter = RejoinRules.SeedCounter(tripCounter, use[1]);
        }

        private void OnEnable()
        {
            PhotonNetwork.AddCallbackTarget(this);
            if (photonView != null && photonView.IsMine)
                Local = this;
        }

        private void OnDisable()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
            if (Local == this)
                Local = null;
        }

        // ---- traveller half -------------------------------------------------------------------------

        private void Update()
        {
            if (!photonView.IsMine || !PhotonNetwork.InRoom || capsule == null || body == null || displacement == null)
                return;

            bool alive = lifecycle == null || lifecycle.IsAlive;
            bool stunned = status != null && status.IsStunned;
            bool silenced = overheat != null && overheat.IsSilenced;
            bool canAct = CastGate.ForActor(alive, stunned, silenced) == CastBlock.None;

            TryJoinPendingGroupTrip();

            Portal current = FindStandingPortal(out Player portalOwner, out bool sameTeam);
            Portal other = null;
            bool canChannel = false;

            if (current != null)
            {
                other = TeleportAbility.FindOther(Portal.ForOwner(portalOwner.ActorNumber), current);
                bool ownerReady = portalOwner.CustomProperties.TryGetValue(ReadyKey, out object raw) && raw is bool ready && ready;

                channelState.SetChannelSeconds(current.ChannelSeconds);
                canChannel = canAct && other != null && current.ChannelSeconds > 0f
                             && PortalUseRules.MayUse(isOwner: false, sameTeam: sameTeam, userAlive: alive, ownerHasCharge: ownerReady)
                             && TeleportAbility.IsExitClear(capsule, other, transform);
            }

            PortalChannelState.Result result = channelState.Tick(Time.deltaTime, current, canChannel);
            if (result == PortalChannelState.Result.Completed)
                CompleteTrip(current, other, portalOwner);
        }

        private Portal FindStandingPortal(out Player portalOwner, out bool sameTeam)
        {
            portalOwner = null;
            sameTeam = false;
            Portal best = null;
            bool bestUsable = false;
            float bestDistanceSqr = float.MaxValue;
            bool alive = lifecycle == null || lifecycle.IsAlive;
            // The Rigidbody's own position, not the Transform's: TeleportTo writes the Rigidbody, and the Transform only
            // catches up on the next physics step. Reading the stale Transform for a few frames after a trip showed the
            // traveller still standing on the DEPARTURE portal, which lifted the arrival latch and let the trip repeat.
            Vector3 position = body.position;

            foreach (Player other in PhotonNetwork.PlayerListOthers)
            {
                // A player whose connection dropped keeps their portals standing, but nobody is there to pay the charge
                // (their "ready" flag would read whatever it last was): a portal of an absent player is not usable.
                if (!PresenceRules.IsPresent(other.IsInactive))
                    continue;

                // Unknown teams count as not teammates: a portal is never opened to a maybe-enemy. Enemy portals are
                // still found (so PortalUseRules.MayUse is the one place that refuses them).
                bool otherIsTeammate = Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out _) && Teams.TryGetTeam(other, out _)
                                       && Teams.AreSameTeam(PhotonNetwork.LocalPlayer, other);

                foreach (Portal p in Portal.ForOwner(other.ActorNumber))
                {
                    if (p == null || !PortalUseRules.IsOnPortal(p.transform.position, position, p.Radius, capsule.radius))
                        continue;

                    Vector3 delta = p.transform.position - position;
                    delta.y = 0f;
                    bool ready = other.CustomProperties.TryGetValue(ReadyKey, out object raw) && raw is bool r && r;
                    bool usable = PortalUseRules.MayUse(isOwner: false, sameTeam: otherIsTeammate, userAlive: alive, ownerHasCharge: ready);

                    // A portal this player may use beats one it may not, so a nearer enemy portal cannot block a teammate's.
                    // With nothing usable the nearest is still returned, so the arrival latch keeps seeing the portal the
                    // player stands on; Update's MayUse check is what stops the channel.
                    if (PortalUseRules.IsBetterCandidate(usable, delta.sqrMagnitude, best != null, bestUsable, bestDistanceSqr))
                    {
                        best = p;
                        bestUsable = usable;
                        bestDistanceSqr = delta.sqrMagnitude;
                        portalOwner = other;
                        sameTeam = otherIsTeammate;
                    }
                }
            }

            return best;
        }

        private void CompleteTrip(Portal from, Portal to, Player portalOwner)
        {
            // A knockback that started in the frame the channel finished refuses the jump - then nothing is spent.
            if (!displacement.TeleportTo(TeleportAbility.ArrivalRoot(capsule, to)))
                return;

            channelState.LatchArrival(to);
            tripCounter++;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { UseKey, new[] { portalOwner.ActorNumber, tripCounter, from.Seq } } });
        }

        // ---- group travel -----------------------------------------------------------------

        /// <summary>A trip through a portal of `ownerActor` just completed: if this player is alive, on the owner's team and
        /// touching that departure portal, it follows to the paired portal at the same offset from the centre (no charge
        /// spent); a blocked offset falls back to the centre.</summary>
        public void JoinGroupTrip(int ownerActor, Portal from, int travellerActor)
        {
            // Only stores the request: this is called from Photon's message handling (PhotonHandler.FixedUpdate), where
            // PlayerMotor.Move's MovePosition in the same physics step can undo a teleport. The checks and the jump run
            // in the next Update, like every other portal teleport (TryJoinPendingGroupTrip).
            if (from == null || !photonView.IsMine)
                return;
            pendingGroupFrom = from;
            pendingGroupOwner = ownerActor;
            pendingGroupTraveller = travellerActor;
        }

        private Portal pendingGroupFrom;
        private int pendingGroupOwner;
        private int pendingGroupTraveller;

        private void TryJoinPendingGroupTrip()
        {
            Portal from = pendingGroupFrom;
            int ownerActor = pendingGroupOwner;
            int travellerActor = pendingGroupTraveller;
            pendingGroupFrom = null;
            if (from == null)
                return;
            ExecuteGroupTrip(ownerActor, from, travellerActor);
        }

        private void ExecuteGroupTrip(int ownerActor, Portal from, int travellerActor)
        {
            if (from == null || !photonView.IsMine || !PhotonNetwork.InRoom || capsule == null || body == null || displacement == null)
                return;

            Player portalOwner = PhotonNetwork.CurrentRoom?.GetPlayer(ownerActor);
            if (portalOwner == null)
                return;

            bool iAmOwner = ownerActor == PhotonNetwork.LocalPlayer.ActorNumber;
            bool sameTeam = iAmOwner || (Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out _) && Teams.TryGetTeam(portalOwner, out _)
                                        && Teams.AreSameTeam(PhotonNetwork.LocalPlayer, portalOwner));
            // "Alive" only, by design: a stunned or silenced teammate standing on the portal travels along too.
            bool alive = lifecycle == null || lifecycle.IsAlive;
            bool onDeparture = PortalUseRules.IsOnPortal(from.transform.position, body.position, from.Radius, capsule.radius);
            // Just arrived on this portal (latched): a trip from it finishing now must not pull the player straight back.
            TeleportAbility ownAbility = iAmOwner && runner != null ? runner.StatusFor(AbilitySlot.Mobility) as TeleportAbility : null;
            bool latched = iAmOwner && ownAbility != null ? ownAbility.IsLatchedOn(from) : channelState.IsLatchedOn(from);

            if (!PortalUseRules.JoinsGroupTrip(alive, sameTeam, onDeparture, isTheTraveller: travellerActor == PhotonNetwork.LocalPlayer.ActorNumber, latchedOnDeparture: latched))
                return;

            Portal to = TeleportAbility.FindOther(Portal.ForOwner(ownerActor), from);
            if (to == null)
                return;

            Vector3 landing = PortalUseRules.GroupArrivalPoint(from.transform.position, body.position, to.transform.position, from.Radius, capsule.radius * 2f);
            // Ground under the landing spot, probed like Blink does; a failed probe falls back to the centre below.
            Vector3 root;
            PlayerMotor motor = GetComponent<PlayerMotor>();
            bool grounded = GroundProbe.TryFindGround(to.transform.position.y, landing, 0.6f, 2f,
                                motor != null ? motor.KillHeight : float.NegativeInfinity,
                                LayerMask.GetMask("Default", "Building"), transform, out Vector3 ground);
            root = PlayerSpaceProbe.RootOnGround(capsule, grounded ? ground : landing);
            if (!grounded || !TeleportAbility.IsRootClear(capsule, root, transform))
            {
                root = TeleportAbility.ArrivalRoot(capsule, to);
                if (!TeleportAbility.IsExitClear(capsule, to, transform))
                    return;
            }

            if (!displacement.TeleportTo(root))
                return;

            channelState.Reset();
            channelState.LatchArrival(to);
            if (iAmOwner)
                ownAbility?.NoteArrivedWithGroup(to);
        }

        // ---- owner half -----------------------------------------------------------------------------

        public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
        {
            if (photonView == null || !photonView.IsMine || targetPlayer == null || targetPlayer.IsLocal)
                return; // Only my own copy listens, and only to OTHER players' writes - never the echo of my own.

            if (!changedProps.TryGetValue(UseKey, out object raw) || !(raw is int[] use) || use.Length < 2)
                return;

            lastSeenCounter.TryGetValue(targetPlayer.ActorNumber, out int lastSeen);
            bool teammate = Teams.AreSameTeam(PhotonNetwork.LocalPlayer, targetPlayer);

            if (PortalUseRules.ShouldSpendForAllyTrip(lastSeen, use[1], use[0], photonView.OwnerActorNr, teammate))
                (runner != null ? runner.StatusFor(AbilitySlot.Mobility) as TeleportAbility : null)?.SpendChargeForAllyTrip();

            lastSeenCounter[targetPlayer.ActorNumber] = PortalUseRules.NewLastSeen(lastSeen, use[1]);

            // The third value is the departure portal's Seq: a fresh trip by a teammate of the portal's owner pulls this
            // player along if it stands on that portal. Only when the writer really is on the owner's team.
            if (use.Length >= 3 && use[1] > lastSeen)
            {
                Player portalOwner = PhotonNetwork.CurrentRoom?.GetPlayer(use[0]);
                if (portalOwner != null && Teams.AreSameTeam(targetPlayer, portalOwner))
                {
                    Portal from = null;
                    foreach (Portal p in Portal.ForOwner(use[0]))
                    {
                        if (p != null && p.Seq == use[2])
                            from = p;
                    }
                    JoinGroupTrip(use[0], from, targetPlayer.ActorNumber);
                }
            }
        }

        // Unused IInRoomCallbacks members.
        public void OnPlayerEnteredRoom(Player newPlayer) { }
        public void OnPlayerLeftRoom(Player otherPlayer) { }
        public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { }
        public void OnMasterClientSwitched(Player newMasterClient) { }
    }
}
