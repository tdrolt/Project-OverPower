using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>The world-look numbers and the look of every ability, in one asset (UiTheme.arenaLook points here). The table is
    /// read by id, never by position; effects take their colour from it through LookColours as they are built.</summary>
    [CreateAssetMenu(menuName = "Overpower/Arena Look", fileName = "ArenaLook")]
    public sealed class ArenaLook : ScriptableObject
    {
        [Header("Look table")]
        [Tooltip("The look of every ability: its shape and what its colour means. One row per ability Id from 14 Dash to 28 Scope; the three debug pings have none. No two rows may share both shape and colour family, so no two abilities look alike.")]
        [SerializeField] private List<AbilityLook> abilityLooks = new List<AbilityLook>();
        [Tooltip("The look of the respawn bubble. It is not an ability, so its Ability Id is unused; it must differ from the Shield in both shape and colour family so a fresh respawn never reads as a cast Shield.")]
        [SerializeField] private AbilityLook respawnBubbleLook = new AbilityLook(-1, LookShape.HologramSphere, LookFamily.Team);

        [Header("Respawn bubble (hologram)")]
        [Tooltip("How much brighter the bubble's rim is than the middle of its glass: 0 is an even tint, 1 is a glowing edge with an almost empty middle.")]
        [Range(0f, 1f)] public float hologramRim = 0.8f;
        [Tooltip("How many thin horizontal scan-lines fit in each metre of the bubble's height.")]
        public float hologramScanDensity = 4f;
        [Tooltip("How fast the scan-lines drift upward over the bubble, in metres per second.")]
        public float hologramScanSpeed = 0.6f;
        [Tooltip("How strongly the bubble flickers: 0 is perfectly steady, 1 is flickering hard.")]
        [Range(0f, 1f)] public float hologramFlicker = 0.08f;
        [Tooltip("How bright the whole bubble is. It adds light to what is behind it, so keep it modest over a floor already painted in the same team colour.")]
        public float hologramIntensity = 1f;

        [Header("Shield shell")]
        [Tooltip("How wide the dark seams between the Shield's hexagon plates are, as a fraction of one plate: 0 and the plates touch, higher shows more of the player through the gaps.")]
        [Range(0f, 0.4f)] public float shieldSeamFraction = 0.08f;
        [Tooltip("How long the Shield's plates take to snap up from the ring on the floor when the Shield triggers, in seconds.")]
        public float shieldRiseSeconds = 0.15f;
        [Tooltip("How much the lowest plates lead the highest ones as the Shield rises, as a fraction of the rise time: 0 moves every plate together, higher sends the bottom plates up first.")]
        [Range(0f, 1f)] public float shieldRiseStagger = 0.35f;

        [Header("Mech team colours")]
        [Tooltip("How much of the team colour the mech's body takes: 0 is a plain pale grey, 1 is the full team colour. Never so dark that the mech reads as black.")]
        [Range(0f, 1f)] public float mechBaseMix = 0.5f;
        [Tooltip("How bright the glow lines on a mech are, as a multiple of the team colour. About 1.5 gives a thin halo; much higher washes the mech out.")]
        public float mechGlowStrength = 1.5f;

        public IReadOnlyList<AbilityLook> AbilityLooks => abilityLooks;
        public AbilityLook RespawnBubbleLook => respawnBubbleLook;

        /// <summary>Finds a row by ability Id, never by position.</summary>
        public bool TryGetLook(int abilityId, out AbilityLook look)
        {
            for (int i = 0; i < abilityLooks.Count; i++)
            {
                if (abilityLooks[i].AbilityId != abilityId)
                    continue;
                look = abilityLooks[i];
                return true;
            }
            look = default;
            return false;
        }
    }
}
