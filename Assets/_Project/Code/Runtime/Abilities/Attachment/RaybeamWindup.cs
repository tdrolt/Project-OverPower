using UnityEngine;
using Overpower.Combat;

namespace Overpower.Abilities
{
    public enum RaybeamWindupStep { Waiting, Fire, Cancel }

    /// <summary>The Raybeam's wind-up: from the press the beams wait Wind-up Seconds, then fire; a stun or death first cancels them (an overheat
    /// does not: the Raybeam is an ultimate, not the gun), and only firing spends the ultimate meter. Every client meanwhile draws a hexagon
    /// at the caster's gun shrinking over the wind-up.</summary>
    public static class RaybeamWindup
    {
        /// <summary>How long past the wind-up a client keeps the hexagon while no fire or cancel has arrived. Not a look number: a safety net for an
        /// end that never comes (the caster left the room), well above any real lag.</summary>
        public const float MissingEndSeconds = 1f;

        /// <summary>The owner's frame: cancelled the moment a stun or death lands, else fired once the wind-up has run.</summary>
        public static RaybeamWindupStep Step(float elapsed, float windupSeconds, CastBlock actorBlock)
        {
            if (BlockCancels(actorBlock))
                return RaybeamWindupStep.Cancel;
            return elapsed >= windupSeconds ? RaybeamWindupStep.Fire : RaybeamWindupStep.Waiting;
        }

        public static bool BlockCancels(CastBlock actorBlock) => actorBlock == CastBlock.Dead || actorBlock == CastBlock.Stunned;

        public static bool InterruptCancels(InterruptReason reason) => reason != InterruptReason.Silenced;

        /// <summary>The meter is spent only by beams that fire: a cancelled wind-up leaves it full.</summary>
        public static bool SpendsUltimate(RaybeamWindupStep end) => end == RaybeamWindupStep.Fire;

        public static bool IsReady(bool meterFull, bool windingUp) => meterFull && !windingUp;

        /// <summary>From the start size at the press down to the end size as the beams fire, then held there until the fire message arrives.</summary>
        public static float ChargeRadius(float elapsed, float windupSeconds, float startRadius, float endRadius)
        {
            float t = windupSeconds > 0f ? Mathf.Clamp01(elapsed / windupSeconds) : 1f;
            return Mathf.Lerp(startRadius, endRadius, t);
        }

        public static bool ChargeShown(float elapsed, float windupSeconds) => elapsed < Mathf.Max(0f, windupSeconds) + MissingEndSeconds;
    }
}
