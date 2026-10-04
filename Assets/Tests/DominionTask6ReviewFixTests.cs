using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Dominion;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>
    /// Dominion Task 6 review fixes: the decisions the game makes in PlayerHealth, SpawnHealArea, PlayerLifecycle, the capture tower and the shop
    /// header now live in one pure function each, and the component calls it (no second copy of the branching). Every number is a literal made up
    /// for the test. Where a call site cannot be unit-tested the report names the recorder row that proves it.
    /// </summary>
    public class DominionTask6ReviewFixTests
    {
        // ---- the spawn: a registered area, or (3v3v3 only) the team's own capital circle

        [Test] public void InThreeTeamsTheOwnCapitalIsTheSpawn() => Assert.IsTrue(DominionHealRules.InOwnSpawn(false, 3, true));
        [Test] public void InTwoTeamsTheCapitalCircleIsNotASpawn() => Assert.IsFalse(DominionHealRules.InOwnSpawn(false, 2, true));
        [Test] public void AnEnemyCapitalIsNeverTheSpawn()
        {
            Assert.IsFalse(DominionHealRules.InOwnSpawn(false, 3, false));
            Assert.IsFalse(DominionHealRules.InOwnSpawn(false, 2, false));
        }
        [Test] public void ARegisteredAreaIsTheSpawnInEitherSize()
        {
            Assert.IsTrue(DominionHealRules.InOwnSpawn(true, 2, false));
            Assert.IsTrue(DominionHealRules.InOwnSpawn(true, 3, false));
        }

        // ---- a 2v2 match that goes live with a team lacking its healing area

        [Test] public void AMissingAreaIsNamedInTwoTeams()
        {
            List<int> missing = DominionHealRules.TeamsMissingHealArea(new[] { 0, 1 }, team => team == 0);
            CollectionAssert.AreEqual(new[] { 1 }, missing);
        }
        [Test] public void NoTeamIsMissingWhenEachHasAnArea() =>
            CollectionAssert.IsEmpty(DominionHealRules.TeamsMissingHealArea(new[] { 0, 1 }, team => true));
        [Test] public void ThreeTeamsNeverNeedAreas() =>
            CollectionAssert.IsEmpty(DominionHealRules.TeamsMissingHealArea(new[] { 0, 1, 2 }, team => false));

        // ---- the warm-up stays a sandbox (A22)

        [Test] public void DominionRulesApplyOnlyInADominionRoomOnceLive()
        {
            Assert.IsTrue(DominionRules.RulesApply(true, true));
            Assert.IsFalse(DominionRules.RulesApply(true, false), "the warm-up");
            Assert.IsFalse(DominionRules.RulesApply(false, true), "Conquest");
            Assert.IsFalse(DominionRules.RulesApply(false, false));
        }

        // ---- the one respawn decision (death-scaled, fixed, rejoin; literal numbers: base 5, +1 per death, cap 10, rejoin 3, fixed 7)

        private static float Wait(bool dominionLive, bool rejoin, int deaths) => RespawnDelayRules.WaitFor(dominionLive, rejoin, 7f, deaths, 5f, 1f, 10f, 3f);

        [Test] public void ALiveDominionDeathWaitsTheFixedTimeWhateverTheCount()
        {
            Assert.AreEqual(7f, Wait(true, false, 1));
            Assert.AreEqual(7f, Wait(true, false, 9));
        }
        [Test] public void ALiveDominionRejoinerWaitsTheFixedTimeToo() => Assert.AreEqual(7f, Wait(true, true, 4));
        [Test] public void OutsideLiveDominionTheWaitGrowsWithDeathsUpToTheCap()
        {
            Assert.AreEqual(5f, Wait(false, false, 1), "Conquest and the Dominion warm-up");
            Assert.AreEqual(8f, Wait(false, false, 4));
            Assert.AreEqual(10f, Wait(false, false, 9));
        }
        [Test] public void OutsideLiveDominionARejoinerWaitsTheFlatRejoinTime() => Assert.AreEqual(3f, Wait(false, true, 4));

        // ---- the capture tower's "does this neighbour close the link" question

        [Test] public void ACapturableZoneUnderAttackClosesTheLink() =>
            Assert.IsTrue(ZoneThreat.ZoneClosesLink(4, zone => true, zone => true));
        [Test] public void ACapitalUnderAttackWarnsButNeverClosesTheLink() =>
            Assert.IsFalse(ZoneThreat.ZoneClosesLink(6, zone => zone != 6, zone => true));
        [Test] public void AZoneNotUnderAttackClosesNothing() =>
            Assert.IsFalse(ZoneThreat.ZoneClosesLink(4, zone => true, zone => false));
        [Test] public void BothAnswersAreAskedAboutTheSameZone()
        {
            var asked = new List<string>();
            ZoneThreat.ZoneClosesLink(5, zone => { asked.Add("capturable " + zone); return true; }, zone => { asked.Add("attack " + zone); return true; });
            CollectionAssert.AreEquivalent(new[] { "capturable 5", "attack 5" }, asked);
        }

        // ---- the shop header's free note

        [Test] public void TheHeaderUsesTheLateJoinersWordsWhileTheirPickIsOpenMidRound()
        {
            Assert.AreEqual("late", DominionShopRules.HeaderFreeText(DominionStage.Round, true, "break", "late"));
            Assert.AreEqual("late", DominionShopRules.HeaderFreeText(DominionStage.SuddenDeath, true, "break", "late"));
        }
        [Test] public void TheHeaderUsesTheBreaksWordsOtherwise()
        {
            Assert.AreEqual("break", DominionShopRules.HeaderFreeText(DominionStage.Break, true, "break", "late"));
            Assert.AreEqual("break", DominionShopRules.HeaderFreeText(DominionStage.Round, false, "break", "late"));
        }
    }
}
