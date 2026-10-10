using System.Collections.Generic;
using Overpower.Arena;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.Match;
using Overpower.Net;
using Overpower.Vision;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The minimap (GDD p.27): a triangular corner map, one vertex toward each capital, and a large one while M is on.
    /// OWNER ONLY, built in code like PlayerHud, from state every client has (BuildingManager, ZonePresenceTracker,
    /// replicated positions). Enemies are red dots only for what my team sees now (TeamSight.CanSeePlayer, no last-seen
    /// marks, D10), so an ally's sighting says someone is there, not who. NEVER BLOCKS A SHOT: no GraphicRaycaster and
    /// no raycast-target Graphic. Turns with CameraTracking's team yaw; labels and markers are turned back upright.
    /// Each link is two half Images split at the middle of the GAP between the bubbles' edges (the centres' midpoint
    /// starved the capital's half); only Border is two-toned, so ApplyLinkStyle stays one path. Colours change only on
    /// ownership change; markers sit on a nested Canvas so moving them re-batches only that; nothing allocates per frame.
    /// Out-of-play zones are hidden, not grey, so UiTheme.outOfPlayZoneColor on the Fill is never seen. SetZoneShown is unused.
    /// </summary>
    public sealed class MinimapView : MonoBehaviourPun
    {
        [SerializeField, Tooltip("Sizes, colours and widths for the minimap (UiTheme > Minimap). The team colours, " +
                 "warning colour and paused blink come from the Shots and Capture ring sections, shared with the rings " +
                 "on the ground.")]
        private UiTheme theme;

        [SerializeField, Tooltip("The baked arena picture and the world square it covers. Rebuild thirds re-bakes it, " +
                 "or use OverPower > Arena > Bake minimap image.")]
        private MinimapConfig config;

        /// <summary>The local player's minimap, or null before it spawns.</summary>
        public static MinimapView Local { get; private set; }

        public bool IsBuilt => built;
        public bool IsLargeOpen => largeOpen;
        /// <summary>The map's root, for harness checks (its screen rectangle).</summary>
        public RectTransform MapRoot => root;
        public int ZoneBubbleCount => zones.Count;
        public int LinkCount => links.Count;
        /// <summary>Whether the zone's bubble is on the map right now, for harness checks.</summary>
        public bool IsZoneDrawn(int zone) => zoneById.TryGetValue(zone, out ZoneUi ui) && ui.Upright.gameObject.activeSelf;
        /// <summary>How many links are drawn right now, for harness checks.</summary>
        public int DrawnLinkCount
        {
            get
            {
                int drawn = 0;
                foreach (LinkUi link in links)
                    if (link.LineA.gameObject.activeSelf) drawn++;
                return drawn;
            }
        }
        /// <summary>The opacity the map is actually drawn at this frame, for harness checks.</summary>
        public float CurrentOpacity => fade != null ? fade.alpha : 1f;
        /// <summary>The smoothed planar speed the moving/stopped test is made against, in metres per second.</summary>
        public float MeasuredSpeed => smoothedSpeed;
        /// <summary>Whether the map currently counts the player as moving.</summary>
        public bool IsMoving => moving;
        /// <summary>The theme this map is laid out from. Read-only, for one caller: the F1 debug log overlay must clear the
        /// corner map's reserved band, and installs itself at runtime with nothing serialized, so this is its only handle.</summary>
        public UiTheme Theme => theme;

        private sealed class ZoneUi
        {
            public int Zone;
            public int Tier;
            public float Diameter;
            public Vector2 MapPosition;
            public RectTransform Upright;
            public Image Ring;
            public Image Outline;
            public Image Fill;
            public TextMeshProUGUI Label;
            public bool Shown = true;
            /// <summary>A Dominion capital: not drawn, and no link to it is.</summary>
            public bool IsSpawn;
            /// <summary>MatchDirector.IsOutOfPlay(Zone), refreshed by RecolourOwnership; read by ApplyLinkStyle (no link
            /// touches it), UpdateZones (CaptureRingState.From) and RecolourOwnership (its bubble hides like its tower).</summary>
            public bool OutOfPlay;
            public float ShownRingFill = -1f;
            public Color ShownRingColor;
            public Color ShownOutlineColor;
            /// <summary>Health pack badge: built the first time this zone has a pack in play, then only recoloured
            /// (ShownPackReady: -1 not decided yet, 0 grey, 1 green) and shown or hidden.</summary>
            public RectTransform PackBadge;
            public Image PackBadgeBarA;
            public Image PackBadgeBarB;
            public int ShownPackReady = -1;
        }

        private sealed class LinkUi
        {
            public int A;
            public int B;
            /// <summary>The half nearest A, from A's bubble to the link's midpoint.</summary>
            public Image LineA;
            /// <summary>The half nearest B, from the midpoint to B's bubble.</summary>
            public Image LineB;
            public Image Arrow;
        }

        private readonly List<ZoneUi> zones = new List<ZoneUi>();
        private readonly Dictionary<int, ZoneUi> zoneById = new Dictionary<int, ZoneUi>();
        private readonly List<LinkUi> links = new List<LinkUi>();
        private readonly List<RectTransform> teammateDots = new List<RectTransform>();
        private readonly List<RectTransform> enemyDots = new List<RectTransform>();
        private readonly HashSet<int> hiddenZones = new HashSet<int>();

        private PlayerInputRouter inputRouter;
        private LoadoutScreen loadoutScreen;
        private PlayerLifecycle lifecycle;
        private BuildingManager manager;

        private RectTransform root;
        private RectTransform canvasRect;
        private RectTransform viewport;
        /// <summary>The picture's size in canvas units: a square for the triangle arena, the map's own shape in a rectangular frame (MinimapLayout.FrameSize).</summary>
        private Vector2 frameSize;
        private RectTransform edgeShape;
        private RectTransform map;
        private RectTransform linksLayer;
        private RectTransform zonesLayer;
        private RectTransform packsLayer;
        private RectTransform markersLayer;
        private RectTransform teammatesLayer;
        private RectTransform scanLayer;
        private RectTransform scanRing;
        private RectTransform scanDotsLayer;
        private readonly List<Image> scanRingSegments = new List<Image>();
        private readonly List<RectTransform> scanDotMarkers = new List<RectTransform>();
        private readonly List<CanvasGroup> scanDotGroups = new List<CanvasGroup>();
        private RectTransform ownMarker;
        private Material textMaterial;

        // Phase two cut: the closed corner darkened and the wall line drawn, one overlay texture over the baked picture (PaintCutOverlay).
        private RawImage cutOverlay;
        private SuddenDeathMinimapGraphic suddenDeath;
        private Texture2D cutTexture;
        private PhaseTwoCutGeometry paintedCut;
        private const int CutOverlayPixels = 256; // the overlay's sharpness, not a gameplay value

        // The fog layer: the sight picture drawn as darkness over the baked arena picture, same rect (D10).
        private RawImage fogLayer;
        private Material fogMaterial;
        private readonly Dictionary<RectTransform, Image> dotFills = new Dictionary<RectTransform, Image>(); // each dot's fill, to recolour live

        private bool built;
        private bool largeOpen;
        private CanvasGroup fade;
        private float shownAlpha = -1f;   // < 0 = "not set yet", so the first frame snaps instead of fading in.
        private float smoothedSpeed;
        private bool moving;
        private Vector3 lastSamplePosition;
        private bool hasSamplePosition;
        private bool ownershipDirty = true;
        private float appliedYaw = float.NaN;
        // The triangular mask/edge's constant offset from the map's yaw rotation, computed once in TryBuild from the real
        // capital positions (ComputeTriangleBaseRotation). 0 for the shipped arena, but never hard-coded as 0.
        private float triangleBaseRotationDegrees;
        // TryBuild waits until every tower has registered; a tower that never does would leave the map silently blank, hence the warning.
        private float buildWaitStartTime = -1f;
        private bool warnedSlowBuild;

        private void Awake()
        {
            // Every remote copy stays dormant, like PlayerHud: nobody needs another player's minimap.
            if (!photonView.IsMine)
            {
                enabled = false;
                return;
            }
            config = SceneMinimapConfig.Resolve(config); // a map with its own picture (the Dominion lane) replaces the prefab's
            if (theme == null || config == null)
            {
                Debug.LogError($"[Minimap] {name}: UiTheme or MinimapConfig is not assigned - the minimap is not built.");
                enabled = false;
                return;
            }
            if (config.ArenaImage == null)
                Debug.LogError("[Minimap] MinimapConfig has no arena image - press OverPower > Arena > Bake minimap image. " +
                               "The minimap draws zones on a plain background meanwhile.");

            frameSize = MinimapLayout.FrameSize(config.RectangularFrame, config.WorldSizeMetres, config.WorldDepthMetres, theme.minimapCornerSize);

            inputRouter = GetComponent<PlayerInputRouter>();
            loadoutScreen = GetComponent<LoadoutScreen>();
            lifecycle = GetComponent<PlayerLifecycle>();
            Local = this;
            if (inputRouter != null)
                inputRouter.MapToggled += ToggleLarge;
        }

        private void OnDestroy()
        {
            if (inputRouter != null)
                inputRouter.MapToggled -= ToggleLarge;
            if (manager != null)
                manager.OwnershipChanged -= HandleOwnershipChanged;
            if (MatchDirector.Instance != null)
                MatchDirector.Instance.LiveStateChanged -= HandleLiveStateChanged;
            if (Local == this)
                Local = null;
            if (textMaterial != null)
                Destroy(textMaterial);
            if (cutTexture != null)
                Destroy(cutTexture);
            if (fogMaterial != null)
                Destroy(fogMaterial);
        }

        /// <summary>M: open the large map (closing the loadout screen), or close it.</summary>
        public void ToggleLarge()
        {
            if (!built)
                return;
            if (largeOpen)
            {
                SetLarge(false);
                return;
            }
            if (loadoutScreen != null && LoadoutScreen.IsOpen)
                loadoutScreen.Close();
            SetLarge(true);
        }

        /// <summary>Hides (or shows again) a zone's bubble and every link to it. Remembered if called before the map is built.</summary>
        public void SetZoneShown(int zone, bool shown)
        {
            if (shown) hiddenZones.Remove(zone);
            else hiddenZones.Add(zone);

            if (!built || !zoneById.TryGetValue(zone, out ZoneUi ui) || ui.Shown == shown)
                return;
            ui.Shown = shown;
            ui.Upright.gameObject.SetActive(shown);
            ownershipDirty = true; // re-applies which links show, next LateUpdate
        }

        private void LateUpdate()
        {
            if (!built && !TryBuild())
                return;

            // Repainted only when the cut changes, not every frame: reference equality is enough, since ArenaPhaseTwoCut
            // only builds a new geometry on an actual change (ApplyCutChange).
            PhaseTwoCutGeometry cut = ArenaPhaseTwoCut.Active != null ? ArenaPhaseTwoCut.Active.Geometry : null;
            if (cut != paintedCut)
                PaintCutOverlay(cut);

            // The P screen opened (by P or by its button): the large map closes.
            if (largeOpen && LoadoutScreen.IsOpen)
                SetLarge(false);

            ApplyYawIfChanged();

            // With the zone switch off the bubbles show what the team knows, so a change in it repaints them.
            ZoneKnowledge knowledge = ZoneKnowledge.Instance;
            if (knowledge != null && knowledge.Version != shownKnowledgeVersion)
            {
                shownKnowledgeVersion = knowledge.Version;
                ownershipDirty = true;
            }

            if (ownershipDirty && manager.Current != null)
            {
                RecolourOwnership();
                ownershipDirty = false;
            }

            UpdateZones();
            UpdateFog();
            UpdatePlayers();
            UpdateScan();
            UpdateSuddenDeath();
            UpdateOpacity();
        }

        /// <summary>Dominion sudden death: the circle's edge and the red outside it, from the same circle the world draws (SuddenDeathZone), so the two
        /// always agree. The large map is this one map scaled, so one pass draws both.</summary>
        private void UpdateSuddenDeath()
        {
            SuddenDeathZone zone = SuddenDeathZone.Instance;
            bool show = zone != null && zone.IsSuddenDeath && config.WorldSizeMetres > 0f;
            if (suddenDeath.gameObject.activeSelf != show)
                suddenDeath.gameObject.SetActive(show);
            if (!show)
                return;

            Vector2 centre = MinimapLayout.WorldToMap(new Vector3(zone.Centre.x, 0f, zone.Centre.y), config.WorldCentre, config.WorldSizeMetres, frameSize.x);
            float radius = zone.CurrentRadius * frameSize.x / config.WorldSizeMetres;
            Color edge = theme.suddenDeathColor;
            Color outside = edge;
            outside.a = theme.suddenDeathMinimapOutsideAlpha;
            suddenDeath.Set(centre, radius, theme.suddenDeathMinimapRingWidth, theme.minimapCornerSize * 3f, edge, outside);
        }

        /// <summary>The corner map is see-through, M makes it solid, moving with M open dims it again. The rule (with the
        /// deadzone that stops strobing) is MinimapOpacity; this measures speed and hands the answer to a CanvasGroup.
        /// Speed is MEASURED from this player's position, not PlayerMotor.CurrentSpeed: that is the CONFIGURED speed,
        /// which still reads full while a stunned player stands still. A position delta is true for dash, knockback and stun.</summary>
        private void UpdateOpacity()
        {
            if (fade == null)
                return;

            float deltaTime = Time.unscaledDeltaTime;
            Vector3 position = transform.position;
            if (hasSamplePosition && deltaTime > 0f)
            {
                Vector3 step = position - lastSamplePosition;
                step.y = 0f; // Planar: falling off the arena is not "moving with the map open".
                smoothedSpeed = MinimapOpacity.SmoothSpeed(smoothedSpeed, step.magnitude / deltaTime,
                                                           deltaTime, theme.minimapSpeedSmoothingSeconds);
            }
            lastSamplePosition = position;
            hasSamplePosition = true;

            moving = MinimapOpacity.IsMoving(moving, smoothedSpeed,
                                             theme.minimapMovingEnterSpeed, theme.minimapMovingExitSpeed);
            float target = MinimapOpacity.TargetAlpha(largeOpen, moving, theme.minimapCornerOpacity,
                                                      theme.minimapLargeOpacity,
                                                      theme.minimapLargeMovingOpacityDrop);
            // The first frame snaps: a map fading up from nothing at every spawn would read as a bug.
            float next = shownAlpha < 0f
                ? target
                : MinimapOpacity.Step(shownAlpha, target, deltaTime, theme.minimapOpacityFadeSeconds);
            if (!Mathf.Approximately(next, shownAlpha))
            {
                fade.alpha = next;
                shownAlpha = next;
            }
        }

        private void HandleOwnershipChanged(int zone, int oldOwner, int newOwner, TerritorySnapshot snapshot) =>
            ownershipDirty = true;

        /// <summary>The countdown starting/cancelling or the match going live (see TryBuild on why this is needed on top of OwnershipChanged).</summary>
        private void HandleLiveStateChanged() => ownershipDirty = true;

        // ---------------------------------------------------------------- build (once)

        private bool TryBuild()
        {
            if (buildWaitStartTime < 0f)
                buildWaitStartTime = Time.unscaledTime;

            manager = BuildingManager.Instance;
            if (manager == null || manager.Map == null || manager.TowerDictionary == null || manager.TowerDictionary.Count == 0)
            {
                WarnIfSlowToBuild();
                return false;
            }
            // Towers register in their own Start; wait until every zone has, so no bubble is missing.
            foreach (int zone in manager.TowerDictionary.Keys)
                if (manager.TierOf(zone) <= 0 || !manager.TryGetZoneCentre(zone, out _))
                {
                    WarnIfSlowToBuild();
                    return false;
                }

            BuildFrame();
            var zoneIds = new List<int>(manager.TowerDictionary.Keys);
            zoneIds.Sort();

            // Keeps the triangle's vertex aligned with its capital at every yaw. map still turns by exactly the camera
            // yaw: this constant offset only cancels viewport's constant part, set once because it never changes.
            triangleBaseRotationDegrees = config.RectangularFrame ? 0f : ComputeTriangleBaseRotationDegrees(zoneIds); // a rectangle has no vertex to point at a capital
            map.localEulerAngles = new Vector3(0f, 0f, -triangleBaseRotationDegrees);

            foreach (int zone in zoneIds)
                BuildZone(zone);
            foreach ((int a, int b) in MinimapLayout.LinkPairs(manager.Map, zoneIds))
                BuildLink(a, b);

            ownMarker = BuildMarker("You", markersLayer, GeneratedSprites.Triangle, theme.minimapOwnMarkerColor, theme.minimapOwnMarkerSize);

            manager.OwnershipChanged += HandleOwnershipChanged;
            // MatchDirector.IsOutOfPlay changes on the live edge with no OwnershipChanged event (a host start's third
            // capital is neutral before AND after going live), so re-colour on that edge too or it never leaves the in-play look.
            if (MatchDirector.Instance != null)
                MatchDirector.Instance.LiveStateChanged += HandleLiveStateChanged;
            built = true;
            SetLarge(false);
            return true;
        }

        /// <summary>Logs once, after ~5s of the map still not building: a tower that never registered (RegisterCapture
        /// never ran) would otherwise leave the minimap silently blank.</summary>
        private void WarnIfSlowToBuild()
        {
            if (warnedSlowBuild || Time.unscaledTime - buildWaitStartTime < 5f)
                return;
            warnedSlowBuild = true;

            if (manager == null || manager.Map == null || manager.TowerDictionary == null || manager.TowerDictionary.Count == 0)
            {
                Debug.LogWarning("[Minimap] Still waiting to build after 5s - BuildingManager, its territory map or " +
                                  "TowerDictionary is not ready yet.");
                return;
            }
            var missing = new List<int>();
            foreach (int zone in manager.TowerDictionary.Keys)
                if (manager.TierOf(zone) <= 0 || !manager.TryGetZoneCentre(zone, out _))
                    missing.Add(zone);
            Debug.LogWarning($"[Minimap] Still waiting to build after 5s - zone(s) not yet registered: " +
                              $"{string.Join(",", missing)}. Check that each tower's BuildingCapture has run its Start.");
        }

        /// <summary>The UI rotation (degrees) that turns the apex-up MaskTriangle/EdgeTriangle so its "up" vertex points at
        /// a real Tier 1 capital, read from BuildingManager's zone centres. The first Tier 1 zone by id will do:
        /// ArenaSymmetry's 3-fold layout puts the other two capitals ~120 degrees from it.</summary>
        private float ComputeTriangleBaseRotationDegrees(List<int> zoneIds)
        {
            foreach (int zone in zoneIds)
            {
                if (manager.TierOf(zone) != 1 || !manager.TryGetZoneCentre(zone, out Vector3 centre))
                    continue;
                Vector2 direction = new Vector2(centre.x - config.WorldCentre.x, centre.z - config.WorldCentre.y);
                if (direction.sqrMagnitude <= 0.0001f)
                    continue;
                // The generated apex sits at map-space (0,1), 90 degrees; turn it by (angle - 90) onto this capital.
                float directionDegrees = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                return directionDegrees - 90f;
            }
            // Reachable, not dead: TryBuild only requires tier > 0, which does NOT guarantee a capital (tier 1) exists.
            // 0 leaves the apex at map-space "up", the same default CapitalDirections falls back to.
            return 0f;
        }

        private void BuildFrame()
        {
            var canvasGo = new GameObject("Minimap Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the HUD (-10), below the loadout screen (-5), MatchUI's panels (0) and the F1 test panel (500).
            canvas.overrideSorting = true;
            canvas.sortingOrder = -9;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            // No GraphicRaycaster, on purpose: it would make every shot fired with the cursor over the map silently fail.

            canvasRect = (RectTransform)canvasGo.transform;

            root = NewRect("Minimap", canvasGo.transform);
            // The root is the picture's own size, so a rectangular lane map does not hang below its frame (and the large map scales the same rectangle).
            root.sizeDelta = frameSize;

            // One CanvasGroup multiplies every Graphic underneath (including the markers' nested Canvas), so all fade
            // together and nothing remembers its own alpha. Interactable and blocksRaycasts off keep "NEVER BLOCKS A SHOT".
            fade = root.gameObject.AddComponent<CanvasGroup>();
            fade.interactable = false;
            fade.blocksRaycasts = false;

            // No round backdrop: nothing may show outside the triangle, only its window and the EdgeTriangle frame below.
            // The frame band is minimapFrameWidth at the corner size (GeneratedSprites.BuildTriangleEdge explains the fraction).
            float frameBandFraction = theme.minimapFrameWidth / Mathf.Max(1f, theme.minimapCornerSize);
            // MaskTriangle is 512 px: a UGUI Mask reads its sprite's alpha as a 1-bit stencil test, and a finer source
            // traces a smoother contour first. Shrunk inward by half the frame band so the stencil cut sits under solid
            // frame colour. viewport carries the yaw+base rotation (ApplyYawIfChanged); map's own rotation only cancels
            // viewport's constant part (set in TryBuild), so the contents still turn by exactly the camera yaw.
            Image viewportImage = config.RectangularFrame
                ? NewRectangleImage("Viewport", root, Color.white, frameSize)
                : NewImage("Viewport", root, GeneratedSprites.BuildTriangleMask(frameBandFraction), Color.white, theme.minimapCornerSize);
            viewport = viewportImage.rectTransform;
            // Triangular mask: the turned square picture never shows a corner, and the shape frames the arena.
            viewportImage.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            map = NewRect("Map", viewport);
            map.sizeDelta = frameSize;

            var pictureGo = new GameObject("Arena Picture", typeof(RectTransform));
            pictureGo.transform.SetParent(map, false);
            RawImage picture = pictureGo.AddComponent<RawImage>();
            picture.texture = config.ArenaImage;
            picture.color = config.ArenaImage != null ? theme.minimapBackgroundTint : theme.minimapFrameColor;
            picture.raycastTarget = false;
            Stretch(picture.rectTransform);

            // Vision fog: covers the same world square as the baked picture, same rect; UpdateFog shows it and sets its
            // colour. Under the cut overlay, links, bubbles and markers, so the knocked-out corner's wall line is not dimmed.
            var fogGo = new GameObject("Vision Fog", typeof(RectTransform));
            fogGo.transform.SetParent(map, false);
            fogLayer = fogGo.AddComponent<RawImage>();
            fogLayer.raycastTarget = false;
            fogLayer.enabled = false;
            Stretch(fogLayer.rectTransform);

            // Phase two cut: under links and bubbles, on the baked picture; PaintCutOverlay fills it only once a cut stands.
            var cutGo = new GameObject("Phase Two Cut", typeof(RectTransform));
            cutGo.transform.SetParent(map, false);
            cutOverlay = cutGo.AddComponent<RawImage>();
            cutOverlay.raycastTarget = false;
            cutOverlay.enabled = false;
            Stretch(cutOverlay.rectTransform);

            // Sudden death: the red outside the circle, over the picture and under links, bubbles and markers so they stay crisp.
            var suddenDeathGo = new GameObject("Sudden Death", typeof(RectTransform));
            suddenDeathGo.transform.SetParent(map, false);
            suddenDeath = suddenDeathGo.AddComponent<SuddenDeathMinimapGraphic>();
            suddenDeath.raycastTarget = false; // never swallow a shot
            Stretch(suddenDeath.rectTransform);
            suddenDeathGo.SetActive(false);

            // Sibling order is draw order: lines under bubbles, bubbles under player markers.
            linksLayer = NewLayer("Links", map);
            zonesLayer = NewLayer("Zones", map);
            packsLayer = NewLayer("Health Packs", map);
            markersLayer = NewLayer("Markers", map);
            // A nested Canvas: markers move every frame, and without it UGUI rebuilds the WHOLE minimap's batched mesh
            // (links, bubbles, labels) just to redraw two dots. Its own batch means an idle map never rebuilds.
            markersLayer.gameObject.AddComponent<Canvas>();
            // The centre scan: under the player markers, so a dot never hides a teammate.
            scanLayer = NewLayer("Centre Scan", markersLayer);
            scanRing = NewRect("Wave", scanLayer);
            scanDotsLayer = NewLayer("Scan Dots", scanLayer);
            teammatesLayer = NewLayer("Teammates", markersLayer);

            // Drawn LAST, a sibling of Viewport so it is not masked: a thin anti-aliased triangular ring covering the
            // mask's stencil seam. Rotated like viewport (ApplyYawIfChanged) to stay aligned with the triangle.
            edgeShape = config.RectangularFrame
                ? BuildRectangleEdge(root, frameSize, theme.minimapFrameWidth, theme.minimapFrameColor)
                : NewImage("Edge Triangle", root, GeneratedSprites.BuildTriangleEdge(frameBandFraction), theme.minimapFrameColor, theme.minimapCornerSize).rectTransform;
        }

        private void BuildZone(int zone)
        {
            manager.TryGetZoneCentre(zone, out Vector3 centre);
            var ui = new ZoneUi { Zone = zone };
            ui.MapPosition = MinimapLayout.WorldToMap(centre, config.WorldCentre, config.WorldSizeMetres, frameSize.x);

            ui.Upright = NewRect($"Zone {zone}", zonesLayer);
            ui.Upright.anchoredPosition = ui.MapPosition;

            // The progress ring is a Filled disc behind the outline disc, so exactly Progress Ring Width shows around it
            // at every bubble size. Sprite first: a Filled Image without a sprite ignores its fill amount. Sized by
            // ApplyTier below, once this tier's Diameter is known.
            ui.Ring = NewImage("Progress Ring", ui.Upright, GeneratedSprites.Disc, Color.white, 0f);
            ui.Ring.type = Image.Type.Filled;
            ui.Ring.fillMethod = Image.FillMethod.Radial360;
            ui.Ring.fillOrigin = (int)Image.Origin360.Top; // the top of the screen, like the band on the ground
            ui.Ring.fillClockwise = true;
            ui.Ring.fillAmount = 0f;
            ui.Ring.enabled = false;

            ui.Outline = NewImage("Outline", ui.Upright, GeneratedSprites.Disc, theme.minimapBubbleOutlineColor, 0f);
            ui.ShownOutlineColor = theme.minimapBubbleOutlineColor;
            ui.Fill = NewImage("Fill", ui.Upright, GeneratedSprites.Disc, theme.minimapNeutralColor, 0f);
            ui.Label = AddLabel(ui.Upright, string.Empty, theme.minimapLabelSize);

            ApplyTier(ui, manager.TierOf(zone));

            ui.Shown = !hiddenZones.Contains(zone);
            ui.Upright.gameObject.SetActive(ui.Shown);
            zones.Add(ui);
            zoneById[zone] = ui;
        }

        /// <summary>Sizes a zone's bubble and label for a tier. Called at build and again whenever
        /// BuildingManager.TierOf(zone) changes (RecolourOwnership): the centre plays as a Tier III while a corner is cut,
        /// so its bubble shrinks and relabels "III", then grows back to "IV" when the cut clears.</summary>
        private void ApplyTier(ZoneUi ui, int tier)
        {
            ui.Tier = tier;
            ui.Diameter = theme.MinimapBubbleDiameter(tier);
            ui.Upright.sizeDelta = Vector2.one * ui.Diameter;
            ui.Ring.rectTransform.sizeDelta =
                Vector2.one * (ui.Diameter + 2f * (theme.minimapBubbleOutlineWidth + theme.minimapProgressRingWidth));
            ui.Outline.rectTransform.sizeDelta = Vector2.one * (ui.Diameter + 2f * theme.minimapBubbleOutlineWidth);
            ui.Fill.rectTransform.sizeDelta = Vector2.one * ui.Diameter;
            ui.Label.text = MinimapLayout.TierLabel(tier);

            // BuildLink places the seam once from each end's ZoneRadius, so a resized zone (only the centre, IV<->III)
            // leaves its links' seams off-centre until re-placed. Width and colour stay as ApplyLinkStyle set them.
            foreach (LinkUi link in links)
                if (link.A == ui.Zone || link.B == ui.Zone)
                    RePlaceLinkSegments(link);
        }

        /// <summary>BuildLink's gap split redone for a bubble resized after build (ApplyTier); keeps each half's current
        /// width instead of resetting it, so it never fights ApplyLinkStyle.</summary>
        private void RePlaceLinkSegments(LinkUi link)
        {
            ZoneUi a = zoneById[link.A];
            ZoneUi b = zoneById[link.B];
            Vector2 posA = a.MapPosition;
            Vector2 posB = b.MapPosition;
            Vector2 delta = posB - posA;
            float length = delta.magnitude;
            Vector2 direction = length > 0.0001f ? delta / length : Vector2.right;
            float gap = Mathf.Max(0f, length - ZoneRadius(a) - ZoneRadius(b));
            Vector2 mid = posA + direction * (ZoneRadius(a) + gap / 2f);
            PlaceHalfSegment(link.LineA, posA, mid, link.LineA.rectTransform.sizeDelta.y);
            PlaceHalfSegment(link.LineB, mid, posB, link.LineB.rectTransform.sizeDelta.y);
        }

        /// <summary>Two half-line Images, split in the middle of the VISIBLE gap between the bubbles' edges, not at the
        /// centres' midpoint (that left the capital's half almost no visible line once its progress ring showed). Owned,
        /// WayIn and Neutral colour both halves alike in ApplyLinkStyle; only Border is two-toned. Geometry is fixed
        /// here; only colour and width change later.</summary>
        private void BuildLink(int a, int b)
        {
            var link = new LinkUi { A = a, B = b };
            Vector2 posA = zoneById[a].MapPosition;
            Vector2 posB = zoneById[b].MapPosition;
            Vector2 delta = posB - posA;
            float length = delta.magnitude;
            Vector2 direction = length > 0.0001f ? delta / length : Vector2.right;
            // Splitting at posA + direction * (rA + gap/2) puts the seam mid-gap, so each visible half is equal whatever the bubble size.
            float rA = ZoneRadius(zoneById[a]);
            float rB = ZoneRadius(zoneById[b]);
            float gap = Mathf.Max(0f, length - rA - rB);
            Vector2 mid = posA + direction * (rA + gap / 2f);

            link.LineA = NewImage($"Link {a}-{b} A", linksLayer, null, theme.minimapNeutralColor, 0f);
            PlaceHalfSegment(link.LineA, posA, mid, theme.minimapNeutralLinkWidth);
            link.LineB = NewImage($"Link {a}-{b} B", linksLayer, null, theme.minimapNeutralColor, 0f);
            PlaceHalfSegment(link.LineB, mid, posB, theme.minimapNeutralLinkWidth);

            link.Arrow = NewImage($"Arrow {a}-{b}", linksLayer, GeneratedSprites.Triangle, theme.minimapNeutralColor, theme.minimapArrowheadSize);
            link.Arrow.gameObject.SetActive(false);
            links.Add(link);
        }

        /// <summary>A bubble's visible radius, fill plus outline and progress ring: where a link or arrowhead stops. Shared by
        /// BuildLink and ApplyLinkStyle so the two agree on where a bubble "ends".</summary>
        private float ZoneRadius(ZoneUi zone) =>
            zone.Diameter / 2f + theme.minimapBubbleOutlineWidth + theme.minimapProgressRingWidth;

        // ---------------------------------------------------------------- per-frame updates

        private void SetLarge(bool open)
        {
            largeOpen = open;
            Vector2 anchor = open ? new Vector2(0.5f, 0.5f) : Vector2.one;
            root.anchorMin = anchor;
            root.anchorMax = anchor;
            root.pivot = anchor;
            if (open)
            {
                // Fits above the HUD: the band runs from the top margin down to Bottom Clearance, and the map is centred in it, not on screen.
                float canvasHeight = canvasRect != null ? canvasRect.rect.height : theme.minimapLargeSize;
                float largeDiameter = Mathf.Min(theme.minimapLargeSize,
                    canvasHeight - theme.minimapLargeBottomClearance - 2f * theme.minimapCornerMargin);
                root.anchoredPosition = new Vector2(0f, theme.minimapLargeBottomClearance / 2f);
                // One hierarchy for both views: the large map is the corner map scaled up, so every size scales together.
                root.localScale = Vector3.one * (largeDiameter / Mathf.Max(1f, theme.minimapCornerSize));
            }
            else
            {
                float inset = theme.minimapCornerMargin + theme.minimapFrameWidth;
                root.anchoredPosition = new Vector2(-inset, -inset);
                root.localScale = Vector3.one;
            }
        }

        private void ApplyYawIfChanged()
        {
            float yaw = CameraTracking.Instance != null ? CameraTracking.Instance.Yaw : 0f;
            if (Mathf.Approximately(yaw, appliedYaw))
                return;
            appliedYaw = yaw;
            // viewport (mask+edge) carries the triangle's base rotation on top of yaw; map's own rotation (TryBuild)
            // cancels that constant part, so its contents still turn by exactly the camera yaw.
            float triangleRotation = triangleBaseRotationDegrees + MinimapLayout.MapRotationDegrees(yaw);
            viewport.localEulerAngles = new Vector3(0f, 0f, triangleRotation);
            edgeShape.localEulerAngles = new Vector3(0f, 0f, triangleRotation);
            var upright = Quaternion.Euler(0f, 0f, MinimapLayout.UprightRotationDegrees(yaw));
            foreach (ZoneUi zone in zones)
            {
                zone.Upright.localRotation = upright;
                if (zone.PackBadge != null)
                    zone.PackBadge.localRotation = upright;
            }
        }

        private int shownKnowledgeVersion = -1;

        /// <summary>The owner this player's team believes a zone has: the known one while the zone switch is off
        /// (ZoneKnowledge), else the live snapshot's.</summary>
        private static int OwnerShown(TerritorySnapshot snapshot, int zone) =>
            ZoneKnowledge.TryGetDisplayed(zone, out ZoneView known) ? known.OwnerTeam : snapshot.OwnerOf(zone);

        private void RecolourOwnership()
        {
            TerritorySnapshot snapshot = manager.Current;
            MatchDirector director = MatchDirector.Instance;
            foreach (ZoneUi zone in zones)
            {
                // Out of play wins over ownership: a capital nobody plays for (host start) or a zone behind the phase-two
                // wall is neutral underneath but must not read as ordinary grey; its bubble disappears like its tower
                // (links touching it are hidden by ApplyLinkStyle).
                zone.OutOfPlay = director != null && director.IsOutOfPlay(zone.Zone);

                // The centre plays as a Tier III while any corner is cut: its bubble follows BuildingManager's
                // effective tier, the same source TowerLook reads.
                int tier = manager.TierOf(zone.Zone);
                if (tier > 0 && tier != zone.Tier)
                    ApplyTier(zone, tier);
                zone.IsSpawn = manager.IsSpawnZone(zone.Zone);
                zone.Upright.gameObject.SetActive(zone.Shown && !zone.OutOfPlay && !zone.IsSpawn);

                int owner = OwnerShown(snapshot, zone.Zone);
                zone.Fill.color = zone.OutOfPlay ? theme.outOfPlayZoneColor
                    : owner >= 0 ? theme.ShotColorFor(owner) : theme.minimapNeutralColor;
            }
            foreach (LinkUi link in links)
                ApplyLinkStyle(link, MinimapLinkStyle.For(OwnerShown(snapshot, link.A), OwnerShown(snapshot, link.B)));
        }

        /// <summary>The closed part darkened and the wall drawn, painted once per cut into one texture over the baked
        /// picture (the same world square, so it turns with the map).</summary>
        private void PaintCutOverlay(PhaseTwoCutGeometry cut)
        {
            paintedCut = cut;
            if (cut == null)
            {
                cutOverlay.enabled = false;
                return;
            }
            if (cutTexture == null)
                cutTexture = new Texture2D(CutOverlayPixels, CutOverlayPixels, TextureFormat.RGBA32, false)
                {
                    name = "Minimap Phase Two Cut", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                };
            float metresPerCanvasUnit = config.WorldSizeMetres / Mathf.Max(1f, frameSize.x);
            cutTexture.SetPixels32(MinimapCutMask.Paint(CutOverlayPixels, config.WorldCentre, config.WorldSizeMetres, cut,
                theme.minimapCutWallWidth * 0.5f * metresPerCanvasUnit, theme.minimapCutAreaColor, theme.minimapCutWallColor));
            cutTexture.Apply();
            cutOverlay.texture = cutTexture;
            cutOverlay.color = Color.white;
            cutOverlay.enabled = true;
        }

        /// <summary>One code path for every MinimapLinkKind: Team colours the A half, TeamB the B half. Neutral is grey on
        /// both; Owned and WayIn have Team == TeamB (MinimapLinkStyle.For) so read as one line; only Border is two-toned.
        /// Only WayIn has an arrowhead.</summary>
        private void ApplyLinkStyle(LinkUi link, MinimapLinkStyle style)
        {
            ZoneUi a = zoneById[link.A];
            ZoneUi b = zoneById[link.B];
            // A link touching an out-of-play zone is hidden too: a grey line would read as a way in.
            bool shown = a.Shown && b.Shown && !a.OutOfPlay && !b.OutOfPlay && !a.IsSpawn && !b.IsSpawn;
            link.LineA.gameObject.SetActive(shown);
            link.LineB.gameObject.SetActive(shown);
            bool arrow = shown && style.Kind == MinimapLinkKind.WayIn;
            link.Arrow.gameObject.SetActive(arrow);
            if (!shown)
                return;

            bool neutral = style.Kind == MinimapLinkKind.Neutral;
            float width = neutral ? theme.minimapNeutralLinkWidth : theme.minimapOwnedLinkWidth;
            Color colourA = neutral ? theme.minimapNeutralColor : theme.ShotColorFor(style.Team);
            Color colourB = neutral ? theme.minimapNeutralColor : theme.ShotColorFor(style.TeamB);
            SetHalfWidth(link.LineA, width);
            SetHalfWidth(link.LineB, width);
            link.LineA.color = colourA;
            link.LineB.color = colourB;

            if (!arrow)
                return;
            ZoneUi from = style.TowardB ? a : b;
            ZoneUi to = style.TowardB ? b : a;
            // Just outside the target's bubble (ZoneRadius, as BuildLink uses), pointing at it.
            float stop = ZoneRadius(to) + theme.minimapArrowheadSize / 2f;
            RectTransform arrowRect = link.Arrow.rectTransform;
            arrowRect.anchoredPosition = MinimapLayout.PointBeforeEnd(from.MapPosition, to.MapPosition, stop);
            // The triangle sprite points up (+y); a segment's angle is measured from +x.
            arrowRect.localEulerAngles = new Vector3(0f, 0f, MinimapLayout.Segment(from.MapPosition, to.MapPosition).AngleDegrees - 90f);
            link.Arrow.color = colourA; // WayIn: Team == TeamB, so colourA == colourB anyway.
        }

        private void UpdateZones()
        {
            int nowMs = PhotonNetwork.ServerTimestamp;
            float time = Time.unscaledTime;
            float pulse = CaptureRingGeometry.Pulse01(time, theme.captureRingPulseSpeed);
            float blink = CaptureRingGeometry.Blink01(time, theme.captureRingPausedBlinkSpeed);
            ZonePresenceTracker presence = ZonePresenceTracker.Instance;
            TerritorySnapshot snapshot = manager.Current;

            foreach (ZoneUi zone in zones)
            {
                // A hidden out-of-play bubble is already inactive (RecolourOwnership): no ring state to compute for it.
                UpdatePackBadge(zone);
                if (!zone.Shown || zone.OutOfPlay || zone.IsSpawn)
                    continue;

                // The known state while the zone switch is off, else the live one.
                CaptureRingState state;
                if (ZoneKnowledge.TryGetDisplayed(zone.Zone, out ZoneView known))
                    state = known.Ring;
                else
                {
                    int owner = snapshot != null ? snapshot.OwnerOf(zone.Zone) : TerritoryMap.Neutral;
                    bool attacked = presence != null && presence.IsUnderAttack(zone.Zone);
                    state = CaptureRingState.From(manager.CaptureProgressOf(zone.Zone), owner, attacked, nowMs,
                                                  outOfPlay: zone.OutOfPlay);
                }

                Color outline = state.UnderAttack
                    ? Color.Lerp(theme.minimapBubbleOutlineColor, theme.captureRingWarningColor, pulse)
                    : theme.minimapBubbleOutlineColor;
                if (outline != zone.ShownOutlineColor)
                {
                    zone.Outline.color = outline;
                    zone.ShownOutlineColor = outline;
                }

                bool ringShown = state.ShowsArc;
                if (zone.Ring.enabled != ringShown)
                    zone.Ring.enabled = ringShown;
                if (!ringShown)
                    continue;

                if (Mathf.Abs(state.Fill01 - zone.ShownRingFill) > 0.001f)
                {
                    zone.Ring.fillAmount = state.Fill01;
                    zone.ShownRingFill = state.Fill01;
                }
                Color ring = theme.ShotColorFor(state.ArcTeam);
                if (state.Phase == CaptureRingPhase.Paused)
                    ring.a *= theme.captureRingPausedOpacity * blink;
                if (ring != zone.ShownRingColor)
                {
                    zone.Ring.color = ring;
                    zone.ShownRingColor = ring;
                }
            }
        }

        /// <summary>A small cross at the health pack's real spot (same world-to-map transform as the bubbles, so it sits in
        /// the recess toward the map edge). Ready/taken colours are the pack's own (HealthPackConfig). Hidden when the pack
        /// or its zone is out of play or hidden; built once, then recoloured only when the ready state changes.</summary>
        private void UpdatePackBadge(ZoneUi zone)
        {
            HealthPackManager packs = HealthPackManager.Instance;
            bool inPlay = zone.Shown && !zone.OutOfPlay && packs != null && packs.Config != null
                && packs.TryGetPack(zone.Zone, out _, out _);
            if (!inPlay)
            {
                if (zone.PackBadge != null && zone.PackBadge.gameObject.activeSelf)
                    zone.PackBadge.gameObject.SetActive(false);
                zone.ShownPackReady = -1;
                return;
            }
            packs.TryGetPack(zone.Zone, out Vector3 packWorld, out bool ready);
            if (zone.PackBadge == null)
                BuildPackBadge(zone, packWorld);
            if (!zone.PackBadge.gameObject.activeSelf)
                zone.PackBadge.gameObject.SetActive(true);
            int state = ready ? 1 : 0;
            if (state == zone.ShownPackReady)
                return;
            zone.ShownPackReady = state;
            Color colour = ready ? packs.Config.ReadyColour : packs.Config.TakenColour;
            zone.PackBadgeBarA.color = colour;
            zone.PackBadgeBarB.color = colour;
        }

        /// <summary>A plus made of two bars, each with a dark copy behind it so it reads on any bubble colour.</summary>
        private void BuildPackBadge(ZoneUi zone, Vector3 packWorld)
        {
            float size = theme.minimapPackBadgeSize;
            float bar = size * theme.minimapPackBadgeBarFraction;
            float outline = theme.minimapBubbleOutlineWidth * 0.5f;
            RectTransform badge = NewRect($"Health Pack {zone.Zone}", packsLayer);
            badge.sizeDelta = Vector2.one * size;
            badge.anchoredPosition = MinimapLayout.WorldToMap(packWorld, config.WorldCentre, config.WorldSizeMetres, frameSize.x);
            badge.localRotation = zone.Upright.localRotation;
            Color dark = theme.minimapBubbleOutlineColor;
            NewImage("Outline Horizontal", badge, null, dark, 0f).rectTransform.sizeDelta = new Vector2(size + 2f * outline, bar + 2f * outline);
            NewImage("Outline Vertical", badge, null, dark, 0f).rectTransform.sizeDelta = new Vector2(bar + 2f * outline, size + 2f * outline);
            zone.PackBadgeBarA = NewImage("Bar Horizontal", badge, null, Color.white, 0f);
            zone.PackBadgeBarA.rectTransform.sizeDelta = new Vector2(size, bar);
            zone.PackBadgeBarB = NewImage("Bar Vertical", badge, null, Color.white, 0f);
            zone.PackBadgeBarB.rectTransform.sizeDelta = new Vector2(bar, size);
            zone.PackBadge = badge;
        }

        // The fog layer: on while TeamSight draws the fog, with its colour and darkness; off (today's map) otherwise.
        private void UpdateFog()
        {
            TeamSight sight = TeamSight.Local;
            bool on = sight != null && sight.FogActive && sight.MinimapFogShader != null;
            if (on && fogMaterial == null)
            {
                fogMaterial = new Material(sight.MinimapFogShader) { hideFlags = HideFlags.HideAndDontSave };
                fogLayer.material = fogMaterial;
            }
            on &= fogMaterial != null;
            if (fogLayer.enabled != on)
                fogLayer.enabled = on;
            if (!on)
                return;
            if (fogLayer.texture != sight.SightTexture)
                fogLayer.texture = sight.SightTexture;
            Color fog = sight.Config.FogColour;
            fog.a = 1f; // the shader takes darkness and lift from globals; alpha here carries only the canvas group opacity
            if (fogLayer.color != fog)
                fogLayer.color = fog;
        }

        private void UpdatePlayers()
        {
            bool alive = lifecycle == null || lifecycle.IsAlive;
            if (ownMarker.gameObject.activeSelf != alive)
                ownMarker.gameObject.SetActive(alive);
            if (alive)
            {
                ownMarker.anchoredPosition = MinimapLayout.WorldToMap(transform.position, config.WorldCentre, config.WorldSizeMetres, frameSize.x);
                ownMarker.localEulerAngles = new Vector3(0f, 0f, MinimapLayout.FacingRotationDegrees(transform.eulerAngles.y));
            }

            int used = 0;
            int enemiesUsed = 0;
            TeamSight sight = TeamSight.Local;
            bool fogOn = sight != null && sight.FogActive;
            Room room = PhotonNetwork.CurrentRoom;
            if (room != null && Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int myTeam))
            {
                // Spectating: the watched team are the teammates (TeamSight knows it); otherwise my own team.
                int friendlyTeam = fogOn && sight.FriendlyTeamId >= 0 ? sight.FriendlyTeamId : myTeam;
                // Room.Players is a Dictionary: its enumerator is a struct, so this allocates nothing (PhotonNetwork.PlayerList
                // would build a new sorted array every frame).
                foreach (KeyValuePair<int, Player> pair in room.Players)
                {
                    Player player = pair.Value;
                    if (player.IsLocal || !Teams.TryGetPlayingTeam(player, out int team)) // a seat spectator is no dot
                        continue;
                    bool friendly = team == friendlyTeam;
                    // A player whose connection dropped is not on the map (PresenceRules), whatever "alive" they last wrote.
                    bool? aliveFlag = player.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object raw) && raw is bool isAlive ? isAlive : (bool?)null;
                    bool playerAlive = PresenceRules.CountsAsAlive(player.IsInactive, aliveFlag);
                    if (!playerAlive || (!friendly && !fogOn))
                        continue;
                    PhotonView view = PlayerLookup.GetPhotonViewFor(player.ActorNumber);
                    if (view == null)
                        continue;

                    Vector2 mapPosition = MinimapLayout.WorldToMap(view.transform.position, config.WorldCentre, config.WorldSizeMetres, frameSize.x);
                    if (friendly)
                        DotAt(teammateDots, used++, "Teammate", theme.minimapTeammateDotColor).anchoredPosition = mapPosition;
                    else if (MinimapEnemyRule.ShowDot(false, playerAlive, sight.CanSeePlayer(view), fogOn))
                        DotAt(enemyDots, enemiesUsed++, "Enemy", sight.Config.MinimapEnemyColour).anchoredPosition = mapPosition;
                }
            }
            HideDotsFrom(teammateDots, used);
            HideDotsFrom(enemyDots, enemiesUsed);
        }

        // The centre scan, only for the team holding the centre when it started: the wave as a ring round the centre's
        // bubble, and a frozen red dot per enemy the front passed, fading as it ages. The large map is this one map
        // scaled, so one pass draws both.
        private const int ScanRingSegments = 64;

        private void UpdateScan()
        {
            CentreScan scan = CentreScan.Instance;
            TeamSight sight = TeamSight.Local;
            // The layer stays while dots are left, so they finish their time after the centre is lost (the ring stops at once).
            bool show = scan != null && sight != null && sight.Config != null && (scan.SeenByMyTeam || scan.Dots.Dots.Count > 0);
            if (scanLayer.gameObject.activeSelf != show)
                scanLayer.gameObject.SetActive(show);
            if (!show)
                return;

            VisionConfig vision = sight.Config;
            float now = Time.time;

            bool ring = scan.SeenByMyTeam && scan.WaveVisible;
            if (scanRing.gameObject.activeSelf != ring)
                scanRing.gameObject.SetActive(ring);
            if (ring)
            {
                Vector2 centre = MinimapLayout.WorldToMap(scan.CentrePosition, config.WorldCentre, config.WorldSizeMetres, frameSize.x);
                float radius = CentreScanDisplayRules.MinimapRadius(scan.Frame.Radius, config.WorldSizeMetres, frameSize.x);
                while (scanRingSegments.Count < ScanRingSegments)
                    scanRingSegments.Add(NewImage("Segment", scanRing, null, vision.ScanWaveColour, 0f));
                for (int i = 0; i < ScanRingSegments; i++)
                {
                    float a0 = i * 2f * Mathf.PI / ScanRingSegments;
                    float a1 = (i + 1) * 2f * Mathf.PI / ScanRingSegments;
                    Vector2 from = centre + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
                    Vector2 to = centre + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
                    scanRingSegments[i].color = vision.ScanWaveColour;
                    PlaceHalfSegment(scanRingSegments[i], from, to, theme.minimapScanRingWidth);
                }
            }

            var dots = scan.Dots.Dots;
            for (int i = 0; i < dots.Count; i++)
            {
                while (scanDotMarkers.Count <= i)
                {
                    RectTransform marker = BuildMarker("Scan Dot", scanDotsLayer, GeneratedSprites.Disc, vision.MinimapEnemyColour, theme.minimapTeammateDotSize);
                    dotFills[marker] = marker.Find("Fill").GetComponent<Image>();
                    scanDotGroups.Add(marker.gameObject.AddComponent<CanvasGroup>());
                    scanDotMarkers.Add(marker);
                }
                RectTransform dot = scanDotMarkers[i];
                if (!dot.gameObject.activeSelf)
                    dot.gameObject.SetActive(true);
                dot.anchoredPosition = MinimapLayout.WorldToMap(dots[i].Position, config.WorldCentre, config.WorldSizeMetres, frameSize.x);
                Image fill = dotFills[dot];
                if (fill.color != vision.MinimapEnemyColour)
                    fill.color = vision.MinimapEnemyColour;
                scanDotGroups[i].alpha = CentreScanDisplayRules.DotAlpha(now - dots[i].BornTime, vision.ScanDotSeconds, vision.ScanDotFadeSeconds);
            }
            HideDotsFrom(scanDotMarkers, dots.Count);
        }

        private static void HideDotsFrom(List<RectTransform> dots, int first)
        {
            for (int i = first; i < dots.Count; i++)
                if (dots[i].gameObject.activeSelf)
                    dots[i].gameObject.SetActive(false);
        }

        private RectTransform DotAt(List<RectTransform> pool, int index, string dotName, Color colour)
        {
            while (pool.Count <= index)
            {
                RectTransform marker = BuildMarker(dotName, teammatesLayer, GeneratedSprites.Disc, colour, theme.minimapTeammateDotSize);
                dotFills[marker] = marker.Find("Fill").GetComponent<Image>();
                pool.Add(marker);
            }
            RectTransform dot = pool[index];
            if (!dot.gameObject.activeSelf)
                dot.gameObject.SetActive(true);
            Image fill = dotFills[dot];
            if (fill.color != colour)
                fill.color = colour; // a changed Minimap Enemy Colour shows live
            return dot;
        }

        // ---------------------------------------------------------------- UI helpers

        /// <summary>A marker with a dark outline copy behind it, so it reads on any part of the picture.</summary>
        private RectTransform BuildMarker(string name, Transform parent, Sprite sprite, Color colour, float size)
        {
            RectTransform marker = NewRect(name, parent);
            marker.sizeDelta = Vector2.one * size;
            NewImage("Outline", marker, sprite, theme.minimapBubbleOutlineColor, size + 2f * theme.minimapBubbleOutlineWidth);
            NewImage("Fill", marker, sprite, colour, size);
            return marker;
        }

        /// <summary>Places a half-line Image between two map-space points. Only colour and width (SetHalfWidth) change afterwards.</summary>
        private static void PlaceHalfSegment(Image image, Vector2 from, Vector2 to, float width)
        {
            (Vector2 centre, float length, float angle) = MinimapLayout.Segment(from, to);
            RectTransform rect = image.rectTransform;
            rect.anchoredPosition = centre;
            rect.sizeDelta = new Vector2(length, width);
            rect.localEulerAngles = new Vector3(0f, 0f, angle);
        }

        private static void SetHalfWidth(Image image, float width)
        {
            RectTransform rect = image.rectTransform;
            if (!Mathf.Approximately(rect.sizeDelta.y, width))
                rect.sizeDelta = new Vector2(rect.sizeDelta.x, width);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }

        private static RectTransform NewLayer(string name, Transform parent)
        {
            RectTransform layer = NewRect(name, parent);
            Stretch(layer);
            return layer;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>A solid rectangle of the given size (the rectangular frame's mask: a Mask reads its alpha, so it is plain white).</summary>
        private static Image NewRectangleImage(string name, Transform parent, Color colour, Vector2 size)
        {
            RectTransform rect = NewRect(name, parent);
            rect.sizeDelta = size;
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>The rectangular frame's edge: four bars of the frame width lying just inside the rectangle's sides, drawn on top of the mask.</summary>
        private static RectTransform BuildRectangleEdge(Transform parent, Vector2 size, float width, Color colour)
        {
            RectTransform edge = NewRect("Edge Rectangle", parent);
            edge.sizeDelta = size;
            for (int i = 0; i < 4; i++)
            {
                bool horizontal = i < 2;
                RectTransform bar = NewRectangleImage("Bar " + i, edge, colour, horizontal ? new Vector2(size.x, width) : new Vector2(width, size.y)).rectTransform;
                bar.anchorMin = bar.anchorMax = bar.pivot = horizontal ? new Vector2(0.5f, i == 0 ? 1f : 0f) : new Vector2(i == 2 ? 0f : 1f, 0.5f);
                bar.anchoredPosition = Vector2.zero;
            }
            return edge;
        }

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color colour, float size)
        {
            RectTransform rect = NewRect(name, parent);
            rect.sizeDelta = Vector2.one * size;
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.raycastTarget = false; // never swallow a shot
            return image;
        }

        /// <summary>Same recipe as PlayerHud.AddLabel/ApplyOutline: one shared outline material for every label.</summary>
        private TextMeshProUGUI AddLabel(Transform parent, string text, float fontSize)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.name = "Label";
            go.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            if (theme.font != null)
                tmp.font = theme.font; // before touching the material: assigning a font resets it
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = theme.textColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            if (textMaterial == null)
            {
                textMaterial = new Material(tmp.fontSharedMaterial);
                theme.ApplyHudTextStyle(textMaterial);
            }
            tmp.fontSharedMaterial = textMaterial;
            Stretch(tmp.rectTransform);
            return tmp;
        }
    }
}
