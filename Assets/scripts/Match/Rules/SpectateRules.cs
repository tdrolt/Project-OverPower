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

        /// <summary>The Spectate / Next button shows on the lose panel while the match still runs; once it is over the normal result
        /// screen (whose button leads back to the name screen) takes over.</summary>
        public static bool ButtonVisible(bool losePanelShown, MatchPhase phase) =>
            losePanelShown && phase != MatchPhase.Over;
    }
}
