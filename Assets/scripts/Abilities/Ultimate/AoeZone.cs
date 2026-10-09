using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Combat;
using Overpower.Vision;

namespace Overpower.Abilities
{
    /// <summary>
    /// The AoE ultimate's zone: a disc that ticks damage (Radius, Damage Per Tick, Duration Seconds, Tick Seconds below) and
    /// follows its caster. AoeZoneAbility only decides WHEN it goes down; everything it does once placed lives here.
    /// TICK ON EVERY CLIENT, DESTROY ON THE OWNER ONLY (the FireField pattern): ApplyDamage on a remote copy of THEIR
    /// PlayerHealth returns default(), so owner-only ticking would hurt nobody else. Every client runs its own
    /// ZoneTickSchedule on the same clock (Age + real time on this client, as Mine.cs does), so a late joiner pays exactly
    /// the ticks it is owed. No per-tick logging: the old AoE froze a 9-player playtest doing it. NetworkedDeployable's
    /// Lifetime Seconds is unused: duration and tick count are one number seen two ways, so the finished schedule ends the
    /// zone (RequestDestroy). Following reads the caster's PhotonView transform, which PlayerNetSync lerps toward the last
    /// received position, so a victim can be inside the zone on their own client while the caster's screen shows them
    /// clear, and ApplyTick (victim-side) hits anyway: the accepted victim-favours-the-defender trade-off, as projectiles.
    /// </summary>
    [RequireComponent(typeof(PhotonView))]
    public sealed class AoeZone : NetworkedDeployable, IInRoomCallbacks
    {
        [Header("Zone")]
        [SerializeField, Tooltip("How far the zone reaches from its centre, in metres. Claude's " +
                 "number: 5.")]
        private float radius = 5f;

        [SerializeField, Tooltip("Damage dealt to every enemy standing inside on each tick. Claude's " +
                 "number: 14 - 84 over the full 6-tick duration.")]
        private float damagePerTick = 14f;

        [SerializeField, Tooltip("How long the zone lasts, in seconds, before it disappears on its " +
                 "own. Tudor's spec: 6.")]
        private float durationSeconds = 6f;

        [SerializeField, Tooltip("Seconds between damage ticks - tick k lands at k * Tick Seconds " +
                 "after the zone was placed. Tudor's spec: 1.0, which with a 6s duration gives " +
                 "exactly 6 ticks.")]
        private float tickSeconds = 1f;

        [Header("Anchor")]
        [SerializeField, Tooltip("If checked, the zone follows the caster instead of staying where " +
                 "it was cast. Tudor's spec: the zone follows, so this defaults ON. The throw (pressing the " +
                 "ultimate again to throw the zone to the cursor) only works while the zone follows its caster.")]
        private bool followsCaster = true;

        // A thrown zone stays where it landed, so it counts as a placed deployable for the corner-close clean-up.
        protected override bool FollowsCaster => followsCaster && !Thrown;

        [SerializeField, Tooltip("Which layers this zone can hit. Default is where living players " +
                 "and practice dummies are; nothing on any other layer has an IDamageable to find, so " +
                 "widening this only costs performance.")]
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

        private ZoneTickSchedule schedule;
        private CasterFollower follower;

        /// <summary>Telemetry: the id of the AoeZoneAbility that placed this, threaded through instantiationData because
        /// this deployable is a separate prefab with no AbilityDefinition. -1 if the data is missing.</summary>
        public int AbilityId { get; private set; } = -1;

        /// <summary>Radius, Damage Per Tick, Duration Seconds and Tick Seconds, read-only - the shop's pop-up shows them.</summary>
        public float Radius => radius;
        public float DamagePerTick => damagePerTick;
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
            damagePerTick = Mathf.Max(0f, damagePerTick);
            durationSeconds = Mathf.Max(0.01f, durationSeconds);
            tickSeconds = Mathf.Max(0.01f, tickSeconds);
        }

        protected override void OnPlaced(object[] data, PhotonMessageInfo info)
        {
            if (data != null && data.Length >= 1 && data[0] is int abilityId)
                AbilityId = abilityId;

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

            // Vision: shown when any part of the disc is in sight, or it reaches my team.
            VisibleWhenSeen gate = GetComponent<VisibleWhenSeen>();
            if (gate != null)
                gate.SetSeenRadius(Radius);
        }

        private void FixedUpdate()
        {
            follower?.Tick(transform);

            if (schedule == null)
                return;

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

            // Not a teammate, not the caster, not a structure, and locally authoritative - Mine.cs's rule (MineTargeting).
            List<IDamageable> targets = MineTargeting.SelectTargets(candidateBuffer, OwnerActor, OwnerTeam);
            foreach (IDamageable target in targets)
            {
                target.ApplyDamage(PlacedEffects.AoeZoneTick(damagePerTick, OwnerActor, OwnerTeam, transform.position, AbilityId, PlacedServerTimestampMs));
            }
        }
    }
}
