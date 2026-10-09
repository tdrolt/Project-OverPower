using System.Collections.Generic;
using Overpower.Data;
using Overpower.Dominion;
using Overpower.UI;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.Match
{
    /// <summary>
    /// The bounty floating above a tower, and the pop when it is paid. A HUD label projected from a point above the tower onto its own
    /// screen-overlay canvas (like CentreScanCountdownView), so it keeps one size however far the camera is zoomed out and can never be hidden
    /// by the tower's glow or its capture ring. It is not fogged: zone owners are drawn without sight, and so is this.
    /// Every client works it out from the replicated TerritorySnapshot and the server clock (BountyLabelRules), so there is no extra
    /// network traffic. Text, colour, sizes and height: UiTheme > Bounty fields. Lives on the BuildingManager.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after the camera has moved this frame
    public sealed class TowerBountyView : MonoBehaviour
    {
        private sealed class Pop
        {
            public int Zone;
            public float StartTime;
            public TextMeshProUGUI Label;
        }

        private GameObject canvasGo;
        private RectTransform canvasRect;
        private readonly Dictionary<int, TextMeshProUGUI> labels = new Dictionary<int, TextMeshProUGUI>();
        private readonly Dictionary<int, int> shownAmount = new Dictionary<int, int>();
        private readonly List<Pop> pops = new List<Pop>();
        private readonly List<Material> materials = new List<Material>();
        private BuildingManager hooked;
        private TerritorySnapshot lastSeen;
        private TerritorySnapshot handled;
        private TerritorySnapshot beforeHandled;

        /// <summary>What the label above a zone shows now, or empty while it is hidden.</summary>
        public string ShownText(int zone) => labels.TryGetValue(zone, out TextMeshProUGUI l) && l.gameObject.activeSelf ? l.text : string.Empty;

        /// <summary>The texts of the pops on screen now.</summary>
        public List<string> PopTexts()
        {
            var texts = new List<string>();
            foreach (Pop pop in pops) texts.Add(pop.Label.text);
            return texts;
        }

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
                lastSeen = manager.Current; // the first read is the match as it already was: no pops for it
            }

            TerritorySnapshot snapshot = manager.Current;
            Camera cam = Camera.main;
            bool dominion = DominionMode.IsActive();
            bool roundPays = dominion && DominionPointsRules.PointsRun(DominionRoomState.Read(PhotonNetwork.CurrentRoom.CustomProperties));
            DominionConfig config = dominion ? DominionMode.Config() : null;
            int now = PhotonNetwork.ServerTimestamp;

            for (int zone = 0; zone < snapshot.ZoneCount; zone++)
            {
                int amount = 0;
                UiTheme theme = null;
                if (cam != null && now != 0 && manager.TryGetZoneBounty(zone, out int tierBounty, out float tierHold, out theme))
                {
                    BountyLabelRules.Offer offer = OfferOf(manager, zone, dominion, roundPays, config, tierBounty, tierHold);
                    amount = BountyLabelRules.LabelAmount(snapshot.OwnerOf(zone), snapshot.HeldSinceMs(zone), snapshot.LastOwnerOf(zone),
                                                          snapshot.LastHeldMs(zone), now, offer.HoldMs, offer.Amount);
                }
                if (amount <= 0 || !EnsureCanvas(theme) || !TryScreenPoint(manager, zone, theme, 0f, cam, out Vector2 local))
                {
                    HideLabel(zone);
                    continue;
                }
                TextMeshProUGUI label = LabelFor(zone, theme);
                if (!shownAmount.TryGetValue(zone, out int shown) || shown != amount)
                {
                    shownAmount[zone] = amount;
                    label.text = string.Format(dominion ? theme.bountyLabelFormat : theme.bountyLabelGoldFormat, amount);
                }
                label.fontSize = theme.bountyLabelFontSize;
                label.color = theme.bountyLabelColour;
                label.rectTransform.anchoredPosition = local;
                if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
            }

            UpdatePops(manager, cam);
            lastSeen = snapshot;
        }

        private static BountyLabelRules.Offer OfferOf(BuildingManager manager, int zone, bool dominion, bool roundPays, DominionConfig config,
                                                      int tierBounty, float tierHoldSeconds)
        {
            bool inPlay = manager.IsCapturableZone(zone) && (MatchDirector.Instance == null || !MatchDirector.Instance.IsOutOfPlay(zone));
            bool paysNow = inPlay && (!dominion || (roundPays && config != null));
            return BountyLabelRules.OfferFor(dominion, paysNow, tierBounty, tierHoldSeconds,
                                             config != null ? config.BountyPoints : 0, config != null ? config.BountyHoldSeconds : 0f);
        }

        // Several zones can change in one snapshot: every event of it must see the snapshot BEFORE it, not the one it carries.
        private void OnOwnershipChanged(int zone, int oldOwner, int newOwner, TerritorySnapshot snapshot)
        {
            if (snapshot != handled)
            {
                beforeHandled = lastSeen;
                handled = snapshot;
            }
            lastSeen = snapshot;
            if (beforeHandled == null || newOwner < 0) return;

            BuildingManager manager = BuildingManager.Instance;
            if (manager == null || !manager.TryGetZoneBounty(zone, out int tierBounty, out float tierHold, out UiTheme theme)) return;
            bool dominion = DominionMode.IsActive();
            bool roundPays = dominion && DominionPointsRules.PointsRun(DominionRoomState.Read(PhotonNetwork.CurrentRoom.CustomProperties));
            DominionConfig config = dominion ? DominionMode.Config() : null;
            BountyLabelRules.Offer offer = OfferOf(manager, zone, dominion, roundPays, config, tierBounty, tierHold);
            int paid = BountyLabelRules.PopAmount(newOwner, beforeHandled.LastOwnerOf(zone), beforeHandled.LastHeldMs(zone), offer.HoldMs, offer.Amount);
            if (paid <= 0 || !EnsureCanvas(theme)) return;

            TextMeshProUGUI label = NewLabel("Bounty Pop " + zone, theme);
            label.text = string.Format(dominion ? theme.bountyPopFormat : theme.bountyPopGoldFormat, paid);
            label.fontSize = theme.bountyPopFontSize;
            label.color = theme.bountyLabelColour;
            label.gameObject.SetActive(false);
            pops.Add(new Pop { Zone = zone, StartTime = Time.unscaledTime, Label = label });
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
                if (!TryScreenPoint(manager, pop.Zone, theme, BountyLabelRules.PopRise(age, seconds, rise), cam, out Vector2 local))
                {
                    pop.Label.gameObject.SetActive(false);
                    continue;
                }
                Color colour = theme.bountyLabelColour;
                colour.a *= BountyLabelRules.PopAlpha(age, seconds);
                pop.Label.color = colour;
                pop.Label.rectTransform.anchoredPosition = local;
                pop.Label.gameObject.SetActive(true);
            }
        }

        private bool TryScreenPoint(BuildingManager manager, int zone, UiTheme theme, float liftPixels, Camera cam, out Vector2 local)
        {
            local = default;
            if (!manager.TryGetZoneCentre(zone, out Vector3 centre)) return false;
            float top = manager.TryGetZoneTowerTopY(zone, out float topY) ? topY : centre.y;
            Vector3 screen = cam.WorldToScreenPoint(new Vector3(centre.x, top, centre.z));
            if (screen.z < 0f) return false;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);
            local.y += theme.bountyLabelLift + liftPixels;
            return true;
        }

        private void HideAll()
        {
            foreach (int zone in new List<int>(labels.Keys)) HideLabel(zone);
            foreach (Pop pop in pops) Destroy(pop.Label.gameObject);
            pops.Clear();
        }

        private void HideLabel(int zone)
        {
            shownAmount.Remove(zone);
            if (labels.TryGetValue(zone, out TextMeshProUGUI label) && label.gameObject.activeSelf)
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
