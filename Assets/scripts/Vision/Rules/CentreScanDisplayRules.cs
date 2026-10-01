using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Vision
{
    /// <summary>One red dot the centre scan left: where the enemy was when the front passed over them, and when. Frozen: it
    /// never follows the enemy.</summary>
    public readonly struct ScanDot
    {
        public readonly Vector3 Position;
        public readonly float BornTime;
        public readonly int ServerMs;
        public readonly int Actor;

        public ScanDot(Vector3 position, float bornTime, int serverMs, int actor)
        {
            Position = position; BornTime = bornTime; ServerMs = serverMs; Actor = actor;
        }
    }

    /// <summary>The dots of the holding team's minimap (Tudor 2026-10-01: wave + zones refresh + enemy dots, no text). Every
    /// pass of the front over an enemy adds a new dot, so crossing the wave twice makes two dots.</summary>
    public sealed class ScanDotPool
    {
        /// <summary>Not a tuned value: a safety limit so a long session cannot grow the list without end.</summary>
        public const int MaxDots = 64;

        private readonly List<ScanDot> dots = new List<ScanDot>();

        public IReadOnlyList<ScanDot> Dots => dots;

        /// <summary>How many dots were ever added (a recorder reads the difference between two frames).</summary>
        public int TotalAdded { get; private set; }

        public void Add(Vector3 position, float now, int serverMs, int actor)
        {
            if (dots.Count >= MaxDots)
                dots.RemoveAt(0);
            dots.Add(new ScanDot(position, now, serverMs, actor));
            TotalAdded++;
        }

        /// <summary>Removes the dots that have lived their full time.</summary>
        public void Prune(float now, float lifeSeconds)
        {
            dots.RemoveAll(d => now - d.BornTime >= lifeSeconds);
        }

        public void Clear() => dots.Clear();
    }

    /// <summary>The plain pieces behind what the centre scan draws.</summary>
    public static class CentreScanDisplayRules
    {
        /// <summary>1 for a fresh dot, falling to 0 over its last fadeSeconds (VisionConfig Scan Dot Fade Seconds; the whole
        /// life if that is shorter) as its age reaches the life; 0 beyond it.</summary>
        public static float DotAlpha(float ageSeconds, float lifeSeconds, float fadeSeconds)
        {
            if (lifeSeconds <= 0f || ageSeconds >= lifeSeconds)
                return 0f;
            float fade = Mathf.Min(fadeSeconds, lifeSeconds);
            if (fade <= 0f)
                return 1f;
            float fadeStart = lifeSeconds - fade;
            return ageSeconds <= fadeStart ? 1f : Mathf.Clamp01((lifeSeconds - ageSeconds) / fade);
        }

        /// <summary>Who gets the scan on their maps and the dots: the team that holds the centre (while spectating, the watched
        /// team, which is what the friendly team is then). Nobody before a team is known, nobody while the centre has no holder.</summary>
        public static bool SeesScan(int friendlyTeam, int holderTeam)
        {
            return friendlyTeam >= 0 && friendlyTeam == holderTeam;
        }

        /// <summary>The wave's radius in map units: the map draws worldSizeMetres across mapSize units.</summary>
        public static float MinimapRadius(float worldRadius, float worldSizeMetres, float mapSize)
        {
            return worldSizeMetres > 0f ? worldRadius * mapSize / worldSizeMetres : 0f;
        }

        /// <summary>How far the wave must go to have crossed the whole arena: the farthest outline point from the centre.</summary>
        public static float MaxRadius(IReadOnlyList<Vector2> outline, Vector2 centre)
        {
            float max = 0f;
            if (outline == null)
                return max;
            for (int i = 0; i < outline.Count; i++)
                max = Mathf.Max(max, Vector2.Distance(centre, outline[i]));
            return max;
        }
    }
}
