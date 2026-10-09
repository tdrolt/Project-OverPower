using UnityEngine;
using Overpower.Combat;

namespace Overpower.UI
{
    /// <summary>
    /// Pure precedence rule for a weapon/ability slot's border tint and block-reason text. An ACTIVE module wins
    /// over every other block reason, except Dead, which always wins. Block must not be checked before active:
    /// Invulnerability stays IsActive for its whole armed window while the meter is spent (IsReady false), and
    /// Flamethrower and Invulnerability stay IsActive through Stunned and Silenced by design; the amber glow is
    /// the point. Dead goes first because Invulnerability's status flags survive death until respawn's ClearAll
    /// (latent while minimumTriggerDamage is 0, visible once it is raised), and a corpse can't be un-blocked.
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

        /// <summary>Whether the block-reason line is shown. Hidden while active (except Dead): "not ready" under a
        /// glowing, running ultimate is noise - the meter's fill already shows it refilling.</summary>
        public static bool ShowsBlockReason(bool active, CastBlock block) =>
            block == CastBlock.Dead || (!active && block != CastBlock.None);
    }
}
