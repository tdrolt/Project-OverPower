using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>GDD p.20-21 and p.27 (Tudor, 2026-09-25): the knocked-out team's corner closes. Uses the real arena's
    /// links (the same copy as TerritoryMapTests) and a shuffled-id map, so the rule is shown to follow the links and the
    /// towers' tiers, never zone numbers.</summary>
    public class PhaseTwoCutRulesTests
    {
        private static TerritoryMap RealMap() => new TerritoryMap(
            new List<(int, IEnumerable<int>)>
            {
                (0, new[] { 6, 3, 5 }), (1, new[] { 7, 3, 4 }), (2, new[] { 8, 4, 5 }),
                (3, new[] { 0, 1 }), (4, new[] { 1, 2 }), (5, new[] { 0, 2 }),
                (6, new[] { 0 }), (7, new[] { 1 }), (8, new[] { 2 }),
                (9, new[] { 0, 1, 2 }),
            },
            new List<(int, int)> { (6, 0), (7, 1), (8, 2) });

        // The scene's own tiers: 0-2 Tier II, 3-5 Tier III, 6-8 capitals, 9 the centre.
        private static int RealTier(int zone) => zone <= 2 ? 2 : zone <= 5 ? 3 : zone <= 8 ? 1 : zone == 9 ? 4 : 0;

        [Test]
        public void NothingIsCutBeforeTheMatchIsLive()
        {
            Assert.AreEqual(PhaseTwoCutRules.NoCut, PhaseTwoCutRules.CutTeam(live: false, new[] { 0, 1 }, storedCutTeam: 2));
            Assert.AreEqual(PhaseTwoCutRules.NoCut, PhaseTwoCutRules.CutTeam(false, new int[0], PhaseTwoCutRules.NoCut));
        }

        [Test]
        public void AHostStartCutsTheTeamLeftOutOfTheMatch()
        {
            Assert.AreEqual(1, PhaseTwoCutRules.CutTeam(true, new[] { 0, 2 }, PhaseTwoCutRules.NoCut));
            Assert.AreEqual(2, PhaseTwoCutRules.CutTeam(true, new[] { 0, 1 }, PhaseTwoCutRules.NoCut));
        }

        [Test]
        public void ThreeTeamsAreUncutUntilTheMasterStoresAKnockout()
        {
            Assert.AreEqual(PhaseTwoCutRules.NoCut, PhaseTwoCutRules.CutTeam(true, new[] { 0, 1, 2 }, PhaseTwoCutRules.NoCut));
            Assert.AreEqual(1, PhaseTwoCutRules.CutTeam(true, new[] { 0, 1, 2 }, storedCutTeam: 1));
        }

        [Test]
        public void TheCutTakesTheCapitalItsTierTwoAndTheTierThreesNextToIt()
        {
            TerritoryMap map = RealMap();
            CollectionAssert.AreEqual(new[] { 2, 4, 5, 8 }, PhaseTwoCutRules.CutZones(map, 2, RealTier));
            CollectionAssert.AreEqual(new[] { 0, 3, 5, 6 }, PhaseTwoCutRules.CutZones(map, 0, RealTier));
            CollectionAssert.AreEqual(new[] { 1, 3, 4, 7 }, PhaseTwoCutRules.CutZones(map, 1, RealTier));
        }

        [Test]
        public void IsZoneCutAgreesWithCutZones()
        {
            TerritoryMap map = RealMap();
            for (int team = 0; team < 3; team++)
            {
                List<int> cut = PhaseTwoCutRules.CutZones(map, team, RealTier);
                for (int zone = 0; zone <= 9; zone++)
                    Assert.AreEqual(cut.Contains(zone), PhaseTwoCutRules.IsZoneCut(map, zone, team, RealTier), $"team {team}, zone {zone}");
            }
        }

        [Test]
        public void TheCentreIsNeverCut()
        {
            TerritoryMap map = RealMap();
            for (int team = 0; team < 3; team++)
                Assert.IsFalse(PhaseTwoCutRules.IsZoneCut(map, 9, team, RealTier));
        }

        [Test]
        public void NoCutMeansNoZones()
        {
            TerritoryMap map = RealMap();
            CollectionAssert.IsEmpty(PhaseTwoCutRules.CutZones(map, PhaseTwoCutRules.NoCut, RealTier));
            Assert.IsFalse(PhaseTwoCutRules.IsZoneCut(map, 8, PhaseTwoCutRules.NoCut, RealTier));
        }

        [Test]
        public void TheCutFollowsTheLinksNotTheZoneNumbers()
        {
            // Team 0's capital is zone 0, its Tier II is 5, which links to Tier III 7 and to the centre 3 (Tier IV).
            var map = new TerritoryMap(
                new List<(int, IEnumerable<int>)> { (0, new[] { 5 }), (5, new[] { 7, 3 }), (7, new int[0]), (3, new int[0]) },
                new List<(int, int)> { (0, 0) });
            int Tier(int zone) => zone == 0 ? 1 : zone == 5 ? 2 : zone == 7 ? 3 : zone == 3 ? 4 : 0;
            CollectionAssert.AreEqual(new[] { 0, 5, 7 }, PhaseTwoCutRules.CutZones(map, 0, Tier));
        }

        [Test]
        public void TierFourPlaysAsTierThreeOnlyWhileACornerIsCut()
        {
            Assert.AreEqual(3, PhaseTwoCutRules.EffectiveTier(4, cutActive: true));
            Assert.AreEqual(4, PhaseTwoCutRules.EffectiveTier(4, cutActive: false));
            for (int tier = 0; tier <= 3; tier++)
                Assert.AreEqual(tier, PhaseTwoCutRules.EffectiveTier(tier, true), $"tier {tier} never changes");
        }

        [Test]
        public void TheKnockoutNeutralisesEveryTierThreeTheCentreAndTheCutCorner()
        {
            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5, 8, 9 },
                PhaseTwoCutRules.ZonesToNeutralise(RealMap(), 2, RealTier, zoneCount: 10));
            CollectionAssert.AreEqual(new[] { 3, 4, 5, 9 },
                PhaseTwoCutRules.ZonesToNeutralise(RealMap(), PhaseTwoCutRules.NoCut, RealTier, 10), "no cut: the old Tier III reset plus the centre");
        }

        // ownerOfCapital[team] = who holds that team's own starting capital (-1 = nobody).
        private static int Cut(int knockedOut, int[] survivors, params int[] ownerOfCapital) =>
            PhaseTwoCutRules.ChooseCutTeam(knockedOut, survivors, team => ownerOfCapital[team]);

        [Test]
        public void TheKnockedOutTeamsCornerClosesWhenThatStrandsNobody()
        {
            Assert.AreEqual(2, Cut(2, new[] { 0, 1 }, 0, 1, -1), "its capital is neutral");
            Assert.AreEqual(2, Cut(2, new[] { 0, 1 }, 0, 1, 0), "team 0 took it but still holds its own");
        }

        [Test]
        public void ASurvivorLivingInTheKnockedOutCornerKeepsIt()
        {
            // Team 0 lost its own capital to team 1 and lives in team 2's. Team 1 holds its own and team 0's.
            // Team 0's old corner is the one nobody lives in.
            Assert.AreEqual(0, Cut(2, new[] { 0, 1 }, 1, 1, 0));
            // Team 0's old capital is neutral: same answer.
            Assert.AreEqual(0, Cut(2, new[] { 0, 1 }, -1, 1, 0));
        }

        [Test]
        public void ACornerAnotherSurvivorLivesInIsNeverTheAlternative()
        {
            // Team 0 lives only in team 2's capital; team 1 lives only in team 0's; team 1's own capital is empty.
            Assert.AreEqual(1, Cut(2, new[] { 0, 1 }, 1, -1, 0));
        }

        [Test]
        public void NoKnockedOutTeamMeansNoCut()
        {
            Assert.AreEqual(PhaseTwoCutRules.NoCut, Cut(PhaseTwoCutRules.NoCut, new[] { 0, 1 }, 0, 1, 2));
        }
    }
}
