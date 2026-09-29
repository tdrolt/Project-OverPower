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

        /// <summary>The owner's side of a teammate's trip: true when a "trip finished" message should cost the owner
        /// one charge. Spends only for a message about the owner's own portals, from a teammate, whose counter is
        /// newer than the last one seen from that teammate - so an echo, a repeat or an out-of-date value spends
        /// nothing.</summary>
        /// <summary>The counter to remember for that teammate after a message: never goes backwards.</summary>
        public static int NewLastSeen(int lastSeenCounter, int newCounter) => newCounter > lastSeenCounter ? newCounter : lastSeenCounter;

        public static bool ShouldSpendForAllyTrip(int lastSeenCounter, int newCounter, int targetOwnerActor, int myActor, bool writerIsTeammate)
        {
            return writerIsTeammate && targetOwnerActor == myActor && newCounter > lastSeenCounter;
        }
    }
}
