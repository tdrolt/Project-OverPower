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
        [Tooltip("How much the Shield's gold plates glow on their own, as a multiple of the gold: 0 is plain lit metal, about 0.5 keeps them readable in the dark without a bloom halo that hides the player.")]
        [Min(0f)] public float shieldPlateGlow = 0.4f;
        [Tooltip("The colour seen through the seams between the Shield's plates, wherever the player is not behind them. Keep it near black so the seams read dark over any floor or team colour.")]
        public Color shieldSeamShade = new Color(0.035f, 0.03f, 0.025f, 1f);

        [Header("Materials")]
        [Tooltip("The see-through glowing glass of the respawn bubble (the Overpower/Hologram shader). Every bubble wears its own copy in its owner's team colour, so this file is never changed while playing.")]
        public Material hologram;
        [Tooltip("The metal of the Shield's hexagon plates: solid and lit, so the player inside is only seen through the seams. Its gold and the seams' shade are put on each Shield when it triggers, so this file is never changed while playing.")]
        public Material shieldPlates;

        [Header("Mech team colours")]
        [Tooltip("How much of the team colour the mech's body takes: 0 is a plain pale grey, 1 is the full team colour. Never so dark that the mech reads as black.")]
        [Range(0f, 1f)] public float mechBaseMix = 0.85f;
        [Tooltip("How bright the glow on a mech is: the brightest part of the team colour is shown at this strength, where 1 is as bright as the colour can be shown and more makes the glow bloom. The model only has a few glowing parts (the chin lamp and a strip). Above about 2 the glow turns pale and drifts toward pink or white.")]
        public float mechGlowStrength = 1.8f;
        [Tooltip("The material every team's mech is copied from: the model's own maps, with a greyscale glow-line map so the glow can take any team colour. Each team wears its own copy at runtime, so this file is never changed while playing.")]
        public Material mechTemplate;

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
