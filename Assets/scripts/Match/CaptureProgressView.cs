using Photon.Pun;
using RhinoGame;
using UnityEngine;
using UnityEngine.UI;
using Overpower.UI;

namespace Overpower.Match
{
    /// <summary>
    /// The small world-space bar shown above a tower while its CaptureProgress is not Idle -
    /// created in code by BuildingCapture.Start (Create below) so every tower gets one for free,
    /// tower 9 and any future tower included, with nothing to wire up by hand in the scene.
    ///
    /// Every client (not just the master) calls Refresh every frame with whatever
    /// BuildingManager.CaptureProgressOf(zone) currently reads, and this view extrapolates the fill
    /// itself from PhotonNetwork.ServerTimestamp via CaptureProgress.Evaluate - the whole point of
    /// CaptureProgress is that one room-property publish keeps every screen's bar moving smoothly
    /// for seconds with no further network traffic.
    ///
    /// Deliberately carries no GraphicRaycaster and both Images have raycastTarget = false - the
    /// same fix PlayerHealth.ApplyTheme documents for the player's own overhead bar: a world-space
    /// canvas with a raycaster falls back to Camera.main, which makes PlayerInputRouter treat the
    /// cursor as "over UI" (and swallow a shot) the instant it crosses a tower's bar - a previous
    /// regression on the player bar that this view must not repeat.
    /// </summary>
    public sealed class CaptureProgressView : MonoBehaviour
    {
        private UiTheme theme;
        private Canvas canvas;
        private Image fillImage;
        private bool visible;

        /// <summary>Builds the bar as a child of a tower's own transform, offset upward by
        /// Capture Bar Height Offset. Called once from BuildingCapture.Start.</summary>
        public static CaptureProgressView Create(Transform parent, UiTheme theme)
        {
            var root = new GameObject("Capture Progress Bar", typeof(RectTransform));
            root.transform.SetParent(parent, false);

            // Two of the ten towers in the scene are plain scene objects scaled to fit their
            // footprint (measured: every tower currently sits at a uniform 0.8 lossy scale, but
            // nothing guarantees that stays uniform as more towers are added) rather than unscaled
            // prefab instances. Theme.captureBarWidth/Height/HeightOffset are documented as real
            // world units (metres) - without compensating for the parent's own scale here, a
            // shrunk tower would silently shrink its bar and lower its height too, which is not
            // what "world units" promises a designer tuning those numbers. Dividing out the
            // parent's lossy scale on both the offset and this object's own local scale cancels it,
            // so the bar's rendered size and height are the same real-world metres on every tower
            // regardless of how that tower's own transform happens to be scaled.
            Vector3 parentScale = parent.lossyScale;
            Vector3 scaleCompensation = new Vector3(
                Mathf.Approximately(parentScale.x, 0f) ? 1f : 1f / parentScale.x,
                Mathf.Approximately(parentScale.y, 0f) ? 1f : 1f / parentScale.y,
                Mathf.Approximately(parentScale.z, 0f) ? 1f : 1f / parentScale.z);
            root.transform.localPosition = new Vector3(0f, theme.captureBarHeightOffset * scaleCompensation.y, 0f);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = new Vector2(theme.captureBarWidth, theme.captureBarHeight);
            rootRect.localScale = scaleCompensation;

            // No GraphicRaycaster added here - see the class comment. scaleWithDistance stays OFF
            // (unlike the player's own HealthBarCanvas): that option makes UIBillboard overwrite
            // transform.localScale every frame from camera distance alone, which would silently
            // erase the parent-scale compensation above and shrink the bar to a fraction of a
            // millimetre at any normal viewing distance - measured directly (Task 2.1d review): at
            // 13m the bar's own corners collapsed to a 7cm x 0cm rectangle. A capture bar is meant
            // to be a real object at a real place in the world - like the tower it sits on, it
            // should get smaller with distance exactly like everything else, not hold a constant
            // apparent size the way a screen-adjacent nameplate does. UIBillboard is still needed
            // here for the rotation half of what it does - facing the camera - which is unaffected
            // by scaleWithDistance being off.
            root.AddComponent<UIBillboard>();

            GameObject trackGo = new GameObject("Track", typeof(RectTransform));
            trackGo.transform.SetParent(root.transform, false);
            RectTransform trackRect = trackGo.GetComponent<RectTransform>();
            trackRect.anchorMin = Vector2.zero;
            trackRect.anchorMax = Vector2.one;
            trackRect.offsetMin = Vector2.zero;
            trackRect.offsetMax = Vector2.zero;
            Image track = trackGo.AddComponent<Image>();
            track.sprite = theme.barSprite;
            track.color = theme.captureBarTrackColor;
            track.raycastTarget = false;

            GameObject fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(trackGo.transform, false);
            RectTransform fillRect = fillGo.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            Image fill = fillGo.AddComponent<Image>();
            fill.sprite = theme.barSprite;
            // Sprite assigned before Type/fillAmount matter, same order UiTheme's own comment on
            // barSprite warns about: a Filled Image with no sprite ignores fillAmount entirely.
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;
            fill.raycastTarget = false;

            CaptureProgressView view = root.AddComponent<CaptureProgressView>();
            view.theme = theme;
            view.canvas = canvas;
            view.fillImage = fill;
            view.SetVisible(false);
            return view;
        }

        /// <summary>Reads the current fill from the server clock and shows/hides/colours the bar.
        /// Called every frame, by every client, from BuildingCapture.Update.</summary>
        public void Refresh(CaptureProgress progress)
        {
            float fraction = progress.Evaluate(PhotonNetwork.ServerTimestamp);

            if (progress.Team < 0 || fraction <= 0f)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            fillImage.color = theme.ShotColorFor(progress.Team);
            fillImage.fillAmount = fraction;
        }

        // Guards against writing canvas.enabled (and so touching a Graphic) every single frame
        // while a bar sits fully hidden or fully shown - the same guarded-write habit
        // PlayerHealth/PlayerHud already use for their own bars.
        private void SetVisible(bool value)
        {
            if (visible == value) return;
            visible = value;
            canvas.enabled = value;
        }
    }
}
