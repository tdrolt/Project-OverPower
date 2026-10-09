using NUnit.Framework;
using Overpower.Match;
using UnityEngine;

namespace Overpower.Tests
{
    public class TowerBountyViewWiringTests
    {
        private static System.Reflection.MethodInfo Rule(System.Type type, string name) => type.GetMethod(name);

        [Test]
        public void TheLabelIsDecidedByTheRules()
        {
            System.Type view = typeof(TowerBountyView);
            Assert.IsTrue(IlWiring.Uses(view, "LateUpdate", Rule(typeof(BountyLabelRules), nameof(BountyLabelRules.LabelAmount))));
            Assert.IsTrue(IlWiring.Uses(view, "OfferOf", Rule(typeof(BountyLabelRules), nameof(BountyLabelRules.OfferFor))));
            Assert.IsTrue(IlWiring.Uses(view, "UpdatePops", Rule(typeof(BountyLabelRules), nameof(BountyLabelRules.PopAlpha))));
            Assert.IsTrue(IlWiring.Uses(view, "UpdatePops", Rule(typeof(BountyLabelRules), nameof(BountyLabelRules.PopRise))));
        }

        [Test]
        public void ThePopIsDecidedByTheRules()
        {
            System.Type view = typeof(TowerBountyView);
            Assert.IsTrue(IlWiring.Uses(view, "OnOwnershipChanged", Rule(typeof(BountyLabelRules), nameof(BountyLabelRules.PopAmount))));
            Assert.IsTrue(IlWiring.Uses(view, "OnOwnershipChanged", Rule(typeof(TerritoryHistory), nameof(TerritoryHistory.BeforeEventIn))));
            Assert.IsTrue(IlWiring.Uses(view, "LateUpdate", Rule(typeof(TerritoryHistory), nameof(TerritoryHistory.Seen))));
        }

        [Test]
        public void TheLabelAndThePopAreKeptOnScreenByThePlacementRule()
        {
            System.Type view = typeof(TowerBountyView);
            System.Reflection.MethodInfo place = Rule(typeof(BountyLabelPlacement), nameof(BountyLabelPlacement.Place));
            Assert.IsTrue(IlWiring.Uses(view, "LateUpdate", place));
            Assert.IsTrue(IlWiring.Uses(view, "UpdatePops", place));
        }

        [Test]
        public void TheMinimapRectComesFromTheOneSharedHelper()
        {
            System.Reflection.MethodInfo helper = typeof(Overpower.UI.HudScreenLayout).GetMethod(nameof(Overpower.UI.HudScreenLayout.CornerMinimapRect));
            Assert.IsTrue(IlWiring.Uses(typeof(TowerBountyView), "AvoidRect", helper));
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.Vision.CentreScanCountdownView), "PlaceUnderCornerMinimap", helper));
        }

        [Test]
        public void TheBuildingManagerAttachesTheViewInAwake()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BuildingManager), "Awake", typeof(TowerBountyView).GetMethod(nameof(TowerBountyView.AttachTo))));
        }

        [Test]
        public void AttachingTwiceLeavesOneView()
        {
            var go = new GameObject("bounty view test");
            try
            {
                TowerBountyView first = TowerBountyView.AttachTo(go);
                TowerBountyView second = TowerBountyView.AttachTo(go);
                Assert.AreSame(first, second);
                Assert.AreEqual(1, go.GetComponents<TowerBountyView>().Length);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
