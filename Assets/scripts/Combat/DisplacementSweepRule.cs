using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Combat
{
    /// <summary>
    /// One thing a displacement's capsule sweep touched, reduced to the facts the stop rule needs. Built by
    /// PlayerDisplacement from a real overlap or capsule cast; plain data, so the rule is tested without a scene.
    /// </summary>
    public readonly struct SweepContact
    {
        /// <summary>How far along the move the capsule reaches it. 0 when the capsule already overlaps it.</summary>
        public readonly float Distance;

        /// <summary>The surface normal the cast reported. Meaningless when StartsInside: Unity reports minus the sweep
        /// direction there, which says nothing about which side the wall is on.</summary>
        public readonly Vector3 Normal;

        /// <summary>The capsule already touches or overlaps this collider before the move.</summary>
        public readonly bool StartsInside;

        /// <summary>Physics found an overlap one skin width along the move, so PushOut means something.</summary>
        public readonly bool HasPushOut;

        /// <summary>Unit direction physics would push the capsule out of this collider.</summary>
        public readonly Vector3 PushOut;

        public SweepContact(float distance, Vector3 normal, bool startsInside, bool hasPushOut, Vector3 pushOut)
        {
            Distance = distance;
            Normal = normal;
            StartsInside = startsInside;
            HasPushOut = hasPushOut;
            PushOut = pushOut;
        }

        public static SweepContact Hit(float distance, Vector3 normal) => new SweepContact(distance, normal, false, false, Vector3.zero);

        public static SweepContact Inside(Vector3 pushOut) => new SweepContact(0f, Vector3.zero, true, true, pushOut);

        public static SweepContact InsideWithoutPushOut() => new SweepContact(0f, Vector3.zero, true, false, Vector3.zero);
    }

    /// <summary>
    /// Where a displacement (a dash, a zip pull, a knockback) has to stop; one rule for all three
    /// because they all travel through PlayerDisplacement (see IDisplaceable for why one mover exists).
    ///
    /// Three parts: a wall ahead stops the move a SKIN width short, so the next step does not start
    /// from inside the wall's contact; a wall the capsule ALREADY overlaps blocks only a move that goes
    /// deeper into it, judged by the direction physics would push the capsule out (a dash along a wall
    /// you are pressed against still works); the floor underfoot is never a wall (normal roughly up).
    ///
    /// Why: Rigidbody.SweepTestAll never reports a collider the capsule already overlaps or merely
    /// touches (measured: no hit while touching or overlapping, where Physics.OverlapCapsule and a
    /// zero-distance CapsuleCastAll both saw the wall), so a second dash from a player pressed into a
    /// thin wall sailed through it. Plain C#, tested in edit mode (DisplacementSweepRuleTests).
    /// </summary>
    public static class DisplacementSweepRule
    {
        /// <summary>Metres a move stops short of the wall it hits. Not a tuning value: it is the gap that keeps the
        /// next step's queries from starting flush against that wall.</summary>
        public const float SkinMetres = 0.05f;

        /// <summary>Metres the swept capsule is raised, so the floor the player stands on is neither an overlap nor a
        /// hit. Not a tuning value.</summary>
        public const float LiftMetres = 0.05f;

        /// <summary>Above this, a hit's normal points up enough to be the ground rather than a wall. Not a
        /// tuning value.</summary>
        public const float FloorNormalY = 0.5f;

        /// <summary>How much of the move must point against a touching wall's push-out before it counts as going INTO
        /// it: about 6 degrees. Not a tuning value - it is the tolerance that lets a dash run along a wall the player
        /// is pressed against.</summary>
        public const float IntoSurfaceDot = -0.1f;

        /// <summary>A move that covers less than this isn't movement. Not a tuning value.</summary>
        public const float MinUsefulTravelMetres = 0.01f;

        /// <summary>Does this contact stop a move in <paramref name="moveDirection"/> (unit length)?</summary>
        public static bool Blocks(in SweepContact contact, Vector3 moveDirection)
        {
            if (contact.StartsInside)
                return contact.HasPushOut && Vector3.Dot(moveDirection, contact.PushOut) < IntoSurfaceDot;

            return contact.Normal.y <= FloorNormalY;
        }

        /// <summary>
        /// How far the move may go, and which contact stops it (-1 when none does). A contact that only stops the move
        /// beyond what was requested is not a blocker: nothing is in the way this step.
        /// </summary>
        public static float AllowedTravel(float requested, IReadOnlyList<SweepContact> contacts, Vector3 moveDirection,
                                           out int blockerIndex)
        {
            blockerIndex = -1;
            float allowed = Mathf.Max(0f, requested);

            if (contacts == null)
                return allowed;

            for (int i = 0; i < contacts.Count; i++)
            {
                SweepContact contact = contacts[i];
                if (!Blocks(contact, moveDirection))
                    continue;

                float stop = contact.StartsInside ? 0f : Mathf.Max(0f, contact.Distance - SkinMetres);
                if (stop < allowed)
                {
                    allowed = stop;
                    blockerIndex = i;
                }
            }

            return allowed;
        }

        /// <summary>Worth starting at all - see MinUsefulTravelMetres.</summary>
        public static bool IsUsefulTravel(float allowed) => allowed >= MinUsefulTravelMetres;

        /// <summary>
        /// Only static geometry may refuse a dash outright as a "start already inside" blocker. A
        /// collider attached to a Rigidbody (a living player, anything physics-driven) never counts, so
        /// standing flush against an enemy cannot refuse a dash the way standing flush against a wall
        /// does. A dash travelling TOWARD a player from a distance still stops at their body; this only
        /// guards the "touching, so the whole move is refused before it starts" case.
        /// </summary>
        public static bool CanBlockAsStartInside(bool colliderHasAttachedRigidbody) => !colliderHasAttachedRigidbody;
    }
}
