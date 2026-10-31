using Overpower.Combat;

namespace Overpower.Abilities
{
    public enum RaybeamChannelStep { Channelling, Finished, Broken }

    /// <summary>The Raybeam's channel after its cast time: a beam from the gun along the aim for Channel Seconds, damaging the first enemy it
    /// touches and adding a vulnerability stack as it touches that enemy, then one more each second it stays on them. A stun, a knockback or death breaks it early.</summary>
    public static class RaybeamChannel
    {
        /// <summary>The owner's frame while channelling.</summary>
        public static RaybeamChannelStep Step(float elapsed, float channelSeconds, CastBlock actorBlock, bool knockedBack)
        {
            if (Breaks(actorBlock, knockedBack))
                return RaybeamChannelStep.Broken;
            return elapsed >= channelSeconds ? RaybeamChannelStep.Finished : RaybeamChannelStep.Channelling;
        }

        /// <summary>A stun, death or a knockback ends the beam; an overheat does not (the Raybeam is not the gun).</summary>
        public static bool Breaks(CastBlock actorBlock, bool knockedBack) =>
            knockedBack || actorBlock == CastBlock.Dead || actorBlock == CastBlock.Stunned;

        /// <summary>The damage one frame of beam deals before vulnerability (the damage funnel multiplies by it).</summary>
        public static float TickDamage(float damagePerSecond, float deltaSeconds) => damagePerSecond * deltaSeconds;

        /// <summary>How many vulnerability stacks a target earns going from <paramref name="secondsBefore"/> to <paramref name="secondsAfter"/>
        /// of time in this beam: one as the beam first touches it, then one more each further Seconds Per Stack (stacks at 0, 1, 2 s), so a
        /// whole channel reaches the third stack instead of ending a frame short of it.</summary>
        public static int StacksEarned(float secondsBefore, float secondsAfter, float secondsPerStack) =>
            secondsPerStack <= 0f ? 0 : (int)System.Math.Ceiling(secondsAfter / secondsPerStack) - (int)System.Math.Ceiling(secondsBefore / secondsPerStack);

        /// <summary>How many enemies the beam damages: it stops at the first (BeamResolver passes allies and the caster).</summary>
        public static int TargetsHit => 1;

        /// <summary>While the beam is channelled the caster cannot shoot or use another ability.</summary>
        public static bool LocksCaster(bool channelling) => channelling;
    }
}
