using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Overpower.Combat;

namespace Overpower.UI
{
    /// <summary>
    /// Draws every damage number on the shooter's own screen. Shooter-only by construction: it subscribes to
    /// CombatEvents.LocalHitReported/LocalImpactSeen, which fire only on the machine that dealt the damage.
    /// ONE LIVE NUMBER PER ENEMY: each new report ADDS to that target's number, re-pops it and restarts its life,
    /// which rises and fades damageNumberHoldSeconds after the LAST hit (hence slots keyed by target Transform, not
    /// merged by a time window). It anchors at the LATEST local impact point (LocalImpactSeen arrives before the
    /// possibly networked LocalHitReported), kept as an OFFSET so it follows a moving target; with no impact younger
    /// than ImpactFreshnessSeconds (a burn tick, fire field, mine) it falls back to the centre at
    /// damageNumberAnchorHeight. No allocation per hit: TMP SetText("{0:0}", value), slots in a plain Slot[].
    /// </summary>
    // CameraTracking.LateUpdate has no execution order relative to this LateUpdate, which projects through
    // Camera.main: without an explicit order a number reads one frame's camera against another frame's player
    // position and wobbles. 1000 puts this after any ordinary gameplay script, so it sees the final camera.
    [DefaultExecutionOrder(1000)]
    public class DamageNumberView : MonoBehaviour
    {
        /// <summary>How many labels the pool holds; a code constant, not a look value (Rule 10). 24 covers every
        /// enemy of a 9-player match at once, with headroom for a shotgun's single merged label.</summary>
        public const int PoolSize = 24;

        // How long a LocalImpactSeen point stays "fresh" enough for a LocalHitReported to anchor to it. A technical
        // seam between two events that may arrive a frame or two apart, not a look value.
        private const float ImpactFreshnessSeconds = 1f;

        private struct Slot
        {
            public TextMeshProUGUI label;
            public RectTransform rect;
            // Whether this slot is claimed, tracked SEPARATELY from target: Unity's == reads a DESTROYED-but-not-yet-
            // collected Transform (a victim who left) as null, so `target == null` can't tell "already free" from
            // "just went stale". A plain bool is immune; LateUpdate frees a stale slot explicitly.
            public bool active;
            public Transform target;       // Meaningful only while active - see FreeSlot.
            public Vector3 anchorOffset;   // World-space offset from target.position - the latest impact.
            public float lastHitTime;      // Time.time of the most recent hit added to this number - also this label's own last (re)pop time, since every write to one is paired with the other (see ApplyLabel's callers).
            public float amount;           // Running total shown.
            public bool marked;
            // True for a one-off "Blocked" pop (HandleBlockedSeen), not a running total. It is never registered in
            // activeByTarget, so ApplyLabel (which redraws from amount/marked) is never called on it again; it rides
            // LateUpdate's ordinary hold/rise/fade with its text and colour set once.
            public bool blocked;
            // Dominion respawn shield: > 0 for a BLOCKED pop with its own whole life in seconds (DominionConfig's Blocked Popup Seconds),
            // 0 for every other slot, which rides the theme's hold + lifetime.
            public float popupSeconds;
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
            CombatEvents.LocalShieldBlockedSeen += HandleShieldBlockedSeen;
        }

        private void OnDisable()
        {
            CombatEvents.LocalHitReported -= HandleHitReported;
            CombatEvents.LocalImpactSeen -= HandleImpactSeen;
            CombatEvents.LocalBlockedSeen -= HandleBlockedSeen;
            CombatEvents.LocalShieldBlockedSeen -= HandleShieldBlockedSeen;
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
            slots[slot].popupSeconds = 0f;
            slots[slot].lastHitTime = Time.time;
            slots[slot].label.gameObject.SetActive(true);
            ApplyLabel(slot);
            activeByTarget[victim] = slot;
        }

        /// <summary>A one-off Blocked pop at the impact, never a running total: deliberately NOT looked up in or
        /// added to activeByTarget (see Slot.blocked), so a later real hit on the same victim starts a fresh number
        /// instead of inheriting this slot's zero amount or grey colour. A fast weapon (a shotgun's pellet spread) can
        /// raise this many times against one shielded victim in a burst, so the Blocked pop already showing is
        /// refreshed instead of claiming a slot per projectile (which could fill the pool). FindActiveBlockedSlot is a
        /// linear scan, not a second dictionary: Blocked pops are rare, and a dictionary would have to stay in lockstep
        /// with slots[] on every eviction.</summary>
        private void HandleBlockedSeen(Transform victim) => ShowBlockedPop(victim, theme.blockedText, theme.blockedColor);

