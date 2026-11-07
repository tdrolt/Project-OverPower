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

        /// <summary>
        /// The fraction of the range LEFT that a shot keeps after its Nth bounce (1 = the first), read from the weapon's list: entry 0 is the first
        /// bounce. No shrink (1) for a bounce number below 1, past the end of the list, or when there is no list.
        /// </summary>
        public static float RangeKept(float[] keptAfterBounce, int bounceNumber)
        {
            if (keptAfterBounce == null || bounceNumber < 1 || bounceNumber > keptAfterBounce.Length)
                return 1f;

            return keptAfterBounce[bounceNumber - 1];
        }
    }
}
