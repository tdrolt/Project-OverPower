using UnityEngine;
using Overpower.Combat;

namespace Overpower.UI
{
    /// <summary>
    /// Pure precedence rule for a weapon/ability slot's border tint and block-reason text (invulnerability
    /// rework follow-up, 2026-09-18; HUD review fix, 2026-09-18). An ACTIVE module wins over every OTHER
    /// block reason - except Dead, which always wins over active.
    ///
    /// PlayerHud.UpdateAbilitySlots used to check block BEFORE active, so a module that is deliberately still
    /// IsActive while blocked read as grey instead of the amber glow it exists to show. That is not a rare
    /// edge case: Invulnerability's IsActive is true for its whole armed-and-waiting window, and every real
    /// cast spends the ultimate meter, so IsReady (meter full alone) is false right after casting - the slot
    /// read NotReady (grey) for the entire window and the amber glow was reachable only by bypassing the real
    /// cast path. Flamethrower and Invulnerability itself ALSO stay IsActive through BOTH a Stunned and a
    /// Silenced interrupt, by explicit design in each module's own Interrupt override (Flamethrower: "nothing
    /// but death calls it back"; Invulnerability: "must not tear the caster's own sphere down"). Dash and
    /// ZipGun stay IsActive through Silenced ONLY - their own Interrupt cancels on Stunned exactly like Died
    /// and Unequipped ("Silenced does not stop this - it is not a weapon and spends no heat"), so Stunned
    /// never actually reaches this rule still carrying active=true for those two. The survey behind this rule
    /// (assumptions-for-tudor.md, HUD readability) found no module where an ordinary block reason should win
    /// over active instead - every one of those overlaps is deliberate, not a bug this rule should
    /// special-case around.
    ///
    /// DEAD IS THE ONE EXCEPTION, added by review (2026-09-18): InvulnerabilityAbility.IsActive reads the
    /// status flags directly, and Interrupt(Died) used to leave them untouched (only Unequipped disarmed
    /// them) - they clear on respawn's ClearAll, not on death. Latent at today's values, because
    /// minimumTriggerDamage 0 means the first hit always triggers the shield before a killing blow could
    /// slip under it; raising that tooltipped field un-hides it, and a dead player's Space slot would glow
    /// amber for up to the rest of the armed/immune window. A corpse cannot be un-blocked by anything, so
    /// Dead is checked first, ahead of active, and the reason text always shows under it.
    /// </summary>
    public static class SlotTintRule
    {
        /// <summary>The slot border's colour this frame: Dead beats active beats every other blocked reason
        /// beats ready.</summary>
        public static Color BorderColor(bool active, CastBlock block, Color activeColor, Color blockedColor,
                                        Color readyColor) =>
            block == CastBlock.Dead ? blockedColor
            : active ? activeColor
            : block != CastBlock.None ? blockedColor
            : readyColor;

        /// <summary>Whether the block-reason line ("stunned", "recharging"...) should be shown. Hidden while
        /// active (except Dead, which always shows "dead"): "not ready" under a glowing, running ultimate is
        /// noise - the meter's own fill already shows it refilling, and the reason text would describe a
        /// state the slot is not actually conveying.</summary>
        public static bool ShowsBlockReason(bool active, CastBlock block) =>
            block == CastBlock.Dead || (!active && block != CastBlock.None);
    }
}