        /// <summary>The Blocked pop itself, with the words and colour the caller's source uses: the Invulnerability look's grey Blocked, or the
        /// Dominion respawn shield's own BLOCKED (A23).</summary>
        private void ShowBlockedPop(Transform victim, string text, Color colour)
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
            slots[slot].popupSeconds = 0f;
            slots[slot].lastHitTime = Time.time; // Refreshed even for a reused slot - restarts its own hold/fade life.
            slots[slot].label.gameObject.SetActive(true);
            slots[slot].label.SetText(text);
            slots[slot].label.color = colour;
        }

        /// <summary>Dominion respawn shield: the same Blocked pop (same slot kind) with the shield's own words and colour (UiTheme respawnShieldBlockedText / Color, A23), but raised on every client that sees the shielded
        /// player, from their dBlk stamp, over the player at the usual anchor height, and living popupSeconds in all. Refreshes the pop already
        /// showing for them rather than stacking one per stopped hit.</summary>
        private void HandleShieldBlockedSeen(Transform victim, float popupSeconds)
        {
            if (!theme.showDamageNumbers || victim == null)
                return;

            ShowBlockedPop(victim, theme.respawnShieldBlockedText, theme.respawnShieldBlockedColor);
            int slot = FindActiveBlockedSlot(victim);
            if (slot >= 0)
                slots[slot].popupSeconds = popupSeconds;
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

                // Stale entries are cleared lazily, on read; otherwise a teammate or leaver you once hit would sit in
                // this dictionary forever.
                lastImpactByTarget.Remove(victim);
            }

            return Vector3.up * theme.damageNumberAnchorHeight;
        }

        private void ApplyLabel(int slot)
        {
            slots[slot].label.SetText("{0:0}", (float)DamageNumberMotion.Shown(slots[slot].amount));
            slots[slot].label.color = slots[slot].marked ? theme.markColor : theme.damageNumberColor;
        }

        /// <summary>A free slot, else the least-recently-popped slot in use ("steal the oldest"); must never
        /// allocate. Checks Slot.active, not target == null - see that field.</summary>
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

            // Release only THIS slot's own registration - see OwnsRegistration.
            if (OwnsRegistration(oldest, slots[oldest].target))
                ReleaseTarget(slots[oldest].target);
            return oldest;
        }

        /// <summary>True only when SLOT slotIndex is the one activeByTarget credits with `target`'s running number;
        /// false for a "Blocked" pop (never registered) that merely shares a victim with a live real number in a
        /// DIFFERENT slot. Releasing unconditionally would unregister that other slot when the Blocked pop expired,
        /// opening a duplicate slot for the next real hit and restarting its total. Deliberately does NOT special-case
        /// a destroyed `target`: Dictionary<Transform,int> uses Equals/GetHashCode, which (unlike Unity's overloaded
        /// ==) don't treat a destroyed Transform as null, so TryGetValue still finds its registration.</summary>
        private bool OwnsRegistration(int slotIndex, Transform target) =>
            activeByTarget.TryGetValue(target, out int registeredSlot) && registeredSlot == slotIndex;

        /// <summary>The bookkeeping once a target's number no longer owns a slot, shared by FreeSlot (a number that
        /// finished) and ClaimSlot (a slot stolen from a live number): removes both activeByTarget and
        /// lastImpactByTarget. Callers must check OwnsRegistration first; this method does not, so it must never run
        /// for a slot that doesn't own the registration it would delete from under someone else.</summary>
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

                // Unity's == is true for a DESTROYED victim (a player who left mid-number) too. Slot.active already
                // says the slot is claimed, so free it here, or its label freezes on screen and its activeByTarget
                // entry is never removed.
                if (slots[i].target == null)
                {
                    FreeSlot(i);
                    continue;
                }

                float ageSinceLastHit = Time.time - slots[i].lastHitTime;
                // A shield pop lives its own popupSeconds in all (held for the first half, then rising and fading); every other slot the theme's.
                float hold = slots[i].popupSeconds > 0f ? slots[i].popupSeconds * 0.5f : theme.damageNumberHoldSeconds;
                float lifetime = slots[i].popupSeconds > 0f ? slots[i].popupSeconds * 0.5f : theme.damageNumberLifetimeSeconds;
                if (ageSinceLastHit - hold >= lifetime)
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
                // "last hit" are always written together (see HandleHitReported).
                DamageNumberPose pose = DamageNumberMotion.Evaluate(ageSinceLastHit, ageSinceLastHit, hold,
                    lifetime, theme.damageNumberPopSeconds, theme.damageNumberPopScale,
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
            // Release only THIS slot's own registration - see OwnsRegistration.
            if (OwnsRegistration(i, slots[i].target))
                ReleaseTarget(slots[i].target);
            slots[i].active = false;
            slots[i].target = null;
            slots[i].label.gameObject.SetActive(false);
        }
    }
}
