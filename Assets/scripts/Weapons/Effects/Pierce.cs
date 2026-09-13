using UnityEngine;
using Overpower.Combat;

namespace Overpower.Weapons
{
    /// <summary>
    /// Lets a beam carry on through the people it hits, damaging each of them, instead of stopping
    /// on the first one. Every laser in the upgrade tree has one - a line of enemies is the target
    /// the laser is built to punish.
    ///
    /// A setting, not a script with its own logic. Hitscan reads Max Targets off the same prefab
    /// and hands it to BeamResolver, which is where "stop after this many" is actually decided and
    /// unit tested. A beam prefab without this component stops on the first target it strikes,
    /// exactly like a bullet.
    ///
    /// Pierce never lets a beam through a WALL. That is a separate decision with its own component,
    /// IgnoreWalls, so a designer can have either one without the other.
    /// </summary>
    [DisallowMultipleComponent]
    public class Pierce : MonoBehaviour
    {
        [SerializeField, Tooltip("How many targets one beam may pass through and damage before it " +
                 "stops. -1 means no limit: everyone standing in the line is hit. 1 behaves exactly " +
                 "like having no Pierce at all, and 0 is read as 1. Teammates and the shooter are " +
                 "passed through without counting. The designer's number is -1.")]
        private int maxTargets = BeamResolver.Unlimited;

        public int MaxTargets => maxTargets;
    }
}
