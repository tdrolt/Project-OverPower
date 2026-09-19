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
    /// Private methods are invoked by reflection rather than through the static CombatEvents so this
    /// test never subscribes to that event at all - nothing here depends on whether OnEnable/OnDisable
    /// actually run in this edit-mode context (see PlayerHealthOverheadBarTests's own comment on Awake
    /// not running automatically here either).
    /// </summary>
    public class DamageNumberViewDestroyedTargetTests
    {
        private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject canvasGo;
        private GameObject labelGo;
        private GameObject victimGo;
        private UiTheme theme;
        private DamageNumberView view;

        [SetUp]
        public void CreateRig()
        {
            theme = ScriptableObject.CreateInstance<UiTheme>();

            canvasGo = new GameObject("TestHitFeedbackCanvas", typeof(RectTransform));

            labelGo = new GameObject("TestLabel", typeof(RectTransform));
            labelGo.transform.SetParent(canvasGo.transform);
            TextMeshProUGUI label = labelGo.AddComponent<TextMeshProUGUI>();
            labelGo.SetActive(false); // Matches BuildHitFeedbackCanvas's own pooled labels: built inactive.

            victimGo = new GameObject("TestVictim");

            view = DamageNumberView.Create(canvasGo.transform, theme, (RectTransform)canvasGo.transform, new[] { label });
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

        private void RunLateUpdate() =>
            typeof(DamageNumberView).GetMethod("LateUpdate", NP).Invoke(view, null);

        private Dictionary<Transform, int> ActiveByTarget() =>
            (Dictionary<Transform, int>)typeof(DamageNumberView).GetField("activeByTarget", NP).GetValue(view);

        [Test]
        public void ADestroyedVictimsLabelIsHiddenAndItsSlotFreedOnTheNextLateUpdate()
        {
            Report(victimGo.transform, 21f);
            Assert.IsTrue(labelGo.activeSelf, "label should be showing right after the hit");
            Assert.IsTrue(ActiveByTarget().ContainsKey(victimGo.transform), "the victim should own a slot");

            Transform deadVictim = victimGo.transform; // Kept AFTER destroy - see class comment.
            Object.DestroyImmediate(victimGo);
            victimGo = null; // Already destroyed - TearDown must not double-destroy it.

            RunLateUpdate();

            Assert.IsFalse(labelGo.activeSelf, "a destroyed victim's number must not freeze on screen");
            Assert.IsFalse(ActiveByTarget().ContainsKey(deadVictim), "the dead victim's slot must be released");
        }
    }
}
