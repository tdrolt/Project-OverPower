using Overpower.Data;
using Overpower.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.Vision
{
    /// <summary>
    /// The countdown to the next centre scan, above the centre tower for everyone. A HUD label projected from a point above
    /// the tower onto its own screen-overlay canvas (like DamageNumberView and MarkIndicatorView), so it keeps one size on
    /// screen however far the camera is zoomed out. The canvas has no GraphicRaycaster, so it can never swallow a click
    /// (HANDOFF trap 20), sits behind the main HUD (order -21) and is not fogged. Font UiTheme.font, size UiTheme > Scan
    /// Countdown Font Size, colour Scan Wave Colour.
    /// A second label with the same text sits under the corner minimap, fixed on screen, so the countdown is readable when the
    /// tower is off screen: same conditions minus the camera test. It stays under the CORNER map when M opens the large one
    /// (the large map is centred and leaves the corner free). Size and offset: UiTheme > Scan Minimap Countdown Font Size / Offset.
    /// Reads "Scan 12", whole seconds, and keeps counting while a wave travels (the full interval as a wave starts). Hidden when
    /// CentreScan has no countdown, Scan Countdown Visible is off, or the anchor is behind the camera. Wording and size are on
    /// the UiTheme; height and on/off on the Vision Config.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after the camera has moved this frame
    public sealed class CentreScanCountdownView : MonoBehaviour
    {
        private const string FallbackFormat = "Scan {0}";

        private GameObject canvasGo;
        private RectTransform canvasRect;
        private TextMeshProUGUI label;
        private TextMeshProUGUI minimapLabel;
        private Material labelMaterial;
        private Material minimapLabelMaterial;
        private int shownSeconds = -1;
        private string format;

        /// <summary>What the label shows now, or empty while it is hidden.</summary>
        public string ShownText => label != null && label.gameObject.activeSelf ? label.text : string.Empty;

        /// <summary>What the label under the corner minimap shows now, or empty while it is hidden.</summary>
        public string MinimapShownText => minimapLabel != null && minimapLabel.gameObject.activeSelf ? minimapLabel.text : string.Empty;

        private void OnDestroy()
        {
            if (canvasGo != null)
                Destroy(canvasGo);
            if (labelMaterial != null)
                Destroy(labelMaterial);
            if (minimapLabelMaterial != null)
                Destroy(minimapLabelMaterial);
        }

        private void LateUpdate()
        {
            CentreScan scan = CentreScan.Instance;
            Camera cam = Camera.main;
            VisionConfig config = scan != null ? scan.Config : null;
            if (config == null || cam == null || !config.ScanCountdownVisible || scan.CountdownSeconds < 0)
            {
                Hide();
                return;
            }

            UiTheme theme = LookUpTheme();
            if (label == null && !Build(theme))
            {
                Hide();
                return;
            }

            if (format == null)
                format = !string.IsNullOrEmpty(theme.scanCountdownFormat) ? theme.scanCountdownFormat : FallbackFormat;
            if (scan.CountdownSeconds != shownSeconds)
            {
                shownSeconds = scan.CountdownSeconds;
                string text = string.Format(format, shownSeconds); // once a second, so the allocation does not matter
                label.text = text;
                minimapLabel.text = text;
            }
            // A seat spectator has no corner minimap, so the label under it would float at the right edge: only the one over the tower stays.
            if (Overpower.Net.Teams.IsSpectator(Photon.Pun.PhotonNetwork.LocalPlayer))
            {
                if (minimapLabel.gameObject.activeSelf)
                    minimapLabel.gameObject.SetActive(false);
            }
            else
            {
                minimapLabel.fontSize = theme.scanMinimapCountdownFontSize;
                minimapLabel.color = config.ScanWaveColour;
                PlaceUnderCornerMinimap(theme);
                if (!minimapLabel.gameObject.activeSelf)
                    minimapLabel.gameObject.SetActive(true);
            }

            Vector3 screenPoint = cam.WorldToScreenPoint(scan.CountdownPosition);
            if (screenPoint.z < 0f)
            {
                HideTowerLabel();
                return;
            }

            label.fontSize = theme.scanCountdownFontSize;
            label.color = config.ScanWaveColour;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 local);
            label.rectTransform.anchoredPosition = local;
            if (!label.gameObject.activeSelf)
                label.gameObject.SetActive(true);
        }

        // Straight under the corner minimap's box (top right: margin + frame band in, then the map's size down), centred on it.
        private void PlaceUnderCornerMinimap(UiTheme theme)
        {
            Rect box = Overpower.UI.HudScreenLayout.CornerMinimapRect(theme.minimapCornerMargin, theme.minimapFrameWidth, theme.minimapCornerSize);
            minimapLabel.rectTransform.anchoredPosition = new Vector2(box.center.x + theme.scanMinimapCountdownOffset.x, box.yMin + theme.scanMinimapCountdownOffset.y);
        }

        private void Hide()
        {
            shownSeconds = -1;
            HideTowerLabel();
            if (minimapLabel != null && minimapLabel.gameObject.activeSelf)
                minimapLabel.gameObject.SetActive(false);
        }

        private void HideTowerLabel()
        {
            if (label != null && label.gameObject.activeSelf)
                label.gameObject.SetActive(false);
        }

        // The wording, font and size live in the UiTheme the player prefab already carries (one home for text).
        private static UiTheme LookUpTheme()
        {
            TeamSight sight = TeamSight.Local;
            PlayerHealth health = sight != null ? sight.GetComponent<PlayerHealth>() : null;
            return health != null ? health.Theme : CentreScan.SpectatorTheme; // a spectator has no body: the view hands its theme over
        }

        // Built on first use, once the theme can be read: a screen-overlay canvas like the HUD's hit-feedback one.
        private bool Build(UiTheme theme)
        {
            if (theme == null)
                return false;
            canvasGo = new GameObject("Centre Scan Countdown Canvas (cosmetic only)", typeof(RectTransform));
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = -21; // under PlayerHud's hit feedback canvas (-20): a flash of damage draws over the countdown, never under it
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            // No GraphicRaycaster: nothing here is clickable.
            canvasRect = canvasGo.GetComponent<RectTransform>();

            label = NewLabel("Countdown Label", theme, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), out labelMaterial);
            // Under the corner minimap: anchored to the top right with its top edge at the point PlaceUnderCornerMinimap sets.
            minimapLabel = NewLabel("Minimap Countdown Label", theme, Vector2.one, new Vector2(0.5f, 1f), out minimapLabelMaterial);
            minimapLabel.alignment = TextAlignmentOptions.Top; // the text hugs the top of its box, right under the minimap
            return true;
        }

        private TextMeshProUGUI NewLabel(string name, UiTheme theme, Vector2 anchor, Vector2 pivot, out Material material)
        {
            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(canvasGo.transform, false);
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            if (theme.font != null)
                text.font = theme.font; // before the material is touched (assigning a font swaps the material)
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            material = new Material(text.fontSharedMaterial);
            theme.ApplyHudTextStyle(material);
            text.fontSharedMaterial = material;
            RectTransform rect = text.rectTransform;
            rect.sizeDelta = new Vector2(400f, 80f);
            rect.pivot = pivot;
            rect.anchorMin = rect.anchorMax = anchor;
            go.SetActive(false);
            return text;
        }
    }
}
