using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Vision
{
    /// <summary>How far and how wide one player sees: a cone toward the cursor plus a circle around them (GDD).
    /// Lengths in metres, angle in degrees.</summary>
    public readonly struct SightShape
    {
        public readonly float ConeAngleDegrees, ConeLength, CircleRadius;

        public SightShape(float coneAngleDegrees, float coneLength, float circleRadius)
        {
            ConeAngleDegrees = coneAngleDegrees;
            ConeLength = coneLength;
            CircleRadius = circleRadius;
        }
    }

    /// <summary>One pair of eyes on the ground plane (world XZ passed as x, y): where it is, which way it looks
    /// (toward the cursor) and the shape it sees with right now.</summary>
    public readonly struct Eye
    {
        public readonly Vector2 Position, Facing;
        public readonly SightShape Shape;
        /// <summary>World height of this eye (its own feet plus the eye height), used for the wall tests.</summary>
        public readonly float EyeY;

        public Eye(Vector2 position, Vector2 facing, SightShape shape, float eyeY = 0f)
        {
            Position = position;
            Facing = facing;
            Shape = shape;
            EyeY = eyeY;
        }
    }

    /// <summary>What the local player is doing, which decides whose eyes the team's sight comes from.</summary>
    public enum ViewerMode { Alive, Dead, Spectating }

    /// <summary>
    /// Pure rules for "what can my team see" - Vector2 and Mathf only, no scene, Photon or Renderer, so every rule is
    /// provable in an edit-mode test (same reasoning as MineVisibilityRule and ConeFilter). The game passes in the
    /// wall test; walls are whatever stops bullets.
    /// </summary>
    public static class VisionRules
    {
        /// <summary>The shape an eye uses. Holding the Scope is Tudor's trade: a narrow, long cone in exchange for a
        /// smaller circle (scopedCircleChange is negative to shrink). The circle never goes below 0.</summary>
        public static SightShape ShapeFor(float coneAngle, float coneLength, float circleRadius,
            bool scoped, float scopedConeAngle, float scopedConeLength, float scopedCircleChange)
        {
            if (!scoped)
                return new SightShape(coneAngle, coneLength, circleRadius);
            return new SightShape(scopedConeAngle, scopedConeLength, Mathf.Max(0f, circleRadius + scopedCircleChange));
        }

        /// <summary>Inside the circle, or inside the cone (angle from facing within half the cone angle, distance
        /// within the length). Ignores walls. A zero facing has no direction to look, so it counts as circle only.</summary>
        public static bool InShape(Eye eye, Vector2 point)
        {
            Vector2 offset = point - eye.Position;
            float distance = offset.magnitude;
            if (distance <= eye.Shape.CircleRadius)
                return true;

            if (eye.Facing.sqrMagnitude < 1e-8f || distance > eye.Shape.ConeLength)
                return false;

            return Vector2.Angle(eye.Facing, offset) <= eye.Shape.ConeAngleDegrees * 0.5f;
        }

        /// <summary>Is this candidate one of my team's eyes right now? Alive: me (if alive) and living teammates.
        /// Dead: living teammates (I see through them). Spectating: living players of the team I watch, whatever my
        /// own team is. My own team not arrived yet (localTeam below 0): Alive sees only me, Dead sees nobody.
        /// Enemies and the dead are never eyes.</summary>
        public static bool IsEye(int localTeam, ViewerMode mode, int watchedTeam,
            int candidateTeam, bool candidateAlive, bool candidateIsLocal)
        {
            if (!candidateAlive)
                return false;

            switch (mode)
            {
                case ViewerMode.Alive:
                    return localTeam < 0 ? candidateIsLocal : candidateTeam == localTeam;
                case ViewerMode.Dead:
                    return localTeam >= 0 && candidateTeam == localTeam && !candidateIsLocal;
                case ViewerMode.Spectating:
                    return watchedTeam >= 0 && candidateTeam == watchedTeam;
                default:
                    return false;
            }
        }

        /// <summary>My team sees the point if any eye has it in shape and nothing blocks the line from that eye.
        /// clearLine is passed in so tests can fake walls; the game passes a Linecast on the Building layer.</summary>
        public static bool TeamSees(IReadOnlyList<Eye> eyes, Vector2 point, Func<Vector2, Vector2, bool> clearLine)
        {
            return TeamSeesWithEye(eyes, point, (Eye eye, Vector2 target) => clearLine(eye.Position, target));
        }

        /// <summary>Same, but the wall test receives the whole eye, so it can use that eye's own height.</summary>
        public static bool TeamSeesWithEye(IReadOnlyList<Eye> eyes, Vector2 point, Func<Eye, Vector2, bool> clearLine)
        {
            for (int i = 0; i < eyes.Count; i++)
            {
                if (InShape(eyes[i], point) && clearLine(eyes[i], point))
                    return true;
            }
            return false;
        }
    }
}
