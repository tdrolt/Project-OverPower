using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Combat;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Overpower.UI;

namespace Overpower.Dominion
{
    /// <summary>
    /// Dominion Task 8, the sudden-death circle, on every client. Added at runtime next to DominionDirector (no scene footprint). While the room is in
    /// sudden death it works out the circle from the room alone - the start (dSd), the server clock and the config - so it is the same size on every
    /// screen at the same moment, and does three things with it:
    ///  1. a red ring on the ground and a red tint over everything OUTSIDE it (a flat mesh from the radius out past the map, over the floor, with no
    ///     collider: it hides no player and blocks no shot or sight);
    ///  2. the answers other things ask - IsSuddenDeath, CurrentRadius, SecondsUntilStopped, Centre - the minimap draws its own red outside from them, and the
    ///     sudden-death banner and shrink timer (Task 9) read them;
    ///  3. the damage: this client's own living player on a tied team loses Damage Per Second Outside while outside the circle. Victim-side, through
    ///     PlayerHealth's one damage funnel, as a SuddenDeath hit with no attacker (like a burn with no owner): armour and damage reduction behave as for any damage, but Invulnerability (running or armed) does not stop it (A30). The cost is
    ///     added up every frame and applied every DamageTickSeconds (half a second), so the rate is exact whatever the frame rate.
    ///
    /// The circle's centre is the Tier 4 zone's tower (3v3v3) or halfway between the scoring zones (2v2); SuddenDeathRules.TryCentre decides.
    /// </summary>
    public sealed class SuddenDeathZone : MonoBehaviour
    {
        public static SuddenDeathZone Instance { get; private set; }

        // Looks and cadence, not gameplay values: the ring's smoothness, how far above the floor the sheets lie (so they do not flicker against it),
        // how far the red reaches beyond the circle (past any map), and how often the circle's cost is applied.
        private const int RingSegments = 128;
        private const float RingLiftMetres = 0.12f;
        private const float TintLiftMetres = 0.06f;
        private const float TintReachMetres = 400f;
        private const float DamageTickSeconds = 0.5f;

        /// <summary>Sudden death is on: the room's stage says so, a circle start is written, and the circle can be placed.</summary>
        public bool IsSuddenDeath { get; private set; }

        /// <summary>The circle's radius in metres right now (the start radius until it starts to shrink). 0 while sudden death is not on.</summary>
        public float CurrentRadius { get; private set; }

        /// <summary>The circle's start and final radius for this match size, in metres. 0 while sudden death is not on.</summary>
        public float StartRadius { get; private set; }
        public float FinalRadius { get; private set; }

        /// <summary>Seconds until the circle stops shrinking (counting the get-ready beat before it starts). 0 once it has, and while it is not on.</summary>
        public float SecondsUntilStopped { get; private set; }

        /// <summary>The circle's centre on the ground (x, z). Valid while IsSuddenDeath.</summary>
        public Vector2 Centre { get; private set; }

        /// <summary>The height of the floor at the centre, for drawing.</summary>
        public float FloorY { get; private set; }

        private bool centreResolved;
        private readonly List<Vector3> centreZones = new List<Vector3>();
        private readonly List<Vector3> scoringZones = new List<Vector3>();

        private RoomManager rooms;
        private LineRenderer ringLine;
        private Material ringMaterial;
        private MeshRenderer tintRenderer;
        private MeshFilter tintFilter;
        private Material tintMaterial;
        private Mesh tintMesh;
        private Vector3[] tintVertices;
        private float drawnRadius = -1f;
        private bool setupErrorLogged;

        private float owedDamage;
        private float sinceDamageTick;

        private void Awake()
        {
            if (Instance == null) Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (ringLine != null) Destroy(ringLine.gameObject);
            if (tintRenderer != null) Destroy(tintRenderer.gameObject);
            if (ringMaterial != null) Destroy(ringMaterial);
            if (tintMaterial != null) Destroy(tintMaterial);
            if (tintMesh != null) Destroy(tintMesh);
        }

        private void Update()
        {
            Refresh();
            ApplyOutsideDamage(Time.deltaTime);
        }

        private void LateUpdate() => Draw();

        // ---------------------------------------------------------------- the circle, from the room

