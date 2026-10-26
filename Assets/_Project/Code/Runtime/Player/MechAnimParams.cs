using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>The mech Animator's three bools for this frame, worked out from scratch every frame so nothing (a death, a stun, a shop
    /// click, chat) can leave one stuck, as press/release edges can. A charge weapon never holds the shoot pose while it
    /// charges; every shot that leaves the gun holds it for at least the minimum, so a quick tap still reaches the other screens.</summary>
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

        public static MechAnimParams For(bool primaryHeld, Vector2 moveAxis, bool mayFire, bool canCharge,
                                         float secondsSinceLastShot, float shootPoseSeconds)
        {
            bool run = moveAxis.sqrMagnitude > RestingAxisSqr;
            bool heldShot = primaryHeld && !canCharge;
            bool shoot = mayFire && (heldShot || secondsSinceLastShot < shootPoseSeconds);
            return new MechAnimParams(run, !run, shoot);
        }

        // Not a tuning number: a stick resting a hair off centre must not play the run.
        private const float RestingAxisSqr = 0.01f;
    }
}
