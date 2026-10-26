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

        // The shop takes picks anywhere, any time in a round, as well as in the break, with the same limits.

        [TestCase(DominionStage.Break)]
        [TestCase(DominionStage.Round)]
        [TestCase(DominionStage.Overtime)]
        [TestCase(DominionStage.SuddenDeath)]
        public void PicksAreTakenInTheBreakAndAnyTimeDuringARound(DominionStage stage)
        {
            Assert.IsTrue(DominionShopRules.MayPick(stage), stage.ToString());
            Assert.AreEqual(PurchaseBlock.None, DominionShopRules.PickBlock(true, stage), stage.ToString());
        }

        [TestCase(DominionStage.None)]
        [TestCase(DominionStage.Over)]
        public void PicksAreRefusedOnlyWhileNoRoundOrBreakIsOn(DominionStage stage)
        {
            Assert.IsFalse(DominionShopRules.MayPick(stage), stage.ToString());
            Assert.AreEqual(PurchaseBlock.NotInBreak, DominionShopRules.PickBlock(true, stage), stage.ToString());
        }

        [Test] public void ConquestAndTheWarmupAreNeverBlockedByDominionsPickRule()
        {
            foreach (DominionStage stage in System.Enum.GetValues(typeof(DominionStage)))
                Assert.AreEqual(PurchaseBlock.None, DominionShopRules.PickBlock(false, stage), stage.ToString());
        }

        [Test] public void AMidRoundPickHasTheRoundsLimitsJustAsTheBreak()
        {
            System.Reflection.MethodInfo stage = typeof(DominionDirector).GetProperty(nameof(DominionDirector.Stage)).GetGetMethod();
            System.Reflection.MethodInfo round = typeof(DominionDirector).GetProperty(nameof(DominionDirector.Round)).GetGetMethod();
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.ShopPricing), nameof(Overpower.UI.ShopPricing.DominionLimits), round), "the limits are the round's");
            Assert.IsFalse(IlWiring.Uses(typeof(Overpower.UI.ShopPricing), nameof(Overpower.UI.ShopPricing.DominionLimits), stage),
                "and never depend on whether the round is being played or about to start");
            var limits = new DominionShopLimits(2, Depth, Armor, "Round {0}");
            Assert.AreEqual(UpgradeNodeState.Locked, limits.NodeState(UpgradeNodeState.Selectable, 2), "past the round's weapon depth: still refused");
            Assert.AreEqual(UpgradeNodeState.Selectable, limits.NodeState(UpgradeNodeState.Locked, 1), "within it: pickable");
            Assert.AreEqual(1, limits.ArmorCap(3), "the round's armour allowance");
        }

        [Test] public void TheShopAndItsButtonAskTheTestedRule()
        {
            System.Reflection.MethodInfo closed = typeof(Overpower.UI.ShopPricing).GetMethod(nameof(Overpower.UI.ShopPricing.DominionClosed));
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.ShopPricing), nameof(Overpower.UI.ShopPricing.DominionClosed),
                typeof(DominionShopRules).GetMethod(nameof(DominionShopRules.PickBlock))), "the stage goes through PickBlock");
            Assert.IsTrue(IlWiring.Uses(typeof(DominionShopRules), nameof(DominionShopRules.PickBlock),
                typeof(DominionShopRules).GetMethod(nameof(DominionShopRules.MayPick))), "PickBlock asks MayPick");
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.LoadoutScreen), nameof(Overpower.UI.LoadoutScreen.Open), closed), "opening the shop");
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.LoadoutScreen), "Update", closed), "the Loadout button and an open shop");
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.ShopPricing), nameof(Overpower.UI.ShopPricing.Build), closed), "every pick's check");
        }

        [Test] public void AFreshJoinerFindsTheShopOpenByItselfOnlyWhileTheirRoundIsGoing()
        {
            Assert.IsTrue(DominionShopRules.LateJoinerWindowOpen(DominionStage.Round));
            Assert.IsTrue(DominionShopRules.LateJoinerWindowOpen(DominionStage.SuddenDeath));
            Assert.IsFalse(DominionShopRules.LateJoinerWindowOpen(DominionStage.Break), "in a break the break card points everyone to the shop");
            Assert.IsFalse(DominionShopRules.LateJoinerWindowOpen(DominionStage.Over));
            Assert.IsFalse(DominionShopRules.LateJoinerWindowOpen(DominionStage.None));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(Overpower.UI.LoadoutScreen), "Start",
                typeof(DominionShopRules).GetMethod(nameof(DominionShopRules.LateJoinerWindowOpen))), "the shop asks the rule when it starts");
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.UI.LoadoutScreen), "Start",
                typeof(Overpower.UI.LoadoutScreen).GetMethod(nameof(Overpower.UI.LoadoutScreen.Open))), "and opens itself on its answer");
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
