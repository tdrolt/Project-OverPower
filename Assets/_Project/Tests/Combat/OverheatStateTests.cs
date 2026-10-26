using NUnit.Framework;
using Overpower.Combat;
using Overpower.UI;

namespace Overpower.Tests
{
    public class OverheatStateTests
    {
        // Real weapon values: max 100, decay starts 1.5s after the last Add, then falls at
        // 25/s, and the HUD starts warning at 80. Vent: 2.0s into the silence before the window
        // opens, then a 0.8s window - Tudor's own numbers. Every test uses these so the numbers
        // in the assertions match the numbers a playtester would actually see.
        private static OverheatState NewState(float ventDelay = 2f, float ventWindow = 0.8f) => new OverheatState(
            max: 100f, decayDelay: 1.5f, decayPerSecond: 25f, warningThreshold: 80f,
            ventDelay: ventDelay, ventWindow: ventWindow);

        /// <summary>A state that is silenced from t=0 of this silence, ready for the vent tests -
        /// every one of them cares about "how long into the silence", not about how the bar filled.</summary>
        private static OverheatState Silenced(float ventDelay = 2f, float ventWindow = 0.8f)
        {
            var s = NewState(ventDelay, ventWindow);
            s.Add(100f);
            return s;
        }

        [Test]
        public void AddingBelowMaxDoesNotSilence()
        {
            var s = NewState();
            s.Add(99f);

            Assert.IsFalse(s.IsSilenced);
            Assert.IsTrue(s.CanAct);
        }

        [Test]
        public void AddingExactlyToMaxSilences()
        {
            var s = NewState();
            s.Add(100f);

            Assert.IsTrue(s.IsSilenced);
            Assert.IsFalse(s.CanAct);
        }

        [Test]
        public void DecayingJustBelowMaxIsStillSilenced()
        {
            // The rule is "silenced until the bar reaches 0", not "until it drops below max" -
            // reaching 99 by decaying down from a silenced 100 must not clear the silence.
            var s = NewState();
            s.Add(100f);
            s.Tick(1.5f);   // decayDelay elapsed, decay starts
            s.Tick(0.04f);  // 0.04 * 25 = 1 heat lost -> 99

            Assert.AreEqual(99f, s.Heat, 0.01f);
            Assert.IsTrue(s.IsSilenced);
        }

        [Test]
        public void OnceSilencedDecayingToHalfIsStillSilenced()
        {
            var s = NewState();
            s.Add(100f);
            s.Tick(1.5f);  // decayDelay elapsed
            s.Tick(2f);    // 2 * 25 = 50 heat lost -> 50

            Assert.AreEqual(50f, s.Heat, 0.01f);
            Assert.IsTrue(s.IsSilenced);
            Assert.IsFalse(s.CanAct);
        }

        [Test]
        public void ReachingZeroClearsTheSilence()
        {
            var s = NewState();
            s.Add(100f);
            s.Tick(1.5f);  // decayDelay elapsed
            s.Tick(4f);    // 4 * 25 = 100 heat lost -> exactly 0

            Assert.AreEqual(0f, s.Heat, 0.01f);
            Assert.IsFalse(s.IsSilenced);
            Assert.IsTrue(s.CanAct);
        }

        [Test]
        public void DecayDoesNotStartBeforeTheDelayHasElapsed()
        {
            var s = NewState();
            s.Add(50f);
            s.Tick(1.0f);   // less than decayDelay (1.5s)

            Assert.AreEqual(50f, s.Heat, 0.01f);
        }

        [Test]
        public void AddingHeatResetsTheDecayDelay()
        {
            var s = NewState();
            s.Add(50f);
            s.Tick(1.4f);   // still short of decayDelay - no decay yet
            s.Add(10f);     // this restarts the delay countdown
            s.Tick(1.4f);   // still short of decayDelay again - still no decay

            Assert.AreEqual(60f, s.Heat, 0.01f);
        }

