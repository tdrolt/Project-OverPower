using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// Makes the arena three identical thirds, turned 120° apart about Centre.
    ///
    /// HOW TO EDIT THE ARENA: change only the objects under Source. Then press "Rebuild thirds" on this component
    /// (or OverPower > Arena > Rebuild thirds), look at the result, and save the scene. The two generated thirds are
    /// deleted and copied again from Source on every rebuild, so an edit made directly to them is thrown away. The
    /// Inspector greys them out to make that obvious.
    ///
    /// Towers, their flag carpets and spawn points are NOT copied: they are networked or carry ids that must stay
    /// unique. They are listed in Snapped Triplets instead. You place the Source one, and the rebuild moves its two
    /// partners to match. Objects in Centred are moved onto the centre (their height is kept).
    ///
    /// This component does nothing during play or in a build. It only stores the setup that the Editor tool
    /// (Assets/scripts/Editor/Arena/ArenaSymmetryBuilder.cs) reads.
    /// </summary>
    public class ArenaSymmetry : MonoBehaviour
    {
        [Serializable]
        public class SnappedTriplet
        {
            [Tooltip("The one you place by hand, in the Source third.")]
            public Transform source;

            [Tooltip("Moved by Rebuild thirds to the Source one turned 120° about the centre. Don't place it by hand.")]
            public Transform at120;

            [Tooltip("Moved by Rebuild thirds to the Source one turned 240° about the centre. Don't place it by hand.")]
            public Transform at240;
        }

        [Tooltip("The point the thirds turn about, in world space (only X and Z matter). Moving it moves every " +
                 "generated object and snapped partner on the next rebuild.")]
        public Vector3 centre = new Vector3(65.05f, 0f, 53.34f);

        [Tooltip("Drawing aid only: the map angle (degrees counter-clockwise from +X, seen from above) where the Source " +
                 "third's lines are drawn in the Scene view. Rebuild copies everything under Source regardless.")]
        public float sourceStartDegrees = 25f;

        [Tooltip("The only third you edit. Everything under it is copied into the two generated thirds.")]
        public Transform source;

        [Tooltip("Rebuilt from Source turned 120°. Never edit its children by hand.")]
        public Transform generated120;

        [Tooltip("Rebuilt from Source turned 240°. Never edit its children by hand.")]
        public Transform generated240;

        [Tooltip("Networked or id-carrying objects kept symmetric by moving, not copying: towers, their flag carpets, " +
                 "spawn points.")]
        public List<SnappedTriplet> snappedTriplets = new List<SnappedTriplet>();

        [Tooltip("Objects moved onto the centre by Rebuild thirds, keeping their height (the Tier 4 tower and its " +
                 "carpet).")]
        public List<Transform> centred = new List<Transform>();

        private void OnDrawGizmosSelected()
        {
            // The three dividing lines, so a designer can see where the Source third ends.
            Gizmos.color = Color.yellow;
            for (int i = 0; i < 3; i++)
            {
                float radians = (sourceStartDegrees + RadialSymmetry.ThirdDegrees * i) * Mathf.Deg2Rad;
                Gizmos.DrawLine(centre, centre + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * 80f);
            }
            Gizmos.DrawWireSphere(centre, 1f);
        }
    }
}
