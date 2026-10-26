using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Scope step 1 (Tudor, 2026-09-18). Pins the ordering the whole Scope ability depends on: the scroll zoom is
    /// clamped FIRST (CameraTracking.LateUpdate does that, not this class - currentZoom arrives here already
    /// clamped), and every active extra-zoom multiplier (Scope today; whatever else wants one tomorrow) multiplies
    /// the CLAMPED result - never the other way around, or a player already at the scroll wheel's own limit would
    /// gain nothing from the ability, because the very next scroll tick would re-clamp the inflated value away.
    /// See CameraZoomStack's own class comment for the full reasoning.
    /// </summary>
    public class CameraZoomStackTests
    {
        private static readonly Vector3 BaseOffset = new Vector3(0f, 10f, -5f);

        [Test]
        public void TheMultiplierAppliesAfterTheClampNotBeforeIt()
        {
            // currentZoom already at the scroll limit (CameraTracking.maxZoomMultiplier, 2) - the case that proves
            // the multiplier survives the clamp instead of being folded into the value the clamp would re-clamp.
            const float clampedZoom = 2f;
            const float scopeMultiplier = 1.2f;

            Vector3 result = CameraZoomStack.ApplyZoom(BaseOffset, clampedZoom, scopeMultiplier);

            Assert.AreEqual(BaseOffset * 2.4f, result);
        }

        [Test]
        public void AnEmptyStackMultipliesByExactlyOne()
        {
            // The product of zero active multipliers is 1 - "nobody is scoped" must be a true no-op.
            float product = CameraZoomStack.Product(new float[0]);

            Assert.AreEqual(1f, product);
        }

        [Test]
        public void RemovingTheOnlyMultiplierReturnsExactlyTheUnscopedOffset()
        {
            // Multiplying by exactly 1f introduces no floating-point error, so this is an EXACT comparison, not an
            // approximate one - proving "removed" really means "gone", not "still there at a value close to 1".
            const float clampedZoom = 1.4f;

            Vector3 unscoped = CameraZoomStack.ApplyZoom(BaseOffset, clampedZoom, CameraZoomStack.Product(new float[0]));

            Assert.AreEqual(BaseOffset * clampedZoom, unscoped);
        }

        [Test]
        public void TwoActiveMultipliersMultiplyTogether()
        {
            float product = CameraZoomStack.Product(new[] { 1.2f, 1.5f });

            Assert.AreEqual(1.2f * 1.5f, product, 1e-6f);
        }

        // CameraTracking's own per-frame call site (zoomMultipliers.Values) is a
        // Dictionary<object, float>.ValueCollection, not a bare array - pins the concrete-typed
        // overload (the LateUpdate/ZoomMultiplierProduct allocation fix) computes the identical
        // product as the general IEnumerable<float> overload above, for both an empty and a
        // populated dictionary.
        [Test]
        public void TheConcreteDictionaryOverloadMatchesTheGeneralOverloadWhenEmpty()
        {
            var empty = new Dictionary<object, float>();

            float product = CameraZoomStack.Product(empty.Values);

            Assert.AreEqual(1f, product);
        }

        [Test]
        public void TheConcreteDictionaryOverloadMatchesTheGeneralOverloadWithValues()
        {
            var multipliers = new Dictionary<object, float>
            {
                { new object(), 1.2f },
                { new object(), 1.5f },
            };

            float product = CameraZoomStack.Product(multipliers.Values);

            Assert.AreEqual(1.2f * 1.5f, product, 1e-6f);
        }
    }
}
