using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>
    /// captureFadeSpeed (Tudor, 2026-09-24: "when someone is capturing and steps outside while the zone isnt fully
    /// captured the zone slowly becomes neutral/what it previously was instead of snapping to the previous state").
    /// Pure math + the one decision (is the claiming team actually absent right now) Building capture.cs's
    /// CalculateCaptureProgress/HandleCapturedState wire together - see their own comments for exactly where.
    /// Uses its own numbers throughout, never TerritoryConfig.asset's (the project rule since 91eceb4).
    /// </summary>
    public class CaptureFadeRuleTests
    {
        [Test]
        public void NobodyOfTheClaimingTeamIsAbsent()
        {
            Assert.IsTrue(CaptureFadeRule.CapturingTeamAbsent(0, new List<int>()));
        }

        [Test]
        public void OnlyAnotherTeamMeansTheClaimingTeamIsAbsent()
        {
            Assert.IsTrue(CaptureFadeRule.CapturingTeamAbsent(0, new List<int> { 1 }));
        }

        [Test]
        public void TheClaimingTeamStandingThereAloneIsNotAbsent()
        {
            Assert.IsFalse(CaptureFadeRule.CapturingTeamAbsent(0, new List<int> { 0 }));
        }

        [Test]
        public void ContestedTheClaimingTeamIsStillNotAbsent()
        {
            // Both teams inside: the brief's "contested (unchanged: holds)" - CapturingTeamAbsent must read false
            // here so the caller falls through to the ordinary Held/contested path, not the fade.
            Assert.IsFalse(CaptureFadeRule.CapturingTeamAbsent(0, new List<int> { 0, 1 }));
        }

        [Test]
        public void AnUnclaimedZoneIsNeverAbsent()
        {
            // capturingId -1 (never claimed) is CaptureClaimRule's job, not a fade - nothing to fade from.
            Assert.IsFalse(CaptureFadeRule.CapturingTeamAbsent(-1, new List<int> { 1 }));
            Assert.IsFalse(CaptureFadeRule.CapturingTeamAbsent(-1, new List<int>()));
        }

        [Test]
        public void StepFadesTowardZeroAtTheGivenRate()
        {
            // The test's own numbers (not TerritoryConfig.asset's): rate 2 one-player-seconds/s, half a second.
            Assert.AreEqual(9f, CaptureFadeRule.Step(10f, 2f, 0.5f), 1e-5f);
        }

        [Test]
        public void StepNeverGoesBelowZero()
        {
            Assert.AreEqual(0f, CaptureFadeRule.Step(1f, 5f, 1f), 1e-5f);
        }

        [Test]
        public void StepAtZeroRateHoldsWhereItWas()
        {
            // captureFadeSpeed 0 = "it holds where it was" (the field's own tooltip).
            Assert.AreEqual(6.25f, CaptureFadeRule.Step(6.25f, 0f, 10f), 1e-5f);
        }

        [Test]
        public void RefillGrowsTowardFullAtTheGivenRate()
        {
            Assert.AreEqual(6f, CaptureFadeRule.Refill(5f, 12.5f, 2f, 0.5f), 1e-5f);
        }

        [Test]
        public void RefillNeverPassesCaptureSeconds()
        {
            Assert.AreEqual(12.5f, CaptureFadeRule.Refill(12f, 12.5f, 5f, 1f), 1e-5f);
        }

        [Test]
        public void RefillAtZeroRateHoldsWhereItWas()
        {
            Assert.AreEqual(6.25f, CaptureFadeRule.Refill(6.25f, 12.5f, 0f, 10f), 1e-5f);
        }

        [Test]
        public void AFadedClaimResumesFromWhereItLeftOffOnceTheTeamReturns()
        {
            // 10 one-player-seconds banked, rate 2/s, faded for 1.5s of absence (a real step loop would call this
            // once per frame; one big step is equivalent for a pure function with no side effects).
            float progress = CaptureFadeRule.Step(10f, 2f, 1.5f);
            Assert.AreEqual(7f, progress, 1e-5f);

            // The team steps back in: CapturingTeamAbsent goes false, so the caller stops fading and instead runs
            // the ordinary CaptureClaimRule.Resolve - which, since the claiming team is listed again, keeps exactly
            // what fading left behind rather than resetting it.
            var teamsInZone = new List<int> { 0 };
            Assert.IsFalse(CaptureFadeRule.CapturingTeamAbsent(0, teamsInZone));
            var (capturingId, resumed) = CaptureClaimRule.Resolve(0, progress, teamsInZone);
            Assert.AreEqual(0, capturingId);
            Assert.AreEqual(7f, resumed, 1e-5f, "resumes from the faded value, not from 0");
        }

        [Test]
        public void AnotherTeamAlonePushesTheOldClaimDownThenTakesOverAtZero()
        {
            var onlyTeam1 = new List<int> { 1 };
            Assert.IsTrue(CaptureFadeRule.CapturingTeamAbsent(0, onlyTeam1));

            // Fades all the way to 0 while only team 1 stands there (three ticks of a 1s-worth rate).
            float progress = 3f;
            for (int i = 0; i < 3; i++)
                progress = CaptureFadeRule.Step(progress, 1f, 1f);
            Assert.AreEqual(0f, progress, 1e-5f);

            // Reaching 0 is the caller's cue to resolve fresh (capturingId -1) from whoever is actually listed -
            // team 1 takes the claim, from 0, exactly like an unset claim's first entry.
            var (capturingId, fresh) = CaptureClaimRule.Resolve(-1, 0f, onlyTeam1);
            Assert.AreEqual(1, capturingId);
            Assert.AreEqual(0f, fresh);
        }
    }
}
