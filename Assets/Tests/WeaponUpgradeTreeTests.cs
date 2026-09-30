using System.Collections.Generic;
using NUnit.Framework;
using Overpower.Combat;
using Overpower.Data;
using UnityEditor;

namespace Overpower.Tests
{
    public class WeaponUpgradeTreeTests
    {
        // The real shape: 1 Baseline -> 2 Rocket, 5 Burst, 8 SMG, 11 Laser; each path -> its two leaves.
        private static WeaponUpgradeTree RealShape() => new WeaponUpgradeTree(new List<(int, int)>
        {
            (1, -1),
            (2, 1), (3, 2), (4, 2),
            (5, 1), (6, 5), (7, 5),
            (8, 1), (9, 8), (10, 8),
            (11, 1), (12, 11), (13, 11),
        });

        [Test]
        public void TheRootIsTheWeaponWithNoParent()
        {
            Assert.AreEqual(1, RealShape().RootId);
        }

        [Test]
        public void TheRootsChildrenAreTheFourPathsInIdOrder()
        {
            CollectionAssert.AreEqual(new[] { 2, 5, 8, 11 }, RealShape().ChildrenOf(1));
        }

        [Test]
        public void LeavesHaveNoChildren()
        {
            var tree = RealShape();
            Assert.IsTrue(tree.IsLeaf(3));
            Assert.IsFalse(tree.IsLeaf(2));
            Assert.IsFalse(tree.IsLeaf(1));
        }

        [Test]
        public void FromBaselineOnlyThePathsAreSelectable()
        {
            var tree = RealShape();
            Assert.AreEqual(UpgradeNodeState.Equipped, tree.StateOf(1, equippedId: 1));
            Assert.AreEqual(UpgradeNodeState.Selectable, tree.StateOf(2, equippedId: 1));
            Assert.AreEqual(UpgradeNodeState.Locked, tree.StateOf(3, equippedId: 1));
        }

        [Test]
        public void OnRocketItsLeavesUnlockAndOtherPathsLock()
        {
            var tree = RealShape();
            Assert.AreEqual(UpgradeNodeState.Owned, tree.StateOf(1, equippedId: 2));
            Assert.AreEqual(UpgradeNodeState.Selectable, tree.StateOf(3, equippedId: 2));
            Assert.AreEqual(UpgradeNodeState.Selectable, tree.StateOf(4, equippedId: 2));
            Assert.AreEqual(UpgradeNodeState.Locked, tree.StateOf(5, equippedId: 2));
            Assert.AreEqual(UpgradeNodeState.Locked, tree.StateOf(6, equippedId: 2));
        }

        [Test]
        public void OnALeafNothingIsSelectableAndItsAncestorsAreOwned()
        {
            var tree = RealShape();
            Assert.AreEqual(UpgradeNodeState.Equipped, tree.StateOf(3, equippedId: 3));
            Assert.AreEqual(UpgradeNodeState.Owned, tree.StateOf(2, equippedId: 3));
            Assert.AreEqual(UpgradeNodeState.Owned, tree.StateOf(1, equippedId: 3));
            Assert.AreEqual(UpgradeNodeState.Locked, tree.StateOf(4, equippedId: 3));
        }

        [Test]
        public void CanUpgradeOnlyToADirectChild()
        {
            var tree = RealShape();
            Assert.IsTrue(tree.CanUpgrade(1, 2));
            Assert.IsTrue(tree.CanUpgrade(2, 3));
            Assert.IsFalse(tree.CanUpgrade(1, 3));
            Assert.IsFalse(tree.CanUpgrade(2, 5));
            Assert.IsFalse(tree.CanUpgrade(3, 2));
        }

        [Test]
        public void AnUnknownIdIsLockedAndCannotBeUpgradedTo()
        {
            var tree = RealShape();
            Assert.AreEqual(UpgradeNodeState.Locked, tree.StateOf(99, equippedId: 1));
            Assert.IsFalse(tree.CanUpgrade(1, 99));
        }

        [Test]
        public void AParentCycleIsReportedInsteadOfLoopingForever()
        {
            var tree = new WeaponUpgradeTree(new List<(int, int)> { (1, -1), (2, 3), (3, 2) });
            Assert.IsNotEmpty(tree.Problems);
            Assert.AreEqual(UpgradeNodeState.Locked, tree.StateOf(2, equippedId: 1));
            Assert.AreEqual(UpgradeNodeState.Locked, tree.StateOf(1, equippedId: 2));
        }

        [Test]
        public void TwoRootsAreReportedAndTheLowestIdIsUsed()
        {
            var tree = new WeaponUpgradeTree(new List<(int, int)> { (4, -1), (1, -1), (2, 1) });
            Assert.IsNotEmpty(tree.Problems);
            Assert.AreEqual(1, tree.RootId);
        }

        [Test]
        public void ADuplicateIdInTheInputIsReportedAndTheLastEntryWins()
        {
            // Two entries claim id 2 with different parents (the shape a duplicated
            // WeaponDefinition.Id would take) - the constructor must not throw, and the tree
            // still has to build deterministically, so the later entry silently wins the same
            // way a plain Dictionary write would. But that silence is exactly the failure mode
            // WeaponCatalogue.Validate exists to catch for ids, so it belongs in Problems here too.
            var tree = new WeaponUpgradeTree(new List<(int, int)> { (1, -1), (2, 1), (3, 1), (2, 3) });

            Assert.IsNotEmpty(tree.Problems);
            CollectionAssert.AreEqual(new[] { 3 }, tree.ChildrenOf(1)); // 2's first parent link (1) is gone
            CollectionAssert.AreEqual(new[] { 2 }, tree.ChildrenOf(3)); // the later entry (parent 3) is what took effect
        }

        [Test]
        public void TheRealWeaponCatalogueBuildsAProblemFreeTree()
        {
            // Builds the tree from the actual 13 WeaponDefinition assets rather than a hand-rolled
            // stand-in, so a designer mistake in the real Parent links (a typo'd link, a leaf
            // pointed at the wrong path, an accidental cycle) is caught here instead of surfacing
            // as a broken loadout screen later.
            var catalogue = AssetDatabase.LoadAssetAtPath<WeaponCatalogue>(
                "Assets/Gameplay/Weapons/WeaponCatalogue.asset");
            Assert.NotNull(catalogue,
                "Expected the real WeaponCatalogue asset at Assets/Gameplay/Weapons/WeaponCatalogue.asset - " +
                "if it moved, update this test's path.");

            var nodes = new List<(int, int)>();
            foreach (var weapon in catalogue.Weapons)
                nodes.Add((weapon.Id, weapon.Parent != null ? weapon.Parent.Id : -1));

            var tree = new WeaponUpgradeTree(nodes);

            CollectionAssert.IsEmpty(tree.Problems);
            Assert.AreEqual(1, tree.RootId);
            CollectionAssert.AreEqual(new[] { 2, 5, 8, 11 }, tree.ChildrenOf(1));
        }
    }
}
