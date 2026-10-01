using Overpower.Data;
using Overpower.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overpower.Vision
{
    /// <summary>
    /// The countdown to the next centre scan, above the centre tower for everyone (Tudor 2026-10-01: "display the timer until
    /// the next scan above the tower"). A HUD label, not a world-space one: it is projected from a point above the tower
    /// onto its own screen-overlay canvas (the same pattern as DamageNumberView and MarkIndicatorView), so it keeps one
    /// size on screen however far the camera is zoomed out. The canvas has no GraphicRaycaster, so it can never swallow a
    /// click (HANDOFF trap 20), sits behind the main HUD (order -20) and is not fogged. The font is UiTheme.font, the size
    /// UiTheme > Scan Countdown Font Size, the colour the scan wave's red.
    ///
    /// LOOK CHOICE: it reads "Scan 12", whole seconds counting down, and keeps counting while a wave is travelling (so it
    /// is simply the time to the next wave: the full interval right as a wave starts). Hidden when CentreScan has no
    /// countdown (the map has shrunk, fog is off, not in a room), Scan Countdown Visible is off, or the anchor is behind
    /// the camera. The wording and size are on the UiTheme; the height and on/off are on the Vision Config.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after the camera has moved this frame
    public sealed class CentreScanCountdownView : MonoBehaviour
    {
        private const string FallbackFormat = "Scan {0}";

        private GameObject canvasGo;
        private RectTransform canvasRect;
        private TextMeshProUGUI label;
        private Material labelMaterial;
        private int shownSeconds = -1;
        private string format;

        /// <summary>What the label shows now, or empty while it is hidden (recorders read it).</summary>
        public string ShownText => label != null && label.gameObject.activeSelf ? label.text : string.Empty;

        private void OnDestroy()
        {
            if (canvasGo != null)
                Destroy(canvasGo);
            if (labelMaterial != null)
                Destroy(labelMaterial);
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

            Vector3 screenPoint = cam.WorldToScreenPoint(scan.CountdownPosition);
            if (screenPoint.z < 0f)
            {
                Hide();
                return;
            }

            if (format == null)
                format = !string.IsNullOrEmpty(theme.scanCountdownFormat) ? theme.scanCountdownFormat : FallbackFormat;
            if (scan.CountdownSeconds != shownSeconds)
            {
                shownSeconds = scan.CountdownSeconds;
                label.text = string.Format(format, shownSeconds); // once a second, so the allocation does not matter
            }
            label.fontSize = theme.scanCountdownFontSize;
            label.color = config.ScanWaveColour;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 local);
            label.rectTransform.anchoredPosition = local;
            if (!label.gameObject.activeSelf)
                label.gameObject.SetActive(true);
        }

        private void Hide()
        {
            shownSeconds = -1;
            if (label != null && label.gameObject.activeSelf)
                label.gameObject.SetActive(false);
        }

        // The wording, font and size live in the UiTheme the player prefab already carries (one home for text).
        private static UiTheme LookUpTheme()
        {
            TeamSight sight = TeamSight.Local;
            PlayerHealth health = sight != null ? sight.GetComponent<PlayerHealth>() : null;
            return health != null ? health.Theme : null;
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
            canvas.sortingOrder = -20;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = theme.referenceResolution;
            scaler.matchWidthOrHeight = theme.matchWidthOrHeight;
            // No GraphicRaycaster: nothing here is clickable.
            canvasRect = canvasGo.GetComponent<RectTransform>();

            GameObject go = TMP_DefaultControls.CreateText(new TMP_DefaultControls.Resources());
            go.name = "Countdown Label";
            go.transform.SetParent(canvasGo.transform, false);
            label = go.GetComponent<TextMeshProUGUI>();
            if (theme.font != null)
                label.font = theme.font; // before the material is touched (assigning a font swaps the material)
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            labelMaterial = new Material(label.fontSharedMaterial);
            theme.ApplyHudTextStyle(labelMaterial);
            label.fontSharedMaterial = labelMaterial;
            RectTransform rect = label.rectTransform;
            rect.sizeDelta = new Vector2(400f, 80f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            label.gameObject.SetActive(false);
            return true;
        }
    }
}
