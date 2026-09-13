using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// A flat speed buff that pays for itself out of the same heat pool the primary weapon spends -
    /// Tudor called this the most interesting interaction in the mobility list: sprinting costs you
    /// the ability to shoot, and it turns itself off the moment overheat silences you, without any
    /// timer of its own. No cooldown gate at all (MaxCharges 0 on this module, per the framework's
    /// convention) - self-limiting through heat instead.
    ///
    /// Two activation modes, chosen with the Toggle checkbox below rather than two separate scripts:
    /// hold behaves like every shooter's sprint key, toggle lets a long straight run continue after
    /// letting go. Both read the SAME active/inactive logic in OwnerTick - only which input decides
    /// "should this be on right now" differs.
    ///
    /// Never writes PlayerMotor's speed directly (see AddSpeedMultiplier's own class comment for the
    /// old respawn bug that direct writes caused) - the keyed multiplier is added and removed here,
    /// and only here, so a stale multiplier can never survive past the moment this ability stops.
    /// </summary>
    public sealed class SprintAbility : AbilityModule
    {
        [Header("Movement")]
        [SerializeField, Tooltip("Multiplier on top of the player's base walking speed while " +
                 "sprinting. 1.5 means 50% faster. Composes with slows and every other speed " +
                 "effect through PlayerMotor's keyed multiplier stack.")]
        private float speedMultiplier = 1.5f;

        [Header("Cost")]
        [SerializeField, Tooltip("Overheat added per second of sprinting WHILE ACTUALLY MOVING - " +
                 "standing still with the key held (or toggled on) costs nothing, so parking on " +
                 "Shift is never a trap. This is the SAME heat the primary weapon and every other " +
                 "ability share out of one 100-point bar, so sprinting competes with shooting for it.")]
        private float overheatPerSecond = 18f;

        [Header("Activation")]
        [SerializeField, Tooltip("Off (default): sprint runs only while the key is held down, and " +
                 "stops the instant it is released - the usual hold-to-sprint feel. On: pressing the " +
                 "key flips sprint on or off, so it keeps running after you let go - better for a " +
                 "long straight run. Either way, overheat silence always turns it off, and a toggled " +
                 "sprint does NOT resume by itself once the silence clears - Tudor's clarification.")]
        private bool toggleMode = false;

        // Owner only: the toggle's own on/off state, independent of whether the key happens to be
        // held right now. Meaningless (and never touched) in hold mode.
        private bool toggledOn;

        // This frame's actual sprinting state, so OwnerTick and Interrupt agree on whether there is
        // a multiplier to remove and a stop to announce. Stays false forever on a remote copy, since
        // OwnerTick never runs there - see AbilityRunner.Update's IsMine guard.
        private bool active;

        public override bool IsActive => active;

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            if (toggleMode)
                toggledOn = !toggledOn;

            // Hold mode needs no state here at all: OwnerTick's own `held` parameter already tracks
            // the key continuously (AbilityRunner.IsHeld), so "starting" a hold sprint is just this
            // cast going out for other clients to see, not a flag this class has to remember.
            payload = default;
            return true;
        }

        public override void ExecuteCast(in CastEvent cast)
        {
            // Reserved for a future cosmetic hook (a dust trail on phase 0, dust-off on phase 1).
            // Sprint needs no functional network message at all - the caster's own faster movement
            // already reaches every other client through PlayerNetSync's ordinary position replication.
        }

        public override void OwnerTick(float deltaTime, bool held, bool canAct)
        {
            bool wantsActive = (toggleMode ? toggledOn : held) && canAct;

            if (wantsActive)
            {
                Owner.Motor?.AddSpeedMultiplier(this, speedMultiplier);

                // Standing still while holding (or having toggled on) Shift must cost nothing - the
                // ability rewards covering ground, not rewarding parking on the key.
                if (Owner.Motor != null && Owner.Motor.IsMoving)
                    Owner.Overheat?.Add(overheatPerSecond * deltaTime);
            }
            else if (active)
            {
                StopSprinting();
            }

            active = wantsActive;
        }

        public override void Interrupt(InterruptReason reason)
        {
            if (reason == InterruptReason.Silenced)
            {
                // A toggled sprint must not resume by surprise the instant heat decays back down -
                // Tudor's clarification. Hold mode needs nothing here: `held` already reads false
                // the moment silence makes canAct false, by the same OwnerTick check above.
                toggledOn = false;
                LogSilenced();
            }

            if (!active)
                return;

            StopSprinting();
        }

        /// <summary>Belt and braces: OwnerTick already drops the multiplier and toggledOn is reset
        /// above on Silenced, but a stray "on" from the life just ended must never carry into the
        /// next one - the exact shape of bug 2.10's respawn speed bug, just for toggle state instead
        /// of a raw speed value.</summary>
        public override void OnRespawned()
        {
            toggledOn = false;
        }

        private void StopSprinting()
        {
            Owner.Motor?.RemoveSpeedMultiplier(this);
            active = false;
            SendPhase(1, default); // No-ops on every machine but the owner's - see SendPhase's own comment.
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogSilenced()
        {
            Debug.Log("[SPRINT] silenced - stopping.");
        }
    }
}