        [Test]
        public void RefundCannotPushHeatBelowZero()
        {
            var s = NewState();
            s.Add(10f);
            s.Refund(50f);

            Assert.AreEqual(0f, s.Heat, 0.01f);
        }

        [Test]
        public void IsWarningIsTrueAtExactly80AndFalseAt79()
        {
            var below = NewState();
            below.Add(79f);
            Assert.IsFalse(below.IsWarning);

            var at = NewState();
            at.Add(80f);
            Assert.IsTrue(at.IsWarning);
        }

        [Test]
        public void IsWarningIsFalseWhileSilenced()
        {
            // The warning's job is to precede the silence, not to accompany it - showing a
            // warning during the punishment it was meant to warn about reads as a HUD bug.
            var s = NewState();
            s.Add(100f);

            Assert.IsTrue(s.IsSilenced);
            Assert.IsFalse(s.IsWarning);
        }

        [Test]
        public void ClearZeroesHeatAndUnSilences()
        {
            var s = NewState();
            s.Add(100f);
            s.Clear();

            Assert.AreEqual(0f, s.Heat, 0.01f);
            Assert.IsFalse(s.IsSilenced);
            Assert.IsTrue(s.CanAct);
        }

        [Test]
        public void NormalisedReflectsHeatAsAFractionOfMax()
        {
            var s = NewState();
            Assert.AreEqual(0f, s.Normalised, 0.001f);

            s.Add(50f);
            Assert.AreEqual(0.5f, s.Normalised, 0.001f);

            s.Add(50f);
            Assert.AreEqual(1f, s.Normalised, 0.001f);
        }

        // ================================================================================
        // Vent - press R inside the window (2.0s..2.8s into the silence, both ends inclusive)
        // and the rest of the silence is cut in half.
        // ================================================================================

        [Test]
        public void PressBeforeTheWindowIsEarlyAndSpendsTheAttemptWithHeatUnchanged()
        {
            var s = Silenced();
            s.Tick(1f); // 1.0s in, before ventDelay (2.0)
            float heatBefore = s.Heat;

            VentResult result = s.TryVent();

            Assert.AreEqual(VentResult.Early, result);
            Assert.AreEqual(heatBefore, s.Heat, 0.01f);
        }

        [Test]
        public void ALaterInWindowPressAfterAnEarlyMissIsAlreadyUsed()
        {
            var s = Silenced();
            s.Tick(1f);
            Assert.AreEqual(VentResult.Early, s.TryVent()); // spends the one attempt

            s.Tick(1f); // now 2.0s in - inside the window, but the attempt is already gone
            Assert.AreEqual(VentResult.AlreadyUsed, s.TryVent());
        }

        [Test]
        public void PressInsideTheWindowHits()
        {
            var s = Silenced();
            s.Tick(2.2f); // inside [2.0, 2.8]

            Assert.AreEqual(VentResult.Hit, s.TryVent());
        }

        [Test]
        public void AHitHalvesHeatWhichEndsTheSilenceAtAboutHalfTheRemainingTime()
        {
            var s = Silenced();
            s.Tick(2.4f); // decay has been running since 1.5s: heat = 100 - 25*(2.4-1.5) = 77.5
            float heatBeforeHit = s.Heat;
            Assert.AreEqual(77.5f, heatBeforeHit, 0.01f);

            Assert.AreEqual(VentResult.Hit, s.TryVent());
            Assert.AreEqual(heatBeforeHit * 0.5f, s.Heat, 0.01f); // 38.75

            // Remaining time before the hit would have been 77.5/25 = 3.1s. Halved heat means
            // half that, 1.55s - tick just short of it (still silenced), then past it (cleared).
            s.Tick(1.5f);
            Assert.IsTrue(s.IsSilenced, "should still be silenced just short of half the remaining time");
            s.Tick(0.1f);
            Assert.IsFalse(s.IsSilenced, "should have cleared at about half the remaining time");
        }

