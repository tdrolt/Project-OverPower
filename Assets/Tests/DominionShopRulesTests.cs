using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Combat;
using Overpower.Dominion;
using Overpower.Match;

namespace Overpower.Tests
{
    /// <summary>Dominion Task 1: what the shop opens each round. Tables are literal test values, never the asset.</summary>
    public class DominionShopRulesTests
    {
        private static readonly int[] Depth = { 0, 1, 2 };
        private static readonly int[] Armor = { 0, 1, 2 };

        [Test] public void RoundOneAllowsOnlyTheBaselineAndNoArmorUpgrade()
        {
            Assert.AreEqual(0, DominionShopRules.MaxWeaponDepth(1, Depth));
            Assert.AreEqual(0, DominionShopRules.ArmorUpgradesAllowed(1, Armor));
            Assert.IsTrue(DominionShopRules.WeaponAllowed(0, 1, Depth));
            Assert.IsFalse(DominionShopRules.WeaponAllowed(1, 1, Depth));
        }

        [Test] public void RoundTwoAllowsAFamilyAndOneUpgrade()
        {
            Assert.AreEqual(1, DominionShopRules.MaxWeaponDepth(2, Depth));
            Assert.AreEqual(1, DominionShopRules.ArmorUpgradesAllowed(2, Armor));
            Assert.IsTrue(DominionShopRules.WeaponAllowed(1, 2, Depth));
            Assert.IsFalse(DominionShopRules.WeaponAllowed(2, 2, Depth));
        }

        [Test] public void RoundThreeAllowsAFamilysUpgradeAndTwoUpgrades()
        {
            Assert.AreEqual(2, DominionShopRules.MaxWeaponDepth(3, Depth));
            Assert.AreEqual(2, DominionShopRules.ArmorUpgradesAllowed(3, Armor));
            Assert.IsTrue(DominionShopRules.WeaponAllowed(2, 3, Depth));
        }

        [Test] public void ARoundPastTheTableUsesTheLastEntryAndRoundBelowOneTheFirst()
        {
            Assert.AreEqual(2, DominionShopRules.MaxWeaponDepth(9, Depth));
            Assert.AreEqual(2, DominionShopRules.ArmorUpgradesAllowed(9, Armor));
            Assert.AreEqual(0, DominionShopRules.MaxWeaponDepth(0, Depth));
            Assert.AreEqual(0, DominionShopRules.ArmorUpgradesAllowed(-3, Armor));
        }

        [Test] public void AnEmptyTableAllowsNothingBeyondTheBaseline()
        {
            Assert.AreEqual(0, DominionShopRules.MaxWeaponDepth(2, null));
            Assert.AreEqual(0, DominionShopRules.ArmorUpgradesAllowed(2, new int[0]));
        }

        [Test] public void PickingIsAllowedInTheBreakOnlyPlusALateJoinersFirstPick()
        {
            Assert.IsTrue(DominionShopRules.MayPick(DominionStage.Break, false));
            Assert.IsFalse(DominionShopRules.MayPick(DominionStage.Round, false));
            Assert.IsFalse(DominionShopRules.MayPick(DominionStage.SuddenDeath, false));
            Assert.IsFalse(DominionShopRules.MayPick(DominionStage.Over, false));
            Assert.IsFalse(DominionShopRules.MayPick(DominionStage.None, false));
            Assert.IsTrue(DominionShopRules.MayPick(DominionStage.Round, true));
        }

        [Test] public void TheLockedLabelNamesTheFirstRoundThatOpensTheDepth()
        {
            Assert.AreEqual(1, DominionShopRules.FirstRoundAllowingDepth(0, Depth));
            Assert.AreEqual(2, DominionShopRules.FirstRoundAllowingDepth(1, Depth));
            Assert.AreEqual(3, DominionShopRules.FirstRoundAllowingDepth(2, Depth));
            Assert.AreEqual(-1, DominionShopRules.FirstRoundAllowingDepth(3, Depth));
            Assert.AreEqual(-1, DominionShopRules.FirstRoundAllowingDepth(-1, Depth), "an unknown weapon is opened by no round");
            Assert.AreEqual("Round 2", DominionShopRules.LockedLabel("Round {0}", 2));
            Assert.AreEqual("Round 3", DominionShopRules.LockedLabel("Round {0}", 3));
            Assert.AreEqual("", DominionShopRules.LockedLabel("Round {0}", -1));
        }

