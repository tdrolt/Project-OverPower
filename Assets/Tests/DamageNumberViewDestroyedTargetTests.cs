using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>
    /// Regression test for a real bug the opus review of mark steps 1-2 found: a victim who leaves the
    /// room (or is otherwise destroyed) while their damage number is still live used to freeze that
    /// number on screen forever. The old free-slot check everywhere in DamageNumberView was
    /// `target == null`, but Unity overloads Transform's `==` so it also reads true for a
    /// DESTROYED-but-not-yet-collected Transform, not only a real null - so LateUpdate's `continue` on
    /// that same check silently skipped the slot instead of ever hiding its label or releasing its
    /// activeByTarget entry. See DamageNumberView.Slot.active's own comment for the fix: a plain bool,
    /// immune to Unity's fake-null, that lets LateUpdate tell "already free" apart from "just died" and
    /// call FreeSlot for the latter.
    ///
    /// Also covers two bugs the opus review of mark steps 3-4 found in the "Blocked" feature (Tudor's
    /// answer 7): a Blocked pop is deliberately never registered in activeByTarget (see
    /// DamageNumberView.HandleBlockedSeen's own comment), but the pool is now wide enough (2 labels,
    /// not 1) for a Blocked pop and a real running number to coexist on the SAME victim in different
    /// slots at once, which is exactly the scenario those two bugs need to reproduce.
    ///
    /// Private methods are invoked by reflection rather than through the static CombatEvents so this
    /// test never subscribes to that event at all - nothing here depends on whether OnEnable/OnDisable
    /// actually run in this edit-mode context (see PlayerHealthOverheadBarTests's own comment on Awake
    /// not running automatically here either).
    /// </summary>
    public class DamageNumberViewDestroyedTargetTests
    {
        private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags SlotFieldFlags = BindingFlags.Public | BindingFlags.Instance;

        private GameObject canvasGo;
        private GameObject[] labelGos;
        private GameObject victimGo;
        private UiTheme theme;
        private DamageNumberView view;

        [SetUp]
        public void CreateRig()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();

            canvasGo = new GameObject("TestHitFeedbackCanvas", typeof(RectTransform));

            // Two labels, not one - the Blocked-vs-real-number regression tests below need a real
            // number and a Blocked pop to occupy two DIFFERENT slots on the same victim at once.
            var labels = new TextMeshProUGUI[2];
            labelGos = new GameObject[2];
            for (int i = 0; i < labels.Length; i++)
            {
                var labelGo = new GameObject("TestLabel" + i, typeof(RectTransform));
                labelGo.transform.SetParent(canvasGo.transform);
                labels[i] = labelGo.AddComponent<TextMeshProUGUI>();
                labelGo.SetActive(false); // Matches BuildHitFeedbackCanvas's own pooled labels: built inactive.
                labelGos[i] = labelGo;
            }

            victimGo = new GameObject("TestVictim");

            view = DamageNumberView.Create(canvasGo.transform, theme, (RectTransform)canvasGo.transform, labels);
        }

        [TearDown]
        public void DestroyRig()
        {
            if (victimGo != null)
                Object.DestroyImmediate(victimGo);
            Object.DestroyImmediate(canvasGo);
            Object.DestroyImmediate(theme);
        }

        private void Report(Transform victim, float amount) =>
            typeof(DamageNumberView).GetMethod("HandleHitReported", NP).Invoke(view, new object[] { victim, amount, false });

        private void ReportBlocked(Transform victim) =>
            typeof(DamageNumberView).GetMethod("HandleBlockedSeen", NP).Invoke(view, new object[] { victim });

        private void RunLateUpdate() =>
            typeof(DamageNumberView).GetMethod("LateUpdate", NP).Invoke(view, null);

        private void InvokeFreeSlot(int index) =>
            typeof(DamageNumberView).GetMethod("FreeSlot", NP).Invoke(view, new object[] { index });

        private Dictionary<Transform, int> ActiveByTarget() =>
            (Dictionary<Transform, int>)typeof(DamageNumberView).GetField("activeByTarget", NP).GetValue(view);

        private System.Array Slots() => (System.Array)typeof(DamageNumberView).GetField("slots", NP).GetValue(view);

        /// <summary>-1 if no slot is both active and blocked right now.</summary>
        private int FindBlockedSlotIndex()
        {
            System.Array slots = Slots();
            System.Type slotType = slots.GetType().GetElementType();
            for (int i = 0; i < slots.Length; i++)
            {
                object slot = slots.GetValue(i);
                bool active = (bool)slotType.GetField("active", SlotFieldFlags).GetValue(slot);
                bool blocked = (bool)slotType.GetField("blocked", SlotFieldFlags).GetValue(slot);
                if (active && blocked)
                    return i;
            }
            return -1;
        }

        private int ActiveSlotCount()
        {
            System.Array slots = Slots();
            System.Type slotType = slots.GetType().GetElementType();
            int count = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if ((bool)slotType.GetField("active", SlotFieldFlags).GetValue(slots.GetValue(i)))
                    count++;
            }
            return count;
        }

        [Test]
        public void ADestroyedVictimsLabelIsHiddenAndItsSlotFreedOnTheNextLateUpdate()
        {
            Report(victimGo.transform, 21f);
            Assert.IsTrue(labelGos[0].activeSelf, "label should be showing right after the hit");
            Assert.IsTrue(ActiveByTarget().ContainsKey(victimGo.transform), "the victim should own a slot");

            Transform deadVictim = victimGo.transform; // Kept AFTER destroy - see class comment.
            Object.DestroyImmediate(victimGo);
            victimGo = null; // Already destroyed - TearDown must not double-destroy it.

            RunLateUpdate();

            Assert.IsFalse(labelGos[0].activeSelf, "a destroyed victim's number must not freeze on screen");
            Assert.IsFalse(ActiveByTarget().ContainsKey(deadVictim), "the dead victim's slot must be released");
        }

        [Test]
        public void FreeingAnExpiredBlockedPopDoesNotUnregisterAnotherLiveNumberOnTheSameVictim()
        {
            // Bug, opus review of mark steps 3-4: a "Blocked" pop is never registered in
            // activeByTarget (HandleBlockedSeen's own comment), but the OLD FreeSlot/ClaimSlot
            // called ReleaseTarget(slots[i].target) UNCONDITIONALLY - so freeing an EXPIRED Blocked
            // slot for a victim who ALSO has a live real number (a different slot) used to
            // unregister the real slot's own activeByTarget entry too, opening a duplicate slot for
            // the very next hit on that same victim.
            Report(victimGo.transform, 21f); // Claims a slot for the real, running total.
            int realSlot = ActiveByTarget()[victimGo.transform];

            ReportBlocked(victimGo.transform); // Claims a SECOND slot - never registered in activeByTarget.
            int blockedSlot = FindBlockedSlotIndex();
            Assert.AreNotEqual(-1, blockedSlot, "the rig needs a distinct Blocked slot to reproduce this");
            Assert.AreNotEqual(realSlot, blockedSlot, "the rig needs two distinct slots for this to reproduce");

            InvokeFreeSlot(blockedSlot); // The Blocked pop's own natural expiry - what LateUpdate would call.

            Assert.IsTrue(ActiveByTarget().ContainsKey(victimGo.transform),
                "the real number's own registration must survive the unrelated Blocked pop's own free");
            Assert.AreEqual(realSlot, ActiveByTarget()[victimGo.transform]);

            Report(victimGo.transform, 5f); // A further hit must ADD into the still-registered real slot...
            Assert.AreEqual(1, ActiveByTarget().Count, "...not open a duplicate slot for the same victim");
        }

        [Test]
        public void RepeatedBlockedReportsForTheSameVictimReuseOneSlotInsteadOfStackingLabels()
        {
            // Risk noted alongside the bug above: a fast weapon (SMG Double Rate, a shotgun pull)
            // used to claim a FRESH slot per projectile for the same Blocked victim, which could
            // fill the whole 24-slot pool with "Blocked" labels on one enemy.
            ReportBlocked(victimGo.transform);
            int firstSlot = FindBlockedSlotIndex();
            Assert.AreNotEqual(-1, firstSlot);

            ReportBlocked(victimGo.transform);
            int secondSlot = FindBlockedSlotIndex();

            Assert.AreEqual(firstSlot, secondSlot,
                "a second Blocked report for the same victim must refresh the same slot, not claim a new one");
            Assert.AreEqual(1, ActiveSlotCount(), "only one slot should be active in total");
        }
    }
}
