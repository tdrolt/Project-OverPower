namespace Overpower.Combat
{
    /// <summary>
    /// What a ricocheting shot bounces off besides level geometry, provable without a scene. BounceOffWalls asks it for every victim it meets
    /// that is not a player.
    /// </summary>
    public static class BounceRules
    {
        /// <summary>
        /// True when a bouncing shot ricochets off a Cover wall instead of stopping on it: only the player who placed the wall, and only off their own.
        /// Both numbers are ones every client already agrees on (the shooter the shot carries, the placer stored on the wall), so every machine
        /// simulating the shot reaches the same answer. A real actor number is positive; an unknown one (-1) on either side is nobody's own, so
        /// the shot stops on the wall as it does for everyone else.
        /// </summary>
        public static bool BouncesOffCover(int shooterActorNumber, int wallOwnerActorNumber)
        {
            return shooterActorNumber > 0 && shooterActorNumber == wallOwnerActorNumber;
        }
    }
}
