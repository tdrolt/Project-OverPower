using UnityEngine;
using Overpower.Arena;

namespace Overpower.Weapons
{
    /// <summary>
    /// The layer-mask rule every hit-detecting shot shares, whether it travels as a swept
    /// projectile (ProjectileMotor) or lands instantly (Hitscan). Pulled out so the two stop
    /// carrying their own copies of the same invariant - see the code review after the laser path
    /// landed (commits 3af6dde, 3dd26c2).
    /// </summary>
    public static class HitMasks
    {
        /// <summary>The designer's layer choices, minus three that are never negotiable: Bullet
        /// (bullets colliding with each other was a fixed playtest bug), DeadPlayer (a corpse
        /// blocking shots was another), and Barrier (GDD p.29: a jersey barrier blocks movement
        /// only - every projectile passes it, including Stun Gun Bullet and Raybeam, whose own
        /// hitMask ticks every layer). All three are correctness invariants rather than tuning, so
        /// they are stripped here instead of trusted to the Inspector dropdown.</summary>
        public static int StripNonNegotiableLayers(LayerMask designerMask)
        {
            return designerMask.value & ~LayerBit("Bullet") & ~LayerBit("DeadPlayer") & ~ArenaLayers.Barrier;
        }

        private static int LayerBit(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            return layer >= 0 ? 1 << layer : 0;
        }
    }
}
