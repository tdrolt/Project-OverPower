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

        /// <summary>Space: the middle of the arena outline (its bounding box) and the distance from there to its farthest point.
        /// Zero for no outline.</summary>
        public static void WholeMapFrame(IReadOnlyList<UnityEngine.Vector2> outline, out UnityEngine.Vector2 centre, out float radius)
        {
            centre = UnityEngine.Vector2.zero;
            radius = 0f;
            if (outline == null || outline.Count == 0)
                return;
            UnityEngine.Vector2 min = outline[0], max = outline[0];
            for (int i = 1; i < outline.Count; i++)
            {
                min = UnityEngine.Vector2.Min(min, outline[i]);
                max = UnityEngine.Vector2.Max(max, outline[i]);
            }
            centre = (min + max) * 0.5f;
            for (int i = 0; i < outline.Count; i++)
                radius = UnityEngine.Mathf.Max(radius, UnityEngine.Vector2.Distance(centre, outline[i]));
        }

        /// <summary>How far the camera must stand from the middle of the map to keep a ground circle of this radius on screen.
        /// The camera looks down at the target (tilt = degrees below the horizon): the circle's near and far edges are foreshortened
        /// so the height is the tight one (the near edge sits closer and higher in the picture), and the width is the other limit
        /// for a narrow window. margin &gt; 1 leaves a border. Zero for a zero radius.</summary>
        public static float WholeMapDistance(float radius, float verticalFovDegrees, float aspect, float tiltDegrees, float margin)
        {
            if (radius <= 0f)
                return 0f;
            float halfVertical = UnityEngine.Mathf.Deg2Rad * verticalFovDegrees * 0.5f;
            float halfHorizontal = UnityEngine.Mathf.Atan(UnityEngine.Mathf.Tan(halfVertical) * UnityEngine.Mathf.Max(0.01f, aspect));
            float tilt = UnityEngine.Mathf.Deg2Rad * tiltDegrees;
            float forHeight = radius * (UnityEngine.Mathf.Cos(tilt) + UnityEngine.Mathf.Sin(tilt) / UnityEngine.Mathf.Tan(halfVertical));
            float forWidth = radius / UnityEngine.Mathf.Tan(halfHorizontal);
            return UnityEngine.Mathf.Max(forHeight, forWidth) * margin;
        }
    }
}
