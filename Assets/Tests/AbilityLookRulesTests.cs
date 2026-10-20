using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Overpower.Arena;

namespace Overpower.Tests
{
    /// <summary>The rule that audits the look table, on small made-up tables: it must be able to fail for each of its four
    /// complaints, or the checks on the real table prove nothing.</summary>
    public class AbilityLookRulesTests
    {
        private static AbilityLook Look(int id, LookShape shape, LookFamily family) => new AbilityLook(id, shape, family);

        private static List<string> Problems(int[] ids, AbilityLook[] table, params int[] exempt) =>
            AbilityLookRules.Problems(ids, table, new HashSet<int>(exempt));

        [Test]
        public void ACompleteTableHasNoProblems()
        {
            var table = new[] { Look(1, LookShape.Streak, LookFamily.Silver), Look(2, LookShape.Bolt, LookFamily.ElectricBlue) };
            Assert.IsEmpty(Problems(new[] { 1, 2 }, table));
        }

        [Test]
        public void AMissingIdIsReportedByNumber()
        {
            var table = new[] { Look(1, LookShape.Streak, LookFamily.Silver) };
            List<string> problems = Problems(new[] { 1, 2 }, table);
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("2", problems[0]);
        }

        [Test]
        public void ADuplicateIdIsReported()
        {
            var table = new[] { Look(1, LookShape.Streak, LookFamily.Silver), Look(1, LookShape.Bolt, LookFamily.ElectricBlue) };
            Assert.AreEqual(1, Problems(new[] { 1 }, table).Count);
        }

        [Test]
        public void TwoEntriesWithTheSameShapeAndFamilyAreReported()
        {
            var table = new[] { Look(1, LookShape.Streak, LookFamily.Silver), Look(2, LookShape.Streak, LookFamily.Silver) };
            Assert.AreEqual(1, Problems(new[] { 1, 2 }, table).Count);
        }

        [Test]
        public void TheSameShapeInAnotherFamilyIsFine()
        {
            var table = new[] { Look(1, LookShape.Beam, LookFamily.Danger), Look(2, LookShape.Beam, LookFamily.Silver) };
            Assert.IsEmpty(Problems(new[] { 1, 2 }, table));
        }

        [Test]
        public void SeveralNoLookEntriesMayShareThePair()
        {
            var table = new[] { Look(1, LookShape.NoLook, LookFamily.None), Look(2, LookShape.NoLook, LookFamily.None) };
            Assert.IsEmpty(Problems(new[] { 1, 2 }, table));
        }

        [Test]
        public void AnExemptIdNeedsNoEntry()
        {
            var table = new[] { Look(1, LookShape.Streak, LookFamily.Silver) };
            Assert.IsEmpty(Problems(new[] { 1, 901 }, table, 901));
            Assert.AreEqual(1, Problems(new[] { 1, 901 }, table).Count, "without the exemption it is missing");
        }

        [Test]
        public void AnEntryForAnIdTheCatalogueDoesNotHaveIsReported()
        {
            var table = new[] { Look(1, LookShape.Streak, LookFamily.Silver), Look(7, LookShape.Bolt, LookFamily.ElectricBlue) };
            List<string> problems = Problems(new[] { 1 }, table);
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("7", problems[0]);
        }
    }
}
