using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Overpower.Abilities;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Match;
using Overpower.UI;
using Overpower.Vision;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Overpower.Dominion
{
    /// <summary>
    /// Dominion Task 7, the respawn shield, on the player prefab. After a death-respawn in a live Dominion match the owner writes the Player
    /// Property dShd (the server ms the shield ends). Every client draws a blue bubble from dShd and the server clock alone, so it vanishes on time
    /// everywhere even if nobody writes. While it is up PlayerHealth asks BlocksHit before any health, armour or combat-clock change; a stopped
    /// hit stamps dBlk (at most once per Blocked Popup Seconds) and every client that can see the player pops BLOCKED over them. Dealing damage
    /// (the credit RPC reaching this attacker) clears dShd; a cast that hits nobody does not. Zones read the same property (IsUpFor) and ignore the
    /// player. Conquest never writes dShd.
    ///
    /// No RPC: the state is two Player Properties, so late joiners and rejoiners read it like any other player value.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RespawnShield : MonoBehaviourPun
    {
        [SerializeField, Tooltip("The Invulnerability ultimate's prefab. The respawn shield's bubble is built from its shield size (times Shield Bubble Scale in the " +
                 "Dominion Config), so the size is tuned in one place and never copied.")]
        private InvulnerabilityAbility invulnerabilityReference;

        private PlayerLifecycle lifecycle;
        private PlayerHealth health;

        private GameObject bubble;
        private Material bubbleMaterial;
        private VisibleWhenSeen bubbleGate;
        private bool setupErrorLogged;

        private bool diedBefore;          // owner: this body has died since it spawned, so its next AliveChanged(true) is a respawn
        private int lastStampWritten;     // owner: the dBlk value last written (0 = none)
        private int stampSeen;            // every client: the dBlk value already popped
        private bool stampPrimed;         // the first read only takes the value in: a joiner never pops an old stamp

        private void Awake()
        {
            lifecycle = GetComponent<PlayerLifecycle>();
            health = GetComponent<PlayerHealth>();
            // Subscribed in Awake, not Start: PlayerLifecycle's own Start can already raise AliveChanged (AbilityRunner does the same).
            if (lifecycle != null) lifecycle.AliveChanged += HandleAliveChanged;
        }

        private void OnEnable() => CombatEvents.LocalDamageDealt += HandleDamageDealt;

        private void OnDisable() => CombatEvents.LocalDamageDealt -= HandleDamageDealt;

        private void OnDestroy()
        {
            if (lifecycle != null) lifecycle.AliveChanged -= HandleAliveChanged;
            DestroyBubble();
            if (bubbleMaterial != null) Destroy(bubbleMaterial);
        }

        // ---------------------------------------------------------------- state

        /// <summary>True while this player's respawn shield is up, from the room's server clock and their dShd. False outside a room, before the
        /// clock has synced, and in Conquest (nothing is ever written there).</summary>
        public bool IsUp => IsUpFor(photonView != null ? photonView.Owner : null);

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
            bool start = RespawnShieldRules.StartsAfterRespawn(DominionMode.IsActive(), match != null && match.IsLive, diedBefore, fresh);
            diedBefore = false; // one death, one shield
            if (start) StartShield();
        }

        private void StartShield()
        {
            DominionConfig config = DominionMode.Config();
            int now = PhotonNetwork.ServerTimestamp;
            if (config == null || now == 0) return; // no number to use and no clock to count on: no shield, rather than a guessed one
            int end = RespawnShieldRules.EndMs(now, config.ShieldSeconds);
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { RespawnShieldRules.ShieldKey, end } });
            Debug.Log($"[DOMINION] respawn shield up for {config.ShieldSeconds:0.#} s (ends {end})");
        }

        /// <summary>Owner: drop the shield (writes 0, only when something is set). Called on death and at a round's or break's fresh start.</summary>
        public void ClearShield()
        {
            if (!photonView.IsMine || photonView.Owner == null) return;
            if (ReadInt(photonView.Owner, RespawnShieldRules.ShieldKey) == RespawnShieldRules.EndAfterDamageDealt()) return;
            PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { RespawnShieldRules.ShieldKey, RespawnShieldRules.EndAfterDamageDealt() } });
        }

        /// <summary>A round's or break's fresh start: no shield (A16), and the next respawn must follow a new death.</summary>
        public void ClearForFreshStart()
        {
            diedBefore = false;
            ClearShield();
        }

        private void HandleDamageDealt(float amount)
        {
            // LocalDamageDealt is raised only on the machine whose player dealt the damage, so this is the owner of the shield that dealt it.
            if (!photonView.IsMine || !RespawnShieldRules.ClearsOnDamageDealt(amount) || !IsUp) return;
            ClearShield();
            Debug.Log("[DOMINION] respawn shield ended: this player dealt damage");
        }

        // ---------------------------------------------------------------- victim: stop a hit

        /// <summary>Called by PlayerHealth.ApplyDamage on the victim's own client before anything changes: true = stop the hit here. A stopped hit
        /// stamps dBlk when one is due; the combat clock and the credit message are never reached, so the attacker earns neither charge nor an
        /// early end to its own shield.</summary>
        public bool BlocksHit(bool fromTeammate)
        {
            if (!photonView.IsMine) return false;
            int now = PhotonNetwork.ServerTimestamp;
            DominionConfig config = DominionMode.Config();
            int popupMs = config != null ? Mathf.RoundToInt(config.BlockedPopupSeconds * 1000f) : 0;
            RespawnShieldRules.HitDecision decision = RespawnShieldRules.OnIncomingHit(IsUp, fromTeammate, lastStampWritten, now, popupMs);
            if (decision.WriteStamp && now != 0)
            {
                lastStampWritten = now;
                PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { RespawnShieldRules.BlockedKey, now } });
            }
            return decision.Blocked;
        }

        // ---------------------------------------------------------------- every client: bubble and BLOCKED

        private void Update()
        {
            if (photonView == null || photonView.Owner == null) return;

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

            // Like every other popup: only if this client can see the shielded player (the bubble carries the fog gate; the player's own is always shown).
            if (!photonView.IsMine && bubbleGate != null && !bubbleGate.Shown) return;
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
