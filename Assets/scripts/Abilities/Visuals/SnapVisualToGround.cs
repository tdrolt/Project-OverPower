using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// Puts this visual on the floor the moment it spawns (ability visuals step 2) - for things placed at the caster's
    /// root (mine, electric fence) or at a rocket's airburst height (the cursor rocket's fire field), whose flat visuals
    /// otherwise float. Moves only this visual transform: the gameplay object, and every radius measured from it, stays
    /// exactly where the game put it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnapVisualToGround : MonoBehaviour
    {
        [SerializeField, Tooltip("How far above the floor this visual's origin sits, in metres - a hair, so flat parts " +
                 "don't flicker into the ground.")]
        private float heightAboveGround = 0.02f;

        private void Awake()
        {
            Vector3 from = transform.parent != null ? transform.parent.position : transform.position;
            if (GroundSnap.TryFindGroundY(from, out float groundY))
                transform.position = new Vector3(transform.position.x, groundY + heightAboveGround, transform.position.z);
        }
    }
}
