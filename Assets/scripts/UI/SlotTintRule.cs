using UnityEngine;
using Overpower.Combat;

namespace Overpower.UI
{
    /// <summary>
    /// Pure precedence rule for a weapon/ability slot's border tint and block-reason text (invulnerability
    /// rework follow-up, 2026-09-18). An ACTIVE module always wins, whatever its block state.
    ///
    /// PlayerHud.UpdateAbilitySlots used to check block BEFORE active, so a module that is deliberately still
    /// IsActive while blocked read as grey instead of the amber glow it exists to show. That is not a rare
    /// edge case: Invulnerability's IsActive is true for its whole armed-and-waiting window, and every real
    /// cast spends the ultimate meter, so IsReady (meter full alone) is false right after casting - the slot
    /// read NotReady (grey) for the entire window and the amber glow was reachable only by bypassing the real
    /// cast path. Flamethrower, Dash, ZipGun and (once armed) Invulnerability itself all ALSO stay IsActive
    /// through a Stunned or Silenced interrupt, by explicit design in each module's own Interrupt override -
    /// see their class comments (Flamethrower: "nothing but death calls it back"; Dash/ZipGun: "Silenced does
    /// not stop this"; Invulnerability: "must not tear the caster's own sphere down"). The survey behind this
    /// rule (assumptions-for-tudor.md, HUD readability) found no module where block should win instead - every
    /// active/blocked overlap in this codebase is deliberate, not a bug this rule should special-case around.
    /// </summary>
    public static class SlotTintRule
    {
        /// <summary>The slot border's colour this frame: active beats blocked beats ready.</summary>
        public static Color BorderColor(bool active, CastBlock block, Color activeColor, Color blockedColor,
                                        Color readyColor) =>
            active ? activeColor : block != CastBlock.None ? blockedColor : readyColor;

        /// <summary>Whether the block-reason line ("stunned", "recharging"...) should be shown. Hidden while
        /// active: "not ready" under a glowing, running ultimate is noise - the meter's own fill already shows
        /// it refilling, and the reason text would describe a state the slot is not actually conveying.</summary>
        public static bool ShowsBlockReason(bool active, CastBlock block) => !active && block != CastBlock.None;
    }
}
