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

        // --------------------------------------------------------- review fix: fade speed 0 locking a neutral
        // zone to the first team forever once ANOTHER team stands there alone (2026-09-24). Decided: it pushes the
        // old claim down at max(fadeRate, N x ProgressPerPlayerPerSecond), N = that team's player count - never
        // slower than their own capture speed, and N players push N times as fast, like capturing. The plain fade
        // (nobody inside at all) is unaffected: it still keeps fadeRate exactly.
        //
        // --------------------------------------------------------- opus re-review, same day: EffectiveFadeRate
        // (above) counted every non-claim player, so two DIFFERENT enemy teams fighting inside a faded claim
        // pushed it at their combined speed, and it ignored TeamMayCaptureNow, so a team whose only way in was
        // itself under attack still pushed the claim down. Replaced by NeutralFadeRate, which takes the zone's
        // actual team list and a "may this team capture right now" delegate it asks about the single pusher
        // itself, instead of a raw headcount or a caller-worked-out bool - see its own doc comment for the full
        // decision table.

        [Test]
        public void SinglePushingTeamIsMinusOneWhenTheZoneIsEmpty()
        {
            Assert.AreEqual(-1, CaptureFadeRule.SinglePushingTeam(0, new List<int>()));
        }

        [Test]
        public void SinglePushingTeamIsMinusOneWithOnlyTheClaimTeamPresent()
        {
            Assert.AreEqual(-1, CaptureFadeRule.SinglePushingTeam(0, new List<int> { 0, 0 }));
        }

        [Test]
        public void SinglePushingTeamIsTheOtherTeamWhenItStandsAloneWithSeveralPlayers()
        {
            Assert.AreEqual(2, CaptureFadeRule.SinglePushingTeam(1, new List<int> { 2, 2, 2 }));
        }

        [Test]
        public void SinglePushingTeamIgnoresTheClaimTeamsOwnPlayersMixedIn()
        {
            // Not a shape either production caller ever passes (they only call this once CapturingTeamAbsent is
            // true), but the pure function is well-defined here too: it skips claimTeam's own entries.
            Assert.AreEqual(2, CaptureFadeRule.SinglePushingTeam(1, new List<int> { 1, 2, 2 }));
        }

        [Test]
        public void SinglePushingTeamIsMinusOneWithTwoDifferentOtherTeams()
        {
            Assert.AreEqual(-1, CaptureFadeRule.SinglePushingTeam(1, new List<int> { 2, 3 }));
        }

        [Test]
        public void NeutralFadeRateWithNobodyElseIsThePlainFadeRateUnchanged()
        {
            Assert.AreEqual(0f, CaptureFadeRule.NeutralFadeRate(0f, claimTeam: 1, new List<int>(), perPlayerSpeed: 1f, mayCapture: _ => true));
            Assert.AreEqual(2f, CaptureFadeRule.NeutralFadeRate(2f, claimTeam: 1, new List<int>(), perPlayerSpeed: 1f, mayCapture: _ => true));
        }

        [Test]
        public void NeutralFadeRateWithTheClaimTeamStillPresentIsThePlainFadeRate()
        {
            // The claim team standing there (alone, or contested with an enemy too) is never a fade at all - the
            // caller's ordinary capture/contest logic runs instead (CapturingTeamAbsent gates it out before this
            // is ever called in production); this pure function still returns a well-defined, harmless value.
            Assert.AreEqual(2f, CaptureFadeRule.NeutralFadeRate(2f, claimTeam: 1, new List<int> { 1 }, perPlayerSpeed: 1f, mayCapture: _ => true));
            Assert.AreEqual(2f, CaptureFadeRule.NeutralFadeRate(2f, claimTeam: 1, new List<int> { 1, 2 }, perPlayerSpeed: 1f, mayCapture: _ => true));
        }

        [Test]
        public void NeutralFadeRateAtSpeedOneWithOnePlayerIsUnchangedFromToday()
        {
            // fadeRate already at one player's own speed: max(1, 1) is still 1 - no regression for the
            // default/common case (captureFadeSpeed 1, one enemy standing alone who may capture).
            Assert.AreEqual(1f, CaptureFadeRule.NeutralFadeRate(1f, claimTeam: 1, new List<int> { 2 }, perPlayerSpeed: 1f, mayCapture: _ => true));
        }

        [Test]
        public void NeutralFadeRateAtSpeedZeroStillPushesAtTheOtherTeamsOwnCaptureSpeed()
        {
            // The "important" fix itself: speed 0 must not hold forever once another team is actually here alone
            // and may capture.
            Assert.AreEqual(1f, CaptureFadeRule.NeutralFadeRate(0f, claimTeam: 1, new List<int> { 2 }, perPlayerSpeed: 1f, mayCapture: _ => true));
        }

        [Test]
        public void NeutralFadeRateForThreePlayersIsThreeTimesOnePlayersWhenThatBeatsTheFadeRate()
        {
            float rateForOne = CaptureFadeRule.NeutralFadeRate(0.5f, claimTeam: 1, new List<int> { 2 }, perPlayerSpeed: 1f, mayCapture: _ => true);
            float rateForThree = CaptureFadeRule.NeutralFadeRate(0.5f, claimTeam: 1, new List<int> { 2, 2, 2 }, perPlayerSpeed: 1f, mayCapture: _ => true);
            Assert.AreEqual(1f, rateForOne, 1e-5f);
            Assert.AreEqual(3f, rateForThree, 1e-5f);
            Assert.AreEqual(3f, rateForThree / rateForOne, 1e-5f, "three push three times as fast as one, like capturing");
        }

        [Test]
        public void NeutralFadeRateNeverGoesBelowTheConfiguredFadeRate()
        {
            // A slow lone enemy (0.2 progress/s) must never slow the fade below the configured speed.
            Assert.AreEqual(2f, CaptureFadeRule.NeutralFadeRate(2f, claimTeam: 1, new List<int> { 2 }, perPlayerSpeed: 0.2f, mayCapture: _ => true));
        }

        [Test]
        public void NeutralFadeRateWithTwoDifferentOtherTeamsIsThePlainFadeRateNotTheirCombinedSpeed()
        {
            // Review fix (minor 2): two DIFFERENT enemy teams fighting inside a faded claim (3 total players, two
            // different teams) must NOT push at their combined headcount - contested among the pushers, plain
            // fade only, same as EffectiveFadeRate's old bug (N counted every non-claim player regardless of team).
            float rate = CaptureFadeRule.NeutralFadeRate(0.5f, claimTeam: 1, new List<int> { 2, 2, 3 }, perPlayerSpeed: 1f, mayCapture: _ => true);
            Assert.AreEqual(0.5f, rate);
        }

        [Test]
        public void NeutralFadeRateWhenTheSinglePusherMayNotCaptureIsThePlainFadeRate()
        {
            // Review fix (minor 3): a lone pushing team whose only way in is itself under attack (TeamMayCaptureNow
            // false) must not push the claim down at all - plain fade only, same as it can't capture the zone.
            float rate = CaptureFadeRule.NeutralFadeRate(0.5f, claimTeam: 1, new List<int> { 2, 2, 2 }, perPlayerSpeed: 1f, mayCapture: _ => false);
            Assert.AreEqual(0.5f, rate);
        }

        [Test]
        public void NeutralFadeRateAsksMayCaptureAboutThePushingTeamNeverTheClaimTeam()
        {
            // Opus re-review, 2026-09-24 (second pass): "the wiring still isn't tested" - both production call
            // sites used to compute SinglePushingTeam and TeamMayCaptureNow(pushingTeam) themselves, copied at
            // both sites; nothing caught a call site asking TeamMayCaptureNow(claimTeam) instead, or a call site
            // reverted to the plain fadeRate. NeutralFadeRate now picks the pushing team itself and asks the
            // delegate about THAT team, so a recording delegate here proves it directly instead of trusting each
            // call site to get it right.
            var askedTeams = new List<int>();
            bool RecordAndAllow(int team)
            {
                askedTeams.Add(team);
                return true;
            }

            float rate = CaptureFadeRule.NeutralFadeRate(0.5f, claimTeam: 1, new List<int> { 2, 2, 2 },
                perPlayerSpeed: 1f, RecordAndAllow);

            Assert.AreEqual(3f, rate, 1e-5f);
            CollectionAssert.AreEqual(new[] { 2 }, askedTeams,
                "must ask exactly once, about the pushing team (2), never the claim team (1)");
        }

        [Test]
        public void FadeSpeedZeroWithAnotherTeamAloneStillReachesZeroAndHandsOver()
        {
            // Speed 0, one enemy player alone in the zone who may capture: must still reach 0 (not hold forever)
            // and let CaptureClaimRule.Resolve hand the claim to the team actually there - the bug this fixes.
            float rate = CaptureFadeRule.NeutralFadeRate(0f, claimTeam: 0, new List<int> { 1 }, perPlayerSpeed: 1f, mayCapture: _ => true);
            float progress = 2f;
            for (int i = 0; i < 2; i++)
                progress = CaptureFadeRule.Step(progress, rate, 1f);
            Assert.AreEqual(0f, progress, 1e-5f);

            var (capturingId, fresh) = CaptureClaimRule.Resolve(-1, 0f, new List<int> { 1 });
            Assert.AreEqual(1, capturingId);
            Assert.AreEqual(0f, fresh);
        }
    }
}