        [Test]
        public void PressAfterTheWindowIsLateAndSpendsTheAttemptWithHeatUnchanged()
        {
            var s = Silenced();
            s.Tick(3f); // window closed at 2.8
            float heatBefore = s.Heat;

            Assert.AreEqual(VentResult.Late, s.TryVent());
            Assert.AreEqual(heatBefore, s.Heat, 0.01f);
        }

        [Test]
        public void ASecondPressInTheSameSilenceIsAlwaysAlreadyUsedEvenAfterAHit()
        {
            var s = Silenced();
            s.Tick(2.2f);
            Assert.AreEqual(VentResult.Hit, s.TryVent());
            Assert.AreEqual(VentResult.AlreadyUsed, s.TryVent());
        }

        [Test]
        public void NotSilencedReturnsNotSilencedAndSpendsNoAttempt()
        {
            var s = NewState();
            Assert.AreEqual(VentResult.NotSilenced, s.TryVent());

            // A later silence must still get its own, fresh attempt.
            s.Add(100f);
            s.Tick(2f);
            Assert.AreEqual(VentResult.Hit, s.TryVent());
        }

        [Test]
        public void WindowOpenBoundaryIsInclusiveHit()
        {
            var s = Silenced();
            s.Tick(2.0f); // exactly ventDelay
            Assert.AreEqual(VentResult.Hit, s.TryVent());
        }

        [Test]
        public void WindowCloseBoundaryIsInclusiveHit()
        {
            var s = Silenced();
            s.Tick(2.8f); // exactly ventDelay + ventWindow
            Assert.AreEqual(VentResult.Hit, s.TryVent());
        }

        [Test]
        public void JustBeforeTheWindowOpensIsEarly()
        {
            var s = Silenced();
            s.Tick(1.99f);
            Assert.AreEqual(VentResult.Early, s.TryVent());
        }

        [Test]
        public void JustAfterTheWindowClosesIsLate()
        {
            var s = Silenced();
            s.Tick(2.81f);
            Assert.AreEqual(VentResult.Late, s.TryVent());
        }

        [Test]
        public void ALargeTickPastTheWholeWindowButStillSilencedThenAPressIsLate()
        {
            // A single big Tick (a hitch/lag spike) landing after the window has closed but before
            // the silence itself has fully decayed away - must read Late, not hand a free Hit to
            // whoever happens to press during a slow frame that swept straight past the window.
            var s = Silenced();
            s.Tick(3.5f); // window closes at 2.8s; the silence itself doesn't clear until 5.5s

            Assert.IsTrue(s.IsSilenced);
            Assert.AreEqual(VentResult.Late, s.TryVent());
        }

        [Test]
        public void ATickThatJumpsPastTheEntireSilenceIsSafeAndReadsNotSilenced()
        {
            // An even more extreme jump - past the point heat fully decays to 0 - must not crash,
            // and must read NotSilenced rather than something stale from the silence that ended.
            var s = Silenced();
            s.Tick(100f);

            Assert.IsFalse(s.IsSilenced);
            Assert.AreEqual(VentResult.NotSilenced, s.TryVent());
        }

        [Test]
        public void ANewSilenceAfterThePreviousOneEndedGetsAFreshAttempt()
        {
            var s = Silenced();
            s.Tick(2.2f);
            Assert.AreEqual(VentResult.Hit, s.TryVent()); // uses the attempt, halves heat

            while (s.IsSilenced)
                s.Tick(0.1f); // run the (now-halved) silence out to completion

            s.Add(100f); // a brand new silence
            s.Tick(2f);
            Assert.AreEqual(VentResult.Hit, s.TryVent(), "the new silence should get its own fresh attempt");
        }

        [Test]
        public void ClearResetsTheVentClockAndAttempt()
        {
            var s = Silenced();
            s.Tick(1f);
            s.TryVent(); // Early - spends the attempt

            s.Clear();
            s.Add(100f);
            s.Tick(2f);

            Assert.AreEqual(VentResult.Hit, s.TryVent(), "Clear must free up a fresh attempt for the next silence");
        }

