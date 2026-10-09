using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Combat;

namespace Overpower.UI
{
    /// <summary>
    /// Draws a diamond over every enemy the LOCAL player has marked. Shooter-only by construction: it subscribes to
    /// CombatEvents.LocalMarkReported, which fires only on the machine holding the mark (PlayerCombatCredit.
    /// RPC_DamageCredit is a TARGETED RPC), and PlayerHud disables itself on a non-owner. The state is the victim's
    /// truth (markSecondsLeft in every credit message), never a guess from the beam: it appears with a fresh mark and
    /// hides on a cash-in, expiry or death report (markSecondsLeft 0). One slot per target keyed by Transform, from
    /// a pool of PoolSize.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class MarkIndicatorView : MonoBehaviour
    {
        /// <summary>One slot per possible enemy with a live mark from YOU; a code constant, not a look value
        /// (Rule 10). 8 covers a room (at most one live mark per attacker per target).</summary>
        public const int PoolSize = 8;

        private struct Slot
        {
            public Image image;
            public RectTransform rect;
            public bool active;
            public Transform target;
            public float expiresAt; // Time.time this slot's own diamond goes fully hidden.
            public float total;     // secondsLeft AT THE REPORT THAT (RE)STARTED this slot - Alpha's fade ramp.
        }

        private UiTheme theme;
        private RectTransform canvasRect;
        private Slot[] slots;

        // Keyed by victim. HandleMarkReported never claims a second slot for a target that has one, so every
        // active slot's target is the one activeByTarget points at: no aliasing to guard against on eviction.
        private readonly Dictionary<Transform, int> activeByTarget = new Dictionary<Transform, int>();

        public static MarkIndicatorView Create(Transform owner, UiTheme theme, RectTransform canvasRect, Image[] diamonds)
        {
            var go = new GameObject("Mark Indicator View", typeof(RectTransform));
            go.transform.SetParent(owner, false);

            var view = go.AddComponent<MarkIndicatorView>();
            view.theme = theme;
            view.canvasRect = canvasRect;
            view.slots = new Slot[diamonds.Length];
            for (int i = 0; i < diamonds.Length; i++)
            {
                view.slots[i].image = diamonds[i];
                view.slots[i].rect = diamonds[i].rectTransform;
            }
            return view;
        }

        private void OnEnable() => CombatEvents.LocalMarkReported += HandleMarkReported;
        private void OnDisable() => CombatEvents.LocalMarkReported -= HandleMarkReported;

        private void HandleMarkReported(Transform victim, float secondsLeft)
        {
            if (victim == null)
                return;

            // 0 covers a cash-in, an expiry the victim's client already knows about, and a death report
            // (marks clear before the death flush): all three hide the diamond if one is showing.
            if (secondsLeft <= 0f)
            {
                if (activeByTarget.TryGetValue(victim, out int existing))
                    FreeSlot(existing);
                return;
            }

            int slot = activeByTarget.TryGetValue(victim, out int found) ? found : ClaimSlot(victim);
            slots[slot].active = true;
            slots[slot].target = victim;
            slots[slot].expiresAt = Time.time + secondsLeft;
            slots[slot].total = secondsLeft;
            slots[slot].image.gameObject.SetActive(true);
            activeByTarget[victim] = slot;
        }

        /// <summary>A free slot, else the one closest to its own expiry (DamageNumberView's "steal the least
        /// valuable", keyed by time-to-live).</summary>
        private int ClaimSlot(Transform newTarget)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].active)
                    return i;
            }

            int soonest = 0;
            float soonestExpiry = slots[0].expiresAt;
            for (int i = 1; i < slots.Length; i++)
            {
                if (slots[i].expiresAt < soonestExpiry)
                {
                    soonest = i;
                    soonestExpiry = slots[i].expiresAt;
                }
            }

            activeByTarget.Remove(slots[soonest].target);
            return soonest;
        }

        private void FreeSlot(int i)
        {
            activeByTarget.Remove(slots[i].target);
            slots[i].active = false;
            slots[i].target = null;
            slots[i].image.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].active)
                    continue;

                // Unity's == reads a destroyed-but-not-yet-collected Transform (a victim who left the room) as
                // null; Slot.active, not this check, tells "already free" from "just went stale" (see
                // DamageNumberView.Slot.active).
                if (slots[i].target == null || Time.time >= slots[i].expiresAt)
                {
                    FreeSlot(i);
                    continue;
                }

                if (cam == null)
                {
                    slots[i].image.gameObject.SetActive(false);
                    continue;
                }

                Vector3 worldAnchor = slots[i].target.position + Vector3.up * theme.markIndicatorAnchorHeight;
                Vector3 screenPoint = cam.WorldToScreenPoint(worldAnchor);
                if (screenPoint.z < 0f)
                {
                    slots[i].image.gameObject.SetActive(false);
                    continue;
                }

                if (!slots[i].image.gameObject.activeSelf)
                    slots[i].image.gameObject.SetActive(true);

                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 local);
                slots[i].rect.anchoredPosition = local;

                float secondsLeft = slots[i].expiresAt - Time.time;
                float alpha = MarkIndicatorRule.Alpha(secondsLeft, slots[i].total, theme.markIndicatorMinAlpha, theme.markIndicatorPulseSpeed, Time.time);
                Color c = slots[i].image.color;
                c.a = alpha;
                slots[i].image.color = c;
            }
        }
    }
}
