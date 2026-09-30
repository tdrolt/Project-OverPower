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
        public void TheRememberedCounterNeverGoesBackwards()
        {
            Assert.AreEqual(7, PortalUseRules.NewLastSeen(lastSeenCounter: 5, newCounter: 7));
            Assert.AreEqual(5, PortalUseRules.NewLastSeen(lastSeenCounter: 5, newCounter: 4));
            Assert.AreEqual(5, PortalUseRules.NewLastSeen(lastSeenCounter: 5, newCounter: 5));
        }

        [Test]
        public void AUsablePortalBeatsANearerUnusableOne()
        {
            Assert.IsTrue(PortalUseRules.IsBetterCandidate(usable: true, distanceSqr: 4f, haveBest: true, bestUsable: false, bestDistanceSqr: 1f));
            Assert.IsFalse(PortalUseRules.IsBetterCandidate(usable: false, distanceSqr: 1f, haveBest: true, bestUsable: true, bestDistanceSqr: 4f));
        }

        [Test]
        public void BetweenTheSameKindTheNearerWinsAndTheFirstAlwaysStands()
        {
            Assert.IsTrue(PortalUseRules.IsBetterCandidate(true, 1f, haveBest: true, bestUsable: true, bestDistanceSqr: 4f));
            Assert.IsFalse(PortalUseRules.IsBetterCandidate(true, 4f, haveBest: true, bestUsable: true, bestDistanceSqr: 1f));
            Assert.IsTrue(PortalUseRules.IsBetterCandidate(false, 9f, haveBest: false, bestUsable: false, bestDistanceSqr: float.MaxValue));
        }

        [Test]
        public void AnOlderCounterSpendsNothing()
        {
            Assert.IsFalse(PortalUseRules.ShouldSpendForAllyTrip(lastSeenCounter: 5, newCounter: 4, targetOwnerActor: 3, myActor: 3, writerIsTeammate: true));
        }

        // ---- the portal's glow: grey while its owner has no charge (Tudor D23) ----------------------

        [Test]
        public void AnOwnerWithAChargeShowsThePortalUsable()
        {
            Assert.IsTrue(PortalUseRules.ShowsUsable(flagKnown: true, ownerHasCharge: true));
        }

        [Test]
        public void AnOwnerWithoutAChargeShowsThePortalGrey()
        {
            Assert.IsFalse(PortalUseRules.ShowsUsable(flagKnown: true, ownerHasCharge: false));
        }

        [Test]
        public void AnAbsentFlagShowsThePortalUsableSoNothingGreysBeforeTheFirstPublish()
        {
            Assert.IsTrue(PortalUseRules.ShowsUsable(flagKnown: false, ownerHasCharge: false));
        }
        // ---- group travel: teammates standing on the portal when a trip completes ------------------

        [Test]
        public void ALivingTeammateOnTheDeparturePortalJoinsTheTrip()
        {
            Assert.IsTrue(PortalUseRules.JoinsGroupTrip(userAlive: true, sameTeamAsOwner: true, onDeparturePortal: true, isTheTraveller: false));
        }

        [Test]
        public void ADeadPlayerDoesNotJoinTheTrip()
        {
            Assert.IsFalse(PortalUseRules.JoinsGroupTrip(userAlive: false, sameTeamAsOwner: true, onDeparturePortal: true, isTheTraveller: false));
        }

        [Test]
        public void AnEnemyOnThePortalDoesNotJoinTheTrip()
        {
            Assert.IsFalse(PortalUseRules.JoinsGroupTrip(userAlive: true, sameTeamAsOwner: false, onDeparturePortal: true, isTheTraveller: false));
        }

        [Test]
        public void ATeammateOnTheOtherPortalOfThePairDoesNotJoinTheTrip()
        {
            Assert.IsFalse(PortalUseRules.JoinsGroupTrip(userAlive: true, sameTeamAsOwner: true, onDeparturePortal: false, isTheTraveller: false));
        }

        [Test]
        public void TheTravellerItselfIsNotPulledAlongASecondTime()
        {
            Assert.IsFalse(PortalUseRules.JoinsGroupTrip(userAlive: true, sameTeamAsOwner: true, onDeparturePortal: true, isTheTraveller: true));
        }

        [Test]
        public void AGroupSignalOlderThanTheFreshWindowIsIgnored()
        {
            Assert.IsTrue(PortalUseRules.IsFreshGroupSignal(0.3f));
            Assert.IsFalse(PortalUseRules.IsFreshGroupSignal(PortalUseRules.GroupSignalFreshSeconds + 0.1f));
        }

        [Test]
        public void AGroupMemberArrivesAtTheSameOffsetFromTheOtherPortalsCentre()
        {
            Vector3 arrival = PortalUseRules.GroupArrivalPoint(new Vector3(10f, 0f, 0f), new Vector3(10.6f, 0f, -0.4f),
                                                              new Vector3(-20f, 0f, 5f), PortalRadius, 0.5f);
            Assert.AreEqual(-19.4f, arrival.x, 0.001f);
            Assert.AreEqual(4.6f, arrival.z, 0.001f);
        }

        [Test]
        public void AnOffsetBeyondThePortalRadiusIsClampedToTheRim()
        {
            Vector3 arrival = PortalUseRules.GroupArrivalPoint(Vector3.zero, new Vector3(1.4f, 0f, 0f), new Vector3(10f, 0f, 0f), PortalRadius, 0.5f);
            Assert.AreEqual(11f, arrival.x, 0.001f);
            Assert.AreEqual(0f, arrival.z, 0.001f);
        }

        [Test]
        public void AMemberStandingNearTheCentreIsPushedOutSoItDoesNotLandInsideTheTraveller()
        {
            Vector3 arrival = PortalUseRules.GroupArrivalPoint(Vector3.zero, new Vector3(0.1f, 0f, 0f), new Vector3(10f, 0f, 0f), PortalRadius, 0.5f);
            Assert.AreEqual(10.5f, arrival.x, 0.001f);
        }

        [Test]
        public void AMemberExactlyOnTheCentreStillGetsAFixedSeparationFromIt()
        {
            Vector3 arrival = PortalUseRules.GroupArrivalPoint(Vector3.zero, Vector3.zero, new Vector3(10f, 0f, 0f), PortalRadius, 0.5f);
            Vector3 flat = arrival - new Vector3(10f, 0f, 0f);
            flat.y = 0f;
            Assert.AreEqual(0.5f, flat.magnitude, 0.001f);
        }

        [Test]
        public void TheArrivalPointKeepsTheOtherPortalsHeight()
        {
            Vector3 arrival = PortalUseRules.GroupArrivalPoint(new Vector3(0f, 3f, 0f), new Vector3(0.5f, 3.5f, 0f), new Vector3(10f, 1f, 0f), PortalRadius, 0.25f);
            Assert.AreEqual(1f, arrival.y, 0.001f);
        }
        [Test]
        public void ASeparationWiderThanThePortalRadiusIsStillKept()
        {
            // A player is wider than half the portal: landing 0.8 m from the traveller would overlap the body, so the
            // member is placed a full body width (1.4 m) out, just past the rim, still touching the portal.
            Vector3 arrival = PortalUseRules.GroupArrivalPoint(Vector3.zero, new Vector3(0.8f, 0f, 0f), new Vector3(10f, 0f, 0f), PortalRadius, 1.4f);
            Assert.AreEqual(11.4f, arrival.x, 0.001f);
        }
    }
}