        [Test]
        public void VentWindowZeroNeverHits()
        {
            var s = Silenced(ventWindow: 0f);
            s.Tick(2f); // exactly ventDelay - the whole (zero-width) window, if it existed at all

            Assert.AreEqual(VentResult.Late, s.TryVent());
        }

        [Test]
        public void TheBandsHeatRangeMatchesTheWorkedExampleForTheDefaultNumbers()
        {
            // max 100, decay delay 1.5, 25/s, vent delay 2.0, window 0.8: the band runs from heat
            // 87.5 down to 67.5, i.e. fractions 0.875 -> 0.675.
            var s = Silenced();

            Assert.AreEqual(0.875f, s.VentBandHighFraction, 0.001f);
            Assert.AreEqual(0.675f, s.VentBandLowFraction, 0.001f);
        }

        [Test]
        public void TheFillStaysInsideTheBandsRangeWhileTheWindowIsOpen()
        {
            var s = Silenced();
            float high = s.VentBandHighFraction;
            float low = s.VentBandLowFraction;

            s.Tick(2f); // window opens
            Assert.IsTrue(s.IsVentWindowOpen);
            Assert.LessOrEqual(s.Normalised, high + 0.001f);
            Assert.GreaterOrEqual(s.Normalised, low - 0.001f);

            s.Tick(0.4f); // mid-window
            Assert.IsTrue(s.IsVentWindowOpen);
            Assert.LessOrEqual(s.Normalised, high + 0.001f);
            Assert.GreaterOrEqual(s.Normalised, low - 0.001f);

            s.Tick(0.4f); // window closes (2.8s)
            Assert.IsTrue(s.IsVentWindowOpen);
            Assert.LessOrEqual(s.Normalised, high + 0.001f);
            Assert.GreaterOrEqual(s.Normalised, low - 0.001f);
        }

        [Test]
        public void WindowIsClosedBeforeItOpensAndAfterItCloses()
        {
            var s = Silenced();
            Assert.IsFalse(s.IsVentWindowOpen); // t=0
            s.Tick(1.9f);
            Assert.IsFalse(s.IsVentWindowOpen);
            s.Tick(0.2f); // t=2.1, inside
            Assert.IsTrue(s.IsVentWindowOpen);
            s.Tick(1f); // t=3.1, past
            Assert.IsFalse(s.IsVentWindowOpen);
        }

        [Test]
        public void OutcomeIsNoneThenMissedOnceTheWindowPassesUnused()
        {
            var s = Silenced();
            Assert.AreEqual(VentOutcome.None, s.Outcome);
            s.Tick(2.9f); // window has closed, nothing was ever pressed
            Assert.AreEqual(VentOutcome.Missed, s.Outcome);
        }

        [Test]
        public void OutcomeIsHitAfterAHit()
        {
            var s = Silenced();
            s.Tick(2f);
            s.TryVent();
            Assert.AreEqual(VentOutcome.Hit, s.Outcome);
        }

        [Test]
        public void OutcomeIsMissedAfterAnEarlyOrLatePress()
        {
            var s = Silenced();
            s.Tick(1f);
            s.TryVent(); // Early
            Assert.AreEqual(VentOutcome.Missed, s.Outcome);
        }

        // ================================================================================
        // Review fix (Important): ventWindow <= 0 means Vent is off (GameplayConfig's own
        // tooltip: "0 turns Vent off entirely"). Outcome used to keep reading Missed for the
        // rest of the silence the instant silenceClock passed the (zero-width, already-closed)
        // window, painting a permanent grey band a designer who disabled Vent never asked for.
        // ================================================================================

