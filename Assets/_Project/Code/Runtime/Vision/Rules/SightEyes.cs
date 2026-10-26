using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Vision
{
    /// <summary>One player as the sight rules need them: plain values, no PhotonView or Transform, so the eye list is
    /// provable in an edit-mode test.</summary>
    public readonly struct SightCandidate
    {
        public readonly int Team;
        public readonly bool Alive, IsLocal;
        /// <summary>True while this player holds the Scope: their eye uses the scoped shape.</summary>
        public readonly bool Scoped;
        public readonly Vector2 Position, Facing;
        /// <summary>World height of this player's eyes: their own feet plus the Eye Height.</summary>
        public readonly float EyeY;

        public SightCandidate(int team, bool alive, bool isLocal, Vector2 position, Vector2 facing, float eyeY = 0f, bool scoped = false) // eyeY: the game always passes it; the default is for tests that never test walls
        {
            EyeY = eyeY;
            Scoped = scoped;
            Team = team;
            Alive = alive;
            IsLocal = isLocal;
            Position = position;
            Facing = facing;
        }
    }

    /// <summary>Turns a plain list of players into the eyes my team sees through (the filter is VisionRules.IsEye).</summary>
    public static class SightEyes
    {
        /// <summary>Spectating after a knockout comes first, then being dead, else alive.</summary>
        public static ViewerMode ModeFor(bool spectating, bool localAlive)
        {
            if (spectating)
                return ViewerMode.Spectating;
            return localAlive ? ViewerMode.Alive : ViewerMode.Dead;
        }

        /// <summary>The team whose members are always shown: the watched team while spectating, else my own.</summary>
        public static int FriendlyTeam(ViewerMode mode, int localTeam, int watchedTeam) =>
            mode == ViewerMode.Spectating ? watchedTeam : localTeam;

        /// <summary>Clears <paramref name="into"/> and fills it with one eye per candidate that is an eye right now, all
        /// with the same shape.</summary>
        public static void Build(IReadOnlyList<SightCandidate> candidates, int localTeam, ViewerMode mode, int watchedTeam,
            SightShape shape, List<Eye> into) => Build(candidates, localTeam, mode, watchedTeam, shape, shape, into);

        /// <summary>Same, but an eye whose player holds the Scope uses <paramref name="scopedShape"/>.</summary>
        public static void Build(IReadOnlyList<SightCandidate> candidates, int localTeam, ViewerMode mode, int watchedTeam,
            SightShape shape, SightShape scopedShape, List<Eye> into)
        {
            into.Clear();
            for (int i = 0; i < candidates.Count; i++)
            {
                SightCandidate c = candidates[i];
                if (VisionRules.IsEye(localTeam, mode, watchedTeam, c.Team, c.Alive, c.IsLocal))
                    into.Add(new Eye(c.Position, c.Facing, c.Scoped ? scopedShape : shape, c.EyeY));
            }
        }
    }
}
