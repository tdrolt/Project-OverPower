using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Overpower.UI;
using Overpower.Vision;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Dominion
{
    /// <summary>
    /// The respawn shield, on the player prefab. After a death-respawn in a live Dominion match the owner writes two Player Properties: dShs (the
    /// server ms the shield began) and dShd (the server ms it ends); every client draws the bubble from dShd and the server clock alone, so it
    /// vanishes on time everywhere even if nobody writes. PlayerHealth asks BlocksHit before any health or armour change, PlayerStatusEffects and
    /// PlayerDisplacement ask StopsEnemyEffectFrom before an enemy's stun, slow or push lands (A24); a stopped hit stamps dBlk (at most once per
    /// Blocked Popup Seconds) and every client that can see the player pops BLOCKED. The shield ends early when this player affects an enemy (A25,
    /// not for an effect set up before the respawn, A26) or stands outside their own spawn area (A51); it never comes back until a new respawn.
    /// Zones read IsUpFor and ignore the player. Conquest never writes dShd. No RPC: late joiners read the properties like any other player value.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RespawnShield : MonoBehaviourPun, IEffectShield
    {
        [SerializeField, Tooltip("The Invulnerability ultimate's prefab. The respawn shield's bubble is built from its shield size (times Shield Bubble Scale in the " +
                 "Dominion Config), so the size is tuned in one place and never copied.")]
        private InvulnerabilityAbility invulnerabilityReference;

        private PlayerLifecycle lifecycle;
        private PlayerHealth health;
        private Rigidbody body;

        private GameObject bubble;
        private Material bubbleMaterial;
        private VisibleWhenSeen bubbleGate;
        private bool setupErrorLogged;

        private bool diedBefore;          // owner: this body has died since it spawned, so its next AliveChanged(true) is a respawn
        private ShieldJudge judge;        // owner: the per-hit judgement and the dBlk stamp spacing
        private ShieldSpawnWatch spawnWatch; // owner: drops the shield on the first frame outside the player's own spawn
        private bool shieldClearSent;     // owner: a clear is written and its echo may not be back yet: down for this client now, and not written again every frame
        private int stampSeen;            // every client: the dBlk value already popped
        private bool stampPrimed;         // the first read only takes the value in: a joiner never pops an old stamp

        private void Awake()
        {
            lifecycle = GetComponent<PlayerLifecycle>();
            health = GetComponent<PlayerHealth>();
            body = GetComponent<Rigidbody>();
            // Subscribed in Awake, not Start: PlayerLifecycle's own Start can already raise AliveChanged.
            if (lifecycle != null) lifecycle.AliveChanged += HandleAliveChanged;
            judge = new ShieldJudge(() => IsUp, () => PhotonNetwork.ServerTimestamp, PopupMs, WriteBlockedStamp);
            spawnWatch = new ShieldSpawnWatch(
                () => RespawnShieldRules.WatchArmed(IsUp, shieldClearSent),
                () => SpawnHealArea.InOwnSpawn(health != null ? health.TeamId : -1, BodyPosition()),
                () => { ClearShield(); Debug.Log("[DOMINION] respawn shield ended: this player left their spawn"); });
        }

        private void OnEnable() => CombatEvents.LocalEnemyAffected += HandleEnemyAffected;

        private void OnDisable() => CombatEvents.LocalEnemyAffected -= HandleEnemyAffected;

        private static int PopupMs()
        {
            DominionConfig config = DominionMode.Config();
            return config != null ? Mathf.RoundToInt(config.BlockedPopupSeconds * 1000f) : 0;
        }

        private static void WriteBlockedStamp(int now) =>
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { RespawnShieldRules.BlockedKey, now } });

        private void OnDestroy()
        {
            if (lifecycle != null) lifecycle.AliveChanged -= HandleAliveChanged;
            DestroyBubble();
            if (bubbleMaterial != null) Destroy(bubbleMaterial);
        }

        // ---------------------------------------------------------------- state

        /// <summary>True while this player's respawn shield is up, from the server clock and their dShd. False outside a room, before the clock has
        /// synced, and in Conquest.</summary>
        public bool IsUp => photonView != null && photonView.IsMine ? OwnerIsUp() : IsUpFor(photonView != null ? photonView.Owner : null);

        /// <summary>The end the owner's client itself started the shield with (0 = none): the property only comes back after the server's echo, so until
        /// then this is what the owner's own hit-block asks.</summary>
        private int ownEndMs;

        private bool OwnerIsUp()
        {
            Player owner = photonView.Owner;
            if (owner == null || !PhotonNetwork.InRoom) return false;
            int now = PhotonNetwork.ServerTimestamp;
            if (now == 0) return false;
            // The property still holds the end until the echo of a clear comes back: once this client has sent one, the shield is down here at once.
            return RespawnShieldRules.OwnerShieldUp(ReadInt(owner, RespawnShieldRules.ShieldKey), ownEndMs, now, shieldClearSent);
        }

        /// <summary>True while that player's respawn shield is up. The one answer the damage block, the bubble and the zones all share.</summary>
        public static bool IsUpFor(Player player)
        {
            if (player == null || !PhotonNetwork.InRoom) return false;
            int now = PhotonNetwork.ServerTimestamp;
            if (now == 0) return false;
            return RespawnShieldRules.IsUp(ReadInt(player, RespawnShieldRules.ShieldKey), now);
        }

        private static int ReadInt(Player player, string key) =>
            player != null && player.CustomProperties.TryGetValue(key, out object raw) && raw is int value ? value : 0;

        // ---------------------------------------------------------------- owner: start and end

        private void HandleAliveChanged(bool alive)
        {
            if (!photonView.IsMine) return;
            if (!alive)
            {
                diedBefore = true;
                ClearShield();
                return;
            }

            MatchDirector match = MatchDirector.Instance;
            bool fresh = lifecycle != null && lifecycle.LastAliveChangeWasFreshStart;
            // A rejoiner's new body spawns "dead" and respawns like after a death, but this component may not have seen a death in it.
            bool afterRejoin = lifecycle != null && lifecycle.RespawnIsAfterRejoin;
            bool start = RespawnShieldRules.StartsAfterRespawn(DominionMode.IsActive(), match != null && match.IsLive, diedBefore, fresh, afterRejoin);
            diedBefore = false; // one death, one shield
            if (start) StartShield();
        }

        /// <summary>The Rigidbody's position, which a respawn teleport sets at once, rather than the transform, which can still read the death spot for
        /// a physics step after it: a shield must never drop on arrival.</summary>
        private Vector3 BodyPosition() => body != null ? body.position : transform.position;

        private void StartShield()
        {
            DominionConfig config = DominionMode.Config();
            int now = PhotonNetwork.ServerTimestamp;
            if (config == null || now == 0) return; // no number to use and no clock to count on: no shield, rather than a guessed one
            int end = RespawnShieldRules.EndMs(now, config.ShieldSeconds);
            // Start and end together in one write: A26 judges an old effect from the start, so nobody subtracts their own Shield Seconds from the end.
            ownEndMs = end; // up on this client at once, not a round trip later
            shieldClearSent = false; // a new shield is up again at once and may drop again when it leaves the spawn
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { RespawnShieldRules.StartKey, now }, { RespawnShieldRules.ShieldKey, end } });
            Debug.Log($"[DOMINION] respawn shield up for {config.ShieldSeconds:0.#} s (ends {end})");
        }

        /// <summary>Owner: drop the shield (writes 0, only when something is set). Called on death, on a hit, on leaving the spawn and at a fresh start.</summary>
        public void ClearShield()
        {
            if (!photonView.IsMine || photonView.Owner == null) return;
            shieldClearSent = true; // down for this client now, whatever the property still holds
            bool startedHere = ownEndMs != 0; // started on this client and maybe not echoed yet: the cleared value must still be written
            ownEndMs = 0;
            if (!RespawnShieldRules.MustWriteClear(startedHere, ReadInt(photonView.Owner, RespawnShieldRules.ShieldKey))) return;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable
            {
                { RespawnShieldRules.ShieldKey, RespawnShieldRules.EndAfterDamageDealt() },
                { RespawnShieldRules.StartKey, 0 },
            });
        }

        /// <summary>A round's or break's fresh start: no shield (A16), and the next respawn must follow a new death.</summary>
        public void ClearForFreshStart()
        {
            diedBefore = false;
            ClearShield();
        }

        private void HandleEnemyAffected(bool victimShielded, bool fromBeforeRespawn)
        {
            // LocalEnemyAffected is raised only on the machine whose player did the thing, so this is the owner of the shield that did it.
            if (!photonView.IsMine || !RespawnShieldRules.EndsOnEnemyAffected(IsUp, victimShielded, fromBeforeRespawn)) return;
            ClearShield();
            Debug.Log("[DOMINION] respawn shield ended: this player hit an enemy");
        }

        /// <summary>The attacker's side of A25, called by a status or push this client simulates on its copy of a player it does not own. When the
        /// effect is this client's own player's and the target is a living enemy, the attacker's shield hears of it (no message: the attacker's client
        /// simulates the effect anyway, and the victim's own shield is read from dShd). effectPlacedMs is when the mine or fence behind the effect was
        /// set up (0 = a direct effect), so an old one does not end the new bubble (A26).</summary>
        public static void NoteMyEffectOnCopy(PhotonView victim, int sourceActor, int effectPlacedMs = 0)
        {
            if (victim == null || victim.Owner == null || !PhotonNetwork.InRoom) return;
            Player me = PhotonNetwork.LocalPlayer;
            if (me == null) return;
            PlayerLifecycle victimLife = victim.GetComponent<PlayerLifecycle>();
            RespawnShieldRules.EffectReportDecision decision = RespawnShieldRules.EffectReport(
                victim.IsMine, sourceActor == me.ActorNumber, victimLife == null || victimLife.IsAlive, Teams.AreSameTeam(me, victim.Owner),
                IsUpFor(victim.Owner), effectPlacedMs, ShieldStartOf(me));
            if (decision.Report) CombatEvents.RaiseEnemyAffected(decision.VictimShielded, decision.FromBeforeRespawn);
        }

        /// <summary>The server ms that player's shield ends (0 = none). Read from dShd.</summary>
        public static int ShieldEndOf(Player player) => ReadInt(player, RespawnShieldRules.ShieldKey);

        /// <summary>The server ms that player's shield began (0 = none). Read from dShs.</summary>
        public static int ShieldStartOf(Player player) => ReadInt(player, RespawnShieldRules.StartKey);

        /// <summary>A26, on the victim's client: true when the effect that hurt it was set up before the attacker's current shield began (a mine laid
        /// before its respawn). effectPlacedMs 0 = a direct hit.</summary>
        public static bool IsFromBeforeRespawn(Player attacker, int effectPlacedMs)
        {
            return RespawnShieldRules.IsFromBeforeRespawn(effectPlacedMs, ShieldStartOf(attacker));
        }

        // ---------------------------------------------------------------- victim: stop a hit

        /// <summary>Called by PlayerHealth.ApplyDamage on the victim's own client before anything changes: true = stop the hit here. A stopped hit
        /// stamps dBlk when one is due; the combat clock and the credit message are never reached, so the attacker earns neither charge nor an
        /// early end to its own shield.</summary>
        public bool BlocksHit(RespawnShieldRules.Origin origin) => photonView.IsMine && judge != null && judge.JudgeHit(origin);

        /// <summary>A24: called by PlayerStatusEffects.Apply and PlayerDisplacement.Displace on this player's own client before an enemy's stun,
        /// slow, burn, vulnerability or push lands. True = stop it (and BLOCKED is stamped like for a stopped hit). Own and teammate effects land.</summary>
        public bool StopsEnemyEffectFrom(int sourceActor)
        {
            if (!photonView.IsMine || judge == null) return false;
            bool itself = sourceActor == photonView.OwnerActorNr;
            bool sameTeam = !itself && Teams.AreSameTeam(PhotonNetwork.CurrentRoom?.GetPlayer(sourceActor), photonView.Owner);
            return judge.JudgeEffect(RespawnShieldRules.OriginOf(itself, sameTeam));
        }

        // ---------------------------------------------------------------- every client: bubble and BLOCKED

        private void Update()
        {
            if (photonView == null || photonView.Owner == null) return;

            if (photonView.IsMine && (lifecycle == null || lifecycle.IsAlive)) spawnWatch.Tick();

            bool wanted = IsUp && (lifecycle == null || lifecycle.IsAlive);
            if (wanted && bubble == null) BuildBubble();
            else if (!wanted && bubble != null) DestroyBubble();

            WatchBlockedStamp();
        }

        private void WatchBlockedStamp()
        {
            int stamp = ReadInt(photonView.Owner, RespawnShieldRules.BlockedKey);
            if (!stampPrimed)
            {
                stampPrimed = true;
                stampSeen = stamp;
                return;
            }
            if (!RespawnShieldRules.IsNewStamp(stampSeen, stamp)) return;
            stampSeen = stamp;

            // Only if this client can see the shielded player, asked of the team's sight directly: the bubble may not exist yet when the stamp
            // arrives, and a popup must never show where the player is hidden in the fog.
            if (TeamSight.Local != null && !TeamSight.Local.CanSeePlayer(photonView)) return;
            DominionConfig config = DominionMode.Config();
            if (config == null) return;
            CombatEvents.RaiseShieldBlockedSeen(transform, config.BlockedPopupSeconds);
        }

        private void BuildBubble()
        {
            DominionConfig config = DominionMode.Config();
            UiTheme theme = health != null ? health.Theme : null;
            if (config == null || theme == null || invulnerabilityReference == null)
            {
                if (!setupErrorLogged)
                {
                    setupErrorLogged = true;
                    Debug.LogError($"[DOMINION] {name}: the respawn shield bubble needs the Dominion config, the UiTheme and the Invulnerability reference - none drawn.");
                }
                return;
            }

            bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bubble.name = "Respawn Shield VFX (cosmetic only)";
            DestroyImmediate(bubble.GetComponent<Collider>()); // never a solid, physics-blocking object, not even for a frame
            var renderer = bubble.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = BubbleMaterial(theme.respawnShieldColor);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            bubble.transform.SetParent(transform, false);
            bubble.transform.localPosition = Vector3.zero;
            bubble.transform.localScale = Vector3.one * (invulnerabilityReference.ShieldDiameter * config.ShieldBubbleScale);
            bubbleGate = VisibleWhenSeen.AttachToCaster(bubble, photonView); // hidden in the fog together with the player it surrounds
        }

        private Material BubbleMaterial(Color color)
        {
            if (bubbleMaterial == null)
            {
                bubbleMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                bubbleMaterial.SetFloat("_Surface", 1f); // transparent, so the player can be seen through the bubble
                bubbleMaterial.SetFloat("_Blend", 0f);
                bubbleMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                bubbleMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                bubbleMaterial.SetFloat("_ZWrite", 0f);
                bubbleMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                bubbleMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                bubbleMaterial.SetOverrideTag("RenderType", "Transparent");
            }
            bubbleMaterial.color = color;
            return bubbleMaterial;
        }

        private void DestroyBubble()
        {
            if (bubble != null) Destroy(bubble);
            bubble = null;
            bubbleGate = null;
        }
    }
}
