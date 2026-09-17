using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class CaptureClaimRuleTests
    {
        [Test]
        public void AStaleClaimWithNobodyOfThatTeamListedGoesToTheFirstListedTeamFromZero()
        {
            // capturingID 1 with only team 0 listed: a remote trigger graze (or any other stale write)
            // must not lock team 0 out - the claim moves to the team that is actually here, from 0.
            var (capturingId, captureProgress) = CaptureClaimRule.Resolve(1, 7f, new List<int> { 0 });
            Assert.AreEqual(0, capturingId);
            Assert.AreEqual(0f, captureProgress);
        }

        [Test]
        public void AContestedClaimKeepsItsTeamAndBankedProgress()
        {
            var (capturingId, captureProgress) = CaptureClaimRule.Resolve(0, 3f, new List<int> { 0, 1 });
            Assert.AreEqual(0, capturingId);
            Assert.AreEqual(3f, captureProgress);
        }

        [Test]
        public void NobodyListedClearsTheClaim()
        {
            var (capturingId, captureProgress) = CaptureClaimRule.Resolve(0, 5f, new List<int>());
            Assert.AreEqual(-1, capturingId);
            Assert.AreEqual(0f, captureProgress);
        }

        [Test]
        public void AnUnsetClaimGoesToTheFirstListedTeam()
        {
            var (capturingId, captureProgress) = CaptureClaimRule.Resolve(-1, 0f, new List<int> { 1, 0 });
            Assert.AreEqual(1, capturingId);
            Assert.AreEqual(0f, captureProgress);
        }

        [Test]
        public void AClaimResolvingIntoAPublishRuleChainNeverGoesIdleWhileContested()
        {
            // The bug's real shape, end to end: team 0 alone starts a solo capture (rate > 0), then an
            // enemy joins the zone (contested) - the claim rule must keep team 0 and its banked
            // progress, and the publish rule must read that as Held, never Idle, so the ring/telemetry
            // never sees the capture silently vanish.
            var (capturingId, captureProgress) = CaptureClaimRule.Resolve(-1, 0f, new List<int> { 0 });
            Assert.AreEqual(0, capturingId);

            CaptureProgress capturing = CaptureProgressPublishRule.Decide(false, false, false, 15f, 5f, false,
                capturingId, eligibleCount: 1, enemyPresent: false, mayCaptureNow: true, captureProgress: 3f, nowMs: 1000);
            Assert.AreEqual(0, capturing.Team);
            Assert.IsTrue(capturing.RatePerSecond01 > 0f);

            (capturingId, captureProgress) = CaptureClaimRule.Resolve(capturingId, 3f, new List<int> { 0, 1 });
            Assert.AreEqual(0, capturingId, "team 0 is still listed, so the contested claim stays theirs");
            Assert.AreEqual(3f, captureProgress, "and its banked progress must not reset just because it's contested");

            CaptureProgress contested = CaptureProgressPublishRule.Decide(false, false, false, 15f, 5f, false,
                capturingId, eligibleCount: 1, enemyPresent: true, mayCaptureNow: true, captureProgress: captureProgress, nowMs: 2000);
            Assert.AreEqual(0, contested.Team);
            Assert.IsTrue(contested.IsHeld);
            Assert.Greater(contested.Progress01, 0f);
        }
    }
}
