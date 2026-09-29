using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Net;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Abilities
{
    /// <summary>
    /// Lets a player travel through their TEAMMATES' teleport portals (Tudor D15) - stand on one for the owner's
    /// channel time and you arrive at the owner's other portal. Lives on every player; only the local player's copy
    /// ever acts, and it has two halves:
    ///
    /// TRAVELLER (this player is the teammate). Every frame, look at each teammate's portals (never enemies', never
    /// your own - those stay with your own TeleportAbility), run the same PortalChannelState the owner's ability
    /// runs, gated by the same "can act" rule as AbilityRunner (alive, not stunned, not silenced), the same exit
    /// check, and the owner's published "has a charge" flag. On completion this player moves itself through
    /// PlayerDisplacement.TeleportTo and writes a "trip finished" Player Property on itself.
    ///
    /// OWNER (this player owns the portals). When a teammate's "trip finished" property arrives, spend one charge
    /// through your own TeleportAbility - the same spend your own trip makes, so the cooldown starts and the HUD
    /// shows it.
    ///
    /// NO NEW RPC. Charges live only on the owner's machine (client-authoritative, like every ability), so the two
    /// halves talk through two Player Properties: ReadyKey (owner to everyone: "I have a portal charge", published
    /// by TeleportAbility only when it changes) and UseKey (teammate to everyone: { owner actor, counter }).
    ///
    /// ACCEPTED RACE. If the owner's own trip and a teammate's finish in the same instant with one charge left,
    /// both trips happen and the pool simply stays at 0 - nobody is refunded. A teammate who leaves the room
    /// mid-trip changes nothing: no message arrives, no charge is spent.
    ///
    /// COSMETIC. The channel marker and arrival rings the owner's ability draws through its phases are NOT shown for a
    /// teammate's trip (they need an RPC phase from the owner's module); the teammate simply channels and arrives.
    /// </summary>
    public sealed class AllyPortalTraveller : MonoBehaviourPun, IInRoomCallbacks
    {
        /// <summary>Player Property, bool: this player (a portal owner) currently has a portal charge to spend.
        /// Written by TeleportAbility on change; read by teammates before they channel. Absent = false.</summary>
        public const string ReadyKey = "tpRdy";

        /// <summary>Player Property, int[2]: { owner actor number whose portal was used, trip counter }. Written by the
        /// TRAVELLER on themselves when a trip finishes; the counter makes every trip a new value.</summary>
        public const string UseKey = "tpUse";

        // Counts this client's finished trips. Static so it survives the player object being rebuilt: the portal
        // owners remember the last counter they saw from this actor, and a restarted count would be ignored.
        private static int tripCounter;

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
        }

        private void OnEnable() => PhotonNetwork.AddCallbackTarget(this);
        private void OnDisable() => PhotonNetwork.RemoveCallbackTarget(this);

        // ---- traveller half -------------------------------------------------------------------------

        private void Update()
        {
            if (!photonView.IsMine || !PhotonNetwork.InRoom || capsule == null || body == null || displacement == null)
                return;

            bool alive = lifecycle == null || lifecycle.IsAlive;
            bool stunned = status != null && status.IsStunned;
            bool silenced = overheat != null && overheat.IsSilenced;
            bool canAct = CastGate.ForActor(alive, stunned, silenced) == CastBlock.None;

            Portal current = FindStandingTeammatePortal(out Player portalOwner);
            Portal other = null;
            bool canChannel = false;

            if (current != null)
            {
                other = TeleportAbility.FindOther(Portal.ForOwner(portalOwner.ActorNumber), current);
                bool ownerReady = portalOwner.CustomProperties.TryGetValue(ReadyKey, out object raw) && raw is bool ready && ready;

                channelState.SetChannelSeconds(current.ChannelSeconds);
                canChannel = canAct && other != null && current.ChannelSeconds > 0f
                             && PortalUseRules.MayUse(isOwner: false, sameTeam: true, userAlive: alive, ownerHasCharge: ownerReady)
                             && TeleportAbility.IsExitClear(capsule, other, transform);
            }

            PortalChannelState.Result result = channelState.Tick(Time.deltaTime, current, canChannel);
            if (result == PortalChannelState.Result.Completed)
                CompleteTrip(other, portalOwner);
        }

        private Portal FindStandingTeammatePortal(out Player portalOwner)
        {
            portalOwner = null;
            Portal best = null;
            float bestDistanceSqr = float.MaxValue;
            // The Rigidbody's own position, not the Transform's: TeleportTo writes the Rigidbody, and the Transform only
            // catches up on the next physics step. Reading the stale Transform for a few frames after a trip showed the
            // traveller still standing on the DEPARTURE portal, which lifted the arrival latch and let the trip repeat.
            Vector3 position = body.position;

            foreach (Player other in PhotonNetwork.PlayerListOthers)
            {
                // Unknown teams count as not teammates: a portal is never opened to a maybe-enemy.
                if (!Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out _) || !Teams.TryGetTeam(other, out _)
                    || !Teams.AreSameTeam(PhotonNetwork.LocalPlayer, other))
                    continue;

                foreach (Portal p in Portal.ForOwner(other.ActorNumber))
                {
                    if (p == null || !PortalUseRules.IsOnPortal(p.transform.position, position, p.Radius, capsule.radius))
                        continue;

                    Vector3 delta = p.transform.position - position;
                    delta.y = 0f;
                    if (delta.sqrMagnitude < bestDistanceSqr)
                    {
                        best = p;
                        bestDistanceSqr = delta.sqrMagnitude;
                        portalOwner = other;
                    }
                }
            }

            return best;
        }

        private void CompleteTrip(Portal to, Player portalOwner)
        {
            // A knockback that started in the frame the channel finished refuses the jump - then nothing is spent.
            if (!displacement.TeleportTo(TeleportAbility.ArrivalRoot(capsule, to)))
                return;

            channelState.LatchArrival(to);
            tripCounter++;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { UseKey, new[] { portalOwner.ActorNumber, tripCounter } } });
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

            if (use[1] > lastSeen)
                lastSeenCounter[targetPlayer.ActorNumber] = use[1];
        }

        // Unused IInRoomCallbacks members.
        public void OnPlayerEnteredRoom(Player newPlayer) { }
        public void OnPlayerLeftRoom(Player otherPlayer) { }
        public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { }
        public void OnMasterClientSwitched(Player newMasterClient) { }
    }
}
