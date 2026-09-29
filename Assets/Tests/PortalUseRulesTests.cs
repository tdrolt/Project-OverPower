using NUnit.Framework;
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Tests
{
    public class PortalUseRulesTests
    {
        private const float PortalRadius = 1f;
        private const float CapsuleRadius = 0.5f;

        // ---- on the portal: the body's circle touching the portal's circle -------------------------

        [Test]
        public void CentreInsideThePortalIsOnIt()
        {
            Assert.IsTrue(PortalUseRules.IsOnPortal(Vector3.zero, new Vector3(0.5f, 0f, 0f), PortalRadius, CapsuleRadius));
        }

        [Test]
        public void CentreOutsideTheRimButCapsuleTouchingTheRimIsOnIt()
        {
            // 1.3 m from the centre: outside the 1 m portal, but the 0.5 m body reaches 0.8 m in.
            Assert.IsTrue(PortalUseRules.IsOnPortal(Vector3.zero, new Vector3(1.3f, 0f, 0f), PortalRadius, CapsuleRadius));
        }

        [Test]
        public void BeyondPortalRadiusPlusCapsuleRadiusIsOff()
        {
            Assert.IsFalse(PortalUseRules.IsOnPortal(Vector3.zero, new Vector3(1.6f, 0f, 0f), PortalRadius, CapsuleRadius));
        }

        [Test]
        public void HeightDoesNotMatterOnlyTheGroundDistance()
        {
            Assert.IsTrue(PortalUseRules.IsOnPortal(Vector3.zero, new Vector3(0f, 0.9f, 1.4f), PortalRadius, CapsuleRadius));
        }

        // ---- who may use ---------------------------------------------------------------------------

        [Test]
        public void TheOwnerMayUseItWhenItHasACharge()
        {
            Assert.IsTrue(PortalUseRules.MayUse(isOwner: true, sameTeam: true, userAlive: true, ownerHasCharge: true));
        }

        [Test]
        public void ALivingTeammateMayUseIt()
        {
            Assert.IsTrue(PortalUseRules.MayUse(isOwner: false, sameTeam: true, userAlive: true, ownerHasCharge: true));
        }

        [Test]
        public void ADeadTeammateMayNot()
        {
            Assert.IsFalse(PortalUseRules.MayUse(isOwner: false, sameTeam: true, userAlive: false, ownerHasCharge: true));
        }

        [Test]
        public void AnEnemyMayNot()
        {
            Assert.IsFalse(PortalUseRules.MayUse(isOwner: false, sameTeam: false, userAlive: true, ownerHasCharge: true));
        }

        [Test]
        public void NobodyMayWhenTheOwnerHasNoCharge()
        {
            Assert.IsFalse(PortalUseRules.MayUse(isOwner: true, sameTeam: true, userAlive: true, ownerHasCharge: false));
            Assert.IsFalse(PortalUseRules.MayUse(isOwner: false, sameTeam: true, userAlive: true, ownerHasCharge: false));
        }

        // ---- the owner's side: a teammate's "I travelled" message ----------------------------------

        [Test]
        public void ANewCounterFromATeammateForMeSpendsOneCharge()
        {
            Assert.IsTrue(PortalUseRules.ShouldSpendForAllyTrip(lastSeenCounter: 0, newCounter: 1, targetOwnerActor: 3, myActor: 3, writerIsTeammate: true));
        }

        [Test]
        public void TheSameCounterAgainSpendsNothing()
        {
            Assert.IsFalse(PortalUseRules.ShouldSpendForAllyTrip(lastSeenCounter: 1, newCounter: 1, targetOwnerActor: 3, myActor: 3, writerIsTeammate: true));
        }

        [Test]
        public void ANonTeammatesMessageSpendsNothing()
        {
            Assert.IsFalse(PortalUseRules.ShouldSpendForAllyTrip(lastSeenCounter: 0, newCounter: 1, targetOwnerActor: 3, myActor: 3, writerIsTeammate: false));
        }

        [Test]
        public void AMessageAboutSomeoneElsesPortalsSpendsNothing()
        {
            Assert.IsFalse(PortalUseRules.ShouldSpendForAllyTrip(lastSeenCounter: 0, newCounter: 1, targetOwnerActor: 4, myActor: 3, writerIsTeammate: true));
        }

        [Test]
        public void AnOlderCounterSpendsNothing()
        {
            Assert.IsFalse(PortalUseRules.ShouldSpendForAllyTrip(lastSeenCounter: 5, newCounter: 4, targetOwnerActor: 3, myActor: 3, writerIsTeammate: true));
        }
    }
}
