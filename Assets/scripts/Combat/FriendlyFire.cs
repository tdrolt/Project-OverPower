namespace Overpower.Combat
{
    /// <summary>
    /// The one no-friendly-fire rule every hit-detecting shot shares: you cannot hit yourself and
    /// you cannot hit a teammate. Used to live as three near-identical copies (ProjectileMotor's
    /// sweep, ExplodeOnImpact's splash, BeamResolver's beam) - pulled out after the code review that
    /// followed the laser path (commits 3af6dde, 3dd26c2).
    ///
    /// Plain ints in, bool out, on purpose: BeamResolver has no UnityEngine dependency beyond maths
    /// types so its edit-mode tests run without a scene, and it must not gain one just to share this
    /// rule. Callers that hold Photon references (ProjectileMotor, ExplodeOnImpact) pull the actor
    /// and team numbers out first and pass them in here, the same way Teams.AreSameTeam is resolved
    /// from actor numbers rather than Player references elsewhere in the project.
    /// </summary>
    public static class FriendlyFire
    {
        /// <summary>True when the target is the shooter or shares the shooter's team - a shot
        /// should carry on through it rather than stopping or dealing damage. Unknown shooter teams
        /// (a negative shooterTeamId) fail OPEN and everyone stays a valid target, matching
        /// Teams.AreSameTeam: an unknown team silently making someone invulnerable is far worse to
        /// debug than one stray friendly-fire hit.
        ///
        /// This does not consider whether the target is alive - callers that need to pass through
        /// corpses too (BeamResolver does, because a dead practice dummy keeps its collider while it
        /// waits to reset) check that separately before or after calling this.</summary>
        public static bool IsSelfOrTeammate(int shooterActorNumber, int targetActorNumber,
                                             int shooterTeamId, int targetTeamId)
        {
            if (targetActorNumber == shooterActorNumber)
                return true;

            return shooterTeamId >= 0 && targetTeamId == shooterTeamId;
        }
    }
}
