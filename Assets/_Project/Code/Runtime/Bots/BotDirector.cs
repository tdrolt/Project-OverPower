using System.Collections.Generic;
using Overpower.Abilities;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Overpower.Vision;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace Overpower.Bots
{
    /// <summary>
    /// The host's side of the bots: once the room's scene is up and the room has bots, it builds the walkable maps (BotNavMesh) and gives
    /// every bot body this client controls a BotBrain and a scoreboard publisher. It rebuilds the maps when the arena changes shape (the
    /// shrink). It also keeps what every brain shares: the list of brains and the Heal Zones standing now. On any other client, and on a
    /// host with no bots, it does nothing. Added by RoomManager.
    /// </summary>
    public sealed class BotDirector : MonoBehaviour
    {
        // The body's half width when the player prefab cannot be read: what the capsule measures.
        private const float FallbackBodyRadius = 0.7f;
        // The Heal Zones are looked up at most this often, for every bot at once.
        private const float HealZoneLookupSeconds = 0.5f;

        private RoomManager roomManager;
        private BotConfig config;
        private GameplayConfig gameplay;
        private VisionConfig vision;

        private readonly BotNavMesh navMesh = new BotNavMesh();
        private readonly List<PhotonView> bodies = new List<PhotonView>();
        private readonly List<BotBrain> brains = new List<BotBrain>();
        private readonly List<AoeZone> healZones = new List<AoeZone>();
        private float healZonesReadAt = -99f;
        private int builtForCut = int.MinValue;
        private float rebuildAt = -1f;
        private float nextCheckAt;

        public BotNavMesh NavMesh => navMesh;
        public BotConfig Config => config;
        public GameplayConfig Gameplay => gameplay;
        public RoomManager Room => roomManager;
        /// <summary>Every bot brain on this host (a destroyed body's brain leaves the list).</summary>
        public IReadOnlyList<BotBrain> Brains => brains;

        public static BotDirector Instance { get; private set; }

        public void Init(RoomManager manager, BotConfig botConfig, GameplayConfig gameplayConfig, VisionConfig visionConfig)
        {
            roomManager = manager;
            config = botConfig;
            gameplay = gameplayConfig;
            vision = visionConfig;
        }

        private void OnEnable() => Instance = this;

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDestroy() => navMesh.Dispose();

        /// <summary>The Heal Zones standing now (a destroyed one reads as null until the next look).</summary>
        public IReadOnlyList<AoeZone> HealZones()
        {
            if (Time.time - healZonesReadAt >= HealZoneLookupSeconds)
            {
                healZonesReadAt = Time.time;
                healZones.Clear();
                healZones.AddRange(FindObjectsByType<AoeZone>(FindObjectsSortMode.None));
            }
            return healZones;
        }

        public void Forget(BotBrain brain) => brains.Remove(brain);

        private void Update()
        {
            navMesh.Tick();
            if (Time.unscaledTime < nextCheckAt) return;
            nextCheckAt = Time.unscaledTime + 0.25f;
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || roomManager == null || !roomManager.RoomSceneIsLoaded || config == null) return;
            if (!RoomHasBots()) return;

            int cut = MatchDirector.Instance != null ? MatchDirector.Instance.CutTeam : -1;
            if (cut != builtForCut)
            {
                // The shrink's wall goes up when the cut happens: the map is read again a moment later, once it stands.
                rebuildAt = builtForCut == int.MinValue ? Time.unscaledTime : Time.unscaledTime + 1f;
                builtForCut = cut;
            }
            if (rebuildAt >= 0f && Time.unscaledTime >= rebuildAt)
            {
                rebuildAt = -1f;
                ConfigureSight();
                navMesh.Build(config, BodyRadius());
            }

            foreach (Player player in PhotonNetwork.CurrentRoom.Players.Values)
            {
                if (!(player is BotPlayer bot)) continue;
                PhotonView body = PlayerLookup.GetPhotonViewFor(bot.ActorNumber);
                if (body == null || !body.IsMine || body.GetComponent<BotBrain>() != null) continue;
                body.GetComponent<PlayerInputRouter>()?.BeginBotDrive();
                if (gameplay != null) ScoreboardPublisher.Create(body.gameObject, gameplay);
                BotBrain brain = body.gameObject.AddComponent<BotBrain>();
                brain.Init(this, bot);
                brains.Add(brain);
                Debug.Log($"[BOTS] {bot.NickName} ({bot.ActorNumber}) is playing build {bot.Build}");
            }
        }

        private static bool RoomHasBots()
        {
            foreach (Player player in PhotonNetwork.CurrentRoom.Players.Values)
                if (player is BotPlayer) return true;
            return false;
        }

        // The tight walkable map is exactly as wide as the player's capsule.
        private static float BodyRadius()
        {
            float radius = BuildingManager.Instance != null ? BuildingManager.Instance.PlayerBodyRadius : 0f;
            return radius > 0f ? radius : FallbackBodyRadius;
        }

        /// <summary>A bot sees by its team's sight, read from the same VisionConfig as players; a bot holding Scope by the Scope's own
        /// numbers, from the module prefab of the build that carries it.</summary>
        private void ConfigureSight()
        {
            if (vision == null) return;
            ScopeAbility scope = null;
            foreach (BotConfig.Build build in config.Builds)
            {
                GameObject module = build != null && build.Attachment != null ? build.Attachment.ModulePrefab : null;
                scope = module != null ? module.GetComponent<ScopeAbility>() : null;
                if (scope != null) break;
            }
            TeamSightQuery.Configure(vision, scope != null ? scope.SightConeAngleDegrees : 0f, scope != null ? scope.SightConeLength : 0f,
                scope != null ? scope.SightCircleChange : 0f);
        }
    }
}
