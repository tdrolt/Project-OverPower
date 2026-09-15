using NUnit.Framework;
using Overpower.Combat;

namespace Overpower.Tests
{
    /// <summary>Covers DeployableLifetime.ShouldScheduleOwnerDestroy (code review finding, Task
    /// 2.1a-era pass): a hidden, collider-less object used to leak forever whenever the OWNER's own
    /// copy computed IsExpired true, because the destroy schedule was gated by an else-if against that
    /// same flag. The fix's signature has no isExpired parameter at all - these tests exist mainly to
    /// pin down that the two inputs that ARE here (lifetimeSeconds, isOwnerClient) behave exactly as a
    /// plain AND, with nothing else able to suppress the schedule.</summary>
    public class DeployableLifetimeTests
    {
        [Test]
        public void OwnerWithARealLifetimeShouldScheduleItsOwnDestroy()
        {
            Assert.IsTrue(DeployableLifetime.ShouldScheduleOwnerDestroy(lifetimeSeconds: 45f, isOwnerClient: true));
        }

        [Test]
        public void NonOwnerNeverSchedulesADestroyRegardlessOfLifetime()
        {
            Assert.IsFalse(DeployableLifetime.ShouldScheduleOwnerDestroy(lifetimeSeconds: 45f, isOwnerClient: false));
        }

        [Test]
        public void ZeroLifetimeMeansSomethingElseDecidesEvenForTheOwner()
        {
            // Portal, AoeZone - Lifetime Seconds 0 is "not this base class's job to end this".
            Assert.IsFalse(DeployableLifetime.ShouldScheduleOwnerDestroy(lifetimeSeconds: 0f, isOwnerClient: true));
        }

        [Test]
        public void NegativeLifetimeAlsoMeansSomethingElseDecides()
        {
            Assert.IsFalse(DeployableLifetime.ShouldScheduleOwnerDestroy(lifetimeSeconds: -1f, isOwnerClient: true));
        }

        [Test]
        public void ATinyLifetimeStillSchedulesForTheOwner()
        {
            // The exact case the leak bug missed: a Lifetime Seconds so small a single frame can
            // already read Age past it (IsExpired true) by the time this is evaluated. The schedule
            // must still be set up - NetworkedDeployable clamps the wait itself to Mathf.Max(0f, ...),
            // so an already-expired owner's copy destroys itself on the very next frame instead of
            // never. This method has no way to know Age or IsExpired at all, which is the actual fix:
            // there is nothing here that COULD suppress the schedule for that reason.
            Assert.IsTrue(DeployableLifetime.ShouldScheduleOwnerDestroy(lifetimeSeconds: 0.001f, isOwnerClient: true));
        }
    }
}
