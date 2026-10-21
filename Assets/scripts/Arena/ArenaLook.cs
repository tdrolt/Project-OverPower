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
        [Tooltip("The see-through material for effects whose colour must read over any floor, put on the Sonic Pulse bands, the arcs on a stunned player, the Flamethrower's flame tongues, the Dash streak and chevrons, the Blink shards and ring and the Sprint speed-lines as they are built. The stun bolt's crackle and its burst use the same file through their own prefabs, so change the file itself to change them all. It draws whatever colour the effect gives it, so this file is never changed while playing.")]
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

        [Header("Guns")]
        [Tooltip("How many shots can light up the floor under them at the same time. Only shots you can see get a light, the nearest to your view first; more lights cost frame time in a big SMG fight, 0 turns the shot lights off.")]
        [Range(0, 8)] public int shotLightCap = 4;
        [Tooltip("How far, in metres, the light under a shot reaches across the floor and walls around it.")]
        [Min(0f)] public float shotLightRange = 4f;
        [Tooltip("How bright the light under a shot is, in the shooter's team colour: high enough to tint the floor under the shot, low enough that it never hides a player standing there.")]
        [Min(0f)] public float shotLightIntensity = 0.3f;
        [Tooltip("How many copies of one impact or muzzle flash can be on screen at once. Past this the oldest copy is moved to the newest hit, so a long SMG fight never makes new objects.")]
        [Min(1)] public int fxPoolCap = 24;
        [Tooltip("How far, in metres, each impact and muzzle flash is drawn toward your camera from the spot it happened. It stays on the same spot on screen, and a flash on a wall is not half hidden inside the wall; 0 draws it exactly where it happened.")]
        [Min(0f)] public float fxCameraPull = 1f;
        [Tooltip("How thick, in metres, the rocket's flat shockwave ring is drawn at the edge of its real blast.")]
        [Min(0.01f)] public float blastRingWidth = 0.2f;
        [Tooltip("How high, in metres, the light under a shot hangs over the floor straight below the shot. Low, so its glow lands on the floor under the slug instead of a bright dot off to the side.")]
        [Min(0.05f)] public float shotLightHeight = 0.5f;
        [Tooltip("How much brighter than plain Flash White every gun flash is drawn (impacts, the muzzle flash, the ricochet chevron). Above 1 the flash glows past the arena's stripes so a hit stands out; 1 is plain white, and much above 3 the glow swallows the flash's outline into a round blob that hides the player it hit.")]
        [Min(0f)] public float gunFlashGlow = 2.5f;

        [Header("Dash")]
        [Tooltip("How wide the Dash's silver streak is at the dasher's end, in metres. It narrows to a sharp point where the dash began.")]
        [Min(0.01f)] public float dashStreakWidth = 0.5f;
        [Tooltip("How high above the floor the Dash streak runs, in metres. About hip height, so it reads as the path the body took.")]
        [Min(0f)] public float dashStreakHeight = 0.5f;
        [Tooltip("How solid the Dash streak and its floor chevrons are while the dash travels, from 0 (invisible) to 1 (solid).")]
        [Range(0f, 1f)] public float dashStreakAlpha = 1f;
        [Tooltip("How long the Dash streak and its floor chevrons take to fade once the dash has ended, in seconds.")]
        [Min(0f)] public float dashFadeSeconds = 0.25f;
        [Tooltip("How many flat chevrons the Dash leaves on the floor where it began, pointing the way it went. 0 draws only the streak.")]
        [Range(0, 4)] public int dashChevronCount = 3;
        [Tooltip("How far apart the Dash's floor chevrons are, in metres. On a dash too short to hold them all they squeeze closer, and a dash stopped short by a wall shows only the chevrons it reached.")]
        [Min(0f)] public float dashChevronSpacing = 0.7f;
        [Tooltip("How wide each Dash floor chevron is from wing tip to wing tip, in metres.")]
        [Min(0.05f)] public float dashChevronSize = 0.6f;
        [Tooltip("How thick the lines of each Dash floor chevron are, in metres.")]
        [Min(0.01f)] public float dashChevronWidth = 0.1f;
        [Tooltip("How far behind the dasher the Dash streak's wide end stops, in metres, so the streak never paints over the dasher's legs.")]
        [Min(0f)] public float dashStreakGap = 1.05f;

        [Header("Blink")]
        [Tooltip("How many silver shards burst out where a player blinked away from.")]
        [Range(3, 16)] public int blinkShardCount = 8;
        [Tooltip("How long each Blink shard is from its base to its point, in metres.")]
        [Min(0.01f)] public float blinkShardLength = 0.45f;
        [Tooltip("How wide each Blink shard is at its base, in metres. It narrows to a sharp point.")]
        [Min(0.01f)] public float blinkShardWidth = 0.2f;
        [Tooltip("How far the Blink shards fly out from where the player stood, in metres.")]
        [Min(0f)] public float blinkShardReach = 1.5f;
        [Tooltip("How high above the floor the Blink shards burst, in metres: about the middle of the body that vanished.")]
        [Min(0f)] public float blinkShardHeight = 0.8f;
        [Tooltip("How long the Blink shards fly and fade, in seconds.")]
        [Min(0.01f)] public float blinkShardSeconds = 0.35f;
        [Tooltip("How far the flat hexagon ring reaches out on the floor where a player blinked to, in metres, measured to its corners.")]
        [Min(0.05f)] public float blinkRingRadius = 1.3f;
        [Tooltip("How thick the Blink landing ring's line is, in metres.")]
        [Min(0.01f)] public float blinkRingWidth = 0.12f;
        [Tooltip("How long the Blink landing ring takes to snap out to its full size, in seconds.")]
        [Min(0.01f)] public float blinkRingSnapSeconds = 0.12f;
        [Tooltip("How long the Blink landing ring takes to fade once it has snapped out, in seconds.")]
        [Min(0f)] public float blinkRingFadeSeconds = 0.3f;
        [Tooltip("How much wider the Blink landing ring keeps growing while it fades, as a share of its size: 0.35 ends it 35% wider. A ring that keeps moving reads as a landing, not as a lit floor tile; 0 holds it still.")]
        [Min(0f)] public float blinkRingSpread = 0.35f;
        [Tooltip("How solid the Blink shards and landing ring are at their strongest, from 0 (invisible) to 1 (solid).")]
        [Range(0f, 1f)] public float blinkAlpha = 1f;

        [Header("Zip Gun")]
        [Tooltip("The Zip Gun's cable, in flight and during the pull: short silver segments with small chevrons between them that run toward the target. Its colour is put on each cable while playing, so this file is never changed while playing.")]
        public Material zipCable;
        [Tooltip("How wide the Zip Gun's cable is drawn, in metres. Its chevrons fill this width; the cable's segments are a thin line along its middle.")]
        [Min(0.01f)] public float zipCableWidth = 0.24f;
        [Tooltip("How far apart the chevrons on the Zip Gun's cable are, in metres.")]
        [Min(0.05f)] public float zipChevronSpacing = 0.6f;
        [Tooltip("How fast the chevrons run along the Zip Gun's cable toward the target, in metres per second. 0 holds them still.")]
        [Min(0f)] public float zipChevronSpeed = 8f;

        [Header("Sprint")]
        [Tooltip("How many silver speed-lines stream behind a sprinting player's feet.")]
        [Range(1, 6)] public int sprintLineCount = 3;
        [Tooltip("How long each Sprint speed-line is, in metres.")]
        [Min(0.05f)] public float sprintLineLength = 0.75f;
        [Tooltip("How thick each Sprint speed-line is, in metres.")]
        [Min(0.005f)] public float sprintLineWidth = 0.12f;
        [Tooltip("How far apart the Sprint speed-lines are side by side, in metres.")]
        [Min(0f)] public float sprintLineSpread = 0.25f;
        [Tooltip("How far behind the middle of a sprinting player the speed-lines begin, in metres, so they never cover the player or their feet.")]
        [Min(0f)] public float sprintLineBehind = 0.6f;
        [Tooltip("How far back each Sprint speed-line slides before it fades out and starts again, in metres.")]
        [Min(0f)] public float sprintLineTravel = 0.7f;
        [Tooltip("How many times a second each Sprint speed-line slides back and starts again: higher reads as faster.")]
        [Min(0.1f)] public float sprintLineRate = 3f;
        [Tooltip("How fast a sprinting player must actually move before the speed-lines show, in metres per second, so holding Sprint while standing still draws nothing.")]
        [Min(0.01f)] public float sprintLineMinSpeed = 1f;
        [Tooltip("How solid the Sprint speed-lines are at their strongest, from 0 (invisible) to 1 (solid).")]
        [Range(0f, 1f)] public float sprintLineAlpha = 1f;
        [Tooltip("How high above the floor the Sprint speed-lines run, in metres: just above it, so they read as kicked up by the feet.")]
        [Min(0f)] public float sprintLineLift = 0.06f;

        [Header("Portal")]
        [Tooltip("How thick the portal's hexagon frame on the floor is, in metres. Its corners sit on the portal's real edge, where a player can still use it.")]
        [Min(0.01f)] public float portalFrameWidth = 0.14f;
        [Tooltip("How big the turning hexagon inside a portal is, as a share of the portal's size: 0.5 is half as wide as the frame.")]
        [Range(0.1f, 0.95f)] public float portalInnerScale = 0.55f;
        [Tooltip("How thick the turning hexagon inside a portal is, in metres.")]
        [Min(0.01f)] public float portalInnerWidth = 0.07f;
        [Tooltip("How fast the hexagon inside a portal turns, in degrees each second. Slow, so it reads as a live gate without pulling the eye.")]
        public float portalInnerTurnSpeed = 20f;

        [Header("Mines")]
        [Tooltip("How brightly the light on a mine's cap glows in its team colour while it is on: 1 is the plain team colour, more makes it bloom.")]
        [Min(0f)] public float mineCapGlow = 1.5f;
        [Tooltip("Seconds from one blink of a mine's light to the next. Every player sees the same mine blink at the same moment. 0 keeps the light on steadily.")]
        [Min(0f)] public float mineBlinkPeriod = 1f;
        [Tooltip("How many seconds each blink of a mine's light lasts.")]
        [Min(0f)] public float mineBlinkOnSeconds = 0.2f;
        [Tooltip("How bright a mine's light stays between blinks, as a share of the blink: 0 goes dark, 1 never blinks.")]
        [Range(0f, 1f)] public float mineCapDim = 0.3f;

        [Header("Cover")]
        [Tooltip("How thick the glowing team-colour band along every edge of a placed cover wall is, in metres. It is what tells you whose cover it is.")]
        [Min(0f)] public float coverEdgeWidth = 0.08f;
        [Tooltip("How brightly the edges of a placed cover wall glow in its owner's colour: 1 is the plain team colour, more makes it bloom.")]
        [Min(0f)] public float coverEdgeGlow = 1.2f;

        [Header("Electric Fence")]
        [Tooltip("How many times a second the electric arcs between an Electric Fence's pylons jump into a new shape. Every player sees the same shape at the same moment.")]
        [Min(1f)] public float fenceArcRate = 15f;
        [Tooltip("How many kinks each electric arc has between two neighbouring pylons: more reads as a more jagged spark.")]
        [Range(2, 10)] public int fenceArcPointsPerGap = 5;
        [Tooltip("How far an electric arc between pylons jumps in, out, up and down, in metres.")]
        [Min(0f)] public float fenceArcJitter = 0.15f;
        [Tooltip("How thick each electric arc between the fence's pylons is, in metres.")]
        [Min(0.005f)] public float fenceArcWidth = 0.06f;
        [Tooltip("How brightly the fence's arcs glow in its owner's colour: 1 is the plain team colour, more makes them bloom.")]
        [Min(0f)] public float fenceArcGlow = 1.2f;
        [Tooltip("How much of the owner's colour an Electric Fence pylon's body takes: 0 is black, 1 the full team colour. Darker than the arcs, so the arcs carry the glow.")]
        [Range(0f, 1f)] public float fencePylonShade = 0.15f;
        [Tooltip("How much a fence pylon glows in its owner's colour on top of its body: 0 not at all, 1 as bright as the team colour.")]
        [Min(0f)] public float fencePylonGlow = 0.2f;

        [Header("AoE Zone")]
        [Tooltip("How deep the ring of hexagon cells inside the AoE Zone's edge is, in metres. The middle of the zone stays clear so the players standing in it are easy to see; the edge line still marks the whole area that hurts.")]
        [Min(0f)] public float aoeRingBand = 1.6f;
        [Tooltip("How wide one hexagon cell of the AoE Zone's ring is across its flat sides, in metres. Smaller cells make a finer, busier ring.")]
        [Min(0.1f)] public float aoeCellSize = 0.7f;
        [Tooltip("How much dark floor shows between two neighbouring cells of the AoE Zone's ring, in metres, so each cell reads as its own hexagon.")]
        [Min(0f)] public float aoeCellGap = 0.08f;
        [Tooltip("How thick the outline of each hexagon cell in the AoE Zone's ring is, in metres.")]
        [Min(0.005f)] public float aoeCellLineWidth = 0.05f;
        [Tooltip("How thick the hard red line round the AoE Zone's edge is, in metres. It sits on the zone's real radius: inside it you take damage.")]
        [Min(0.01f)] public float aoeEdgeWidth = 0.16f;
        [Tooltip("How solid the line round the AoE Zone's edge is, from 0 (invisible) to 1 (solid).")]
        [Range(0f, 1f)] public float aoeEdgeAlpha = 1f;
        [Tooltip("How many of the AoE Zone's cells are lit at any moment, as a share of them all: 0 leaves every cell dim, 1 lights them all and the ring stops flickering.")]
        [Range(0f, 1f)] public float aoeLitShare = 0.35f;
        [Tooltip("How solid the outline of a lit AoE Zone cell is, from 0 (invisible) to 1 (solid).")]
        [Range(0f, 1f)] public float aoeLitLineAlpha = 0.85f;
        [Tooltip("How solid the outline of a dim AoE Zone cell is, from 0 (invisible) to 1 (solid). Keep it above 0 so the whole ring always shows.")]
        [Range(0f, 1f)] public float aoeDimLineAlpha = 0.3f;
        [Tooltip("How strongly a lit AoE Zone cell fills with red between its lines, from 0 (lines only) to 1 (solid). Keep it low so a player standing on a lit cell stays easy to see.")]
        [Range(0f, 1f)] public float aoeLitFillAlpha = 0.3f;
        [Tooltip("How many times a second the AoE Zone's cells change which ones are lit. The pattern comes from the server clock and the caster, so every player sees the same flicker.")]
        [Min(0.1f)] public float aoeFlickerRate = 8f;
        [Tooltip("How high above the floor the AoE Zone's ring is drawn, in metres: just enough that it never flickers into the floor.")]
        [Min(0f)] public float aoeLift = 0.04f;

        [Header("Raybeam")]
        [Tooltip("How solid each Raybeam's red sheath is, from 0 (invisible) to 1 (solid). The sheath is exactly as wide as what the beam hits; keep it see-through so a player under the beam stays easy to see, the white core carries the beam.")]
        [Range(0f, 1f)] public float raybeamSheathAlpha = 0.4f;
        [Tooltip("How wide each Raybeam's white-hot core is, as a share of the beam's width: 0 draws no core, 1 makes the whole beam white.")]
        [Range(0f, 1f)] public float raybeamCoreShare = 0.25f;
        [Tooltip("How brightly each Raybeam's white core glows: 1 is plain white, more makes it bloom; much above 2 the glow swallows the red sheath.")]
        [Min(0f)] public float raybeamCoreGlow = 2f;

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

        [Header("Ripple when a zone flips")]
        [Tooltip("How long the ring of brightened floor hexagons takes to run from a tower to the edge of its zone's painted ground when the zone changes owner, in seconds. The floor then rests in its new colour.")]
        [Min(0.05f)] public float rippleSeconds = 0.6f;
        [Tooltip("How far the running ring of brightened floor hexagons reaches to either side of its line, in metres, fading out to nothing at that distance. About one floor hexagon lights two rings of tiles at a time; less lights a thinner ring.")]
        [Min(0.1f)] public float rippleRingWidth = 3f;
        [Tooltip("How brightly the running ring lights the floor hexagons in the zone's new colour: 1 is the plain colour, more makes it bloom. It fades out as the ring reaches the edge.")]
        [Min(0f)] public float rippleGlow = 0.8f;

        [Header("Team hexagon under the feet")]
        [Tooltip("Whether every player stands on a flat hexagon in their team's colour, so you can tell teams apart by the ground under them. Untick it to hide every one of these hexagons at once.")]
        public bool feetHexShown = true;
        [Tooltip("How far the hexagon under a player's feet reaches from their middle to one of its corners, in metres. Big enough to show round the mech at the normal camera, small enough to leave most of the floor's own owner tiles in view.")]
        [Min(0.1f)] public float feetHexRadius = 1.1f;
        [Tooltip("How thick the bright edge of the hexagon under a player's feet is, in metres.")]
        [Min(0.005f)] public float feetHexEdgeWidth = 0.08f;
        [Tooltip("How solid the bright edge of the hexagon under a player's feet is, from 0 (invisible) to 1 (solid).")]
        [Range(0f, 1f)] public float feetHexEdgeAlpha = 0.9f;
        [Tooltip("How strongly the inside of the hexagon under a player's feet fills with the team colour, from 0 (edge only) to 1 (solid). Keep it soft so the floor's owner tiles still show through.")]
        [Range(0f, 1f)] public float feetHexFillAlpha = 0.2f;
        [Tooltip("How high above the floor the hexagon under a player's feet is drawn, in metres: just enough that it never flickers into the floor.")]
        [Min(0.001f)] public float feetHexLift = 0.03f;

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

        [Header("Capture towers")]
        [Tooltip("The material of a capture tower's body and columns: the arena wall's dark glossy look, whose circuit stripes take the zone owner's colour from the ground the tower stands on. Point it at another arena surface material, such as the blocks', to give the towers that tint instead. Dominion spawn towers keep their own solid team colour.")]
        public Material towerBody;
        [Tooltip("How much of a capture tower's lid is dark, as a share of its width: the rest is a glowing rim in the owner's colour. 0 makes the whole lid glow; about 0.75 leaves a rim as wide as the glowing base ring, so from above an owned tower reads as a glowing outline instead of a solid disc of colour.")]
        [Range(0f, 0.95f)] public float towerCrownInset = 0.75f;

        public IReadOnlyList<AbilityLook> AbilityLooks => abilityLooks;
        public AbilityLook RespawnBubbleLook => respawnBubbleLook;

        // Only the saved asset: a look made in memory (tests make literal ones) would repaint the real arena.
        private void OnValidate()
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorUtility.IsPersistent(this))
                return;
#endif
            ArenaShaderGlobals.Push(this);
        }

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
