using System;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>Which colour source an effect reads (LookColours maps each family to one UiTheme field).</summary>
    public enum LookFamily { None, Team, Gold, ElectricBlue, Movement, Fire, Danger, White, Heal }

    /// <summary>The silhouette an ability's effect is built from; no two abilities share a shape in the same colour family.</summary>
    public enum LookShape
    {
        NoLook, Streak, Shards, SpeedLines, HexGate, HarpoonCable, HexPuck, GlossyPanel, FlameFan,
        Bolt, ArcBands, Beam, HexShell, ArcPylons, HexGridRing, HologramSphere
    }

    /// <summary>One row of ArenaLook's table: what an ability's effect looks like (shape) and what its colour means (family).</summary>
    [Serializable]
    public struct AbilityLook
    {
        [Tooltip("The ability this row is for: the Id written on the ability asset (14 Dash up to 28 Scope).")]
        [SerializeField] private int abilityId;
        [Tooltip("The silhouette of the ability's effect. No Look means the ability draws nothing at all (the Scope).")]
        [SerializeField] private LookShape shape;
        [Tooltip("What the effect's colour means: Team for things a team owns, Gold for the Shield only, Electric Blue for the Stun Gun and Sonic Pulse, Movement for the movement abilities (grey with a little of the caster's team colour), Fire for the Flamethrower, Danger for the red damage abilities, White for hits and shots, Heal (green) for healing.")]
        [SerializeField] private LookFamily family;

        public AbilityLook(int abilityId, LookShape shape, LookFamily family)
        {
            this.abilityId = abilityId;
            this.shape = shape;
            this.family = family;
        }

        public int AbilityId => abilityId;
        public LookShape Shape => shape;
        public LookFamily Family => family;
    }
}
