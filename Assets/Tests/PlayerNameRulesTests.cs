using NUnit.Framework;
using Overpower.Lobby;

namespace Overpower.Tests
{
    public class PlayerNameRulesTests
    {
        [Test]
        public void ThreeCharactersAreTooShort() => Assert.IsFalse(PlayerNameRules.IsValid("abc", 4, 10));

        [Test]
        public void FourCharactersAreValid() => Assert.IsTrue(PlayerNameRules.IsValid("ab12", 4, 10));

        [Test]
        public void MaxIsValidAndOneMoreIsNot()
        {
            Assert.IsTrue(PlayerNameRules.IsValid("abcdefghij", 4, 10));
            Assert.IsFalse(PlayerNameRules.IsValid("abcdefghijk", 4, 10));
        }

        [Test]
        public void SpacesAndSymbolsAreInvalid()
        {
            Assert.IsFalse(PlayerNameRules.IsValid("Tu dor", 4, 10));
            Assert.IsFalse(PlayerNameRules.IsValid("Tudor!", 4, 10));
            Assert.IsFalse(PlayerNameRules.IsValid("Tudor_", 4, 10));
        }

        [Test]
        public void NullIsInvalid() => Assert.IsFalse(PlayerNameRules.IsValid(null, 4, 10));

        [Test]
        public void AFreeNameIsKept() =>
            Assert.AreEqual("Tudor", PlayerNameRules.UniqueName("Tudor", new[] { "Mara" }, "{0} {1}"));

        [Test]
        public void ATakenNameGetsTwo() =>
            Assert.AreEqual("Tudor 2", PlayerNameRules.UniqueName("Tudor", new[] { "Tudor" }, "{0} {1}"));

        [Test]
        public void TwoTakenGivesThree() =>
            Assert.AreEqual("Tudor 3", PlayerNameRules.UniqueName("Tudor", new[] { "Tudor", "Tudor 2" }, "{0} {1}"));

        [Test]
        public void TheCompareIgnoresCase() =>
            Assert.AreEqual("tudor 2", PlayerNameRules.UniqueName("tudor", new[] { "Tudor" }, "{0} {1}"));
    }
}
