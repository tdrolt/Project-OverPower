namespace Overpower.Combat
{
    /// <summary>
    /// The one no-friendly-fire rule every hit-detecting shot shares (projectile sweep, explosion
    /// splash, beam): you cannot hit yourself or a teammate. Plain ints in, bool out so BeamResolver
    /// keeps no UnityEngine/Photon dependency and its edit-mode tests need no scene; callers holding
    /// Photon references resolve actor and team numbers first, as Teams.AreSameTeam does.
    /// </summary>
    public static class FriendlyFire
    {
        /// <summary>True when the target is the shooter or shares the shooter's team, so the shot
        /// should carry on through it. An unknown shooter team (negative shooterTeamId) fails OPEN
        /// and everyone stays a valid target, matching Teams.AreSameTeam: an unknown team silently
        /// making someone invulnerable is far worse to debug than one stray friendly-fire hit.
        /// Does not consider whether the target is alive; callers that pass through corpses too
        /// (BeamResolver: a dead practice dummy keeps its collider while it waits to reset) check
        /// that separately.</summary>
        public static bool IsSelfOrTeammate(int shooterActorNumber, int targetActorNumber,
                                             int shooterTeamId, int targetTeamId)
        {
            if (targetActorNumber == shooterActorNumber)
                return true;

            return shooterTeamId >= 0 && targetTeamId == shooterTeamId;
        }
    }
}