        private void Refresh()
        {
            DominionDirector director = DominionDirector.Instance;
            DominionConfig config = DominionMode.Config();
            bool on = PhotonNetwork.InRoom && director != null && config != null && DominionMode.IsActive()
                      && director.Stage == DominionStage.SuddenDeath && director.SuddenDeathStartMs != 0;
            if (!on)
            {
                IsSuddenDeath = false;
                CurrentRadius = StartRadius = FinalRadius = SecondsUntilStopped = 0f;
                centreResolved = false; // the zones are looked up afresh for the next sudden death
                return;
            }

            if (!centreResolved) centreResolved = ResolveCentre();
            if (!centreResolved)
            {
                IsSuddenDeath = false; // nothing to centre on yet (the towers register in their Start)
                return;
            }

            int teamCount = DominionMode.TeamCountOfCurrentRoom();
            StartRadius = SuddenDeathRules.StartRadiusFor(teamCount, config.SuddenDeathStartRadius2v2, config.SuddenDeathStartRadius3v3v3);
            FinalRadius = SuddenDeathRules.FinalRadiusFor(teamCount, config.FinalRadius2v2, config.FinalRadius3v3v3);
            int now = PhotonNetwork.ServerTimestamp;
            int start = director.SuddenDeathStartMs;
            // With the server clock not synced there is nothing to count from: the full-size circle, never a guess.
            CurrentRadius = now == 0 ? StartRadius : SuddenDeathRules.Radius(start, now, config.SuddenDeathShrinkSeconds, StartRadius, FinalRadius);
            SecondsUntilStopped = now == 0 ? 0f : SuddenDeathRules.SecondsUntilStopped(start, now, config.SuddenDeathShrinkSeconds);
            IsSuddenDeath = true;
        }

        private bool ResolveCentre()
        {
            BuildingManager buildings = BuildingManager.Instance;
            if (buildings == null) return false;
            centreZones.Clear();
            scoringZones.Clear();
            MatchDirector match = MatchDirector.Instance;
            bool threeTeams = DominionMode.TeamCountOfCurrentRoom() == 3; // only a 3v3v3 match has a centre that plays (as the points beat's CentreInPlay)
            for (int zone = 0; zone < buildings.ZoneCount; zone++)
            {
                bool isCapital = buildings.CathedralBuildingIDs.ContainsKey(zone);
                bool outOfPlay = match != null && match.IsOutOfPlay(zone);
                if (!SuddenDeathRules.ZoneShapesTheCircle(buildings.BaseTierOf(zone), isCapital, outOfPlay)) continue;
                if (!buildings.TryGetZoneCentre(zone, out Vector3 at)) continue;
                if (buildings.BaseTierOf(zone) == DominionRules.CentreTier)
                {
                    if (threeTeams) centreZones.Add(at);
                }
                else scoringZones.Add(at);
            }
            if (!SuddenDeathRules.TryCentre(centreZones, scoringZones, out Vector2 centre, out float floorY)) return false;
            Centre = centre;
            FloorY = floorY;
            return true;
        }

        // ---------------------------------------------------------------- damage, victim-side

        private void ApplyOutsideDamage(float deltaTime)
        {
            if (!IsSuddenDeath || !PhotonNetwork.InRoom)
            {
                owedDamage = sinceDamageTick = 0f;
                return;
            }
            PhotonView view = PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber);
            PlayerHealth health = view != null ? view.GetComponent<PlayerHealth>() : null;
            PlayerLifecycle lifecycle = view != null ? view.GetComponent<PlayerLifecycle>() : null;
            DominionConfig config = DominionMode.Config();
            if (health == null || lifecycle == null || config == null || !Teams.TryGetPlayingTeam(PhotonNetwork.LocalPlayer, out int team))
            {
                owedDamage = sinceDamageTick = 0f;
                return;
            }

            Vector3 at = view.transform.position;
            owedDamage += SuddenDeathRules.DamageAt(true, SuddenDeathRules.TeamPlays(team, DominionDirector.Instance.SuddenDeathTeams), lifecycle.IsAlive,
                new Vector2(at.x, at.z), Centre, CurrentRadius, config.DamagePerSecondOutside, deltaTime);
            sinceDamageTick += deltaTime;
            if (sinceDamageTick < DamageTickSeconds) return;

            sinceDamageTick = 0f;
            if (owedDamage <= 0f) return;
            float amount = owedDamage;
            owedDamage = 0f;
            // No attacker (actor -1, team -1, no weapon): the funnel treats it as neither self nor teammate damage, and the credit and kill-feed code skips
            // a source with no actor, as it does for a burn with no owner. Armour absorbs first, like for any hit; Invulnerability does not stop it.
            health.ApplyDamage(new DamageInfo(amount, -1, -1, -1, DamageSource.SuddenDeath, false, at));
        }

        // ---------------------------------------------------------------- the red in the world

