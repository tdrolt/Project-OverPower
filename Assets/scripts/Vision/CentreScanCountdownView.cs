using Overpower.Data;
using Overpower.UI;
using TMPro;
using UnityEngine;

namespace Overpower.Vision
{
    /// <summary>
    /// The countdown to the next centre scan, floating above the centre tower for everyone (Tudor 2026-10-01: "display the
    /// timer until the next scan above the tower"). A world-space TextMeshPro (not a canvas, so there is no
    /// GraphicRaycaster to swallow a click, HANDOFF trap 20), in the scan wave's red, turned to face the camera every frame
    /// so it reads from the top-down view. TextMeshPro's text shader is a transparent one, and the fog pass runs before the
    /// transparents, so the label is drawn over the fog and is never darkened by it.
    ///
    /// LOOK CHOICE: it reads "Scan 12", whole seconds counting down, and keeps counting while a wave is travelling (so it
    /// is simply the time to the next wave: the full interval right as a wave starts). Hidden when CentreScan has no
    /// countdown (the map has shrunk, fog is off, not in a room) or Scan Countdown Visible is off. The wording is
    /// UiTheme › Scan Countdown Format; the height and size are on the Vision Config.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after the camera has moved this frame
    public sealed class CentreScanCountdownView : MonoBehaviour
    {
        private const string FallbackFormat = "Scan {0}";
        /// <summary>Thickness of the label's dark edge (TextMeshPro's 0..1 scale); a look, not a gameplay value.</summary>
        private const float OutlineWidth = 0.3f;

        private TextMeshPro label;
        private int shownSeconds = -1;
        private string format;

        /// <summary>What the label shows now, or empty while it is hidden (recorders read it).</summary>
        public string ShownText => label != null && label.enabled ? label.text : string.Empty;

        private void Awake()
        {
            // At the scene root: this object's scale must not shrink the text.
            var go = new GameObject("Centre Scan Countdown (cosmetic only)");
            label = go.AddComponent<TextMeshPro>();
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            // A dark edge, so the red text still reads against a red-tinted tower or a bright floor.
            label.outlineColor = new Color32(0, 0, 0, 255);
            label.outlineWidth = OutlineWidth;
            label.enabled = false;
        }

        private void OnDestroy()
        {
            if (label != null)
                Destroy(label.gameObject);
        }

        private void LateUpdate()
        {
            CentreScan scan = CentreScan.Instance;
            Camera cam = Camera.main;
            VisionConfig config = scan != null ? scan.Config : null;
            if (config == null || cam == null || !config.ScanCountdownVisible || scan.CountdownSeconds < 0)
            {
                if (label.enabled)
                    label.enabled = false;
                shownSeconds = -1;
                return;
            }

            if (format == null)
                format = LookUpFormat();
            if (scan.CountdownSeconds != shownSeconds)
            {
                shownSeconds = scan.CountdownSeconds;
                label.text = string.Format(format, shownSeconds); // once a second, so the allocation does not matter
            }
            label.fontSize = config.ScanCountdownFontSize;
            label.color = config.ScanWaveColour;
            label.transform.position = scan.CountdownPosition;
            label.transform.rotation = cam.transform.rotation;
            if (!label.enabled)
                label.enabled = true;
        }

        // The wording lives in the UiTheme the player prefab already carries (one home for text).
        private static string LookUpFormat()
        {
            TeamSight sight = TeamSight.Local;
            PlayerHealth health = sight != null ? sight.GetComponent<PlayerHealth>() : null;
            UiTheme theme = health != null ? health.Theme : null;
            return theme != null && !string.IsNullOrEmpty(theme.scanCountdownFormat) ? theme.scanCountdownFormat : FallbackFormat;
        }
    }
}
