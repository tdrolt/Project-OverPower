using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Overpower.Combat;

namespace Overpower.UI
{
    /// <summary>
    /// Draws a diamond over every enemy the LOCAL player currently has marked - shooter-only by
    /// construction: it subscribes to CombatEvents.LocalMarkReported, which only ever fires on the
    /// machine that actually holds the mark (PlayerCombatCredit.RPC_DamageCredit is a TARGETED RPC,
    /// delivered only to the one attacker it names - see that method's own class comment - and
    /// DummyTarget's single-client raise needs no such guard at all). A teammate or a bystander never
    /// receives that RPC in the first place, so their own copy of this view (on their own PlayerHud,
    /// since PlayerHud disables itself entirely for a non-owner - PlayerHud.Awake's very first check)
    /// never even hears about the mark, regardless of what it shows on screen: there is nothing to see
    /// here except your own marks.
    ///
    /// Comes from the victim's own truth (every credit message carries markSecondsLeft for the sending
    /// attacker - PlayerHealth.MarkSecondsLeftFor), never a guess from the shooter's own beam: the
    /// diamond appears the frame the credit message reports a fresh mark and disappears the frame a
    /// cash-in, an expiry, or a death report (markSecondsLeft 0, sent "always" after the sender check)
    /// arrives - see CombatEvents.LocalMarkReported's own comment.
    ///
    /// One slot per target, keyed by Transform (mirrors DamageNumberView's own activeByTarget), and a
    /// pool of PoolSize covers "every enemy you personally have marked at once" the same way that
    /// class's own pool covers "every enemy you're currently damaging".
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class MarkIndicatorView : MonoBehaviour
    {
        /// <summary>One slot per possible enemy with a live mark from YOU - a designer never tunes this
        /// (Rule 10: pool sizes are code constants, not look values). 8 covers a full room's worth of
        /// enemies (this project's rooms cap well under that) with no reasonable way to have more than
        /// one live mark per enemy anyway (Decision 1: at most one mark per attacker per target).</summary>
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

        // Keyed by victim: which slot (if any) currently shows YOUR mark on them. At most one entry per
        // target always exists here (HandleMarkReported below never claims a second slot for a target
        // that already has one), so - unlike DamageNumberView's Blocked-vs-real split - every active
        // slot's own target is always the one activeByTarget itself points at; there is no aliasing case
        // to guard against on eviction here.
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

            // 0 covers a cash-in, an expiry the victim's own client already knows about, and a death
            // report (Decision 8: marks clear before the death flush) - all three simply hide the
            // diamond if this target happened to have one showing.
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

        /// <summary>A free slot if one exists, otherwise the one closest to its own expiry - the same
        /// "steal the least valuable" fallback DamageNumberView's own ClaimSlot uses, just keyed by
        /// time-to-live instead of last-hit-time (there is no "how recently was this touched" here,
        /// only "how much longer does it have").</summary>
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

                // Unity's == overload reads a destroyed-but-not-yet-collected Transform as null (a
                // victim who left the room) - see DamageNumberView.Slot.active's own comment for why
                // Slot.active, not this check, is what tells "already free" apart from "just went
                // stale", and FreeSlot is what actually frees a slot either way.
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
