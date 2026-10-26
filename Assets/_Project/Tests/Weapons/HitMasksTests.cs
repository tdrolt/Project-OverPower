using NUnit.Framework;
using UnityEngine;
using Overpower.Arena;
using Overpower.Weapons;

namespace Overpower.Tests
{
    /// <summary>Arena amendment 1, step 4a: pins the Barrier layer's existence and the shot-mask invariant that
    /// keeps a barrier out of every hit mask forever - GDD p.29, jersey barriers block movement only, every
    /// projectile passes - the same way Bullet and DeadPlayer are already kept out.</summary>
    public class HitMasksTests
    {
        [Test]
        public void TheBarrierLayerExistsPlayersCollideWithItAndArenaLayersNameIt()
        {
            int layer = LayerMask.NameToLayer(ArenaLayers.BarrierLayerName);
            Assert.GreaterOrEqual(layer, 0, "the Barrier layer must exist (arena step 4a's TagManager change, at layer 8)");

            int defaultLayer = LayerMask.NameToLayer("Default");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(defaultLayer, layer),
                "a player's body (Default) must collide with the Barrier layer - only a living player's body does");

            Assert.AreEqual(1 << layer, ArenaLayers.Barrier);
            Assert.AreEqual(LayerMask.GetMask("Building") | ArenaLayers.Barrier, ArenaLayers.WallsAndBarriers);
            Assert.AreEqual(LayerMask.GetMask("Default", "Building") | ArenaLayers.Barrier, ArenaLayers.BodiesWallsAndBarriers);
        }

        [Test]
        public void NoShotMaskEverKeepsBarrierBulletOrDeadPlayer()
        {
            int stripped = HitMasks.StripNonNegotiableLayers(~0);
            Assert.AreEqual(0, stripped & ArenaLayers.Barrier, "a shot mask must never keep Barrier - a barrier never ticks into a shot");
            Assert.AreEqual(0, stripped & LayerMask.GetMask("Bullet"));
            Assert.AreEqual(0, stripped & LayerMask.GetMask("DeadPlayer"));

            // Default + Building (today's every projectile hitMask) is unaffected: none of the three stripped bits
            // were ever in it, so stripping something already clean must return exactly what went in.
            Assert.AreEqual(9, HitMasks.StripNonNegotiableLayers(9));
        }
    }
}
