using NUnit.Framework;
using Overpower.Dominion;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 4: a capital cannot be taken or drained in Dominion (it still counts as held), and nobody earns or sees gold.</summary>
    public class DominionTerritoryRulesTests
    {
        [Test] public void ACapitalCanBeCapturedInConquestButNotInDominion()
        {
            Assert.IsTrue(DominionTerritoryRules.IsCapturable(isDominion: false, isCapital: true));
            Assert.IsFalse(DominionTerritoryRules.IsCapturable(isDominion: true, isCapital: true));
        }

        [Test] public void AnyOtherZoneCanBeCapturedInBothModes()
        {
            Assert.IsTrue(DominionTerritoryRules.IsCapturable(isDominion: false, isCapital: false));
            Assert.IsTrue(DominionTerritoryRules.IsCapturable(isDominion: true, isCapital: false));
        }

        [Test] public void AZoneThatPaysGoldInConquestPaysNothingInDominion()
        {
            // Team 0 owns zones 0 and 1 (tiers 1 and 2) and not zone 2: 5 + 10 gold a second in Conquest.
            int conquest = GoldMath.TeamIncomePerSecond(0, new[] { 0, 0, 1 }, new[] { 1, 2, 3 }, new[] { 5, 10, 20 });
            Assert.AreEqual(15, conquest, "the made-up Conquest income to compare against");
            Assert.AreEqual(15, DominionTerritoryRules.TeamIncomeFor(isDominion: false, conquest));
            Assert.AreEqual(0, DominionTerritoryRules.TeamIncomeFor(isDominion: true, conquest));
        }

        [Test] public void ACaptureBountyPaysGoldInConquestButNotInDominion()
        {
            Assert.AreEqual(900, DominionTerritoryRules.BountyGoldFor(isDominion: false, 900));
            Assert.AreEqual(0, DominionTerritoryRules.BountyGoldFor(isDominion: true, 900));
        }

        [Test] public void TheGoldReadoutIsShownInConquestAndHiddenInDominion()
        {
            Assert.IsTrue(DominionTerritoryRules.ShowsGold(isDominion: false));
            Assert.IsFalse(DominionTerritoryRules.ShowsGold(isDominion: true));
        }
    }
}