        [Test]
        public void VentWindowZeroKeepsOutcomeNoneForTheWholeSilence()
        {
            var s = Silenced(ventWindow: 0f);

            Assert.AreEqual(VentOutcome.None, s.Outcome, "t=0");
            s.Tick(1f);
            Assert.AreEqual(VentOutcome.None, s.Outcome, "before ventDelay");
            s.Tick(1f); // t=2.0 == ventDelay: exactly where the old bug started reading Missed
            Assert.AreEqual(VentOutcome.None, s.Outcome, "at ventDelay");
            s.Tick(1f); // t=3.0, well past ventDelay
            Assert.AreEqual(VentOutcome.None, s.Outcome, "mid-silence");
            s.Tick(1.4f); // t=4.4, just short of the silence clearing at 5.5s (100/25 + 1.5)
            Assert.IsTrue(s.IsSilenced);
            Assert.AreEqual(VentOutcome.None, s.Outcome, "right up to the silence clearing");
        }

        [Test]
        public void VentWindowZeroNeverOpensTheWindow()
        {
            var s = Silenced(ventWindow: 0f);
            s.Tick(2f); // exactly ventDelay - where the window would open if it could

            Assert.IsFalse(s.IsVentWindowOpen);
        }

        [Test]
        public void VentWindowZeroReadsAsDisabled()
        {
            var s = NewState(ventWindow: 0f);
            Assert.IsFalse(s.VentEnabled);

            var enabled = NewState(ventWindow: 0.8f);
            Assert.IsTrue(enabled.VentEnabled);
        }

        [Test]
        public void VentWindowZeroLookRuleIsHiddenEvenAfterAPressSpendsTheAttempt()
        {
            // Ties straight to the actual HUD symptom: VentBandLookRule.Determine returning
            // Miss (a persistent grey band) is exactly what a disabled Vent must never show.
            var s = Silenced(ventWindow: 0f);
            s.Tick(3f); // well past where the (disabled) window would have been

            // Pinned: TryVent still spends the one attempt and reads Late for window 0 (the
            // early/late split falls through to "silenceClock < ventDelay", which a zero-width
            // window can never make Early once silenceClock has reached ventDelay) - unaffected
            // by this fix, which only changes Outcome/the HUD band, not TryVent's own result.
            Assert.AreEqual(VentResult.Late, s.TryVent());

            VentBandLook look = VentBandLookRule.Determine(s.IsSilenced, s.IsVentWindowOpen, s.Outcome, s.VentEnabled);
            Assert.AreEqual(VentBandLook.Hidden, look);
        }

        // ================================================================================
        // Review fix (Important, reachable today): a laser's Refund(OverheatRefundOnHit) can
        // land in the same trigger pull that caused the overheat (WeaponFiring.
        // RefundHeatIfBeamConnects runs right after the Add that silenced the player), while
        // still silenced and before any Vent attempt. The band used to always project from
        // max, so a refunded fill was already partway through a band drawn as if nothing had
        // been refunded at all.
        // ================================================================================

        [Test]
        public void ARefundAtSilenceStartMovesTheBandDownWithIt()
        {
            // max 100, decay delay 1.5, 25/s, vent delay 2.0, window 0.8; refund 10 at t=0 (the
            // same trigger pull that caused the overheat) drops the band's projection origin
            // from 100 to 90: high = (90 - 25*0.5)/100 = 0.775, low = (90 - 25*1.3)/100 = 0.575.
            var s = Silenced();
            s.Refund(10f);

            Assert.AreEqual(0.775f, s.VentBandHighFraction, 0.001f);
            Assert.AreEqual(0.575f, s.VentBandLowFraction, 0.001f);
        }

        [Test]
        public void AtTheInstantTheWindowOpensNormalisedMatchesTheBandsHighEdge()
        {
            var s = Silenced();
            s.Refund(10f);
            s.Tick(2f); // window opens

            Assert.AreEqual(s.VentBandHighFraction, s.Normalised, 0.001f);
        }

        [Test]
        public void AHitDoesNotMoveTheBand()
        {
            var s = Silenced();
            s.Refund(10f);
            float highBefore = s.VentBandHighFraction;
            float lowBefore = s.VentBandLowFraction;

            s.Tick(2.2f); // inside the window
            Assert.AreEqual(VentResult.Hit, s.TryVent());

            Assert.AreEqual(highBefore, s.VentBandHighFraction, 0.0001f);
            Assert.AreEqual(lowBefore, s.VentBandLowFraction, 0.0001f);
        }

