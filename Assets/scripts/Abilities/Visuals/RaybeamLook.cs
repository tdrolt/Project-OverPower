using System.Collections.Generic;
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>The Raybeam's look: how wide its white-hot core is inside the red sheath (the sheath is the beam's own Beam Width, what
    /// hits), and when a scorch flashes where a beam ends.</summary>
    public static class RaybeamLook
    {
        public static float CoreWidth(float beamWidth, float coreShare) => beamWidth * Mathf.Clamp01(coreShare);

        /// <summary>True when a wall stopped the beam, or a placed structure did (it always ends the beam where it takes the hit);
        /// false when the beam ran out its range, whoever it pierced on the way.</summary>
        public static bool EndsOnSurface(BeamResult beam) =>
            beam.StoppedOnGeometry || (beam.Struck.Count > 0 && beam.Struck[beam.Struck.Count - 1].IsStructure);

        /// <summary>A scorch needs a surface, and is shown where my team sees that spot (a gun's impact rule).</summary>
        public static bool ShowsScorch(bool endsOnSurface, bool endSeen) => endsOnSurface && endSeen;

        /// <summary>Where the scorch lies: on the surface that stopped the beam, at its own touch point (the beam's Length is where the swept
        /// sphere's centre stopped, half a Beam Width short), facing out of it (<paramref name="normals"/> runs alongside the contacts).
        /// A surface the sweep started inside has no touch point (Unity reports distance 0), so the scorch takes the beam's end, facing back
        /// along the beam.</summary>
        public static void ScorchSpot(IReadOnlyList<BeamContact> contacts, IReadOnlyList<Vector3> normals, BeamResult beam, Vector3 end,
                                      Vector3 aim, out Vector3 point, out Quaternion facing)
        {
            point = end;
            Vector3 normal = -aim;
            for (int i = 0; i < contacts.Count; i++)
            {
                BeamContact contact = contacts[i];
                // Length is the stopping contact's own Distance, copied, so the match is exact.
                if (contact.Distance != beam.Length || contact.Distance <= 0f || (contact.Target != null && !contact.IsStructure))
                    continue;
                point = contact.Point;
                if (i < normals.Count && normals[i].sqrMagnitude > 0f)
                    normal = normals[i];
                break;
            }
            facing = Quaternion.LookRotation(normal);
        }
    }
}
