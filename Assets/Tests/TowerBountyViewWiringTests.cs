using NUnit.Framework;
using Overpower.Match;

namespace Overpower.Tests
{
    public class TowerBountyViewWiringTests
    {
        private static System.Reflection.MethodInfo Rule(string name) => typeof(BountyLabelRules).GetMethod(name);

        [Test]
        public void TheLabelIsDecidedByTheRules()
        {
            System.Type view = typeof(TowerBountyView);
            Assert.IsTrue(IlWiring.Uses(view, "LateUpdate", Rule(nameof(BountyLabelRules.LabelAmount))));
            Assert.IsTrue(IlWiring.Uses(view, "OfferOf", Rule(nameof(BountyLabelRules.OfferFor))));
            Assert.IsTrue(IlWiring.Uses(view, "UpdatePops", Rule(nameof(BountyLabelRules.PopAlpha))));
            Assert.IsTrue(IlWiring.Uses(view, "UpdatePops", Rule(nameof(BountyLabelRules.PopRise))));
        }

        [Test]
        public void ThePopIsDecidedByTheRule()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(TowerBountyView), "OnOwnershipChanged", Rule(nameof(BountyLabelRules.PopAmount))));
        }
    }
}