        [Test]
        public void ARefundAfterAHitNoLongerMovesTheBand()
        {
            var s = Silenced();
            s.Tick(2.2f);
            Assert.AreEqual(VentResult.Hit, s.TryVent());
            float highAfterHit = s.VentBandHighFraction;

            s.Refund(10f); // a later refund (e.g. another beam this same frame) must not move it

            Assert.AreEqual(highAfterHit, s.VentBandHighFraction, 0.0001f);
        }

        [Test]
        public void WithNoRefundTheBandStillMatchesTheWorkedExample()
        {
            // Same numbers as TheBandsHeatRangeMatchesTheWorkedExampleForTheDefaultNumbers -
            // guards that projecting from "band origin heat" instead of max changes nothing
            // for the ordinary no-refund case.
            var s = Silenced();

            Assert.AreEqual(0.875f, s.VentBandHighFraction, 0.001f);
            Assert.AreEqual(0.675f, s.VentBandLowFraction, 0.001f);
        }

        // ================================================================================
        // Vent random timing (Tudor, 2026-09-24: "lets keep it always 2s but the option to make
        // it random"). ventRandomTiming picks THIS silence's own vent delay from
        // [ventRandomDelayMin, ventRandomDelayMax] the instant a fresh silence starts (where
        // silenceClock resets in Add), from an injected 0..1 source so tests are deterministic.
        // Fixed mode (every test above this region) is unaffected: ventRandomTiming defaults to
        // false, and every one of those tests keeps passing unchanged.
        // ================================================================================

        private static OverheatState NewRandomState(float ventRandomDelayMin, float ventRandomDelayMax,
            System.Func<float> randomSource, float ventDelay = 2f, float ventWindow = 0.8f) => new OverheatState(
            max: 100f, decayDelay: 1.5f, decayPerSecond: 25f, warningThreshold: 80f,
            ventDelay: ventDelay, ventWindow: ventWindow, ventRandomTiming: true,
            ventRandomDelayMin: ventRandomDelayMin, ventRandomDelayMax: ventRandomDelayMax, randomSource: randomSource);

        [Test]
        public void FixedModeIgnoresAnInjectedRandomSourceEntirely()
        {
            // ventRandomTiming false must ignore the source even when one IS provided - not just
            // when it's null - so flipping the flag off in the Inspector is the only thing that
            // matters, nothing about whether a source happens to be wired up.
            var s = new OverheatState(max: 100f, decayDelay: 1.5f, decayPerSecond: 25f, warningThreshold: 80f,
                ventDelay: 2f, ventWindow: 0.8f, ventRandomTiming: false,
                ventRandomDelayMin: 1.5f, ventRandomDelayMax: 3f, randomSource: () => 1f); // would pick 3.0 if random were on
            s.Add(100f);

            Assert.AreEqual(2f, s.CurrentVentDelay, 0.001f);
        }

        [Test]
        public void RandomTimingSourceZeroPicksTheMinimumDelay()
        {
            var s = NewRandomState(1.5f, 3f, () => 0f);
            s.Add(100f);

            Assert.AreEqual(1.5f, s.CurrentVentDelay, 0.001f);
        }

        [Test]
        public void RandomTimingSourceOnePicksTheMaximumDelay()
        {
            var s = NewRandomState(1.5f, 3f, () => 1f);
            s.Add(100f);

            Assert.AreEqual(3f, s.CurrentVentDelay, 0.001f);
        }

        [Test]
        public void RandomTimingSourceHalfPicksTheMidpoint()
        {
            var s = NewRandomState(1.5f, 3f, () => 0.5f);
            s.Add(100f);

            Assert.AreEqual(2.25f, s.CurrentVentDelay, 0.001f);
        }

