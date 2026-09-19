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
            public Transform target;       // null when this slot is free.
            public Vector3 anchorOffset;   // World-space offset from target.position - the latest impact.
            public float lastHitTime;      // Time.time of the most recent hit added to this number.
            public float popTime;          // Time.time this label was last (re)popped.
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
                slots[existing].popTime = Time.time;
                ApplyLabel(existing);
                return;
            }

            int slot = ClaimSlot(victim);
            slots[slot].target = victim;
            slots[slot].anchorOffset = anchorOffset;
            slots[slot].amount = amount;
            slots[slot].marked = cashedMark;
            slots[slot].lastHitTime = Time.time;
            slots[slot].popTime = Time.time;
            slots[slot].label.gameObject.SetActive(true);
            ApplyLabel(slot);
            activeByTarget[victim] = slot;
        }

        private Vector3 ResolveAnchorOffset(Transform victim)
        {
            if (lastImpactByTarget.TryGetValue(victim, out ImpactRecord impact) && Time.time - impact.seenTime <= ImpactFreshnessSeconds)
                return impact.offset;

            return Vector3.up * theme.damageNumberAnchorHeight;
        }

        private void ApplyLabel(int slot)
        {
            slots[slot].label.SetText("{0:0}", (float)DamageNumberMotion.Shown(slots[slot].amount));
            slots[slot].label.color = slots[slot].marked ? theme.markColor : theme.damageNumberColor;
        }

        /// <summary>A free slot if one exists, otherwise the least-recently-popped slot in use - the
        /// same "steal the oldest" fallback a pool of 24 rarely needs, but must never allocate to
        /// resolve.</summary>
        private int ClaimSlot(Transform newTarget)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].target == null)
                    return i;
            }

            int oldest = 0;
            float oldestPop = slots[0].popTime;
            for (int i = 1; i < slots.Length; i++)
            {
                if (slots[i].popTime < oldestPop)
                {
                    oldest = i;
                    oldestPop = slots[i].popTime;
                }
            }

            activeByTarget.Remove(slots[oldest].target);
            return oldest;
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].target == null)
                    continue;

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

                float popAge = Time.time - slots[i].popTime;
                DamageNumberPose pose = DamageNumberMotion.Evaluate(ageSinceLastHit, popAge, theme.damageNumberHoldSeconds,
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
            activeByTarget.Remove(slots[i].target);
            slots[i].target = null;
            slots[i].label.gameObject.SetActive(false);
        }
    }
}
