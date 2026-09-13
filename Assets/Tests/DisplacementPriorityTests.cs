using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    public class DisplacementPriorityTests
    {
        [Test]
        public void NothingRunningAcceptsVoluntary()
        {
            Assert.IsTrue(DisplacementPriority.Accepts(null, DisplaceKind.Voluntary));
        }

        [Test]
        public void NothingRunningAcceptsTeleport()
        {
            Assert.IsTrue(DisplacementPriority.Accepts(null, DisplaceKind.Teleport));
        }

        [Test]
        public void NothingRunningAcceptsForced()
        {
            Assert.IsTrue(DisplacementPriority.Accepts(null, DisplaceKind.Forced));
        }

        [Test]
        public void NothingRunningNeverCancelsAnything()
        {
            Assert.IsFalse(DisplacementPriority.CancelsCurrent(null, DisplaceKind.Forced));
        }

        [Test]
        public void VoluntaryRunningAcceptsAnotherVoluntaryAndCancelsTheFirst()
        {
            Assert.IsTrue(DisplacementPriority.Accepts(DisplaceKind.Voluntary, DisplaceKind.Voluntary));
            Assert.IsTrue(DisplacementPriority.CancelsCurrent(DisplaceKind.Voluntary, DisplaceKind.Voluntary));
        }

        [Test]
        public void VoluntaryRunningAcceptsTeleportAndCancelsIt()
        {
            Assert.IsTrue(DisplacementPriority.Accepts(DisplaceKind.Voluntary, DisplaceKind.Teleport));
            Assert.IsTrue(DisplacementPriority.CancelsCurrent(DisplaceKind.Voluntary, DisplaceKind.Teleport));
        }

        [Test]
        public void VoluntaryRunningAcceptsForcedAndCancelsIt()
        {
            Assert.IsTrue(DisplacementPriority.Accepts(DisplaceKind.Voluntary, DisplaceKind.Forced));
            Assert.IsTrue(DisplacementPriority.CancelsCurrent(DisplaceKind.Voluntary, DisplaceKind.Forced));
        }

        [Test]
        public void ForcedRunningRefusesVoluntaryAndCancelsNothing()
        {
            Assert.IsFalse(DisplacementPriority.Accepts(DisplaceKind.Forced, DisplaceKind.Voluntary));
            Assert.IsFalse(DisplacementPriority.CancelsCurrent(DisplaceKind.Forced, DisplaceKind.Voluntary));
        }

        [Test]
        public void ForcedRunningRefusesTeleportAndCancelsNothing()
        {
            Assert.IsFalse(DisplacementPriority.Accepts(DisplaceKind.Forced, DisplaceKind.Teleport));
            Assert.IsFalse(DisplacementPriority.CancelsCurrent(DisplaceKind.Forced, DisplaceKind.Teleport));
        }

        [Test]
        public void ForcedRunningAcceptsAnotherForcedAndCancelsTheFirst()
        {
            // A second knockback should not be shrugged off just because the first one has not
            // finished landing - the newest shove wins, same as it would for a Voluntary move.
            Assert.IsTrue(DisplacementPriority.Accepts(DisplaceKind.Forced, DisplaceKind.Forced));
            Assert.IsTrue(DisplacementPriority.CancelsCurrent(DisplaceKind.Forced, DisplaceKind.Forced));
        }
    }
}
