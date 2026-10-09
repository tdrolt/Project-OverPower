using UnityEngine;

namespace Overpower.Weapons
{
    /// <summary>
    /// Makes a beam pass straight through buildings - the "Through Walls" leaf of the laser path. There is nothing to tune: having this component
    /// on the beam prefab IS the setting.
    /// It works by never asking Physics about walls at all, rather than asking and then ignoring the answer: Hitscan passes its hit mask through
    /// RemoveWallsFrom before casting, so the Building layer is simply not in the query and BeamResolver never sees a wall to stop on. Only
    /// Building is removed; anything else that stops a beam, and players (on the Default layer), still counts.
    /// Like every other laser leaf it gets a wind-up warning line - an instant beam gave nobody a way to react, so this leaf could not be balanced
    /// fairly by playing it. See WeaponDefinition.WindupSeconds, LaserWarningLine and Hitscan's beam-colour fields.
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
