using System.Collections.Generic;
using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>The Raybeam's look: its three hit lines drawn as one beam (one red sheath, one white-hot core a share of one line's width, one
    /// team outline), when it is drawn, the charging hexagon's edge, and when a scorch flashes where a line ends.</summary>
    public static class RaybeamLook
    {
        public static float CoreWidth(float beamWidth, float coreShare) => beamWidth * Mathf.Clamp01(coreShare);

        /// <summary>An enemy's merged beam is drawn while ANY of its lines crosses my team's sight, each line asked as a gun's shot.</summary>
        public static bool BeamShown(IReadOnlyList<Vector3> from, IReadOnlyList<Vector3> to, System.Func<Vector3, Vector3, bool> lineSeen)
        {
            int n = Mathf.Min(from.Count, to.Count);
            for (int i = 0; i < n; i++)
                if (lineSeen(from[i], to[i]))
                    return true;
            return false;
        }

        private const int MaxLines = 8; // the Raybeam has three hit lines; room to spare, not a tuning value
        private const float Same = 1e-4f;
        // The shortest narrowing where one line stops: a line cannot take two widths at one point. Not a look number.
        private const float NarrowingStep = 0.02f;
        private static readonly float[] startAlong = new float[MaxLines], endAlong = new float[MaxLines], startSide = new float[MaxLines], endSide = new float[MaxLines];
        private static readonly List<float> breaks = new List<float>(16);

        /// <summary>
        /// The hit lines drawn as ONE beam: stations along the aim (from <paramref name="origin"/>) where the beam's outline turns, each with the middle
        /// of the span and its full width, the span running from the leftmost line's left edge to the rightmost line's right edge (Beam Width each)
        /// among the lines that reach that far. Between stations both sides are straight, because each side follows one straight line until two
        /// lines cross or one ends, and those are the stations. Where a line ends early (on a wall) the beam narrows to the others at 45 degrees
        /// past its end: a line renderer widens each stretch square to that stretch, so a side jumping sideways would be drawn as a blob across
        /// the beam. A last stretch too short to narrow into (the centre line's few centimetres past the tilted outer lines) keeps the width.
        /// </summary>
        public static void MergedSpan(Vector3 origin, Vector3 aim, IReadOnlyList<Vector3> from, IReadOnlyList<Vector3> to, float beamWidth,
                                      List<Vector3> centres, List<float> widths)
        {
            centres.Clear();
            widths.Clear();
            Vector3 axis = Flat(aim);
            Vector3 side = Vector3.Cross(Vector3.up, axis);
            int n = Mathf.Min(MaxLines, Mathf.Min(from.Count, to.Count));
            breaks.Clear();
            for (int i = 0; i < n; i++)
            {
                startAlong[i] = Vector3.Dot(from[i] - origin, axis);
                endAlong[i] = Vector3.Dot(to[i] - origin, axis);
                startSide[i] = Vector3.Dot(from[i] - origin, side);
                endSide[i] = Vector3.Dot(to[i] - origin, side);
                if (!Drawn(i))
                    continue;
                breaks.Add(startAlong[i]);
                breaks.Add(endAlong[i]);
                for (int j = 0; j < i; j++)
                    if (Drawn(j) && TryCross(i, j, out float at))
                        breaks.Add(at);
            }
            breaks.Sort();

            float half = beamWidth * 0.5f;
            for (int k = 0; k + 1 < breaks.Count; k++)
            {
                float a = breaks[k], b = breaks[k + 1];
                if (b - a < Same)
                    continue;
                float inside = (a + b) * 0.5f; // which lines reach this stretch is the same all along it
                if (!SpanAt(a, inside, n, half, out float lo, out float hi))
                    continue;
                int last = centres.Count - 1;
                if (last >= 0)
                {
                    float lastAlong = Vector3.Dot(centres[last] - origin, axis);
                    float lastMid = Vector3.Dot(centres[last] - origin, side), lastHalf = widths[last] * 0.5f;
                    float move = SideMove(lo, hi, lastMid, lastHalf);
                    if (move > a - lastAlong + Same)
                    {
                        float at = lastAlong + Mathf.Max(NarrowingStep, move);
                        if (at <= b)
                        {
                            SpanAt(at, inside, n, half, out lo, out hi);
                            at = lastAlong + Mathf.Max(at - lastAlong, SideMove(lo, hi, lastMid, lastHalf)); // the lines left slant a little too
                        }
                        if (at > b)
                        {
                            // Too short to narrow into: the next stretch carries the narrowing, or at the very end the beam keeps its width.
                            if (b >= breaks[breaks.Count - 1] - Same)
                                AddStation(origin, axis, side, b, lastMid - lastHalf, lastMid + lastHalf, centres, widths);
                            continue;
                        }
                        a = at;
                        SpanAt(a, inside, n, half, out lo, out hi);
                    }
                }
                AddStation(origin, axis, side, a, lo, hi, centres, widths);
                SpanAt(b, inside, n, half, out lo, out hi);
                AddStation(origin, axis, side, b, lo, hi, centres, widths);
            }
        }

        /// <summary>The team-colour outline round the merged beam, as one closed line: down one side a gap outside the sheath, back up the other.</summary>
        public static void MergedOutline(IReadOnlyList<Vector3> centres, IReadOnlyList<float> widths, Vector3 aim, float edgeWidth, float gap,
                                         List<Vector3> outline)
        {
            outline.Clear();
            Vector3 side = Vector3.Cross(Vector3.up, Flat(aim));
            float beyond = edgeWidth * 0.5f + Mathf.Max(0f, gap);
            int n = Mathf.Min(centres.Count, widths.Count);
            for (int k = 0; k < n; k++)
                outline.Add(centres[k] + side * (widths[k] * 0.5f + beyond));
            for (int k = n - 1; k >= 0; k--)
                outline.Add(centres[k] - side * (widths[k] * 0.5f + beyond));
        }

        /// <summary>A line's width along its length: each station's width at its share of the way along, straight in between.</summary>
        public static AnimationCurve WidthCurve(IReadOnlyList<Vector3> centres, IReadOnlyList<float> widths)
        {
            int n = Mathf.Min(centres.Count, widths.Count);
            if (n == 0)
                return AnimationCurve.Constant(0f, 1f, 0f);
            float total = 0f;
            for (int k = 1; k < n; k++)
                total += Vector3.Distance(centres[k - 1], centres[k]);
            if (n == 1 || total <= 0f)
                return AnimationCurve.Constant(0f, 1f, widths[0]);

            var keys = new Keyframe[n];
            float along = 0f;
            for (int k = 0; k < n; k++)
            {
                if (k > 0)
                    along += Vector3.Distance(centres[k - 1], centres[k]);
                keys[k] = new Keyframe(along / total, widths[k]);
            }
            for (int k = 0; k < n; k++)
            {
                float before = k > 0 ? Slope(keys[k - 1], keys[k]) : Slope(keys[k], keys[k + 1]);
                float after = k + 1 < n ? Slope(keys[k], keys[k + 1]) : before;
                keys[k].inTangent = before;
                keys[k].outTangent = after;
            }
            return new AnimationCurve(keys);
        }

        /// <summary>How far out the corners of the charging hexagon's team edge sit: a gap outside its red line, measured square to its sides.</summary>
        public static float HexEdgeRadius(float radius, float bandWidth, float edgeWidth, float gap) =>
            radius + ((bandWidth + edgeWidth) * 0.5f + Mathf.Max(0f, gap)) / Mathf.Cos(30f * Mathf.Deg2Rad);

        private static Vector3 Flat(Vector3 aim)
        {
            var flat = new Vector3(aim.x, 0f, aim.z);
            return flat.sqrMagnitude > 1e-8f ? flat.normalized : Vector3.forward;
        }

        private static bool Drawn(int i) => endAlong[i] - startAlong[i] > Same;

        private static float SideAt(int i, float along) =>
            startSide[i] + (endSide[i] - startSide[i]) * (along - startAlong[i]) / (endAlong[i] - startAlong[i]);

        private static bool TryCross(int i, int j, out float at)
        {
            float slopeI = (endSide[i] - startSide[i]) / (endAlong[i] - startAlong[i]);
            float slopeJ = (endSide[j] - startSide[j]) / (endAlong[j] - startAlong[j]);
            at = 0f;
            if (Mathf.Abs(slopeI - slopeJ) < 1e-6f)
                return false;
            at = (startSide[j] - slopeJ * startAlong[j] - (startSide[i] - slopeI * startAlong[i])) / (slopeI - slopeJ);
            return at > Mathf.Max(startAlong[i], startAlong[j]) && at < Mathf.Min(endAlong[i], endAlong[j]);
        }

        private static bool SpanAt(float along, float inside, int n, float half, out float lo, out float hi)
        {
            lo = float.MaxValue;
            hi = float.MinValue;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (!Drawn(i) || inside < startAlong[i] || inside > endAlong[i])
                    continue;
                float at = SideAt(i, along);
                lo = Mathf.Min(lo, at - half);
                hi = Mathf.Max(hi, at + half);
                any = true;
            }
            return any;
        }

        private static void AddStation(Vector3 origin, Vector3 axis, Vector3 side, float along, float lo, float hi, List<Vector3> centres, List<float> widths)
        {
            Vector3 centre = origin + axis * along + side * ((lo + hi) * 0.5f);
            int last = centres.Count - 1;
            if (last >= 0 && (centres[last] - centre).sqrMagnitude < Same * Same && Mathf.Abs(widths[last] - (hi - lo)) < Same)
                return;
            centres.Add(centre);
            widths.Add(hi - lo);
        }

        private static float SideMove(float lo, float hi, float mid, float halfWidth) =>
            Mathf.Max(Mathf.Abs(lo - (mid - halfWidth)), Mathf.Abs(hi - (mid + halfWidth)));

        private static float Slope(Keyframe a, Keyframe b) => b.time - a.time > 1e-6f ? (b.value - a.value) / (b.time - a.time) : 0f;

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
