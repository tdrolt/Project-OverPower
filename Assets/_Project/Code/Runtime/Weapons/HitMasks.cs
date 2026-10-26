using UnityEngine;
using Overpower.Arena;

namespace Overpower.Weapons
{
    /// <summary>
    /// The layer-mask rule every hit-detecting shot shares, whether it travels as a swept projectile (ProjectileMotor) or lands instantly (Hitscan).
    /// One home so the two never carry their own copies of the invariant.
    /// </summary>
    public static class HitMasks
    {
        /// <summary>The designer's layer choices, minus three that are never negotiable: Bullet (bullets must not collide with each other),
        /// DeadPlayer (a corpse must not block shots) and Barrier (GDD p.29: a jersey barrier blocks movement only, so every projectile passes it,
        /// including Stun Gun Bullet and Raybeam, whose own hitMask ticks every layer). Correctness invariants rather than tuning, so they are
        /// stripped here instead of trusted to the Inspector dropdown.</summary>
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