        private void Draw()
        {
            if (!IsSuddenDeath)
            {
                SetDrawn(false);
                return;
            }
            if (!EnsureViews())
            {
                SetDrawn(false);
                return;
            }
            SetDrawn(true);

            var origin = new Vector3(Centre.x, FloorY, Centre.y);
            if (Mathf.Abs(CurrentRadius - drawnRadius) > 0.001f || ringLine.transform.position != origin + Vector3.up * RingLiftMetres)
            {
                drawnRadius = CurrentRadius;
                ringLine.transform.position = origin + Vector3.up * RingLiftMetres;
                Overpower.Abilities.VisualTint.FillFlatCircle(ringLine, Mathf.Max(0.01f, CurrentRadius), RingSegments);
                tintFilter.transform.position = origin + Vector3.up * TintLiftMetres;
                ShapeTint(Mathf.Max(0.01f, CurrentRadius));
            }
        }

        private void SetDrawn(bool shown)
        {
            if (ringLine != null && ringLine.enabled != shown) ringLine.enabled = shown;
            if (tintRenderer != null && tintRenderer.enabled != shown) tintRenderer.enabled = shown;
            if (!shown) drawnRadius = -1f;
        }

        private bool EnsureViews()
        {
            if (ringLine != null && tintRenderer != null)
            {
                ApplyColours();
                return true;
            }
            if (rooms == null) rooms = FindFirstObjectByType<RoomManager>();
            UiTheme theme = rooms != null ? rooms.Theme : null;
            if (theme == null)
            {
                if (!setupErrorLogged)
                {
                    setupErrorLogged = true;
                    Debug.LogError("[DOMINION] the sudden-death circle needs the UiTheme on the RoomManager - none drawn.");
                }
                return false;
            }

            // The ring: a LineRenderer lying flat, like the centre scan's wave (Sprites/Default is transparent and unlit, drawn after the fog pass).
            var ringGo = new GameObject("Sudden Death Ring (cosmetic only)");
            ringGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            ringLine = ringGo.AddComponent<LineRenderer>();
            ringMaterial = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
            ringLine.sharedMaterial = ringMaterial;
            ringLine.alignment = LineAlignment.TransformZ;
            ringLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringLine.receiveShadows = false;
            ringLine.numCapVertices = 0;
            ringLine.enabled = false;

            // The tint: a flat annulus (no collider) from the circle's edge out past the map, a transparent unlit sheet that depth-tests against the
            // opaque world, so players, walls and towers stay in front of it.
            var tintGo = new GameObject("Sudden Death Outside (cosmetic only)");
            tintFilter = tintGo.AddComponent<MeshFilter>();
            tintRenderer = tintGo.AddComponent<MeshRenderer>();
            tintRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tintRenderer.receiveShadows = false;
            tintMaterial = NewTintMaterial();
            tintRenderer.sharedMaterial = tintMaterial;
            tintMesh = BuildTintMesh();
            tintFilter.sharedMesh = tintMesh;
            tintRenderer.enabled = false;

            ApplyColours();
            return true;
        }

        private void ApplyColours()
        {
            UiTheme theme = rooms != null ? rooms.Theme : null;
            if (theme == null) return;
            Overpower.Abilities.VisualTint.SetLineColor(ringLine, theme.suddenDeathColor);
            ringLine.widthMultiplier = Mathf.Max(0.01f, theme.suddenDeathRingWidthMetres);
            Color tint = theme.suddenDeathColor;
            tint.a = theme.suddenDeathOutsideAlpha;
            tintMaterial.color = tint; // a changed Sudden Death Outside Alpha shows live
        }

        private static Material NewTintMaterial()
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_Surface", 1f); // transparent, so the arena shows through
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f); // seen from either side, whichever way the sheet's triangles wind
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            return material;
        }

        private Mesh BuildTintMesh()
        {
            tintVertices = new Vector3[RingSegments * 2];
            var triangles = new int[RingSegments * 6];
            for (int i = 0; i < RingSegments; i++)
            {
                int inner = i * 2, outer = inner + 1;
                int nextInner = ((i + 1) % RingSegments) * 2, nextOuter = nextInner + 1;
                int t = i * 6;
                triangles[t] = inner; triangles[t + 1] = outer; triangles[t + 2] = nextInner;
                triangles[t + 3] = nextInner; triangles[t + 4] = outer; triangles[t + 5] = nextOuter;
            }
            var mesh = new Mesh { name = "Sudden Death Outside", hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            ShapeVertices(1f);
            mesh.vertices = tintVertices;
            mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(TintReachMetres * 3f, 2f, TintReachMetres * 3f));
            return mesh;
        }

        private void ShapeTint(float radius)
        {
            ShapeVertices(radius);
            tintMesh.vertices = tintVertices;
            tintMesh.bounds = new Bounds(Vector3.zero, new Vector3((radius + TintReachMetres) * 2f, 2f, (radius + TintReachMetres) * 2f));
        }

        private void ShapeVertices(float radius)
        {
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * 2f * Mathf.PI / RingSegments;
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                tintVertices[i * 2] = direction * radius;
                tintVertices[i * 2 + 1] = direction * (radius + TintReachMetres);
            }
        }
    }
}
