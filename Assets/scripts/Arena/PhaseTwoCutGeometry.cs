using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>
    /// The phase-two wall's shape (GDD p.27; Tudor, 2026-09-25: "the wall is below the tier 3 zones and make a new
    /// recess for the tier 3 zone that replaces the tier 4 zone"). A straight wall across the arena, its face Wall
    /// Distance past the centre toward the cut capital, from outer wall to outer wall, with a recess (Recess Width x
    /// Recess Depth) centred behind the centre tower. From that one line come: the smaller outline blink, portals and the
    /// out-of-arena safety net ask while the cut stands (Playable), the closed part (the minimap's dark area, and "is
    /// this behind the wall"), one wall box per segment (through ArenaWallPlan, so its corners close exactly like the
    /// outer walls'), and where the recess's barrier stands.
    ///
    /// Plain C#, like ArenaBounds and ArenaWallPlan: every client builds the identical wall from the same outline and
    /// numbers, so no position ever travels over the network. Vector2 = (world X, world Z).
    /// </summary>
    public sealed class PhaseTwoCutGeometry
    {
        /// <summary>The wall's face, from one outer wall to the other: [edge, recess mouth, recess back, recess back,
        /// recess mouth, edge], or just [edge, edge] with no recess.</summary>
        public IReadOnlyList<Vector2> WallLine { get; }

        /// <summary>The arena with the corner cut off and the recess added.</summary>
        public ArenaBounds Playable { get; }

        /// <summary>The part the wall closes off (the recess excluded).</summary>
        public ArenaBounds Closed { get; }

        /// <summary>One box per WallLine segment, facing the open side.</summary>
        public IReadOnlyList<ArenaWallPlan.Run> WallRuns { get; }

        public bool HasRecess { get; }

        /// <summary>The middle of the recess mouth, level with the wall: where its barrier stands.</summary>
        public Vector2 BarrierCentre { get; }

        /// <summary>A Unity yaw whose local +X runs along the wall (a barrier's length axis).</summary>
        public float BarrierYawDegrees { get; }

        /// <summary>Unit vector along the wall line (exit point to entry point) - a plank's width and the recess
        /// barrier's own length run along this.</summary>
        public Vector2 AlongWall { get; }

        /// <summary>Unit vector from the arena centre toward the cut capital - the direction Past() measures
        /// positively, i.e. into the closed side. Subtracting it from a point on the wall line moves onto the open
        /// side.</summary>
        public Vector2 TowardClosed { get; }

        private PhaseTwoCutGeometry(List<Vector2> wallLine, ArenaBounds playable, ArenaBounds closed,
                                    List<ArenaWallPlan.Run> runs, bool hasRecess, Vector2 barrierCentre, float barrierYaw,
                                    Vector2 alongWall, Vector2 towardClosed)
        {
            WallLine = wallLine;
            Playable = playable;
            Closed = closed;
            WallRuns = runs;
            HasRecess = hasRecess;
            BarrierCentre = barrierCentre;
            BarrierYawDegrees = barrierYaw;
            AlongWall = alongWall;
            TowardClosed = towardClosed;
        }

        /// <summary>True for a point behind the wall: inside the old arena, outside the new one. The wall's own body counts
        /// as behind it.</summary>
        public bool IsBehindWall(Vector3 world) => Closed.SignedDistance(world) >= 0f;

        /// <summary>The two "planks" standing out from the recess mouth at each end of its barrier (Tudor 2026-09-26:
        /// "the zone is too empty"), mirrored across the cut axis: BarrierCentre offset <paramref name="spacing"/>
        /// metres either way along the wall, and <paramref name="inFront"/> metres off the wall line toward the open
        /// side. Pure geometry - makes sense with or without HasRecess; the caller decides whether to build them.</summary>
        public (Vector2 First, Vector2 Second) PlankCentres(float spacing, float inFront)
        {
            Vector2 lateral = AlongWall * spacing;
            Vector2 outward = TowardClosed * inFront;
            return (BarrierCentre + lateral - outward, BarrierCentre - lateral - outward);
        }

        /// <summary>Null when it can't be built: the wall line doesn't cross the outline exactly twice, or the recess
        /// doesn't fit between the outer walls or inside the old arena (with room for its own back wall).</summary>
        /// <param name="fullOutline">The whole arena outline (ArenaSymmetry.FullBounds.Polygon), either winding.</param>
        /// <param name="towardCutCapital">Any vector from the centre toward the cut capital.</param>
        public static PhaseTwoCutGeometry Build(IReadOnlyList<Vector2> fullOutline, Vector2 centre, Vector2 towardCutCapital,
                                                float wallDistance, float recessWidth, float recessDepth, float wallThickness)
        {
            if (fullOutline == null || fullOutline.Count < 3 || towardCutCapital.sqrMagnitude < 1e-8f)
                return null;

            Vector2 u = towardCutCapital.normalized;
            int n = fullOutline.Count;

            // The two outline edges the wall line crosses: "exit" leaves the open side, "entry" comes back into it.
            int exitEdge = -1, entryEdge = -1, crossings = 0;
            for (int i = 0; i < n; i++)
            {
                bool aClosed = Past(fullOutline[i], centre, u, wallDistance) >= 0f;
                bool bClosed = Past(fullOutline[(i + 1) % n], centre, u, wallDistance) >= 0f;
                if (aClosed == bClosed)
                    continue;
                crossings++;
                if (bClosed) exitEdge = i;
                else entryEdge = i;
            }
            if (crossings != 2)
                return null;

            Vector2 exitPoint = CrossingOn(fullOutline[exitEdge], fullOutline[(exitEdge + 1) % n], centre, u, wallDistance);
            Vector2 entryPoint = CrossingOn(fullOutline[entryEdge], fullOutline[(entryEdge + 1) % n], centre, u, wallDistance);
            Vector2 along = (entryPoint - exitPoint).normalized;
            Vector2 mouthMiddle = centre + u * wallDistance;

            var wallLine = new List<Vector2> { exitPoint };
            bool hasRecess = recessWidth > 0f && recessDepth > 0f;
            if (hasRecess)
            {
                Vector2 half = along * (recessWidth * 0.5f);
                Vector2 mouthNear = mouthMiddle - half;
                Vector2 mouthFar = mouthMiddle + half;
                if (Vector2.Dot(mouthNear - exitPoint, along) <= 0f || Vector2.Dot(entryPoint - mouthFar, along) <= 0f)
                    return null;
                Vector2 backNear = mouthNear + u * recessDepth;
                Vector2 backFar = mouthFar + u * recessDepth;
                ArenaBounds full = ArenaBounds.FromPolygon(fullOutline);
                if (full.SignedDistance(backNear) < wallThickness || full.SignedDistance(backFar) < wallThickness)
                    return null;
                wallLine.Add(mouthNear);
                wallLine.Add(backNear);
                wallLine.Add(backFar);
                wallLine.Add(mouthFar);
            }
            wallLine.Add(entryPoint);

            // Playable: the wall line first (so ArenaWallPlan.ForSource returns exactly its runs, corners included),
            // then the open side of the outline in its own order, back to where the wall starts.
            var playable = new List<Vector2>(wallLine);
            for (int i = (entryEdge + 1) % n; ; i = (i + 1) % n)
            {
                playable.Add(fullOutline[i]);
                if (i == exitEdge)
                    break;
            }

            // Closed: from the exit point round the closed side of the outline, then back along the wall line.
            var closed = new List<Vector2> { exitPoint };
            for (int i = (exitEdge + 1) % n; ; i = (i + 1) % n)
            {
                closed.Add(fullOutline[i]);
                if (i == entryEdge)
                    break;
            }
            for (int i = wallLine.Count - 1; i >= 1; i--)
                closed.Add(wallLine[i]);

            List<ArenaWallPlan.Run> runs = ArenaWallPlan.ForSource(playable, wallLine.Count - 1, wallThickness);
            float barrierYaw = Quaternion.LookRotation(new Vector3(-u.x, 0f, -u.y), Vector3.up).eulerAngles.y;

            return new PhaseTwoCutGeometry(wallLine, ArenaBounds.FromPolygon(playable), ArenaBounds.FromPolygon(closed),
                                           runs, hasRecess, mouthMiddle, barrierYaw, along, u);
        }

        // Metres past the wall line (positive = the closed side).
        private static float Past(Vector2 p, Vector2 centre, Vector2 u, float wallDistance) =>
            Vector2.Dot(p - centre, u) - wallDistance;

        private static Vector2 CrossingOn(Vector2 a, Vector2 b, Vector2 centre, Vector2 u, float wallDistance)
        {
            float pa = Past(a, centre, u, wallDistance);
            float pb = Past(b, centre, u, wallDistance);
            return a + (b - a) * (pa / (pa - pb));
        }
    }
}
