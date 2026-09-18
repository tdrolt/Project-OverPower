using System.Collections.Generic;
using Overpower.Data;
using Overpower.Match;
using Overpower.Net;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The minimap (Tudor, 2026-09-16; it looks like GDD p.27): a triangular map in the top-right corner, one vertex
    /// toward each capital, and a large one in the middle of the screen while M is toggled on. What it shows:
    /// - a baked top-down picture of the arena (MinimapConfig);
    /// - a bubble per zone, sized by tier, filled in the owner's colour and labelled I-IV;
    /// - the links between zones (see MinimapLinkStyle): solid when one team owns both ends, an arrowhead when a
    ///   team owns one end and the other is neutral (a way in), or a Border (controller amendment 2, 2026-09-17)
    ///   when the two ends have different owners - drawn as two half-line Images, each in its own end's owner
    ///   colour at the owned-line width, no arrowhead;
    /// - each zone's capture progress as a ring around its bubble, matching the ground ring's arc (same states);
    /// - a pulsing outline on zones under attack;
    /// - your own arrow and your teammates' dots. Enemies aren't shown: there are no vision rules to decide who
    ///   may see whom.
    ///
    /// OWNER ONLY, built in code like PlayerHud (see its class comment for why code-built). Everything comes from state
    /// every client already has: BuildingManager (owners, capture progress, links), ZonePresenceTracker (under attack)
    /// and the replicated player positions.
    ///
    /// NEVER BLOCKS A SHOT: its canvas has no GraphicRaycaster and no Graphic is a raycast target, so
    /// PlayerInputRouter's "pointer over UI" check never sees it.
    ///
    /// TURNS WITH YOUR CAMERA: the map turns by CameraTracking's team yaw, so "up" on the map is "up" on screen. Labels,
    /// progress rings and markers are turned back so they read upright (maths and tests: MinimapLayout).
    ///
    /// EVERY LINK IS TWO HALVES: built once as two Image rectangles, split in the middle of the visible GAP between
    /// the two bubbles' edges (review fix, 2026-09-17; not the midpoint between their centres, which starved the
    /// capital's half of a busy border down to well under a unit once its progress ring showed). Owned and Neutral
    /// colour both halves the same (their two ends share one colour or none), so this reads exactly like one line;
    /// only Border ends up two-toned. This keeps ApplyLinkStyle a single code path for every MinimapLinkKind instead
    /// of a special case for Border.
    ///
    /// COST: bubbles and lines are built once. Bubble and link colours change only when ownership changes
    /// (BuildingManager.OwnershipChanged, plus the first territory read, which raises no event). Each zone's ring and
    /// outline are worked out every frame from CaptureRingState, but written to the UI only when they change, so an
    /// idle zone touches nothing. Player markers move every frame, but on their own nested Canvas (review fix,
    /// 2026-09-17), so moving them re-batches only that Canvas, not the whole minimap. Nothing here allocates per
    /// frame outside a one-time warning if the map is still waiting to build after 5s (WarnIfSlowToBuild).
    ///
    /// M and P: opening the large map closes the loadout screen, and opening the loadout screen closes the large
    /// map. The large map fits itself into the space above the HUD (controller review, 2026-09-17) rather than
    /// covering it - see SetLarge and UiTheme.minimapLargeBottomClearance.
    ///
    /// OPACITY: the corner map is drawn a little see-through, M makes it solid, and moving with M open dims it
    /// again (UiTheme > Minimap, and the MinimapOpacity rule). One CanvasGroup on the map's root carries all of it.
    ///
    /// OUT OF PLAY (2.7b Decision 8): the third capital of a host-started match reads its own look straight from
    /// MatchDirector.IsOutOfPlay, every LateUpdate its ownership is next re-coloured - its bubble fills
    /// UiTheme.outOfPlayZoneColor and every link to it is hidden, so no route reads as a way in. This is separate
    /// from SetZoneShown just below, an unused hook that hides a zone (and its links) entirely: out of play is a
    /// still-visible, unreachable capital, not a hidden one.
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
        /// <summary>The opacity the map is actually drawn at this frame (HUD step 5) - for harness checks, so a
        /// capture is not the only way to tell the three states apart.</summary>
        public float CurrentOpacity => fade != null ? fade.alpha : 1f;
        /// <summary>The smoothed planar speed the moving/stopped test is made against, in metres per second.</summary>
        public float MeasuredSpeed => smoothedSpeed;
        /// <summary>Whether the map currently counts the player as moving.</summary>
        public bool IsMoving => moving;
        /// <summary>The theme this map is laid out from. Read-only, and for one caller: the F1 debug log overlay
        /// (HUD step 6) has to clear the corner map's reserved band, and this is the only live handle on the
        /// numbers that band is computed from - it installs itself at runtime and has nothing serialized.</summary>
        public UiTheme Theme => theme;

        private sealed class ZoneUi
        {
            public int Zone;
            public float Diameter;
            public Vector2 MapPosition;
            public RectTransform Upright;
            public Image Ring;
            public Image Outline;
            public Image Fill;
            public bool Shown = true;
            /// <summary>2.7b Decision 8: MatchDirector.IsOutOfPlay(Zone), refreshed by RecolourOwnership. Read by
            /// ApplyLinkStyle (no link touches an out-of-play zone) and UpdateZones (CaptureRingState.From).</summary>
            public bool OutOfPlay;
            public float ShownRingFill = -1f;
            public Color ShownRingColor;
            public Color ShownOutlineColor;
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
        private readonly HashSet<int> hiddenZones = new HashSet<int>();

        private PlayerInputRouter inputRouter;
        private LoadoutScreen loadoutScreen;
        private PlayerLifecycle lifecycle;
        private BuildingManager manager;

        private RectTransform root;
        private RectTransform canvasRect;
        private RectTransform viewport;
        private RectTransform edgeShape;
        private RectTransform map;
        private RectTransform linksLayer;
        private RectTransform zonesLayer;
        private RectTransform markersLayer;
        private RectTransform teammatesLayer;
        private RectTransform ownMarker;
        private Material textMaterial;

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
        // The triangular mask/edge's constant offset from the map's own yaw rotation (Tudor, 2026-09-17: the mask
        // becomes a triangle, one vertex toward each capital). Computed once in TryBuild from real capital
        // positions - see ComputeTriangleBaseRotation. 0 for the shipped arena (its "up" capital already sits at
        // map-space (0,1), matching the generated triangle's own apex), but never hard-coded as 0.
        private float triangleBaseRotationDegrees;
        // Review fix, 2026-09-17: TryBuild waits (returns false) until every tower has registered; without this, a
        // tower that never does left the minimap silently blank forever with nothing in the console to say why.
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
            if (theme == null || config == null)
            {
                Debug.LogError($"[Minimap] {name}: UiTheme or MinimapConfig is not assigned - the minimap is not built.");
                enabled = false;
                return;
            }
            if (config.ArenaImage == null)
                Debug.LogError("[Minimap] MinimapConfig has no arena image - press OverPower > Arena > Bake minimap image. " +
                               "The minimap draws zones on a plain background meanwhile.");

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

        /// <summary>2.7 hook: hide (or show again) a zone's bubble and every link to it. Remembered if called before the
        /// map is built.</summary>
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

            // The P screen opened (by P or by its button): the large map closes.
            if (largeOpen && LoadoutScreen.IsOpen)
                SetLarge(false);

            ApplyYawIfChanged();

            if (ownershipDirty && manager.Current != null)
            {
                RecolourOwnership();
                ownershipDirty = false;
            }

            UpdateZones();
            UpdatePlayers();
            UpdateOpacity();
        }

        /// <summary>Tudor, 2026-09-17: the corner map is a little see-through, M makes it solid, and moving with
        /// M open dims it again. The rule (including the deadzone that stops it strobing) is MinimapOpacity, so
        /// it is tested in edit mode; this method only measures the speed and hands the answer to a CanvasGroup.
        ///
        /// Speed is MEASURED from this player's own position, not read from PlayerMotor.CurrentSpeed: that
        /// property is the CONFIGURED speed, which still reads 5 m/s while a stunned player stands perfectly
        /// still. A position delta is true for a dash, a knockback and a stun alike.</summary>
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
            // The very first frame snaps: a map that faded up from nothing every time a player spawned would
            // read as a bug, not as a nicety.
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

        /// <summary>2.7b Decision 8: the countdown starting/cancelling or the match going live - see TryBuild's own
        /// comment on why the out-of-play look needs this on top of OwnershipChanged.</summary>
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

            // The triangular mask/edge's own constant rotation (Tudor, 2026-09-17), so their vertex stays aligned
            // with the capital it targets at every yaw - see the field's own comment. map keeps turning by exactly
            // the camera yaw (nothing else moves): its constant offset here just cancels viewport's own constant
            // part, set once rather than every frame because it never changes after this.
            triangleBaseRotationDegrees = ComputeTriangleBaseRotationDegrees(zoneIds);
            map.localEulerAngles = new Vector3(0f, 0f, -triangleBaseRotationDegrees);

            foreach (int zone in zoneIds)
                BuildZone(zone);
            foreach ((int a, int b) in MinimapLayout.LinkPairs(manager.Map, zoneIds))
                BuildLink(a, b);

            ownMarker = BuildMarker("You", markersLayer, GeneratedSprites.Triangle, theme.minimapOwnMarkerColor, theme.minimapOwnMarkerSize);

            manager.OwnershipChanged += HandleOwnershipChanged;
            // 2.7b Decision 8: the out-of-play look depends on MatchDirector.IsOutOfPlay, which changes on the
            // live edge (Warmup -> ...) without any BuildingManager.OwnershipChanged event of its own (a host
            // start's third capital was already neutral before AND after going live) - so re-colour on that edge
            // too, or the third capital's minimap look would never update off its default (in-play) colour.
            if (MatchDirector.Instance != null)
                MatchDirector.Instance.LiveStateChanged += HandleLiveStateChanged;
            built = true;
            SetLarge(false);
            return true;
        }

        /// <summary>Logs once, only after ~5s of the map still not building (review fix, 2026-09-17): before this, a
        /// tower that never registered (RegisterCapture never ran) left the minimap silently blank forever, with
        /// nothing in the console to say why.</summary>
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

        /// <summary>The constant UI rotation (degrees) that turns the generated apex-up MaskTriangle/EdgeTriangle so
        /// their "up" vertex points at a real Tier 1 capital's own map-space direction (Tudor, 2026-09-17) - not
        /// hard-coded to +Z, read from BuildingManager's own zone centres. Picks whichever Tier 1 zone comes first
        /// by id; ArenaSymmetry's 3-fold layout guarantees the other two capitals sit ~120 degrees from it either
        /// way, so any one of the three works as the reference vertex.</summary>
        private float ComputeTriangleBaseRotationDegrees(List<int> zoneIds)
        {
            foreach (int zone in zoneIds)
            {
                if (manager.TierOf(zone) != 1 || !manager.TryGetZoneCentre(zone, out Vector3 centre))
                    continue;
                Vector2 direction = new Vector2(centre.x - config.WorldCentre.x, centre.z - config.WorldCentre.y);
                if (direction.sqrMagnitude <= 0.0001f)
                    continue;
                // The generated triangle's own apex sits at map-space (0,1) (90 degrees) before any rotation; turn
                // it by (direction's angle - 90) so the apex lands on this capital's real direction instead.
                float directionDegrees = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                return directionDegrees - 90f;
            }
            // No Tier 1 zone found among the registered zones. TryBuild only requires tier > 0 on every zone (so
            // every tower has registered), which does NOT guarantee one of them is specifically a capital (tier ==
            // 1) - so this fallback is reachable, not dead code. Identity (0 degrees) leaves the apex at map-space
            // "up", the same default CapitalDirections falls back to when it can't find a real Tier 1 tower either.
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
            // No GraphicRaycaster, on purpose: see the class comment. Adding one would make every shot fired with the
            // cursor over the map silently fail.

            canvasRect = (RectTransform)canvasGo.transform;

            root = NewRect("Minimap", canvasGo.transform);
            root.sizeDelta = Vector2.one * theme.minimapCornerSize;

            // One CanvasGroup over the whole map is what makes Tudor's three opacity states a single number
            // (HUD step 5): it multiplies every Graphic underneath, including the ones on the markers' own nested
            // Canvas, so the picture, the bubbles, the links and the dots all fade together and nothing has to
            // remember its own colour's alpha. Interactable and blocksRaycasts are both off, so the class
            // comment's "NEVER BLOCKS A SHOT" guarantee holds through this component too.
            fade = root.gameObject.AddComponent<CanvasGroup>();
            fade.interactable = false;
            fade.blocksRaycasts = false;

            // No round backdrop (review fix, 2026-09-17): nothing may be visible outside the triangle at all, not
            // even a dark disc peeking out around it - only the triangle window and its EdgeTriangle frame below.
            // The frame's visible band width in canvas units is minimapFrameWidth at the corner size (review fix,
            // 2026-09-17: it used to be a hard-coded fraction that field no longer controlled) - see
            // GeneratedSprites.BuildTriangleEdge's own comment for why this fraction produces that width.
            float frameBandFraction = theme.minimapFrameWidth / Mathf.Max(1f, theme.minimapCornerSize);
            // MaskTriangle (512 px, one vertex toward each capital - Tudor, 2026-09-17), not a disc: a UGUI Mask
            // reads its sprite's alpha as a 1-bit stencil test, and the finer source traces a smoother contour
            // before that test runs. Shrunk inward by half the frame band so that stencil cut sits under solid
            // frame colour, not right at the frame's own outer, visible edge (review fix, 2026-09-17). viewport
            // itself carries the yaw+base rotation (ApplyYawIfChanged) so the triangle turns with the map; map's
            // own rotation only ever cancels viewport's constant part (set once in TryBuild), so the picture/
            // bubbles/links/markers underneath still turn by exactly the camera yaw.
            Image viewportImage = NewImage("Viewport", root, GeneratedSprites.BuildTriangleMask(frameBandFraction), Color.white, theme.minimapCornerSize);
            viewport = viewportImage.rectTransform;
            // Triangular mask: the turned square picture never shows a corner, and the shape frames the arena.
            viewportImage.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            map = NewRect("Map", viewport);
            map.sizeDelta = Vector2.one * theme.minimapCornerSize;

            var pictureGo = new GameObject("Arena Picture", typeof(RectTransform));
            pictureGo.transform.SetParent(map, false);
            RawImage picture = pictureGo.AddComponent<RawImage>();
            picture.texture = config.ArenaImage;
            picture.color = config.ArenaImage != null ? theme.minimapBackgroundTint : theme.minimapFrameColor;
            picture.raycastTarget = false;
            Stretch(picture.rectTransform);

            // Sibling order is draw order: lines under bubbles, bubbles under player markers.
            linksLayer = NewLayer("Links", map);
            zonesLayer = NewLayer("Zones", map);
            markersLayer = NewLayer("Markers", map);
            // A nested Canvas (review fix, 2026-09-17): player markers move every frame, and without this UGUI had
            // to rebuild the WHOLE minimap's batched mesh (links, zone bubbles, labels) each frame just to redraw
            // two tiny dots. A separate Canvas here gives markers their own batch, so an idle map never rebuilds.
            markersLayer.gameObject.AddComponent<Canvas>();
            teammatesLayer = NewLayer("Teammates", markersLayer);

            // Drawn LAST, so on top of and outside the mask (a sibling of Viewport, not a child): a thin,
            // ordinarily anti-aliased triangular ring covering the mask's remaining stencil seam (review fix,
            // 2026-09-17). Rotated the same as viewport (ApplyYawIfChanged), independently of it (a sibling, not a
            // child, so it isn't itself masked), to stay aligned with the triangle underneath.
            edgeShape = NewImage("Edge Triangle", root, GeneratedSprites.BuildTriangleEdge(frameBandFraction), theme.minimapFrameColor, theme.minimapCornerSize).rectTransform;
        }

        private void BuildZone(int zone)
        {
            manager.TryGetZoneCentre(zone, out Vector3 centre);
            int tier = manager.TierOf(zone);
            var ui = new ZoneUi { Zone = zone, Diameter = theme.MinimapBubbleDiameter(tier) };
            ui.MapPosition = MinimapLayout.WorldToMap(centre, config.WorldCentre, config.WorldSizeMetres, theme.minimapCornerSize);

            ui.Upright = NewRect($"Zone {zone}", zonesLayer);
            ui.Upright.anchoredPosition = ui.MapPosition;
            ui.Upright.sizeDelta = Vector2.one * ui.Diameter;

            // The progress ring is a Filled disc behind the outline disc, so exactly Progress Ring Width shows around it
            // at every bubble size. Sprite first: a Filled Image without a sprite ignores its fill amount.
            float ringDiameter = ui.Diameter + 2f * (theme.minimapBubbleOutlineWidth + theme.minimapProgressRingWidth);
            ui.Ring = NewImage("Progress Ring", ui.Upright, GeneratedSprites.Disc, Color.white, ringDiameter);
            ui.Ring.type = Image.Type.Filled;
            ui.Ring.fillMethod = Image.FillMethod.Radial360;
            ui.Ring.fillOrigin = (int)Image.Origin360.Top; // the top of the screen, like the band on the ground
            ui.Ring.fillClockwise = true;
            ui.Ring.fillAmount = 0f;
            ui.Ring.enabled = false;

            ui.Outline = NewImage("Outline", ui.Upright, GeneratedSprites.Disc, theme.minimapBubbleOutlineColor,
                                  ui.Diameter + 2f * theme.minimapBubbleOutlineWidth);
            ui.ShownOutlineColor = theme.minimapBubbleOutlineColor;
            ui.Fill = NewImage("Fill", ui.Upright, GeneratedSprites.Disc, theme.minimapNeutralColor, ui.Diameter);
            AddLabel(ui.Upright, MinimapLayout.TierLabel(tier), theme.minimapLabelSize);

            ui.Shown = !hiddenZones.Contains(zone);
            ui.Upright.gameObject.SetActive(ui.Shown);
            zones.Add(ui);
            zoneById[zone] = ui;
        }

        /// <summary>Every link is two half-line Images, split in the middle of the VISIBLE gap between the two
        /// bubbles' edges - not at the midpoint between their centres (review fix, 2026-09-17): splitting at the
        /// centre midpoint gave the capital's half only ~0.6 units of visible line once its progress ring was
        /// showing, on the border that matters most. Owned/WayIn/Neutral colour both halves the same in
        /// ApplyLinkStyle (their two ends share one team or none), and only Border ends up two-toned. Geometry
        /// (position/length/angle) is fixed here at build time - only colour and width change later, on ownership
        /// change.</summary>
        private void BuildLink(int a, int b)
        {
            var link = new LinkUi { A = a, B = b };
            Vector2 posA = zoneById[a].MapPosition;
            Vector2 posB = zoneById[b].MapPosition;
            Vector2 delta = posB - posA;
            float length = delta.magnitude;
            Vector2 direction = length > 0.0001f ? delta / length : Vector2.right;
            // rA/rB: the same "just outside the bubble, outline and progress ring" radius the arrowhead uses
            // (ZoneRadius). Splitting at posA + direction * (rA + gap/2) puts the seam in the middle of the gap
            // between the two bubbles' edges, so each visible half gets an equal share regardless of bubble size.
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

        /// <summary>A bubble's visible radius: its own fill plus the outline and progress ring drawn around it - the
        /// point a link or arrowhead should stop just outside of. Shared by BuildLink's gap split and
        /// ApplyLinkStyle's arrowhead placement, so the two always agree on where a bubble "ends".</summary>
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
                // Fits above the HUD instead of covering it (controller review, 2026-09-17): the available band runs
                // from the top margin down to Bottom Clearance above the screen's bottom edge, and the map (at
                // whatever diameter fits) sits centred inside that band, not at the screen's true centre.
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
            // viewport (mask+edge) carries the triangle's constant base rotation on top of yaw; map's own local
            // rotation was set once in TryBuild to exactly cancel that constant part, so the picture/bubbles/links/
            // markers it holds still turn by exactly the camera yaw, same as when the mask was a circle.
            float triangleRotation = triangleBaseRotationDegrees + MinimapLayout.MapRotationDegrees(yaw);
            viewport.localEulerAngles = new Vector3(0f, 0f, triangleRotation);
            edgeShape.localEulerAngles = new Vector3(0f, 0f, triangleRotation);
            var upright = Quaternion.Euler(0f, 0f, MinimapLayout.UprightRotationDegrees(yaw));
            foreach (ZoneUi zone in zones)
                zone.Upright.localRotation = upright;
        }

        private void RecolourOwnership()
        {
            TerritorySnapshot snapshot = manager.Current;
            MatchDirector director = MatchDirector.Instance;
            foreach (ZoneUi zone in zones)
            {
                // 2.7b Decision 8: out of play wins over ownership - the cut capital is neutral underneath (nobody
                // can ever capture it), but must not read as ordinary neutral grey.
                zone.OutOfPlay = director != null && director.IsOutOfPlay(zone.Zone);
                int owner = snapshot.OwnerOf(zone.Zone);
                zone.Fill.color = zone.OutOfPlay ? theme.outOfPlayZoneColor
                    : owner >= 0 ? theme.ShotColorFor(owner) : theme.minimapNeutralColor;
            }
            foreach (LinkUi link in links)
                ApplyLinkStyle(link, MinimapLinkStyle.For(snapshot.OwnerOf(link.A), snapshot.OwnerOf(link.B)));
        }

        /// <summary>One code path for every MinimapLinkKind: Team colours the A half, TeamB colours the B half. For
        /// Neutral (Team = TeamB = -1) that's grey on both; for Owned/WayIn (Team == TeamB by construction, see
        /// MinimapLinkStyle.For) both halves land on the same team colour, same as a single solid line; only Border
        /// (Team != TeamB) actually reads two-toned. WayIn keeps its arrowhead; every other kind has none.</summary>
        private void ApplyLinkStyle(LinkUi link, MinimapLinkStyle style)
        {
            ZoneUi a = zoneById[link.A];
            ZoneUi b = zoneById[link.B];
            // 2.7b Decision 8: a link touching an out-of-play zone is hidden too - no route may lead into it, and a
            // grey line would read as a way in.
            bool shown = a.Shown && b.Shown && !a.OutOfPlay && !b.OutOfPlay;
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
            // Just outside the target's bubble (ZoneRadius - the same radius BuildLink's gap split uses), pointing at it.
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
                if (!zone.Shown)
                    continue;

                int owner = snapshot != null ? snapshot.OwnerOf(zone.Zone) : TerritoryMap.Neutral;
                bool attacked = presence != null && presence.IsUnderAttack(zone.Zone);
                CaptureRingState state = CaptureRingState.From(manager.CaptureProgressOf(zone.Zone), owner, attacked, nowMs,
                                                                outOfPlay: zone.OutOfPlay);

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

        private void UpdatePlayers()
        {
            bool alive = lifecycle == null || lifecycle.IsAlive;
            if (ownMarker.gameObject.activeSelf != alive)
                ownMarker.gameObject.SetActive(alive);
            if (alive)
            {
                ownMarker.anchoredPosition = MinimapLayout.WorldToMap(transform.position, config.WorldCentre, config.WorldSizeMetres, theme.minimapCornerSize);
                ownMarker.localEulerAngles = new Vector3(0f, 0f, MinimapLayout.FacingRotationDegrees(transform.eulerAngles.y));
            }

            int used = 0;
            Room room = PhotonNetwork.CurrentRoom;
            if (room != null && Teams.TryGetTeam(PhotonNetwork.LocalPlayer, out int myTeam))
            {
                // Room.Players is a Dictionary: its enumerator is a struct, so this allocates nothing (PhotonNetwork.PlayerList
                // would build a new sorted array every frame).
                foreach (KeyValuePair<int, Player> pair in room.Players)
                {
                    Player player = pair.Value;
                    if (player.IsLocal || !Teams.TryGetTeam(player, out int team) || team != myTeam)
                        continue;
                    if (player.CustomProperties.TryGetValue(PlayerLifecycle.AliveKey, out object raw) && raw is bool isAlive && !isAlive)
                        continue;
                    PhotonView view = PlayerLookup.GetPhotonViewFor(player.ActorNumber);
                    if (view == null)
                        continue;

                    RectTransform dot = DotAt(used++);
                    dot.anchoredPosition = MinimapLayout.WorldToMap(view.transform.position, config.WorldCentre, config.WorldSizeMetres, theme.minimapCornerSize);
                }
            }
            for (int i = used; i < teammateDots.Count; i++)
                if (teammateDots[i].gameObject.activeSelf)
                    teammateDots[i].gameObject.SetActive(false);
        }

        private RectTransform DotAt(int index)
        {
            while (teammateDots.Count <= index)
                teammateDots.Add(BuildMarker("Teammate", teammatesLayer, GeneratedSprites.Disc, theme.minimapTeammateDotColor, theme.minimapTeammateDotSize));
            RectTransform dot = teammateDots[index];
            if (!dot.gameObject.activeSelf)
                dot.gameObject.SetActive(true);
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

        /// <summary>Places a half-line Image between two map-space points: its centre, length and rotation. Used
        /// once at build time for each link's two halves; only colour and width (SetHalfWidth) change afterwards.</summary>
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

        private static Image NewImage(string name, Transform parent, Sprite sprite, Color colour, float size)
        {
            RectTransform rect = NewRect(name, parent);
            rect.sizeDelta = Vector2.one * size;
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = colour;
            image.raycastTarget = false; // never swallow a shot - see the class comment
            return image;
        }

        /// <summary>Same recipe as PlayerHud.AddLabel/ApplyOutline: one shared outline material for every label.</summary>
        private void AddLabel(Transform parent, string text, float fontSize)
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
        }
    }
}
