using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// The marked player's OWN diamond over their own head while ANY attacker's mark on them is live; at most one
    /// diamond, since PlayerHealth.LongestMarkSecondsLeft already gives the longest live mark (see
    /// MarkLedger.LongestSecondsLeft). Owner-only and needs no network message: marks are victim-side, so this is a
    /// per-frame POLL of a value already correct on this machine. PlayerHud builds it and disables itself on a
    /// non-owner before BuildUi, so it is never instantiated anywhere else. It hides the instant the ledger clears
    /// (cash-in, death, respawn, match start; MarkLedger.Clear is synchronous), the next LateUpdate polls 0.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class SelfMarkIndicatorView : MonoBehaviour
    {
        private PlayerHealth playerHealth;
        private UiTheme theme;
        private RectTransform canvasRect;
        private Image image;
        private RectTransform rect;

        // See MarkIndicatorRule.TrackedTotal: LongestMarkSecondsLeft has no "how long was this mark's window",
        // only "how much is left", so this tracks the highest value polled since the diamond was last fully
        // hidden and hands that to Alpha as the fade ramp's total.
        private float trackedTotal;

        public static SelfMarkIndicatorView Create(Transform owner, PlayerHealth playerHealth, UiTheme theme, RectTransform canvasRect, Image diamond)
        {
            var go = new GameObject("Self Mark Indicator View", typeof(RectTransform));
            go.transform.SetParent(owner, false);

            var view = go.AddComponent<SelfMarkIndicatorView>();
            view.playerHealth = playerHealth;
            view.theme = theme;
            view.canvasRect = canvasRect;
            view.image = diamond;
            view.rect = diamond.rectTransform;
            return view;
        }

        private void LateUpdate()
        {
            if (playerHealth == null)
                return;

            float secondsLeft = playerHealth.LongestMarkSecondsLeft;
            trackedTotal = MarkIndicatorRule.TrackedTotal(trackedTotal, secondsLeft);

            Camera cam = Camera.main;
            if (secondsLeft <= 0f || cam == null)
            {
                image.gameObject.SetActive(false);
                return;
            }

            // transform is parented to `owner` at local zero (Create's SetParent(owner, false)), so it sits at the
            // local player's root, the same anchor style MarkIndicatorView uses for an enemy.
            Vector3 worldAnchor = transform.position + Vector3.up * theme.markIndicatorAnchorHeight;
            Vector3 screenPoint = cam.WorldToScreenPoint(worldAnchor);
            if (screenPoint.z < 0f)
            {
                image.gameObject.SetActive(false);
                return;
            }

            if (!image.gameObject.activeSelf)
                image.gameObject.SetActive(true);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 local);
            rect.anchoredPosition = local;

            float alpha = MarkIndicatorRule.Alpha(secondsLeft, trackedTotal, theme.markIndicatorMinAlpha, theme.markIndicatorPulseSpeed, Time.time);
            Color c = image.color;
            c.a = alpha;
            image.color = c;
        }
    }
}
