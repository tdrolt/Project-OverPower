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
        [Tooltip("The material that adds light, put on the Flamethrower's embers each time it sprays. The stun burst's bright spikes use the same file through their own prefab, so change the file itself to change both. Adding light turns any colour paler over the grey floor, so effects whose colour must read stay on Fx Alpha. It draws whatever colour the effect gives it, so this file is never changed while playing.")]
        public Material fxAdditive;
        [Tooltip("The see-through material for effects whose colour must read over any floor, put on the Sonic Pulse bands, the arcs on a stunned player and the Flamethrower's flame tongues as they are built. The stun bolt's crackle and its burst use the same file through their own prefabs, so change the file itself to change them all. It draws whatever colour the effect gives it, so this file is never changed while playing.")]
        public Material fxAlpha;

        [Header("Stun Gun")]
        [Tooltip("How brightly the solid core of an ability's own shot (the Stun Gun bolt) glows in its look colour. About 0.6 keeps a deep electric blue; above about 1 the glow turns the bolt pale cyan, the colour of the cyan team.")]
        [Min(0f)] public float abilityShotGlow = 0.6f;
        [Tooltip("How bright the sparks of an ability's own shot are (the crackle behind the Stun Gun bolt and the burst where it lands), as a multiple of its look colour. 1 is exactly the STUNNED label's blue; above 1 the blue lifts toward the paler blue of the health bar and the cyan team.")]
        [Min(0f)] public float abilitySparkGlow = 1f;
        [Tooltip("How many electric arcs crackle around a stunned player.")]
        [Range(1, 6)] public int stunArcCount = 3;
        [Tooltip("How many times a second the arcs on a stunned player jump to a new shape. The shape comes from the server clock and the player, so everyone sees the same arcs.")]
        [Min(1f)] public float stunArcRate = 15f;
        [Tooltip("How far the arcs on a stunned player sit from the middle of their body, in metres. A little wider than the body, so the arcs wrap around the mech instead of hiding inside it.")]
        [Min(0.1f)] public float stunArcRadius = 0.85f;
        [Tooltip("How far up and down from the middle of a stunned player's body the arcs reach, in metres.")]
        [Min(0f)] public float stunArcHeight = 0.75f;
        [Tooltip("How jagged the arcs on a stunned player are: 0 draws smooth curves, about 0.25 draws sharp zig-zags like lightning.")]
        [Range(0f, 0.6f)] public float stunArcJitter = 0.25f;
        [Tooltip("How thick each arc on a stunned player is, in metres.")]
        [Min(0.005f)] public float stunArcWidth = 0.05f;
        [Tooltip("How far round a stunned player's body each arc runs, in degrees: each arc picks a length between the first number (shortest) and the second (longest). Shorter arcs read as separate sparks; long ones join into a ring round the feet when seen from above.")]
        public Vector2 stunArcSweepDegrees = new Vector2(50f, 100f);
        [Tooltip("How brightly the arcs on a stunned player glow: 1 is exactly the STUNNED label's blue; above 1 the arcs lift toward the paler blue of the player's own health bar and stop matching the label.")]
        [Min(0f)] public float stunArcGlow = 1f;

        [Header("Sonic Pulse")]
        [Tooltip("How many see-through bands sweep out over the Sonic Pulse's cone when it is cast.")]
        [Range(1, 4)] public int pulseBandCount = 3;
        [Tooltip("How long the leading Sonic Pulse band takes to sweep from the caster to the end of the cone, in seconds.")]
        [Min(0.01f)] public float pulseSweepSeconds = 0.2f;
        [Tooltip("How long the Sonic Pulse bands take to fade away once the leading band reaches the end of the cone, in seconds.")]
        [Min(0f)] public float pulseFadeSeconds = 0.25f;
        [Tooltip("How far each Sonic Pulse band trails the one ahead of it, as a fraction of the cone's range: 0 stacks them all on one arc.")]
        [Range(0f, 0.5f)] public float pulseBandSpacing = 0.18f;
        [Tooltip("How thick each Sonic Pulse band is, in metres.")]
        [Min(0.01f)] public float pulseBandWidth = 0.3f;
        [Tooltip("How high above the floor the Sonic Pulse bands sweep, in metres. Around waist height they pass through the players they push.")]
        [Min(0f)] public float pulseBandHeight = 0.4f;
        [Tooltip("How solid the leading Sonic Pulse band is, from 0 (invisible) to 1 (solid); the bands behind it are fainter. Below about 0.8 the blue mixes with the grey floor into a dull slate; at 1 it hides the players inside the cone.")]
        [Range(0f, 1f)] public float pulseBandAlpha = 0.85f;

        [Tooltip("How solid the last Sonic Pulse band is, as a share of the leading band: 1 makes every band as solid as the first, lower fades the trailing bands. Too low and they vanish into the grey floor.")]
        [Range(0f, 1f)] public float pulseTrailAlpha = 0.55f;
        [Tooltip("How pale the thin line along the middle of each Sonic Pulse band is: 0 is the band's own blue, 1 is white. A paler middle keeps the bands visible over a grey floor for players who tell colours apart by brightness.")]
        [Range(0f, 1f)] public float pulseCoreWhiteness = 0.55f;
        [Tooltip("How thick the pale line along the middle of each Sonic Pulse band is, as a share of the band's thickness.")]
        [Range(0.05f, 1f)] public float pulseCoreWidth = 0.3f;

        [Header("Flamethrower")]
        [Tooltip("How many flame tongues the Flamethrower raises each second for every metre of its range: more makes a denser fire along the fan.")]
        [Min(0f)] public float flameTonguesPerMetre = 22f;
        [Tooltip("How many embers rise from the Flamethrower's fan each second.")]
        [Min(0f)] public float flameEmbersPerSecond = 8f;
        [Tooltip("How far from the caster the flame tongues start, as a fraction of the cone's range, so the flames never hide the player holding the Flamethrower.")]
        [Range(0f, 0.9f)] public float flameTongueStart = 0.15f;

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
