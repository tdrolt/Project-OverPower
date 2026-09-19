using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.UI
{
    /// <summary>
    /// Draws every damage number on the shooter's own screen. Shooter-only by construction: it
    /// subscribes to CombatEvents.LocalHitReported/LocalImpactSeen, which only ever fire on the
    /// machine that dealt the damage (see that class's own comment) - a victim never sees a number
    /// for damage taken (Open #2's default, unchanged).
    ///
    /// Tudor's override on the Mark plan (top-of-plan table, answer 4): ONE LIVE NUMBER PER ENEMY.
    /// While you keep damaging the same target, each new report ADDS to that target's number, re-pops
    /// it and restarts its life; the number only starts to rise and fade damageNumberHoldSeconds after
    /// the LAST hit. This is why slots are keyed by target Transform (activeByTarget) rather than
    /// merged by a time window the way the plan's original design worked.
    ///
    /// Anchoring (answer 2): the number sits at the LATEST local impact point for that victim - the
    /// shooter's own client simulates its own shots against every copy it sees, so PlayerHealth/
    /// DummyTarget can tell this view exactly where a shot landed via LocalImpactSeen, well before the
    /// (possibly networked) LocalHitReported confirms how much it did. Kept as an OFFSET from the
    /// victim's own transform, so the number follows a moving target. A victim with no local impact
    /// younger than ImpactFreshnessSeconds (a burn tick, a fire field, a mine - anything not simulated
    /// on the shooter's own screen) falls back to its centre at damageNumberAnchorHeight.
    ///
    /// No allocation per hit: text goes through TMP's SetText("{0:0}", value) overload, never string
    /// interpolation or ToString; slots live in a plain array (Slot[], not List&lt;Slot&gt;) so
    /// per-field writes mutate in place; the only dictionary growth is bounded by how many distinct
    /// enemies you've hit (naturally small - a match has at most a handful of live opponents).
    /// </summary>
    // Review fix (steps 1-2): CameraTracking.LateUpdate moves the camera and has no execution order
    // relative to this class's own LateUpdate below, which projects through Camera.main - without an
    // explicit order, a number can read one frame's camera position against a different frame's
    // player position and wobble/lag while the shooter moves. 1000 puts this comfortably after any
    // ordinary gameplay script (including CameraTracking), so it always reads the camera's final
    // position for the frame. The same ordering will matter for the mark diamond view later.
    [DefaultExecutionOrder(1000)]
    public class DamageNumberView : MonoBehaviour
    {
        /// <summary>How many labels the pool holds - a designer never tunes this (Rule 10: pool sizes
        /// are code constants, not look values). 24 comfortably covers a 9-player match's every enemy
        /// hitting a number's life span at once, with headroom for a shotgun's own single merged label.</summary>
        public const int PoolSize = 24;

        // How long a LocalImpactSeen point stays "fresh" enough for a LocalHitReported to anchor to it
        // (answer 2: "older than ~1 s"). Not a designer field - it's a technical seam between two
        // events that may arrive a frame or two apart, not a look value.
        private const float ImpactFreshnessSeconds = 1f;

        private struct Slot
        {
            public TextMeshProUGUI label;
            public RectTransform rect;
            // Review fix (steps 1-2): whether this slot is claimed, tracked SEPARATELY from target -
            // `target == null` used to be the only "is this slot free" check, but Unity overloads `==`
            // so it also reads true for a DESTROYED-but-not-yet-collected Transform (a victim who left
            // the room). LateUpdate's own free check below used to rely on that same overload and so
            // never noticed the difference between "already free" and "just went stale", which left a
            // dead victim's label frozen on screen forever and its activeByTarget entry never removed.
            // `active` is a plain bool, immune to Unity's fake-null, so ClaimSlot can tell a truly free
            // slot from one whose target merely died - which is now caught explicitly in LateUpdate.
            public bool active;
            public Transform target;       // Meaningful only while active - see FreeSlot.
            public Vector3 anchorOffset;   // World-space offset from target.position - the latest impact.
            public float lastHitTime;      // Time.time of the most recent hit added to this number - also this label's own last (re)pop time, since every write to one is paired with the other (see ApplyLabel's callers).
            public float amount;           // Running total shown.
            public bool marked;
        }

        private UiTheme theme;
        private RectTransform canvasRect;
        private Slot[] slots;

        // Keyed by victim: which slot (if any) is currently showing their running total. Bounded by
        // "distinct enemies hit recently", not by hit count.
        private readonly Dictionary<Transform, int> activeByTarget = new Dictionary<Transform, int>();

        // Keyed by victim: the latest impact point seen for them, whether or not a number is currently
        // live for them yet - LocalImpactSeen can (and usually does) arrive before the matching
        // LocalHitReported. Cleared of anything stale lazily, on read, never scanned proactively.
        private readonly Dictionary<Transform, ImpactRecord> lastImpactByTarget = new Dictionary<Transform, ImpactRecord>();

        private struct ImpactRecord
        {
            public Vector3 offset;
            public float seenTime;
        }

        public static DamageNumberView Create(Transform owner, UiTheme theme, RectTransform canvasRect, TextMeshProUGUI[] labels)
        {
            var go = new GameObject("Damage Number View", typeof(RectTransform));
            go.transform.SetParent(owner, false);

            var view = go.AddComponent<DamageNumberView>();
            view.theme = theme;
            view.canvasRect = canvasRect;
            view.slots = new Slot[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                view.slots[i].label = labels[i];
                view.slots[i].rect = labels[i].rectTransform;
            }
            return view;
        }

        private void OnEnable()
        {
            CombatEvents.LocalHitReported += HandleHitReported;
            CombatEvents.LocalImpactSeen += HandleImpactSeen;
        }

        private void OnDisable()
        {
            CombatEvents.LocalHitReported -= HandleHitReported;
            CombatEvents.LocalImpactSeen -= HandleImpactSeen;
        }

        private void HandleImpactSeen(Transform victim, Vector3 hitPoint)
        {
            if (victim == null)
                return;

            lastImpactByTarget[victim] = new ImpactRecord { offset = hitPoint - victim.position, seenTime = Time.time };
        }

        private void HandleHitReported(Transform victim, float amount, bool cashedMark)
        {
            if (!theme.showDamageNumbers || victim == null || amount <= 0f)
                return;

            Vector3 anchorOffset = ResolveAnchorOffset(victim);

            if (activeByTarget.TryGetValue(victim, out int existing))
            {
                slots[existing].amount += amount;
                slots[existing].marked |= cashedMark;
                slots[existing].anchorOffset = anchorOffset;
                slots[existing].lastHitTime = Time.time;
                ApplyLabel(existing);
                return;
            }

            int slot = ClaimSlot(victim);
            slots[slot].active = true;
            slots[slot].target = victim;
            slots[slot].anchorOffset = anchorOffset;
            slots[slot].amount = amount;
            slots[slot].marked = cashedMark;
            slots[slot].lastHitTime = Time.time;
            slots[slot].label.gameObject.SetActive(true);
            ApplyLabel(slot);
            activeByTarget[victim] = slot;
        }

        private Vector3 ResolveAnchorOffset(Transform victim)
        {
            if (lastImpactByTarget.TryGetValue(victim, out ImpactRecord impact))
            {
                if (Time.time - impact.seenTime <= ImpactFreshnessSeconds)
                    return impact.offset;

                // Review fix (steps 1-2): the class comment always claimed stale entries were "cleared
                // lazily, on read" but nothing ever actually removed one - a teammate or a leaver you
                // once hit would sit in this dictionary forever, just permanently too old to be used.
                lastImpactByTarget.Remove(victim);
            }

            return Vector3.up * theme.damageNumberAnchorHeight;
        }

        private void ApplyLabel(int slot)
        {
            slots[slot].label.SetText("{0:0}", (float)DamageNumberMotion.Shown(slots[slot].amount));
            slots[slot].label.color = slots[slot].marked ? theme.markColor : theme.damageNumberColor;
        }

        /// <summary>A free slot if one exists, otherwise the least-recently-popped slot in use - the
        /// same "steal the oldest" fallback a pool of 24 rarely needs, but must never allocate to
        /// resolve. Checks Slot.active, not target == null - see that field's own comment.</summary>
        private int ClaimSlot(Transform newTarget)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].active)
                    return i;
            }

            int oldest = 0;
            float oldestPop = slots[0].lastHitTime;
            for (int i = 1; i < slots.Length; i++)
            {
                if (slots[i].lastHitTime < oldestPop)
                {
                    oldest = i;
                    oldestPop = slots[i].lastHitTime;
                }
            }

            ReleaseTarget(slots[oldest].target);
            return oldest;
        }

        /// <summary>The bookkeeping a target's number no longer owns a slot needs, shared by FreeSlot
        /// (a number that finished its life) and ClaimSlot (a slot forcibly stolen from a still-live
        /// number to serve a new target) - review fix, steps 1-2: ClaimSlot used to remove only
        /// activeByTarget and leave lastImpactByTarget growing exactly the way ResolveAnchorOffset's
        /// own fix above addresses for the read path.</summary>
        private void ReleaseTarget(Transform target)
        {
            activeByTarget.Remove(target);
            lastImpactByTarget.Remove(target);
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].active)
                    continue;

                // Review fix (steps 1-2): Unity overloads Transform's == so this is true for a
                // DESTROYED victim (a player who left mid-number), not only a real null. The OLD check
                // here doubled as "already free", so a destroyed target's slot was silently skipped
                // forever instead of ever being freed - its label froze on screen at its last pose and
                // its activeByTarget entry never got removed. Free it properly, now that it is caught.
                if (slots[i].target == null)
                {
                    FreeSlot(i);
                    continue;
                }

                float ageSinceLastHit = Time.time - slots[i].lastHitTime;
                if (ageSinceLastHit - theme.damageNumberHoldSeconds >= theme.damageNumberLifetimeSeconds)
                {
                    FreeSlot(i);
                    continue;
                }

                if (cam == null)
                {
                    slots[i].label.gameObject.SetActive(false);
                    continue;
                }

                Vector3 worldAnchor = slots[i].target.position + slots[i].anchorOffset;
                Vector3 screenPoint = cam.WorldToScreenPoint(worldAnchor);
                if (screenPoint.z < 0f)
                {
                    slots[i].label.gameObject.SetActive(false);
                    continue;
                }

                if (!slots[i].label.gameObject.activeSelf)
                    slots[i].label.gameObject.SetActive(true);

                RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, null, out Vector2 local);

                // Evaluate's popAge and ageSinceLastHit are the same clock here: a slot's "pop" and its
                // "last hit" are always written together (see HandleHitReported), so there was never a
                // case where a separate popTime field actually differed from lastHitTime - review fix
                // (steps 1-2), dropped as trivial once traced through.
                DamageNumberPose pose = DamageNumberMotion.Evaluate(ageSinceLastHit, ageSinceLastHit, theme.damageNumberHoldSeconds,
                    theme.damageNumberLifetimeSeconds, theme.damageNumberPopSeconds, theme.damageNumberPopScale,
                    theme.damageNumberRise, theme.damageNumberFadeStart, theme.damageNumberMarkedScale, slots[i].marked);

                slots[i].rect.anchoredPosition = local + theme.damageNumberScreenOffset + new Vector2(0f, pose.Rise);
                slots[i].rect.localScale = Vector3.one * pose.Scale;

                Color color = slots[i].label.color;
                color.a = pose.Alpha;
                slots[i].label.color = color;
            }
        }

        private void FreeSlot(int i)
        {
            ReleaseTarget(slots[i].target);
            slots[i].active = false;
            slots[i].target = null;
            slots[i].label.gameObject.SetActive(false);
        }
    }
}
