using UnityEngine;
using UnityEngine.UI;

namespace Overpower.UI
{
    /// <summary>
    /// Tudor's answer 1 on the mark plan (the table at the top of the plan): "only the player that
    /// applied it and the player it has been applied to should see the mark" - this is the second half
    /// of that, the marked player's OWN diamond over their OWN head, while ANY attacker's mark on them
    /// is live. At most one diamond ever, no matter how many attackers currently have this player
    /// marked (Decision 1 lets each attacker hold their own independent mark) - PlayerHealth.
    /// LongestMarkSecondsLeft already answers "the longest live mark, whoever placed it", so showing
    /// exactly that one value is enough (see MarkLedger.LongestSecondsLeft's own comment).
    ///
    /// Owner-only by construction, and it needs no network message at all, unlike MarkIndicatorView's
    /// shooter-side diamonds: the victim's own client already holds its own MarkLedger (mark step 4 -
    /// damage, and so marks, are victim-side), so this is a plain per-frame POLL of a value that is
    /// already exactly correct on THIS machine, not an event subscriber waiting on a round trip. Built
    /// by PlayerHud alongside MarkIndicatorView, and PlayerHud disables itself entirely before BuildUi
    /// ever runs on a non-owner's copy (PlayerHud.Awake's very first check) - so this component, like
    /// every other piece of the hit-feedback canvas, is never even INSTANTIATED except on the marked
    /// player's own machine. A teammate or a third party has no copy of it to read from, because there
    /// is no copy: nothing here is replicated, and nothing here is built anywhere but the owner's own
    /// client.
    ///
    /// Must hide the instant the ledger clears - a cash-in, a death, a respawn, or the 2.7b match-start
    /// fresh start - which LongestMarkSecondsLeft already reflects immediately (MarkLedger.Clear runs
    /// synchronously in all four cases; see PlayerHealth.ApplyDamage/ResetForRespawn), so the very next
    /// LateUpdate's poll reads 0 and hides on its own with no extra wiring.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class SelfMarkIndicatorView : MonoBehaviour
    {
        private PlayerHealth playerHealth;
        private UiTheme theme;
        private RectTransform canvasRect;
        private Image image;
        private RectTransform rect;

        // See MarkIndicatorRule.TrackedTotal's own comment: LongestMarkSecondsLeft has no "how long was
        // this mark's own window" to read, only "how much is left right now", so this view tracks the
        // highest value it has polled since the diamond was last fully hidden and hands THAT to Alpha
        // as the fade ramp's own total.
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

            // transform here is THIS component's own - parented to `owner` at local position zero
            // (Create's SetParent(owner, false)), so its world position always equals the local
            // player's own root position, the same anchor style MarkIndicatorView uses for an enemy.
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
