using System.Reflection;
using NUnit.Framework;
using Overpower.UI;

namespace Overpower.Tests
{
    public class MinimapLabelWiringTests
    {
        private static MethodInfo Helper(string name) => typeof(MinimapLabelPlacement).GetMethod(name);

        [Test]
        public void EveryLineUnderTheMinimapIsPlacedByTheOneHelper()
        {
            MethodInfo place = Helper(nameof(MinimapLabelPlacement.PlaceUnder));
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.Vision.CentreScanCountdownView), "PlaceUnderCornerMinimap", place), "the scan countdown");
            Assert.IsTrue(IlWiring.Uses(typeof(CentrePayoutLabel), "Refresh", place), "the Dominion centre payout");
            Assert.IsTrue(IlWiring.Uses(typeof(SuddenDeathOverlay), "Refresh", place), "the sudden-death circle countdown");
        }

        [Test]
        public void EveryLineUnderTheMinimapGetsTheOneLook()
        {
            MethodInfo look = Helper(nameof(MinimapLabelPlacement.ApplyLook));
            Assert.IsTrue(IlWiring.Uses(typeof(Overpower.Vision.CentreScanCountdownView), "Build", look), "the scan countdown");
            Assert.IsTrue(IlWiring.Uses(typeof(CentrePayoutLabel), "Build", look), "the Dominion centre payout");
            Assert.IsTrue(IlWiring.Uses(typeof(SuddenDeathOverlay), "Build", look), "the sudden-death circle countdown");
        }

        [Test]
        public void TheHelperHangsTheLabelFromTheDrawnShapeOfTheCornerMap()
        {
            MethodInfo place = Helper(nameof(MinimapLabelPlacement.PlaceUnder));
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapLabelPlacement), "PlaceUnder", Helper(nameof(MinimapLabelPlacement.LabelTop))));
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapLabelPlacement), "PlaceUnder", typeof(MinimapView).GetMethod(nameof(MinimapView.CornerShapeBottomBelowCentre))));
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapLabelPlacement), "PlaceUnder", typeof(HudScreenLayout).GetMethod(nameof(HudScreenLayout.CornerMinimapRect))));
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapLabelPlacement), "PlaceUnder", typeof(MinimapView).GetMethod(nameof(MinimapView.CornerFrameSize))),
                "the lane map's rectangle is not the square box");
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapView), "CornerShapeBottomBelowCentre", Helper(nameof(MinimapLabelPlacement.ShapeBottomBelowCentre))));
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapView), "CornerShapeBottomBelowCentre",
                typeof(GeneratedSprites).GetProperty(nameof(GeneratedSprites.CentredTriangleRadiusFraction)).GetGetMethod()));
        }
    }
}
