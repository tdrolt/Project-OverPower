namespace Overpower.Combat
{
    /// <summary>
    /// Why a cast (or a shot) was refused, in priority order. The order matters for the HUD: a
    /// stunned player's ability icon must read "stunned", not "recharging", even if it also
    /// happens to be on cooldown, so a designer looking at the icon learns the reason that
    /// actually matters right now rather than whichever check happened to run first.
    /// </summary>
    public enum CastBlock
    {
        None,
        Dead,
        Stunned,
        Silenced,
        Recharging,
        NotReady
    }

    /// <summary>
    /// One rule, asked twice. WeaponFiring.TryFire and every AbilityModule both refuse to act for
    /// the same three actor-wide reasons (dead, stunned, silenced), and abilities add two more of
    /// their own (out of charges, or some extra per-ability gate). Putting the rule here instead
    /// of copy-pasting an if-chain into each caller is what keeps the weapon and every future
    /// ability from drifting apart on what "you can't act right now" means.
    /// </summary>
    public static class CastGate
    {
        /// <summary>
        /// The actor-wide part of the gate - true for the weapon and for all three ability slots
        /// alike. Checked in this order because the reason with the biggest consequence should be
        /// the one reported: a dead player being "silenced" would read as a bug, not a corpse.
        /// </summary>
        public static CastBlock ForActor(bool alive, bool stunned, bool silenced)
        {
            if (!alive)
                return CastBlock.Dead;
            if (stunned)
                return CastBlock.Stunned;
            if (silenced)
                return CastBlock.Silenced;
            return CastBlock.None;
        }

        /// <summary>
        /// Adds an ability's own two gates on top of whatever ForActor already decided. hasChargeGate
        /// is false for a module with no charges at all - Sprint spends heat, not a ChargePool - and
        /// such a module must never report Recharging, which would tell the HUD to grey out an icon
        /// that was never on cooldown to begin with.
        /// </summary>
        public static CastBlock ForAbility(CastBlock actorBlock, bool hasChargeGate, bool hasCharge, bool isReady)
        {
            if (actorBlock != CastBlock.None)
                return actorBlock;
            if (hasChargeGate && !hasCharge)
                return CastBlock.Recharging;
            if (!isReady)
                return CastBlock.NotReady;
            return CastBlock.None;
        }

        /// <summary>
        /// Remembers one button press for a short window, so a dash pressed a frame before its
        /// charge returns still fires instead of being silently swallowed by the exact-frame
        /// timing of an Update loop. Plain C# and stateless apart from the one press it is
        /// tracking, so AbilityRunner (Task 1.0b) can own one per slot without any Unity types
        /// getting involved.
        /// </summary>
        public sealed class PressBuffer
        {
            // Negative infinity rather than 0 or -1: a real press can legitimately happen at
            // Time.time == 0 (the very first frame), and either sentinel would make that press
            // indistinguishable from "never pressed".
            private float pressTime = float.NegativeInfinity;

            // Sourcing this from a request to Press() with no matching TryConsume between them is not
            // possible: a re-press always clears this, and TryConsume can only ever set it once,
            // consistent with the "true once" behaviour asked for below.
            private bool consumed;

            /// <summary>Records a press at the given time and makes it available to consume again,
            /// even if the previous press was never consumed.</summary>
            public void Press(float now)
            {
                pressTime = now;
                consumed = false;
            }

            /// <summary>True while the most recent press is still within window seconds of now and
            /// has not already been consumed. A window of exactly 0 is pending only on the exact
            /// frame the press happened - useful mainly for the "never buffer anything" test case.</summary>
            public bool IsPending(float now, float window)
            {
                return !consumed && now - pressTime <= window;
            }

            /// <summary>Consumes the pending press, if there is one, and returns whether it did.
            /// Returns true at most once per Press call - the second attempt to spend the same
            /// press within its window correctly finds nothing left to spend.</summary>
            public bool TryConsume(float now, float window)
            {
                if (!IsPending(now, window))
                    return false;

                consumed = true;
                return true;
            }
        }
    }
}
