using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.Serialization;
using Overpower.Combat;
using Overpower.Vision;

namespace Overpower.Abilities
{
    /// <summary>
    /// The Heal Zone (the zone ultimate): a disc that follows its caster and on every tick heals the caster and the caster's teammates
    /// inside (the numbers below). AoeZoneAbility only decides WHEN it goes down. TICK ON EVERY CLIENT, DESTROY ON THE OWNER ONLY (the
    /// FireField pattern): PlayerHealth.Heal on a remote copy does nothing, so each healed player's own client heals them, as damage is
    /// victim-side. NetworkedDeployable's Lifetime Seconds is unused: the finished tick schedule ends the zone (RequestDestroy). No
    /// per-tick logging: with 9 players it froze the game.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public sealed class AoeZone : NetworkedDeployable, IInRoomCallbacks
    {
        [Header("Zone")]
        [SerializeField, Tooltip("How far the zone reaches from its centre, in metres.")]
        private float radius = 5f;

        [SerializeField, FormerlySerializedAs("damagePerTick"), Tooltip("Health given back on each tick to you and every teammate " +
                 "standing inside. Never past full health, and never armour. Enemies inside are not touched. 14 a tick heals up to 84 " +
                 "over the full 6 ticks.")]
        private float healPerTick = 14f;

        [SerializeField, Tooltip("How long the zone lasts, in seconds, before it disappears on its " +
                 "own.")]
        private float durationSeconds = 6f;

        [SerializeField, Tooltip("Seconds between heal ticks - tick k lands at k * Tick Seconds " +
                 "after the zone was placed. 1 with a 6 s duration gives exactly 6 ticks.")]
        private float tickSeconds = 1f;

        [Header("Anchor")]
        [SerializeField, Tooltip("If checked, the zone follows the caster instead of staying where " +
                 "it was cast (on by default). The throw (pressing the " +
                 "ultimate again to throw the zone to the cursor) only works while the zone follows its caster.")]
        private bool followsCaster = true;

        // A thrown zone stays where it landed, so it counts as a placed deployable for the corner-close clean-up.
        protected override bool FollowsCaster => followsCaster && !Thrown;

        [SerializeField, Tooltip("Which layers this zone looks for players on. Default is every layer; " +
                 "only players can be healed, so narrowing it to the players' layer only saves performance.")]
        private LayerMask detectionMask = ~0;

        [SerializeField, Tooltip("The zone's visible mesh, if any. Resized sideways to match Radius " +
                 "the moment the zone is placed, so one prefab covers every size of zone. Left empty " +
                 "skips resizing anything.")]
        private Transform visual;

        // Not a design tunable: how many colliders one tick considers (FireField's MaxBurningColliders reasoning).
        private const int MaxOverlapColliders = 32;
        private readonly Collider[] overlapBuffer = new Collider[MaxOverlapColliders];

        // Scratch set for one tick's overlap pass, so a target of several colliders is only counted once (as FireField).
        private readonly HashSet<IDamageable> seenThisTick = new HashSet<IDamageable>();
        private readonly List<IDamageable> candidateBuffer = new List<IDamageable>();
        private readonly List<IDamageable> healedBuffer = new List<IDamageable>();

        private ZoneTickSchedule schedule;
        private CasterFollower follower;
        private VisibleWhenSeen gate;

        /// <summary>Radius, Heal Per Tick, Duration Seconds and Tick Seconds, read-only - the shop's pop-up shows them.</summary>
        public float Radius => radius;
        public float HealPerTick => healPerTick;
        public float DurationSeconds => durationSeconds;
        public float TickSeconds => tickSeconds;

        // The real Time.time OnPlaced ran on THIS client, turning Age (a one-time snapshot) into a number that keeps
        // growing - the secondsSincePlaced pattern Mine.cs documents.
        private float localPlacedRealTime;

        /// <summary>True once the zone has been thrown to a spot (D11): it then stays there and no longer follows the
        /// caster. Set on every client by the thrower's own call or by the thrower's Player Property
        /// (AoeZoneRecast.PropertyKey) arriving, which is also how a late joiner finds the zone at the thrown spot.</summary>
        public bool Thrown { get; private set; }

        /// <summary>True while the zone still follows its caster: not thrown, and the caster has not
        /// died or left (the follower switches itself off then).</summary>
        public bool IsFollowing => !Thrown && (follower == null ? followsCaster : follower.IsActive);

        /// <summary>Moves the zone to point and stops it following. Only the first throw counts.</summary>
        public void Throw(Vector3 point)
        {
            if (Thrown)
                return;

            Thrown = true;
            transform.position = point;
            follower?.Stop(); // a zone not yet placed picks the stop up in OnPlaced.
        }

        private void OnEnable() => PhotonNetwork.AddCallbackTarget(this);
        private void OnDisable() => PhotonNetwork.RemoveCallbackTarget(this);

        public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
        {
            if (targetPlayer == null || targetPlayer.ActorNumber != OwnerActor || photonView == null ||
                !changedProps.TryGetValue(AoeZoneRecast.PropertyKey, out object raw))
                return;

            if (AoeZoneRecast.TryDecode(raw, photonView.ViewID, out Vector3 point))
                Throw(point);
        }

        // Unused IInRoomCallbacks members.
        public void OnPlayerEnteredRoom(Player newPlayer) { }
        public void OnPlayerLeftRoom(Player otherPlayer) { }
        public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { }
        public void OnMasterClientSwitched(Player newMasterClient) { }

        private void OnValidate()
        {
            radius = Mathf.Max(0.1f, radius);
            healPerTick = Mathf.Max(0f, healPerTick);
            durationSeconds = Mathf.Max(0.01f, durationSeconds);
            tickSeconds = Mathf.Max(0.01f, tickSeconds);
        }

        protected override void OnPlaced(object[] data, PhotonMessageInfo info)
        {
            int totalTicks = Mathf.Max(1, Mathf.RoundToInt(durationSeconds / tickSeconds));

            // Age already reflects how old this zone really is on THIS client (NetworkedDeployable derives it from the
            // placer's ServerTimestamp in instantiationData, never info.SentServerTime, so a late joiner's replay is not
            // "since I joined"). Seeding the schedule with it skips ticks that passed before this client existed instead of
            // firing them all at once - see ZoneTickSchedule for the hitch-vs-late-start distinction.
            schedule = new ZoneTickSchedule(tickSeconds, totalTicks, (float)Age);
            follower = new CasterFollower(followsCaster, OwnerActor);
            localPlacedRealTime = Time.time;

            // Thrown before this client placed the zone (a late joiner, or the property beating the
            // instantiate): the owner's property already names this zone, so land where it says.
            if (Thrown)
                follower.Stop();
            else if (PhotonNetwork.CurrentRoom != null)
            {
                Player owner = PhotonNetwork.CurrentRoom.GetPlayer(OwnerActor);
                if (owner != null && owner.CustomProperties.TryGetValue(AoeZoneRecast.PropertyKey, out object raw) &&
                    AoeZoneRecast.TryDecode(raw, photonView.ViewID, out Vector3 thrownTo))
                    Throw(thrownTo);
            }

            if (visual != null)
                visual.localScale = new Vector3(radius * 2f, visual.localScale.y, radius * 2f);

            // Vision: an enemy's zone does nothing to us, so reaching my team never shows it (see Update for the following zone).
            gate = GetComponent<VisibleWhenSeen>();
            if (gate != null)
                gate.SetSeenRadius(Radius, false);
        }

        // A zone round its caster is hidden with them: drawn by the spot rule, a ring whose rim crosses my sight would centre on a hidden caster.
        private void Update()
        {
            if (gate != null)
                gate.FollowCaster(IsFollowing ? follower?.CasterView : null);
        }

        private void FixedUpdate()
        {
            follower?.Tick(transform);

            if (schedule == null)
                return;

            // Every client runs its own schedule on the same clock (Age + real time here, as Mine.cs), so a late joiner gets exactly the ticks it is owed.
            float secondsSincePlaced = (float)Age + (Time.time - localPlacedRealTime);
            int dueTicks = schedule.ConsumeDueTicks(secondsSincePlaced);
            for (int i = 0; i < dueTicks; i++)
                ApplyTick();

            // Every client stops ticking when its own schedule completes; only the owner deletes the object (as FireField.Burn).
            if (schedule.IsComplete && IsOwnerClient)
                RequestDestroy();
        }

        private void ApplyTick()
        {
            if (radius <= 0f)
                return;

            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlapBuffer,
                                                       detectionMask, QueryTriggerInteraction.Ignore);

            seenThisTick.Clear();
            candidateBuffer.Clear();
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlapBuffer[i];
                if (collider == null)
                    continue;

                IDamageable candidate = collider.GetComponentInParent<IDamageable>();
                if (candidate == null || !seenThisTick.Add(candidate))
                    continue;

                candidateBuffer.Add(candidate);
            }

            // The zone follows the caster's lerped copy, so a teammate can be inside on their own client while the caster's screen
            // shows them just outside: the heal follows the healed player's own screen.
            HealZoneTargeting.SelectHealed(candidateBuffer, OwnerActor, OwnerTeam, healedBuffer);
            for (int i = 0; i < healedBuffer.Count; i++)
                if (healedBuffer[i] is PlayerHealth player)
                    player.Heal(healPerTick);
        }
    }
}
