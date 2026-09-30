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
            // Mark plan step 4, Tudor's answer 7: true for a one-off "Blocked" pop (HandleBlockedSeen)
            // rather than a running damage total. A blocked slot is never registered in
            // activeByTarget, so ApplyLabel (which redraws from amount/marked) is never called on it
            // again after it is claimed - it just rides LateUpdate's ordinary hold/rise/fade like any
            // other slot, with its text and colour set once, up front.
            public bool blocked;
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
            CombatEvents.LocalBlockedSeen += HandleBlockedSeen;
        }

        private void OnDisable()
        {
            CombatEvents.LocalHitReported -= HandleHitReported;
            CombatEvents.LocalImpactSeen -= HandleImpactSeen;
            CombatEvents.LocalBlockedSeen -= HandleBlockedSeen;
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
            slots[slot].blocked = false; // In case this slot was stolen from an old "Blocked" pop.
            slots[slot].lastHitTime = Time.time;
            slots[slot].label.gameObject.SetActive(true);
            ApplyLabel(slot);
            activeByTarget[victim] = slot;
        }

        /// <summary>Tudor's answer 7 ("Blocked"), option (b): a one-off pop at the impact, never a
        /// running total - deliberately NOT looked up in or added to activeByTarget (see Slot.blocked),
        /// so a later real hit on the same victim always starts its own fresh number rather than
        /// inheriting this slot's zero amount or grey colour.
        ///
        /// Review fix (opus review, mark steps 3-4): a fast weapon (SMG Double Rate, a shotgun's own
        /// pellet spread) can raise this many times against the SAME shielded victim in a single burst -
        /// refresh the one Blocked pop already showing for them instead of claiming a fresh slot per
        /// projectile, which used to stack up to a whole pellet spread's worth of "Blocked" labels on
        /// one enemy and could fill the entire 24-slot pool. FindActiveBlockedSlot is a linear scan, not
        /// a second dictionary: Blocked pops are rare (only while a victim's shield immunity is up), so
        /// a 24-slot scan costs nothing, and a second Dictionary&lt;Transform,int&gt; just for this would
        /// have to stay in lockstep with slots[] every time a Blocked slot got evicted for something
        /// else.</summary>
        private void HandleBlockedSeen(Transform victim)
        {
            if (!theme.showDamageNumbers || victim == null)
                return;

            int existing = FindActiveBlockedSlot(victim);
            int slot = existing >= 0 ? existing : ClaimSlot(victim);
            slots[slot].active = true;
            slots[slot].target = victim;
            slots[slot].anchorOffset = ResolveAnchorOffset(victim); // Refreshed even for a reused slot.
            slots[slot].amount = 0f;
            slots[slot].marked = false;
            slots[slot].blocked = true;
            slots[slot].lastHitTime = Time.time; // Refreshed even for a reused slot - restarts its own hold/fade life.
            slots[slot].label.gameObject.SetActive(true);
            slots[slot].label.SetText(theme.blockedText);
            slots[slot].label.color = theme.blockedColor;
        }

        /// <summary>-1 if `victim` has no active Blocked pop right now. Never matches a REAL number's
        /// slot (Slot.blocked is false for those), so this can never accidentally hand a running total
        /// over to HandleBlockedSeen to overwrite.</summary>
        private int FindActiveBlockedSlot(Transform victim)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].active && slots[i].blocked && slots[i].target == victim)
                    return i;
            }
            return -1;
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

            // Review fix (opus review, mark steps 3-4): only release THIS slot's own registration -
            // see OwnsRegistration's own comment for the bug this guards against.
            if (OwnsRegistration(oldest, slots[oldest].target))
                ReleaseTarget(slots[oldest].target);
            return oldest;
        }

        /// <summary>Review fix (opus review, mark steps 3-4): true only when SLOT slotIndex is the one
        /// activeByTarget currently credits with `target`'s running number - false for a "Blocked" pop
        /// (HandleBlockedSeen never registers one - see its own comment) that merely happens to share a
        /// victim with a real, still-live number sitting in a DIFFERENT slot. Bug this fixes: FreeSlot
        /// and ClaimSlot's eviction used to call ReleaseTarget(slots[i].target) UNCONDITIONALLY, so
        /// freeing an expired Blocked slot for a victim who ALSO had a live real number used to
        /// unregister that OTHER slot's own activeByTarget/lastImpactByTarget entries the moment the
        /// Blocked pop's own (independent) lifetime ran out - opening a duplicate slot for the very
        /// next real hit on that victim and restarting its total. Deliberately does NOT special-case a
        /// destroyed `target`: Dictionary<Transform,int> looks keys up by Equals/GetHashCode, which (unlike
        /// the `==`/`!=` operators Unity overloads) does not treat a destroyed-but-not-yet-collected
        /// Transform as null, so TryGetValue still finds a destroyed target's own real registration -
        /// the steps-1-2 review's destroyed-victim fix (Slot.active) keeps working unchanged.</summary>
        private bool OwnsRegistration(int slotIndex, Transform target) =>
            activeByTarget.TryGetValue(target, out int registeredSlot) && registeredSlot == slotIndex;

        /// <summary>The bookkeeping a target's number no longer owns a slot needs, shared by FreeSlot
        /// (a number that finished its life) and ClaimSlot (a slot forcibly stolen from a still-live
        /// number to serve a new target) - review fix, steps 1-2: ClaimSlot used to remove only
        /// activeByTarget and leave lastImpactByTarget growing exactly the way ResolveAnchorOffset's
        /// own fix above addresses for the read path. Callers must check OwnsRegistration first (steps
        /// 3-4 review) - this method itself does not, so it must never be called for a slot that does
        /// not actually own the registration it would otherwise delete out from under someone else.</summary>
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
            // Review fix (opus review, mark steps 3-4): only release THIS slot's own registration -
            // see OwnsRegistration's own comment for the bug this guards against.
            if (OwnsRegistration(i, slots[i].target))
                ReleaseTarget(slots[i].target);
            slots[i].active = false;
            slots[i].target = null;
            slots[i].label.gameObject.SetActive(false);
        }
    }
}
