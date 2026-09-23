using NUnit.Framework;
using Overpower.Combat;
using Overpower.UI;

namespace Overpower.Tests
{
    /// <summary>Pulled out of PlayerHud.UpdateOverheat so the vent band's dim/bright/hit/miss look
    /// is testable without a scene - see VentBandLookRule's own class comment.</summary>
    public class VentBandLookRuleTests
    {
        [Test]
        public void HiddenWheneverNotSilenced()
        {
            Assert.AreEqual(VentBandLook.Hidden,
                VentBandLookRule.Determine(isSilenced: false, windowOpen: true, outcome: VentOutcome.None));
            Assert.AreEqual(VentBandLook.Hidden,
                VentBandLookRule.Determine(isSilenced: false, windowOpen: false, outcome: VentOutcome.Hit));
        }

        [Test]
        public void DimWhileSilencedBeforeTheWindowOpensWithNoOutcomeYet()
        {
            Assert.AreEqual(VentBandLook.Dim,
                VentBandLookRule.Determine(isSilenced: true, windowOpen: false, outcome: VentOutcome.None));
        }

        [Test]
        public void BrightWhileTheWindowIsOpenWithNoOutcomeYet()
        {
            Assert.AreEqual(VentBandLook.Bright,
                VentBandLookRule.Determine(isSilenced: true, windowOpen: true, outcome: VentOutcome.None));
        }

        [Test]
        public void HitOutcomeWinsOverWindowState()
        {
            Assert.AreEqual(VentBandLook.Hit,
                VentBandLookRule.Determine(isSilenced: true, windowOpen: true, outcome: VentOutcome.Hit));
            Assert.AreEqual(VentBandLook.Hit,
                VentBandLookRule.Determine(isSilenced: true, windowOpen: false, outcome: VentOutcome.Hit));
        }

        [Test]
        public void MissedOutcomeWinsOverWindowState()
        {
            Assert.AreEqual(VentBandLook.Miss,
                VentBandLookRule.Determine(isSilenced: true, windowOpen: true, outcome: VentOutcome.Missed));
            Assert.AreEqual(VentBandLook.Miss,
                VentBandLookRule.Determine(isSilenced: true, windowOpen: false, outcome: VentOutcome.Missed));
        }

        [Test]
        public void HiddenWhenVentIsDisabledRegardlessOfOutcomeOrWindowState()
        {
            // Review fix: ventWindow <= 0 (OverheatState.VentEnabled false) means Vent is off -
            // the band must never appear, even though windowOpen/outcome alone can't tell this
            // case apart from "silenced, no outcome yet" (which is legitimately Dim).
            Assert.AreEqual(VentBandLook.Hidden,
                VentBandLookRule.Determine(isSilenced: true, windowOpen: false, outcome: VentOutcome.None, ventEnabled: false));
            Assert.AreEqual(VentBandLook.Hidden,
                VentBandLookRule.Determine(isSilenced: true, windowOpen: false, outcome: VentOutcome.Missed, ventEnabled: false));
            Assert.AreEqual(VentBandLook.Hidden,
                VentBandLookRule.Determine(isSilenced: true, windowOpen: true, outcome: VentOutcome.Hit, ventEnabled: false));
        }
    }
}
