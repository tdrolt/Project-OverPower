using System.Collections.Generic;

namespace Overpower.Match
{
    /// <summary>A living player a knocked-out player may watch.</summary>
    public readonly struct SpectateCandidate
    {
        public readonly int Actor, Team;
        public SpectateCandidate(int actor, int team) { Actor = actor; Team = team; }
    }

    /// <summary>Task 9g (Tudor D28): a knocked-out player's Spectate button. Pure rules; the view feeds in who is alive (present, on a
    /// team still in the match, not on the spectator's own team).</summary>
    public static class SpectateRules
    {
        public const int None = -1;

        /// <summary>"The team that knocked you out". The room does not record it, and the base's owner is no help after the first knockout
        /// (the phase-two cut sends that corner's zones neutral), so it is what this player's own client saw: the team that landed the
        /// killing blow on them (their last death). When that is unknown (a rejoiner's new process, a self-inflicted death) it falls back to
        /// the team now holding this player's own base, else None (any living player of a team still in). Never the player's own team.</summary>
        public static int KnockerTeam(int lastKillerTeam, int ownCapitalOwner, int myTeam)
        {
            if (lastKillerTeam >= 0 && lastKillerTeam != myTeam)
                return lastKillerTeam;
            return ownCapitalOwner >= 0 && ownCapitalOwner != myTeam ? ownCapitalOwner : None;
        }

        /// <summary>The actor to watch next. The living players of the preferred (knocker) team in actor order; only when it has no
        /// one alive, every living player in team order, then actor order. From no current target (or one no longer in the list) it
        /// is the first of that order; otherwise the one after the current, wrapping. None when nobody is alive.</summary>
        public static int NextTarget(IList<SpectateCandidate> living, int preferredTeam, int currentActor)
        {
            if (living == null || living.Count == 0)
                return None;

            bool preferredHasLiving = false;
            for (int i = 0; i < living.Count; i++)
                if (living[i].Team == preferredTeam) { preferredHasLiving = true; break; }

            var pool = new List<SpectateCandidate>(living.Count);
            for (int i = 0; i < living.Count; i++)
                if (!preferredHasLiving || living[i].Team == preferredTeam)
                    pool.Add(living[i]);

            pool.Sort((a, b) => a.Team != b.Team ? a.Team.CompareTo(b.Team) : a.Actor.CompareTo(b.Actor));

            for (int i = 0; i < pool.Count; i++)
                if (pool[i].Actor == currentActor)
                    return pool[(i + 1) % pool.Count].Actor;
            return pool[0].Actor;
        }

        /// <summary>When nobody is watchable at this moment the current target (and the camera on it) is kept - never a snap back to the
        /// own frozen body; the refresh picks someone as soon as a player is alive. Only the end of the match returns the camera.</summary>
        public static int PickOrKeep(int picked, int currentActor) => picked != None ? picked : currentActor;

        /// <summary>The Spectate / Next button shows on the lose panel while the match still runs; once it is over the normal result
        /// screen (whose button leads back to the name screen) takes over.</summary>
        public static bool ButtonVisible(bool losePanelShown, MatchPhase phase) =>
            losePanelShown && phase != MatchPhase.Over;

        // ---- the spectator seat (lobby Task 6): Q / E over everyone with a body, Space for the whole map

        /// <summary>The players a seat spectator can watch, by actor number (whatever the team). Never null.</summary>
        public static int[] SortedActors(IList<SpectateCandidate> watchable)
        {
            if (watchable == null || watchable.Count == 0)
                return new int[0];
            var actors = new int[watchable.Count];
            for (int i = 0; i < actors.Length; i++)
                actors[i] = watchable[i].Actor;
            System.Array.Sort(actors);
            return actors;
        }

        /// <summary>E: the watched player after the current one in actor order, wrapping. From no target, or one who has left, the
        /// first actor above the current one (the first of all when there is none above). None when nobody can be watched.</summary>
        public static int NextActor(IList<int> sortedActors, int currentActor)
        {
            if (sortedActors == null || sortedActors.Count == 0)
                return None;
            for (int i = 0; i < sortedActors.Count; i++)
                if (sortedActors[i] > currentActor)
                    return sortedActors[i];
            return sortedActors[0];
        }

        /// <summary>Q: the watched player before the current one in actor order, wrapping. From no target, or one who has left, the
        /// last actor below the current one (the last of all when there is none below). None when nobody can be watched.</summary>
        public static int PreviousActor(IList<int> sortedActors, int currentActor)
        {
            if (sortedActors == null || sortedActors.Count == 0)
                return None;
            if (currentActor == None)
                return sortedActors[sortedActors.Count - 1];
            for (int i = sortedActors.Count - 1; i >= 0; i--)
                if (sortedActors[i] < currentActor)
                    return sortedActors[i];
            return sortedActors[sortedActors.Count - 1];
        }

