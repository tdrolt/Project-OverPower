using UnityEngine;

namespace Overpower.Weapons
{
    /// <summary>
    /// Makes a beam pass straight through buildings - the "Through Walls" leaf of the laser path.
    /// There is nothing to tune: having this component on the beam prefab IS the setting.
    ///
    /// It works by never asking Physics about walls at all, rather than by asking and then ignoring
    /// the answer. Hitscan passes its hit mask through RemoveWallsFrom before casting, so the
    /// Building layer is simply not in the query, and BeamResolver never sees a wall to stop on.
    /// Only Building is removed. Anything else that stops a beam - and players, who live on the
    /// Default layer - still counts.
    ///
    /// CHANGED BY TUDOR, 2026-09-14 (Task 11b): this leaf now gets a warning like every other
    /// laser. The line above used to read "Deliberately NO warning... on the designer's explicit
    /// instruction: this leaf is expected to be too strong without fog of war, and he will
    /// rebalance it after playing it rather than having it softened in advance." All three lasers
    /// hit instantly with no way for anyone to react, which made that "after playing it" test
    /// impossible to run fairly - so Tudor's revised call is a short wind-up plus a warning line for
    /// ALL THREE laser leaves, this one included, with the beam itself made more visible too. See
    /// WeaponDefinition.WindupSeconds, LaserWarningLine and Hitscan's beam-colour fields.
    /// </summary>
    [DisallowMultipleComponent]
    public class IgnoreWalls : MonoBehaviour
    {
        // The layer name, not a tuning value - the same way ProjectileMotor names Bullet and
        // DeadPlayer. Every building in the arena sits on this layer.
        private const string WallLayerName = "Building";

        /// <summary>The same mask with the Building layer taken out of it.</summary>
        public int RemoveWallsFrom(int mask)
        {
            int layer = LayerMask.NameToLayer(WallLayerName);
            return layer >= 0 ? mask & ~(1 << layer) : mask;
        }
    }
}
