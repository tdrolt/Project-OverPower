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
    /// The minimap (Tudor, 2026-09-16; it looks like GDD p.27): a round map in the top-right corner, and a large one
    /// in the middle of the screen while M is toggled on. What it shows:
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
    /// EVERY LINK IS TWO HALVES: built once as two Image rectangles, from each end to the link's midpoint. Owned and
    /// Neutral colour both halves the same (their two ends share one colour or none), so this reads exactly like one
    /// line; only Border ends up two-toned. This keeps ApplyLinkStyle a single code path for every MinimapLinkKind
    /// instead of a special case for Border.
    ///
    /// COST: bubbles and lines are built once. Bubble and link colours change only when ownership changes
    /// (BuildingManager.OwnershipChanged, plus the first territory read, which raises no event). Each zone's ring and
    /// outline are worked out every frame from CaptureRingState, but written to the UI only when they change, so an
    /// idle zone touches nothing. Player markers move every frame. Nothing here allocates per frame.
    ///
    /// M and P: opening the large map closes the loadout screen, and opening the loadout screen closes the large map.
    ///
    /// PHASES (2.7): call MinimapView.Local.SetZoneShown(zone, false) for each zone taken out of play; it hides the
    /// bubble and every link to it.
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
        private RectTransform map;
        private RectTransform linksLayer;
        private RectTransform zonesLayer;
        private RectTransform teammatesLayer;
        private RectTransform ownMarker;
        private Material textMaterial;

        private bool built;
        private bool largeOpen;
        private bool ownershipDirty = true;
        private float appliedYaw = float.NaN;

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
        }

        private void HandleOwnershipChanged(int zone, int oldOwner, int newOwner, TerritorySnapshot snapshot) =>
            ownershipDirty = true;

        // ---------------------------------------------------------------- build (once)

        private bool TryBuild()
        {
            manager = BuildingManager.Instance;
            if (manager == null || manager.Map == null || manager.TowerDictionary == null || manager.TowerDictionary.Count == 0)
                return false;
            // Towers register in their own Start; wait until every zone has, so no bubble is missing.
            foreach (int zone in manager.TowerDictionary.Keys)
                if (manager.TierOf(zone) <= 0 || !manager.TryGetZoneCentre(zone, out _))
                    return false;

            BuildFrame();
            var zoneIds = new List<int>(manager.TowerDictionary.Keys);
            zoneIds.Sort();
            foreach (int zone in zoneIds)
                BuildZone(zone);
            foreach ((int a, int b) in MinimapLayout.LinkPairs(manager.Map, zoneIds))
                BuildLink(a, b);

            ownMarker = BuildMarker("You", map, GeneratedSprites.Triangle, theme.minimapOwnMarkerColor, theme.minimapOwnMarkerSize);

            manager.OwnershipChanged += HandleOwnershipChanged;
            built = true;
            SetLarge(false);
            return true;
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

            root = NewRect("Minimap", canvasGo.transform);
            root.sizeDelta = Vector2.one * theme.minimapCornerSize;

            NewImage("Frame", root, GeneratedSprites.Disc, theme.minimapFrameColor, theme.minimapCornerSize + 2f * theme.minimapFrameWidth);

            Image viewport = NewImage("Viewport", root, GeneratedSprites.Disc, Color.white, theme.minimapCornerSize);
            // Round mask: the turned square picture never shows its corners.
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            map = NewRect("Map", viewport.transform);
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
            teammatesLayer = NewLayer("Teammates", map);
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

        /// <summary>Every link is two half-line Images, split at the midpoint (controller amendment 2, 2026-09-17):
        /// Owned/WayIn/Neutral colour both halves the same in ApplyLinkStyle (their two ends share one team or
        /// none), and only Border ends up two-toned. Geometry (position/length/angle) is fixed here at build time -
        /// only colour and width change later, on ownership change.</summary>
        private void BuildLink(int a, int b)
        {
            var link = new LinkUi { A = a, B = b };
            Vector2 posA = zoneById[a].MapPosition;
            Vector2 posB = zoneById[b].MapPosition;
            Vector2 mid = (posA + posB) / 2f;

            link.LineA = NewImage($"Link {a}-{b} A", linksLayer, null, theme.minimapNeutralColor, 0f);
            PlaceHalfSegment(link.LineA, posA, mid, theme.minimapNeutralLinkWidth);
            link.LineB = NewImage($"Link {a}-{b} B", linksLayer, null, theme.minimapNeutralColor, 0f);
            PlaceHalfSegment(link.LineB, mid, posB, theme.minimapNeutralLinkWidth);

            link.Arrow = NewImage($"Arrow {a}-{b}", linksLayer, GeneratedSprites.Triangle, theme.minimapNeutralColor, theme.minimapArrowheadSize);
            link.Arrow.gameObject.SetActive(false);
            links.Add(link);
        }

        // ---------------------------------------------------------------- per-frame updates

        private void SetLarge(bool open)
        {
            largeOpen = open;
            Vector2 anchor = open ? new Vector2(0.5f, 0.5f) : Vector2.one;
            root.anchorMin = anchor;
            root.anchorMax = anchor;
            root.pivot = anchor;
            float inset = theme.minimapCornerMargin + theme.minimapFrameWidth;
            root.anchoredPosition = open ? Vector2.zero : new Vector2(-inset, -inset);
            // One hierarchy for both views: the large map is the corner map scaled up, so every size scales together.
            root.localScale = Vector3.one * (open ? theme.minimapLargeSize / Mathf.Max(1f, theme.minimapCornerSize) : 1f);
        }

        private void ApplyYawIfChanged()
        {
            float yaw = CameraTracking.Instance != null ? CameraTracking.Instance.Yaw : 0f;
            if (Mathf.Approximately(yaw, appliedYaw))
                return;
            appliedYaw = yaw;
            map.localEulerAngles = new Vector3(0f, 0f, MinimapLayout.MapRotationDegrees(yaw));
            var upright = Quaternion.Euler(0f, 0f, MinimapLayout.UprightRotationDegrees(yaw));
            foreach (ZoneUi zone in zones)
                zone.Upright.localRotation = upright;
        }

        private void RecolourOwnership()
        {
            TerritorySnapshot snapshot = manager.Current;
            foreach (ZoneUi zone in zones)
            {
                int owner = snapshot.OwnerOf(zone.Zone);
                zone.Fill.color = owner >= 0 ? theme.ShotColorFor(owner) : theme.minimapNeutralColor;
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
            bool shown = a.Shown && b.Shown;
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
            // Just outside the target's bubble, outline and progress ring, pointing at it.
            float stop = to.Diameter / 2f + theme.minimapBubbleOutlineWidth + theme.minimapProgressRingWidth + theme.minimapArrowheadSize / 2f;
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
                CaptureRingState state = CaptureRingState.From(manager.CaptureProgressOf(zone.Zone), owner, attacked, nowMs);

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
                textMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, theme.textOutlineWidth);
                textMaterial.SetColor(ShaderUtilities.ID_OutlineColor, theme.textOutlineColor);
            }
            tmp.fontSharedMaterial = textMaterial;
            Stretch(tmp.rectTransform);
        }
    }
}
