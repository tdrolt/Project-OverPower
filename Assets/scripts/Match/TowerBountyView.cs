using System.Collections.Generic;
using ExitGames.Client.Photon;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.UI;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.Match
{
    /// <summary>
    /// The bounty floating above a tower, and the pop when it is paid. A HUD label projected from the tower onto its own screen-overlay canvas
    /// (like CentreScanCountdownView), so it keeps one size however far the camera is zoomed out and can never be hidden by the tower's glow or its
    /// capture ring. It is not fogged: zone owners are drawn without sight, and so is this. BountyLabelPlacement keeps it on screen and off the
    /// corner minimap. Every client works it out from the replicated TerritorySnapshot and the server clock (BountyLabelRules), so there is no extra
    /// network traffic. Text, colour, sizes and heights: UiTheme > Bounty fields. Lives on the BuildingManager.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after the camera has moved this frame
    public sealed class TowerBountyView : MonoBehaviour, IInRoomCallbacks
    {
        private const float ScreenMargin = 16f;

        private sealed class Pop
        {
            public int Zone;
            public float StartTime;
            public TextMeshProUGUI Label;
            public Vector2 Size;
        }

        private GameObject canvasGo;
        private RectTransform canvasRect;
        private readonly Dictionary<int, TextMeshProUGUI> labels = new Dictionary<int, TextMeshProUGUI>();
        private readonly Dictionary<int, int> shownAmount = new Dictionary<int, int>();
        private readonly Dictionary<int, float> shownFontSize = new Dictionary<int, float>();
        private readonly Dictionary<int, Vector2> labelSizes = new Dictionary<int, Vector2>();
        private readonly List<Pop> pops = new List<Pop>();
        private readonly List<Material> materials = new List<Material>();
        private readonly TerritoryHistory history = new TerritoryHistory();
        private BuildingManager hooked;
        private Room roundPaysRoom;
        private bool roundPaysStale = true;
        private bool roundPays;

        public static TowerBountyView AttachTo(GameObject host)
        {
            TowerBountyView view = host.GetComponent<TowerBountyView>();
            return view != null ? view : host.AddComponent<TowerBountyView>();
        }

        /// <summary>What the label above a zone shows now, or empty while it is hidden.</summary>
        public string ShownText(int zone) => labels.TryGetValue(zone, out TextMeshProUGUI l) && l != null && l.gameObject.activeSelf ? l.text : string.Empty;

        /// <summary>The texts of the pops on screen now.</summary>
        public List<string> PopTexts()
        {
            var texts = new List<string>();
            foreach (Pop pop in pops) texts.Add(pop.Label.text);
            return texts;
        }

        private void OnEnable() => PhotonNetwork.AddCallbackTarget(this);

        private void OnDisable() => PhotonNetwork.RemoveCallbackTarget(this);

        private void OnDestroy()
        {
            Unhook();
            if (canvasGo != null) Destroy(canvasGo);
            foreach (Material m in materials) if (m != null) Destroy(m);
        }

        private void Unhook()
        {
            if (hooked != null) hooked.OwnershipChanged -= OnOwnershipChanged;
            hooked = null;
        }

        public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) => roundPaysStale = true;
        public void OnPlayerEnteredRoom(Player newPlayer) { }
        public void OnPlayerLeftRoom(Player otherPlayer) { }
        public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps) { }
        public void OnMasterClientSwitched(Player newMasterClient) { }

        // Dominion only: whether a bounty can be paid right now. Read from the room's properties only when they change.
        private bool RoundPays(bool dominion)
        {
            if (!dominion) return false;
            Room room = PhotonNetwork.CurrentRoom;
            if (room != roundPaysRoom) { roundPaysRoom = room; roundPaysStale = true; }
            if (roundPaysStale)
            {
                roundPaysStale = false;
                roundPays = room != null && DominionPointsRules.PointsRun(DominionRoomState.Read(room.CustomProperties));
            }
            return roundPays;
        }

        private void LateUpdate()
        {
            BuildingManager manager = BuildingManager.Instance;
            if (manager == null || manager.Current == null || !PhotonNetwork.InRoom)
            {
                HideAll();
                return;
            }
            if (hooked != manager)
            {
                Unhook();
                manager.OwnershipChanged += OnOwnershipChanged;
                hooked = manager;
                history.Seen(manager.Current); // the first read is the match as it already was: no pops for it
            }

            TerritorySnapshot snapshot = manager.Current;
            Camera cam = Camera.main;
            bool dominion = DominionMode.IsActive();
            bool paysNow = RoundPays(dominion);
            DominionConfig config = dominion ? DominionMode.Config() : null;
            int now = PhotonNetwork.ServerTimestamp;

            for (int zone = 0; zone < snapshot.ZoneCount; zone++)
            {
                int amount = 0;
                UiTheme theme = null;
                if (cam != null && now != 0 && manager.TryGetZoneBounty(zone, out int tierBounty, out float tierHold, out theme))
                {
                    BountyLabelRules.Offer offer = OfferOf(manager, zone, dominion, paysNow, config, tierBounty, tierHold);
                    amount = BountyLabelRules.LabelAmount(snapshot.OwnerOf(zone), snapshot.HeldSinceMs(zone), snapshot.LastOwnerOf(zone),
                                                          snapshot.LastHeldMs(zone), now, offer.HoldMs, offer.Amount);
                }
                if (amount <= 0 || !EnsureCanvas(theme) || !TryScreenPoint(manager, zone, theme, cam, out Vector2 local))
                {
                    HideLabel(zone);
                    continue;
                }
                TextMeshProUGUI label = LabelFor(zone, theme);
                bool hasShown = shownAmount.TryGetValue(zone, out int shown);
                shownFontSize.TryGetValue(zone, out float shownSize);
                if (BountyLabelPlacement.NeedsRedo(hasShown, shown, shownSize, amount, theme.bountyLabelFontSize))
                {
                    shownAmount[zone] = amount;
                    shownFontSize[zone] = theme.bountyLabelFontSize;
                    label.fontSize = theme.bountyLabelFontSize;
                    label.text = string.Format(dominion ? theme.bountyLabelFormat : theme.bountyLabelGoldFormat, amount);
                    labelSizes[zone] = SizeOf(label);
                }
                label.color = theme.bountyLabelColour;
                local.y += theme.bountyLabelLift;
                bool hasAvoid = AvoidRect(theme, out Rect avoid);
                bool hasBar = AvoidBar(out Rect bar);
                label.rectTransform.anchoredPosition = BountyLabelPlacement.Place(local, labelSizes[zone], canvasRect.rect, ScreenMargin, hasAvoid, avoid, hasBar, bar);
                if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            }

            UpdatePops(manager, cam);
            history.Seen(snapshot);
        }

        private static BountyLabelRules.Offer OfferOf(BuildingManager manager, int zone, bool dominion, bool paysNow, DominionConfig config,
                                                      int tierBounty, float tierHoldSeconds)
        {
            bool inPlay = manager.IsCapturableZone(zone) && (MatchDirector.Instance == null || !MatchDirector.Instance.IsOutOfPlay(zone));
            return BountyLabelRules.OfferFor(dominion, inPlay && (!dominion || (paysNow && config != null)), tierBounty, tierHoldSeconds,
                                             config != null ? config.BountyPoints : 0, config != null ? config.BountyHoldSeconds : 0f);
        }

        private void OnOwnershipChanged(int zone, int oldOwner, int newOwner, TerritorySnapshot snapshot)
        {
            TerritorySnapshot before = history.BeforeEventIn(snapshot);
            if (before == null || newOwner < 0) return;

            BuildingManager manager = BuildingManager.Instance;
            if (manager == null || !manager.TryGetZoneBounty(zone, out int tierBounty, out float tierHold, out UiTheme theme)) return;
            bool dominion = DominionMode.IsActive();
            DominionConfig config = dominion ? DominionMode.Config() : null;
            BountyLabelRules.Offer offer = OfferOf(manager, zone, dominion, RoundPays(dominion), config, tierBounty, tierHold);
            int paid = BountyLabelRules.PopAmount(dominion, newOwner, before.LastOwnerOf(zone), before.LastHeldMs(zone), offer.HoldMs, offer.Amount,
                                                  snapshot.BountyPaidOnLastCapture(zone));
            if (paid <= 0 || !EnsureCanvas(theme)) return;

            TextMeshProUGUI label = NewLabel("Bounty Pop " + zone, theme);
            label.fontSize = theme.bountyPopFontSize;
            label.text = string.Format(dominion ? theme.bountyPopFormat : theme.bountyPopGoldFormat, paid);
            label.color = theme.bountyLabelColour;
            label.gameObject.SetActive(false);
            pops.Add(new Pop { Zone = zone, StartTime = Time.unscaledTime, Label = label, Size = SizeOf(label) });
        }

        private void UpdatePops(BuildingManager manager, Camera cam)
        {
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                Pop pop = pops[i];
                UiTheme theme = null;
                float seconds = 0f, rise = 0f;
                if (manager.TryGetZoneBounty(pop.Zone, out _, out _, out theme))
                {
                    seconds = theme.bountyPopSeconds;
                    rise = theme.bountyPopRise;
                }
                float age = Time.unscaledTime - pop.StartTime;
                if (age >= seconds || cam == null || theme == null)
                {
                    Destroy(pop.Label.gameObject);
                    pops.RemoveAt(i);
                    continue;
                }
                if (!TryScreenPoint(manager, pop.Zone, theme, cam, out Vector2 local))
                {
                    pop.Label.gameObject.SetActive(false);
                    continue;
                }
                local.y += theme.bountyLabelLift + BountyLabelRules.PopRise(age, seconds, rise);
                Color colour = theme.bountyLabelColour;
                colour.a *= BountyLabelRules.PopAlpha(age, seconds);
                pop.Label.color = colour;
                bool hasAvoid = AvoidRect(theme, out Rect avoid);
                bool hasBar = AvoidBar(out Rect bar);
                pop.Label.rectTransform.anchoredPosition = BountyLabelPlacement.Place(local, pop.Size, canvasRect.rect, ScreenMargin, hasAvoid, avoid, hasBar, bar);
                pop.Label.gameObject.SetActive(true);
            }
        }

        // The corner minimap's box on this canvas. Nothing to avoid when this player has no minimap or has the large one open (it covers the middle, not the corner).
        private bool AvoidRect(UiTheme theme, out Rect rect)
        {
            MinimapView minimap = MinimapView.Local;
            if (minimap == null || !minimap.IsBuilt || minimap.IsLargeOpen)
            {
                rect = default;
                return false;
            }
            rect = BountyLabelPlacement.CornerRectInCanvas(canvasRect.rect,
                HudScreenLayout.CornerMinimapRect(theme.minimapCornerMargin, theme.minimapFrameWidth, theme.minimapCornerSize));
            return true;
        }

        // The local player's HUD panel (ability slots and bars) on this canvas; nothing to avoid without one.
        private bool AvoidBar(out Rect rect)
        {
            rect = default;
            PlayerHud hud = PlayerHud.Local;
            if (hud == null || !hud.TryGetScreenRect(out Rect screen)) return false;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen.min, null, out Vector2 low);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen.max, null, out Vector2 high);
            rect = Rect.MinMaxRect(low.x, low.y, high.x, high.y);
            return true;
        }

        private static Vector2 SizeOf(TextMeshProUGUI label) =>
            new Vector2(label.GetPreferredValues(label.text).x, BountyLabelPlacement.LabelHeight(label.fontSize));

        // The zone's centre, anchorHeight metres up, on this canvas. False when it is behind the camera.
        private bool TryScreenPoint(BuildingManager manager, int zone, UiTheme theme, Camera cam, out Vector2 local)
        {
            local = default;
            if (!manager.TryGetZoneCentre(zone, out Vector3 centre)) return false;
            Vector3 screen = cam.WorldToScreenPoint(centre + Vector3.up * theme.bountyLabelAnchorHeight);
            if (screen.z < 0f) return false;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);
            return true;
        }

        private void HideAll()
        {
            foreach (TextMeshProUGUI label in labels.Values)
                if (label != null && label.gameObject.activeSelf)
                    label.gameObject.SetActive(false);
            shownAmount.Clear();
            shownFontSize.Clear();
            foreach (Pop pop in pops) Destroy(pop.Label.gameObject);
            pops.Clear();
        }

        private void HideLabel(int zone)
        {
            shownAmount.Remove(zone);
            shownFontSize.Remove(zone);
            if (labels.TryGetValue(zone, out TextMeshProUGUI label) && label != null && label.gameObject.activeSelf)
                label.gameObject.SetActive(false);
        }

        private TextMeshProUGUI LabelFor(int zone, UiTheme theme)
        {
            if (!labels.TryGetValue(zone, out TextMeshProUGUI label) || label == null)
            {
                label = NewLabel("Bounty Label " + zone, theme);
                label.gameObject.SetActive(false);
                labels[zone] = label;
            }
            return label;
        }

        private bool EnsureCanvas(UiTheme theme)
        {
            if (theme == null) return false;
            if (canvasGo != null) return true;
            canvasGo = new GameObject("Tower Bounty Canvas (cosmetic only)", typeof(RectTransform));
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = -21; // same layer as the scan countdown: under the HUD's hit feedback canvas
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            canvasRect = canvasGo.GetComponent<RectTransform>();
            return true;
        }

        private TextMeshProUGUI NewLabel(string name, UiTheme theme)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(canvasGo.transform, false);
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            if (theme.font != null) text.font = theme.font; // before the material is touched (assigning a font swaps the material)
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            var material = new Material(text.fontSharedMaterial);
            theme.ApplyHudTextStyle(material);
            text.fontSharedMaterial = material;
            materials.Add(material);
            RectTransform rect = text.rectTransform;
            rect.sizeDelta = new Vector2(500f, 80f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            return text;
        }
    }
}
