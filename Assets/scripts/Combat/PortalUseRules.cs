using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// The three pure decisions behind teammates using each other's teleport portals - who is standing on a portal,
    /// who may use it, and whether the portal's owner should pay a charge for a teammate's trip. No Photon, no scene:
    /// the callers gather the facts and pass them in.
    /// </summary>
    public static class PortalUseRules
    {
        /// <summary>True when a player's body touches the portal's circle: the flat distance between the two centres
        /// is at most the portal's radius plus the player's own capsule radius. Height is ignored.</summary>
        public static bool IsOnPortal(Vector3 portalCentre, Vector3 playerCentre, float portalRadius, float playerRadius)
        {
            Vector3 delta = portalCentre - playerCentre;
            delta.y = 0f;
            float reach = portalRadius + playerRadius;
            return delta.sqrMagnitude <= reach * reach;
        }

        /// <summary>True when this player may channel through the portal: alive, and either its owner or on its
        /// owner's team (never an enemy), and only while the owner has a portal charge to spend.</summary>
        public static bool MayUse(bool isOwner, bool sameTeam, bool userAlive, bool ownerHasCharge)
        {
            if (!ownerHasCharge || !userAlive)
                return false;
            return isOwner || sameTeam;
        }

        /// <summary>The counter to remember for that teammate after a message: never goes backwards.</summary>
        public static int NewLastSeen(int lastSeenCounter, int newCounter) => newCounter > lastSeenCounter ? newCounter : lastSeenCounter;

        /// <summary>When a body touches several portals, whether a candidate should replace the best so far: a portal
        /// this player may use always beats one it may not (so a nearer enemy portal cannot block a teammate's), and
        /// between two of the same kind the nearer wins.</summary>
        public static bool IsBetterCandidate(bool usable, float distanceSqr, bool haveBest, bool bestUsable, float bestDistanceSqr)
        {
            if (!haveBest)
                return true;
            if (usable != bestUsable)
                return usable;
            return distanceSqr < bestDistanceSqr;
        }

        /// <summary>The owner's side of a teammate's trip: true when a "trip finished" message should cost the owner
        /// one charge. Spends only for a message about the owner's own portals, from a teammate, whose counter is
        /// newer than the last one seen from that teammate - so an echo, a repeat or an out-of-date value spends
        /// nothing.</summary>
        public static bool ShouldSpendForAllyTrip(int lastSeenCounter, int newCounter, int targetOwnerActor, int myActor, bool writerIsTeammate)
        {
            return writerIsTeammate && targetOwnerActor == myActor && newCounter > lastSeenCounter;
        }

        /// <summary>Whether a portal should show its normal colour (true) or its cooling-down grey (false): usable exactly
        /// while its owner has a charge. A flag that has not been published yet counts as usable, so nothing greys by
        /// mistake before the owner's first publish.</summary>
        public static bool ShowsUsable(bool flagKnown, bool ownerHasCharge) => !flagKnown || ownerHasCharge;
        // ---- group travel (Task 15) ----------------------------------------------------------------

        /// <summary>How old, in seconds, a "a trip just finished" signal may be and still pull bystanders along. A late
        /// joiner replaying buffered messages must not teleport anyone for a trip that ended long ago.</summary>
        public const float GroupSignalFreshSeconds = 2f;

        /// <summary>Whether a player standing around when someone else's trip through a portal completes travels too:
        /// alive, on the owner's team, touching the portal that was used (not the other one of the pair), and not the
        /// traveller who already went.</summary>
        public static bool JoinsGroupTrip(bool userAlive, bool sameTeamAsOwner, bool onDeparturePortal, bool isTheTraveller)
        {
            return userAlive && sameTeamAsOwner && onDeparturePortal && !isTheTraveller;
        }

        /// <summary>True while a group-trip signal is recent enough to act on.</summary>
        public static bool IsFreshGroupSignal(float secondsLate) => secondsLate <= GroupSignalFreshSeconds;

        /// <summary>Where a group member lands: the same flat offset from the arrival portal's centre that they had from the
        /// departure portal's centre, clamped to the portal's radius, at the arrival portal's height. The traveller who
        /// triggered the trip lands on the centre, so an offset shorter than minSeparation is pushed out to it (in the same
        /// direction, or along +x when the member stood exactly on the centre) so nobody lands inside anybody.</summary>
        public static Vector3 GroupArrivalPoint(Vector3 departureCentre, Vector3 memberPosition, Vector3 arrivalCentre, float portalRadius, float minSeparation)
        {
            Vector3 offset = memberPosition - departureCentre;
            offset.y = 0f;
            float length = offset.magnitude;
            Vector3 direction = length > 0.0001f ? offset / length : Vector3.right;
            float clamped = Mathf.Min(length, portalRadius);
            clamped = Mathf.Max(clamped, minSeparation); // may exceed the radius: the rim is a ring the body only has to touch
            return arrivalCentre + direction * clamped;
        }
    }
}