        [Test] public void TheBaselineIsDepthZeroAFamilyOneAndItsUpgradeTwo()
        {
            var parents = new Dictionary<int, int> { { 10, -1 }, { 11, 10 }, { 12, 11 }, { 20, 10 } };
            System.Func<int, int?> parentOf = id => parents.TryGetValue(id, out int p) ? p : (int?)null;
            Assert.AreEqual(0, DominionShopRules.WeaponDepth(10, parentOf));
            Assert.AreEqual(1, DominionShopRules.WeaponDepth(11, parentOf));
            Assert.AreEqual(2, DominionShopRules.WeaponDepth(12, parentOf));
            Assert.AreEqual(1, DominionShopRules.WeaponDepth(20, parentOf));
            Assert.AreEqual(-1, DominionShopRules.WeaponDepth(99, parentOf), "an unknown weapon has no depth");
        }

        [Test] public void AParentLoopGivesNoDepthInsteadOfHanging()
        {
            var parents = new Dictionary<int, int> { { 1, 2 }, { 2, 1 } };
            Assert.AreEqual(-1, DominionShopRules.WeaponDepth(1, id => parents[id]));
        }

        [Test] public void DepthAgreesWithTheWeaponUpgradeTree()
        {
            // One list feeds both the tree and the depth lookup, so they cannot drift apart.
            var nodes = new[] { (1, -1), (2, 1), (3, 2), (4, 2), (5, 1) };
            var tree = new WeaponUpgradeTree(nodes);
            var parents = new Dictionary<int, int>();
            foreach (var (id, parent) in nodes) parents[id] = parent;
            System.Func<int, int?> parentOf = id => parents.TryGetValue(id, out int p) ? p : (int?)null;

            Assert.AreEqual(1, tree.RootId);
            Assert.AreEqual(0, DominionShopRules.WeaponDepth(tree.RootId, parentOf));
            foreach (var (id, _) in nodes)
                foreach (int child in tree.ChildrenOf(id))
                    Assert.AreEqual(DominionShopRules.WeaponDepth(id, parentOf) + 1, DominionShopRules.WeaponDepth(child, parentOf),
                        $"weapon {child} is one deeper than its parent {id}");
            Assert.AreEqual(2, DominionShopRules.WeaponDepth(3, parentOf));
            Assert.AreEqual(2, DominionShopRules.WeaponDepth(4, parentOf));
            Assert.AreEqual(1, DominionShopRules.WeaponDepth(5, parentOf));
        }

        [Test] public void AnyNegativeParentIsARootLikeTheTreeSaysNotOnlyMinusOne()
        {
            var tree = new WeaponUpgradeTree(new[] { (7, -5), (8, 7) });
            var parents = new Dictionary<int, int> { { 7, -5 }, { 8, 7 } };
            System.Func<int, int?> parentOf = id => parents.TryGetValue(id, out int p) ? p : (int?)null;
            Assert.AreEqual(7, tree.RootId);
            Assert.AreEqual(0, DominionShopRules.WeaponDepth(7, parentOf));
            Assert.AreEqual(1, DominionShopRules.WeaponDepth(8, parentOf));
        }

        // ---- Task 5: the shop in a live Dominion match

        [TestCase(DominionStage.Round)]
        [TestCase(DominionStage.SuddenDeath)]
        [TestCase(DominionStage.Over)]
        [TestCase(DominionStage.None)]
        public void PicksAreRefusedOutsideTheBreak(DominionStage stage) =>
            Assert.AreEqual(PurchaseBlock.NotInBreak, DominionShopRules.PickBlock(true, stage, false));

        [Test] public void PicksAreAllowedInTheBreak() =>
            Assert.AreEqual(PurchaseBlock.None, DominionShopRules.PickBlock(true, DominionStage.Break, false));

        [Test] public void ALateJoinersOnePickIsAllowedWhateverTheStage()
        {
            Assert.AreEqual(PurchaseBlock.None, DominionShopRules.PickBlock(true, DominionStage.Round, true));
            Assert.AreEqual(PurchaseBlock.None, DominionShopRules.PickBlock(true, DominionStage.SuddenDeath, true));
        }

