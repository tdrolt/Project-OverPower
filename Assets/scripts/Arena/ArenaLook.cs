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

        [Header("Arena surfaces")]
        [Tooltip("The floor's paint and shine under its hexagon grid.")]
        public SurfaceLook floorSurface = new SurfaceLook(new Color(0.16f, 0.17f, 0.19f, 1f), 0.7f, 0f);
        [Tooltip("The paint and shine of the tall boundary walls under their circuit stripes.")]
        public SurfaceLook wallSurface = new SurfaceLook(new Color(0.12f, 0.13f, 0.15f, 1f), 0.75f, 0.1f);
        [Tooltip("The paint and shine of the blocks (houses, crates and other solid cover) under their circuit stripes.")]
        public SurfaceLook blockSurface = new SurfaceLook(new Color(0.2f, 0.21f, 0.24f, 1f), 0.7f, 0.1f);
        [Tooltip("The paint and shine of the low barriers. They get no stripes at all, so low cover always reads as cover.")]
        public SurfaceLook barrierSurface = new SurfaceLook(new Color(0.86f, 0.64f, 0.26f, 1f), 0.8f, 0.6f);
        [Tooltip("How strongly the glossy arena mirrors the sky: 0 shows no sky in the shine, 1 shows all of it. Lower it if the floor turns pale at a shallow view and players get harder to see.")]
        [Range(0f, 1f)] public float reflectionStrength = 0.5f;

        [Header("Floor hexagon grid")]
        [Tooltip("How wide one floor hexagon is across its flat sides, in metres. It is also the size of the tiles that will show who owns the ground.")]
        [Min(0.5f)] public float floorTileSize = 3f;
        [Tooltip("How thick the lines between floor hexagons are, in metres.")]
        [Min(0f)] public float floorLineWidth = 0.05f;
        [Tooltip("How much the floor's hexagon lines glow away from every zone: 0 shows no grid there, about 0.05 is a faint grid that never competes with a player or a shot. Keep it fainter than a neutral zone's lighter lines, or the zone's band will not stand out.")]
        [Min(0f)] public float floorGridGlow = 0.05f;
        [Tooltip("How brightly the hexagon lines glow in the owner's colour on ground a team owns. 1 is the colour at full strength; keep it under about 1.5 so the glow gets only a thin halo.")]
        [Min(0f)] public float ownedEdgeGlow = 1.2f;
        [Tooltip("How much a hexagon on owned ground glows with the owner's colour in its middle, at its strongest near the zone. Keep it low so a player standing on it stays easy to see.")]
        [Min(0f)] public float ownedFillGlow = 0.2f;

        [Header("Ownership on the floor")]
        [Tooltip("How far past a zone's capture ring the floor and walls still show its owner, in floor hexagons: 1 paints one ring of tiles outside the circle you capture in, 0 paints only the tiles whose middle is inside it.")]
        [Min(0f)] public float ownerBandTiles = 1f;
        [Tooltip("How strongly an owned hexagon fills with the owner's colour at the zone's middle, from 0 (lines only) to 1 (the full Owned Fill Glow).")]
        [Range(0f, 1f)] public float ownerFillInside = 1f;
        [Tooltip("How strongly an owned hexagon fills at the outer edge of the painted band, from 0 to 1. The fill fades from the middle's strength down to this, and never grows outward, so the zone reads as a soft pool of its owner's colour.")]
        [Range(0f, 1f)] public float ownerFillEdge = 0.2f;

        [Header("Wall and block circuit stripes")]
        [Tooltip("The colour the floor grid and the circuit stripes glow in away from every zone. A neutral cool white with no blue cast, kept dim, so it never reads as a team's colour.")]
        public Color outsideLineColor = new Color(0.8f, 0.86f, 0.92f, 1f);
        [Tooltip("How much the circuit stripes on walls and blocks glow away from every zone: 0 turns them off there. Keep it well below the owned glow, because only the brightness tells these lines from ground the White team owns.")]
        [Min(0f)] public float outsideStripeGlow = 0.35f;
        [Tooltip("How brightly the circuit stripes glow in the owner's colour on walls and blocks near ground a team owns. Keep it under about 1.5 so the glow gets only a thin halo.")]
        [Min(0f)] public float stripeGlow = 1.2f;
        [Tooltip("The size of one square of the circuit pattern on walls and blocks, in metres. Each square holds at most one turn or one node, so smaller squares make a busier pattern.")]
        [Min(0.25f)] public float circuitCellSize = 1.5f;
        [Tooltip("How thick the circuit stripes are, in metres.")]
        [Min(0f)] public float stripeWidth = 0.06f;
        [Tooltip("How wide the small square nodes where stripes end or meet are, in metres.")]
        [Min(0f)] public float nodeSize = 0.22f;
        [Tooltip("How thick the glowing line around the top of every wall and block is, in metres. It is what shows a wall's shape when seen from above.")]
        [Min(0f)] public float topEdgeWidth = 0.08f;
        [Tooltip("How often a circuit stripe runs on sideways from one square into the next: 0 never, 1 always. Higher gives long level runs.")]
        [Range(0f, 1f)] public float circuitAcrossChance = 0.55f;
        [Tooltip("How often a circuit stripe runs up or down from one square into the next: 0 never, 1 always. Lower than the sideways chance keeps the pattern mostly level.")]
        [Range(0f, 1f)] public float circuitUpChance = 0.3f;

        public IReadOnlyList<AbilityLook> AbilityLooks => abilityLooks;
        public AbilityLook RespawnBubbleLook => respawnBubbleLook;

        private void OnValidate() => ArenaShaderGlobals.Push(this);

        // In a player the asset loads with the first scene that references UiTheme. The Editor pushes the saved asset from
        // ArenaLookEditorGlobals instead: here every CreateInstance (tests make literal looks) would overwrite the real look.
        private void OnEnable()
        {
            if (!Application.isEditor)
                ArenaShaderGlobals.Push(this);
        }

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