        [Test]
        public void ANewSilencePicksANewRandomDelay()
        {
            float[] values = { 0f, 1f };
            int call = 0;
            var s = NewRandomState(1.5f, 3f, () => values[call++]);

            s.Add(100f); // first silence - source returns 0 -> the minimum, 1.5
            Assert.AreEqual(1.5f, s.CurrentVentDelay, 0.001f);

            while (s.IsSilenced)
                s.Tick(0.5f); // run the whole silence out

            s.Add(100f); // second silence - source returns 1 -> the maximum, 3.0
            Assert.AreEqual(3f, s.CurrentVentDelay, 0.001f, "a new silence must pick its own, fresh delay");
        }

        [Test]
        public void PressBeforeThePickedWindowIsEarly()
        {
            var s = NewRandomState(1.5f, 3f, () => 1f); // picked delay = the maximum, 3.0
            s.Add(100f);
            s.Tick(2.9f); // past the fixed 2.0, but still short of the picked window's 3.0

            Assert.AreEqual(VentResult.Early, s.TryVent());
        }

        [Test]
        public void PressInsideThePickedWindowHits()
        {
            var s = NewRandomState(1.5f, 3f, () => 1f); // picked delay = 3.0, window [3.0, 3.8]
            s.Add(100f);
            s.Tick(3.2f);

            Assert.AreEqual(VentResult.Hit, s.TryVent());
        }

        [Test]
        public void PressAfterThePickedWindowIsLate()
        {
            var s = NewRandomState(1.5f, 3f, () => 1f); // picked delay = 3.0, window closes at 3.8
            s.Add(100f);
            s.Tick(4f);

            Assert.AreEqual(VentResult.Late, s.TryVent());
        }

        [Test]
        public void BandFractionsFollowThePickedDelay()
        {
            // max 100, decayDelay 1.5, 25/s, picked delay 3.0 (source=1, min 1.5, max 3), window 0.8:
            // high = HeatAtSilenceTime(3.0) = 100 - 25*(3.0-1.5) = 62.5 -> 0.625
            // low  = HeatAtSilenceTime(3.8) = 100 - 25*(3.8-1.5) = 42.5 -> 0.425
            var s = NewRandomState(1.5f, 3f, () => 1f);
            s.Add(100f);

            Assert.AreEqual(0.625f, s.VentBandHighFraction, 0.001f);
            Assert.AreEqual(0.425f, s.VentBandLowFraction, 0.001f);
        }

        [Test]
        public void ClearThenANewSilencePicksAgain()
        {
            float[] values = { 0f, 1f };
            int call = 0;
            var s = NewRandomState(1.5f, 3f, () => values[call++]);

            s.Add(100f);
            Assert.AreEqual(1.5f, s.CurrentVentDelay, 0.001f);

            s.Clear();
            s.Add(100f);
            Assert.AreEqual(3f, s.CurrentVentDelay, 0.001f, "Clear must free up a fresh pick for the next silence");
        }

        [Test]
        public void SwappedMinMaxUsesTheSmallerAsMinimum()
        {
            // ventRandomDelayMin=3, ventRandomDelayMax=1.5 (a designer swapped them) - source=0
            // still lands on the smaller value and source=1 on the larger, per the field's own
            // tooltip ("if swapped, the smaller value is always used as the minimum").
            var atZero = NewRandomState(3f, 1.5f, () => 0f);
            atZero.Add(100f);
            Assert.AreEqual(1.5f, atZero.CurrentVentDelay, 0.001f);

            var atOne = NewRandomState(3f, 1.5f, () => 1f);
            atOne.Add(100f);
            Assert.AreEqual(3f, atOne.CurrentVentDelay, 0.001f);
        }

        [Test]
        public void VentWindowZeroStillOffInRandomMode()
        {
            var s = NewRandomState(1.5f, 3f, () => 0.5f, ventWindow: 0f); // picked delay 2.25, window off
            s.Add(100f);

            Assert.IsFalse(s.VentEnabled);
            s.Tick(2.25f); // exactly the picked delay - where the window would open if it could
            Assert.IsFalse(s.IsVentWindowOpen);
            Assert.AreEqual(VentResult.Late, s.TryVent());
        }
    }
}