        [Test] public void ConquestAndTheWarmupAreNeverBlockedByTheBreakRule()
        {
            foreach (DominionStage stage in System.Enum.GetValues(typeof(DominionStage)))
                Assert.AreEqual(PurchaseBlock.None, DominionShopRules.PickBlock(false, stage, false), stage.ToString());
        }

        [Test] public void ALateJoinersPickLastsOnlyWhileTheRoundTheyJoinedInIsGoing()
        {
            Assert.IsTrue(DominionShopRules.LateJoinerWindowOpen(true, DominionStage.Round));
            Assert.IsTrue(DominionShopRules.LateJoinerWindowOpen(true, DominionStage.SuddenDeath));
            Assert.IsFalse(DominionShopRules.LateJoinerWindowOpen(true, DominionStage.Break), "the round ended: the break's shop is everyone's");
            Assert.IsFalse(DominionShopRules.LateJoinerWindowOpen(true, DominionStage.Over));
            Assert.IsFalse(DominionShopRules.LateJoinerWindowOpen(false, DominionStage.Round), "only someone who joined mid-round");
        }

        [Test] public void TheArmourCapIsTheSmallerOfTheShopMaximumAndTheRoundsAllowance()
        {
            Assert.AreEqual(1, DominionShopRules.ArmorCap(3, 2, Armor));
            Assert.AreEqual(2, DominionShopRules.ArmorCap(3, 3, Armor));
            Assert.AreEqual(0, DominionShopRules.ArmorCap(3, 1, Armor));
            Assert.AreEqual(2, DominionShopRules.ArmorCap(2, 9, new[] { 0, 1, 5 }), "the shop's own maximum still wins when it is lower");
        }

        private static DominionShopLimits Limits(int round) => new DominionShopLimits(round, Depth, Armor, "Round {0}");

        [Test] public void AWeaponDeeperThanTheRoundOpensIsLockedAndSaysWhichRoundOpensIt()
        {
            DominionShopLimits r1 = Limits(1);
            Assert.IsTrue(r1.IsRoundLocked(UpgradeNodeState.Selectable, 1));
            Assert.AreEqual(UpgradeNodeState.Locked, r1.NodeState(UpgradeNodeState.Selectable, 1));
            Assert.AreEqual("Round 2", r1.LockedLabel(1));
            Assert.AreEqual("Round 3", r1.LockedLabel(2));
            Assert.AreEqual(UpgradeNodeState.Locked, Limits(2).NodeState(UpgradeNodeState.Locked, 2));
        }

        [Test] public void AWeaponAtOrUnderTheRoundsDepthIsPickableFromAnywhereBecauseEveryPickIsFree()
        {
            Assert.AreEqual(UpgradeNodeState.Selectable, Limits(2).NodeState(UpgradeNodeState.Selectable, 1));
            Assert.AreEqual(UpgradeNodeState.Selectable, Limits(3).NodeState(UpgradeNodeState.Locked, 1), "another family, switching: one click");
            Assert.AreEqual(UpgradeNodeState.Selectable, Limits(3).NodeState(UpgradeNodeState.Locked, 2), "an upgrade straight from the Baseline");
            Assert.IsFalse(Limits(2).IsRoundLocked(UpgradeNodeState.Locked, 1));
        }

        [Test] public void WhatYouHoldAndWhatYouPassedAreNeverRoundLocked()
        {
            Assert.AreEqual(UpgradeNodeState.Equipped, Limits(1).NodeState(UpgradeNodeState.Equipped, 2));
            Assert.AreEqual(UpgradeNodeState.Owned, Limits(1).NodeState(UpgradeNodeState.Owned, 1));
            Assert.IsFalse(Limits(1).IsRoundLocked(UpgradeNodeState.Equipped, 2));
        }

        [Test] public void AWeaponTheTreeDoesNotKnowIsLockedWithNoRoundToName()
        {
            Assert.IsTrue(Limits(3).IsRoundLocked(UpgradeNodeState.Locked, -1));
            Assert.AreEqual(UpgradeNodeState.Locked, Limits(3).NodeState(UpgradeNodeState.Locked, -1));
            Assert.AreEqual("", Limits(3).LockedLabel(-1));
        }

        [Test] public void TheLimitsReadTheirRoundsArmourAllowance()
        {
            Assert.AreEqual(1, Limits(2).ArmorCap(3));
            Assert.AreEqual(0, Limits(1).ArmorCap(3));
        }
    }
}
