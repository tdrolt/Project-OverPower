using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Covers the electric fence's per-target hit rule (Task 1.11b addendum): in-band,
    /// side-flip (the "dash across a 1m band" case) and the shared per-target cooldown. See
    /// FenceCrossingState's own class comment for why both triggers exist.</summary>
    public class FenceCrossingStateTests
    {
        [Test]
        public void FirstSampleInsideTheRingIsNotAHit()
        {
            // Radius 6, thickness 1 -> band is 5.5..6.5. 2 is well inside, not in the band, and there
            // is no previous sample to have crossed from.
            var state = new FenceCrossingState(radius: 6f, ringThickness: 1f, perTargetCooldownSeconds: 1f);

            Assert.IsFalse(state.ShouldHit(distance: 2f, now: 0f));
        }

        [Test]
        public void FirstSampleInTheBandIsAHit()
        {
            var state = new FenceCrossingState(radius: 6f, ringThickness: 1f, perTargetCooldownSeconds: 1f);

            Assert.IsTrue(state.ShouldHit(distance: 6f, now: 0f));
        }

        [Test]
        public void StandingJustInsideTheInnerBandEdgeIsAHit()
        {
            var state = new FenceCrossingState(radius: 6f, ringThickness: 1f, perTargetCooldownSeconds: 1f);

            Assert.IsTrue(state.ShouldHit(distance: 5.5f, now: 0f));
        }

        [Test]
        public void StandingJustOutsideTheBandIsNotAHit()
        {
            var state = new FenceCrossingState(radius: 6f, ringThickness: 1f, perTargetCooldownSeconds: 1f);

            Assert.IsFalse(state.ShouldHit(distance: 6.51f, now: 0f));
        }

        [Test]
        public void CrossingFromInsideToOutsideInOneStepIsAHitEvenWithoutTouchingTheBand()
        {
            // A dash/teleport step: 2 (inside) this frame, 20 (far outside) the next - the sampled
            // points never land in the 5.5..6.5 band, but the ring was crossed between them.
            var state = new FenceCrossingState(radius: 6f, ringThickness: 1f, perTargetCooldownSeconds: 1f);
            state.ShouldHit(distance: 2f, now: 0f);

            Assert.IsTrue(state.ShouldHit(distance: 20f, now: 0.02f));
        }

        [Test]
        public void CrossingFromOutsideToInsideIsAlsoAHit()
        {
            var state = new FenceCrossingState(radius: 6f, ringThickness: 1f, perTargetCooldownSeconds: 1f);
            state.ShouldHit(distance: 20f, now: 0f);

            Assert.IsTrue(state.ShouldHit(distance: 2f, now: 0.02f));
        }

        [Test]
        public void StayingOnTheSameSideWithoutEnteringTheBandIsNeverAHit()
        {
            var state = new FenceCrossingState(radius: 6f, ringThickness: 1f, perTargetCooldownSeconds: 1f);
            state.ShouldHit(distance: 2f, now: 0f);
            state.ShouldHit(distance: 3f, now: 0.02f);

            Assert.IsFalse(state.ShouldHit(distance: 4f, now: 0.04f));
        }

        [Test]
        public void ASecondHitBeforeTheCooldownExpiresIsRefused()
        {
            var state = new FenceCrossingState(radius: 6f, ringThickness: 1f, perTargetCooldownSeconds: 1f);
            Assert.IsTrue(state.ShouldHit(distance: 6f, now: 0f)); // in band, hits, cooldown until 1.0

            Assert.IsFalse(state.ShouldHit(distance: 6f, now: 0.99f));
        }

        [Test]
        public void AHitLandsAgainTheInstantTheCooldownExpires()
        {
            var state = new FenceCrossingState(radius: 6f, ringThickness: 1f, perTargetCooldownSeconds: 1f);
            state.ShouldHit(distance: 6f, now: 0f);

            Assert.IsTrue(state.ShouldHit(distance: 6f, now: 1f));
        }

        [Test]
        public void ACrossingDuringTheCooldownIsAlsoRefused()
        {
            // The cooldown is shared by both triggers - a crossing right after an in-band hit must
            // not land a second hit before the cooldown is up.
            var state = new FenceCrossingState(radius: 6f, ringThickness: 1f, perTargetCooldownSeconds: 1f);
            state.ShouldHit(distance: 2f, now: 0f);
            state.ShouldHit(distance: 6f, now: 0.1f); // in band, hits, cooldown until 1.1

            Assert.IsFalse(state.ShouldHit(distance: 20f, now: 0.2f)); // crosses back out, but still cooling down
        }

        [Test]
        public void StrafingBackAndForthAcrossTheBandHitsAtMostOncePerCooldown()
        {
            // Roughly the ~6s strafing scenario from the Task 1.11b verify steps, compressed: repeated
            // crossings faster than the cooldown must not multiply hits.
            var state = new FenceCrossingState(radius: 6f, ringThickness: 1f, perTargetCooldownSeconds: 1f);
            int hits = 0;
            float distance = 2f;

            for (float t = 0f; t < 6f; t += 0.1f)
            {
                distance = distance < 10f ? 20f : 2f; // flips side every sample
                if (state.ShouldHit(distance, t))
                    hits++;
            }

            // 6 seconds at a 1s cooldown can land at most 6 hits, and the alternating pattern above
            // crosses far more often than that - so this pins down that the cooldown is doing its job
            // rather than merely asserting one specific count.
            Assert.LessOrEqual(hits, 6);
            Assert.Greater(hits, 0);
        }
    }
}
