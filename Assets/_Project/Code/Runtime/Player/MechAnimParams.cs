using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>The mech Animator's three bools for this frame, worked out from scratch every frame so nothing (a death, a stun, a shop
    /// click, chat) can leave one stuck, as press/release edges can. Only a shot that leaves the gun shows the shoot pose, for as long
    /// as that shot asks: a held trigger on a gun that cannot fire yet (cooling down, overheated, charging) shows nothing. A stunned or
    /// dead mech never shows it.</summary>
    public readonly struct MechAnimParams
    {
        public readonly bool Run;
        public readonly bool Idle;
        public readonly bool Shoot;

        public MechAnimParams(bool run, bool idle, bool shoot)
        {
            Run = run;
            Idle = idle;
            Shoot = shoot;
        }

        /// <summary>Whether the mech may show a shot at all. Overheat silence is left out on purpose: the shot that fills the meter keeps
        /// its whole motion, and a silenced gun fires no further shot to show.</summary>
        public static bool MayShowShot(bool alive, bool gunOn, bool stunned) => alive && gunOn && !stunned;

        public static MechAnimParams For(Vector2 moveAxis, bool mayShowShot, float secondsSinceLastShot, float shootPoseSeconds)
        {
            bool run = moveAxis.sqrMagnitude > RestingAxisSqr;
            bool shoot = mayShowShot && secondsSinceLastShot < shootPoseSeconds;
            return new MechAnimParams(run, !run, shoot);
        }

        // Not a tuning number: a stick resting a hair off centre must not play the run.
        private const float RestingAxisSqr = 0.01f;
    }
}
