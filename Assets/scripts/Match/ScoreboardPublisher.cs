using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;

namespace Overpower.Match
{
    /// <summary>
    /// Tudor's D12 (hold Tab for the scoreboard): counts THIS player's own kills, deaths, assists, damage dealt and
    /// zones captured, and publishes them as one Player Property, "sb" = int[] { kills, deaths, assists, damage,
    /// captures } (ScoreboardRules.Key and its index constants). Owner only - every remote copy is dormant, and it
    /// is the only writer of this player's "sb", so nobody can overwrite it and no RPC is needed. A late joiner
    /// gets everyone's numbers with the room's Player Properties.
    ///
    /// Where each number comes from (all on this player's own client):
    ///  - kills / assists / damage: CombatEvents.LocalTakedown and LocalDamageDealt, raised when the victim's
    ///    client sends this player its credit (PlayerCombatCredit.RPC_DamageCredit) - same subscription as
    ///    UltimateCharge, gated on IsMine for the same reason (those static events belong to whoever is local).
    ///  - deaths: PlayerHealth.Died, which every kind of death goes through.
    ///  - zones captured: BuildingCapture.ZoneFlipped, raised on every client when a zone flips; this client asks
    ///    BuildingManager.TryGetZoneAt whether its own body is in that zone (ScoreTally.NoteCapture).
    ///
    /// Stats count from go-live: PlayerLifecycle.ResetForMatchStart calls ResetForMatchStart here, which zeroes the
    /// tally and publishes at once (the warm-up does not count, matching the match report).
    ///
    /// Publishing is throttled (ScorePublishThrottle, GameplayConfig.ScoreboardPublishesPerSecond); a kill, death
    /// or capture goes out at once. Built and attached by PlayerHud, which is the owner-only UI root.
    /// </summary>
    public sealed class ScoreboardPublisher : MonoBehaviour
    {
        private PhotonView photonView;
        private PlayerHealth playerHealth;
        private PlayerTeam playerTeam;
        private Rigidbody body;
        private ScoreTally tally;
        private ScorePublishThrottle throttle;
        private bool subscribed;

        /// <summary>Attaches the publisher to a player's root. Owner only; does nothing for a remote copy.</summary>
        public static ScoreboardPublisher Create(GameObject player, GameplayConfig config)
        {
            PhotonView view = player.GetComponent<PhotonView>();
            if (view == null || !view.IsMine)
                return null;

            ScoreboardPublisher publisher = player.AddComponent<ScoreboardPublisher>();
            float rate = config != null ? config.ScoreboardPublishesPerSecond : 4f;
            if (config == null)
                Debug.LogWarning($"[Scoreboard] {player.name}: no GameplayConfig - publishing at the default 4 times a second.");
            publisher.throttle = new ScorePublishThrottle(rate);
            return publisher;
        }

        private void Awake()
        {
            photonView = GetComponent<PhotonView>();
            playerHealth = GetComponent<PlayerHealth>();
            playerTeam = GetComponent<PlayerTeam>();
            body = GetComponent<Rigidbody>();
            tally = new ScoreTally();
            throttle = new ScorePublishThrottle(4f);
        }

        private void OnEnable()
        {
            // Owner only - see the class comment.
            if (photonView == null || !photonView.IsMine)
                return;

            if (playerHealth != null)
                playerHealth.Died += HandleDied;
            CombatEvents.LocalDamageDealt += HandleDamageDealt;
            CombatEvents.LocalTakedown += HandleTakedown;
            BuildingCapture.ZoneFlipped += HandleZoneFlipped;
            subscribed = true;
        }

        private void OnDisable()
        {
            if (!subscribed)
                return;

            if (playerHealth != null)
                playerHealth.Died -= HandleDied;
            CombatEvents.LocalDamageDealt -= HandleDamageDealt;
            CombatEvents.LocalTakedown -= HandleTakedown;
            BuildingCapture.ZoneFlipped -= HandleZoneFlipped;
            subscribed = false;
        }

        private void Update()
        {
            if (!subscribed || !throttle.ShouldPublish(Time.unscaledTime))
                return;

            Publish();
        }

        /// <summary>The fresh start at go-live: zero the tally and publish the zeros at once. Owner only.</summary>
        public void ResetForMatchStart()
        {
            if (photonView == null || !photonView.IsMine)
                return;

            tally.Reset();
            throttle.NoteChange(urgent: true);
            Publish();
        }

        private void HandleDamageDealt(float amount)
        {
            tally.AddDamage(amount);
            throttle.NoteChange(urgent: false);
        }

        private void HandleTakedown(bool kill)
        {
            if (kill)
                tally.AddKill();
            else
                tally.AddAssist();
            throttle.NoteChange(urgent: true);
        }

        private void HandleDied(DamageInfo info)
        {
            tally.AddDeath();
            throttle.NoteChange(urgent: true);
        }

        private void HandleZoneFlipped(int zoneId, int team)
        {
            BuildingManager buildings = BuildingManager.Instance;
            bool standing = false;
            if (buildings != null && body != null && buildings.TryGetZoneAt(body.position, out int here))
                standing = here == zoneId;

            int before = tally.Captures;
            tally.NoteCapture(standing, playerHealth != null && playerHealth.IsAlive,
                              team, playerTeam != null ? playerTeam.teamID : PlayerTeam.NoTeam);
            if (tally.Captures != before)
                throttle.NoteChange(urgent: true);
        }

        private void Publish()
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null)
                return; // Left the room: the change stays pending and goes out once there is a room again.

            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { ScoreboardRules.Key, tally.ToArray() } });
            throttle.MarkPublished(Time.unscaledTime);
        }
    }
}
