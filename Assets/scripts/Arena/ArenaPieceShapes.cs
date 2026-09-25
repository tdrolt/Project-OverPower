using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>Box maths shared by the edit-time arena builder and the play-time phase-two cut, so a barrier built
    /// either way blocks exactly the same band.</summary>
    public static class ArenaPieceShapes
    {
        /// <summary>A barrier's BoxCollider, in its own (scaled) space, that blocks from world bottomY to world topY
        /// whatever its look: the barrier's origin sits at world positionY and its look is lookHeight tall (its Y
        /// scale). Amendment 1: only a living player's body collides with the Barrier layer, so a tall band costs
        /// nothing and nobody can be knocked up onto the barrier.</summary>
        public static void BarrierBlockingBox(float bottomY, float topY, float positionY, float lookHeight,
                                              out Vector3 centre, out Vector3 size)
        {
            float blockingCentreYWorld = (bottomY + topY) * 0.5f;
            float blockingHeightWorld = topY - bottomY;
            float scaleY = Mathf.Max(0.0001f, lookHeight);
            centre = new Vector3(0f, (blockingCentreYWorld - positionY) / scaleY, 0f);
            size = new Vector3(1f, blockingHeightWorld / scaleY, 1f);
        }
    }
}