        /// <summary>The whole-map view (Space): where the camera aims on the ground and how far it stands from that point, so the arena
        /// outline fills heightFill of the free screen height (0.88 = 88%), centred in it, and never runs off the sides. The camera is the
        /// follow camera's: tilted down (tiltDegrees below the horizon), a vertical field of view and a window aspect, turned round the
        /// map by yawDegrees exactly as CameraTracking turns its offset (0 = looking up the map, +z). bottomReserve is the share of the
        /// screen's height kept free at the bottom (the spectator bar): the arena is framed in the area above it. The outline is projected
        /// through the camera exactly (a circle round the farthest point framed it too loosely: the outline is no circle). Zero for no outline.</summary>
        public static void WholeMapFraming(IReadOnlyList<UnityEngine.Vector2> outline, float verticalFovDegrees, float aspect, float tiltDegrees,
            float heightFill, float yawDegrees, float bottomReserve, out UnityEngine.Vector2 aimPoint, out float distance)
        {
            aimPoint = UnityEngine.Vector2.zero;
            distance = 0f;
            if (outline == null || outline.Count == 0)
                return;

            // A turned camera sees the arena as an unturned camera sees it rotated the other way: frame the turned-back outline, then turn the aim point forward.
            var turned = new UnityEngine.Vector2[outline.Count];
            for (int i = 0; i < turned.Length; i++)
                turned[i] = TurnAboutY(outline[i], -yawDegrees);

            FrameFromBehind(turned, verticalFovDegrees, aspect, tiltDegrees, heightFill, bottomReserve, out UnityEngine.Vector2 localAim, out distance);
            aimPoint = TurnAboutY(localAim, yawDegrees);
        }

        /// <summary>A ground point (x, z) turned round the vertical axis the way Quaternion.AngleAxis(degrees, up) turns a vector.</summary>
        private static UnityEngine.Vector2 TurnAboutY(UnityEngine.Vector2 p, float degrees)
        {
            float a = UnityEngine.Mathf.Deg2Rad * degrees;
            float cos = UnityEngine.Mathf.Cos(a), sin = UnityEngine.Mathf.Sin(a);
            return new UnityEngine.Vector2(p.x * cos + p.y * sin, -p.x * sin + p.y * cos);
        }

        private static void FrameFromBehind(IReadOnlyList<UnityEngine.Vector2> outline, float verticalFovDegrees, float aspect, float tiltDegrees,
            float heightFill, float bottomReserve, out UnityEngine.Vector2 aimPoint, out float distance)
        {
            UnityEngine.Vector2 min = outline[0], max = outline[0];
            for (int i = 1; i < outline.Count; i++)
            {
                min = UnityEngine.Vector2.Min(min, outline[i]);
                max = UnityEngine.Vector2.Max(max, outline[i]);
            }
            float aimX = (min.x + max.x) * 0.5f;
            float tanV = UnityEngine.Mathf.Tan(UnityEngine.Mathf.Deg2Rad * verticalFovDegrees * 0.5f);
            float tanH = tanV * UnityEngine.Mathf.Max(0.01f, aspect);
            float tilt = UnityEngine.Mathf.Deg2Rad * tiltDegrees;
            float sin = UnityEngine.Mathf.Sin(tilt), cos = UnityEngine.Mathf.Cos(tilt);
            float reserve = UnityEngine.Mathf.Clamp(bottomReserve, 0f, 0.6f);
            // The free area is the top (1 - reserve) of the screen; in half-screens (-1..1) its middle sits at y = reserve.
            float fill = UnityEngine.Mathf.Clamp(heightFill, 0.3f, 1f) * (1f - reserve);
            const float WidthFill = 0.96f; // the map never gets closer than this to the left and right edges

            float radius = 0f;
            for (int i = 0; i < outline.Count; i++)
                radius = UnityEngine.Mathf.Max(radius, UnityEngine.Vector2.Distance(new UnityEngine.Vector2(aimX, (min.y + max.y) * 0.5f), outline[i]));
            radius = UnityEngine.Mathf.Max(radius, (max.y - min.y) * 0.5f) + 0.01f;

            // For an aim point on the ground at z and a camera distance d, a ground point at (dx, dz) from it lands at
            // screen y = dz*sin / ((d + dz*cos) * tanV) and screen x = dx / ((d + dz*cos) * tanH), in -1..1 half-screens.
            void Extents(float aimZ, float d, out float top, out float bottom, out float halfWidth)
            {
                top = float.MinValue;
                bottom = float.MaxValue;
                halfWidth = 0f;
                for (int i = 0; i < outline.Count; i++)
                {
                    float dz = outline[i].y - aimZ;
                    float den = d + dz * cos;
                    float ny = dz * sin / (den * tanV);
                    top = UnityEngine.Mathf.Max(top, ny);
                    bottom = UnityEngine.Mathf.Min(bottom, ny);
                    halfWidth = UnityEngine.Mathf.Max(halfWidth, UnityEngine.Mathf.Abs(outline[i].x - aimX) / (den * tanH));
                }
            }

            // The smallest distance at which the height fills no more than asked and the width fits (both only shrink as it grows).
            float DistanceFor(float aimZ)
            {
                float lo = radius * 1.01f, hi = radius * 400f;
                for (int step = 0; step < 50; step++)
                {
                    float mid = (lo + hi) * 0.5f;
                    Extents(aimZ, mid, out float top, out float bottom, out float halfWidth);
                    bool fits = (top - bottom) * 0.5f <= fill && halfWidth <= WidthFill;
                    if (fits) hi = mid; else lo = mid;
                }
                return hi;
            }

            // Moving the aim point up the map moves the picture down: find where the arena sits in the middle of the free area.
            float low = min.y, high = max.y, aimZBest = (min.y + max.y) * 0.5f;
            for (int step = 0; step < 50; step++)
            {
                aimZBest = (low + high) * 0.5f;
                Extents(aimZBest, DistanceFor(aimZBest), out float top, out float bottom, out _);
                if ((top + bottom) * 0.5f > reserve) low = aimZBest; else high = aimZBest;
            }
            aimPoint = new UnityEngine.Vector2(aimX, aimZBest);
            distance = DistanceFor(aimZBest);
        }
    }
}
