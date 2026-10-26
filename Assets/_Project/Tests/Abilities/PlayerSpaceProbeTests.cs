using NUnit.Framework;
using Overpower.Abilities;

namespace Overpower.Tests
{
    public class PlayerSpaceProbeTests
    {
        // Multiplayer Player.prefab's capsule: centre y 0.8063041, height 2.6126082, so its bottom is 0.5 m below the root.
        private const float CentreY = 0.8063041f;
        private const float Height = 2.6126082f;

        [Test]
        public void AStandingPlayersRootIsHalfAMetreAboveTheFloorItStandsOn()
        {
            // Portal travel used to teleport to the floor point itself, which sank the capsule that half metre.
            Assert.AreEqual(0.5f, PlayerSpaceProbe.RootHeightOnGround(0f, CentreY, Height), 1e-4f);
            Assert.AreEqual(1.3f, PlayerSpaceProbe.RootHeightOnGround(0.8f, CentreY, Height), 1e-4f);
        }
    }
}
