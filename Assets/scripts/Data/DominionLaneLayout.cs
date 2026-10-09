using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Data
{
    /// <summary>One flat, axis-aligned footprint on the ground, in metres: its middle and its size along X and along Z.</summary>
    [Serializable]
    public struct LaneRect
    {
        [Tooltip("For the designer's own reference (it names the object the builder makes).")]
        public string name;
        [Tooltip("Where its middle is on the ground, in metres: first number across (X), second number up the map (Z), from the middle of the whole map.")]
        public Vector2 centre;
        [Tooltip("How big it is on the ground, in metres: first number along X, second along Z.")]
        public Vector2 size;

        public LaneRect(string name, Vector2 centre, Vector2 size) { this.name = name; this.centre = centre; this.size = size; }

        public Vector2 Min => centre - size * 0.5f;
        public Vector2 Max => centre + size * 0.5f;
    }

    /// <summary>One team's end of the lane: its spawn tower, where its players appear and the ground that heals them.</summary>
    [Serializable]
    public struct LaneSpawn
    {
        [Tooltip("Which team this end belongs to (0 = White, 1 = Purple).")]
        public int team;
        [Tooltip("Where the spawn tower stands, in metres. It sits in the middle of the pocket and is cover.")]
        public Vector2 towerCentre;
        [Tooltip("Where this team's players appear, in metres: a few steps from the tower towards the middle of the map.")]
        public Vector2 spawnPoint;
        [Tooltip("The middle of the ground that heals this team, in metres.")]
        public Vector2 healCentre;
        [Tooltip("How big the healing ground is, in metres (first number along X, second along Z). It covers the pocket behind the boxes.")]
        public Vector2 healSize;
    }

    /// <summary>
    /// The 2v2 map as drawn: every wall, jersey barrier, box, zone, spawn and healing area, in metres from the middle of the map.
    /// The Editor tool (DominionLaneBuilder) builds the scene from these rows and the tests guard their structure; nothing else reads them.
    /// The drawing is 12 pixels to the metre, centred on the board's middle (550, 342): the numbers here are the board's, converted.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Dominion Lane Layout", fileName = "DominionLaneLayout")]
    public sealed class DominionLaneLayout : ScriptableObject
    {
        [Header("Walls and barriers")]
        [Tooltip("The white pieces of the drawing: the outer walls of the pocket and the middle hall, and the H in the middle (two big walls and the arms joining them to the plus). " +
                 "They all stop shots, sight and walking. The outer walls are boundary walls, which also stop a Portal; every piece whose name starts with 'H ' is a block " +
                 "like the triangle arena's middle wall, which a Portal can cross.")]
        [SerializeField] private List<LaneRect> walls = new List<LaneRect>();

        [Tooltip("The yellow plus in the middle: jersey barriers. They stop walking only; shots, Dash, Zip, Blink and Portals cross them.")]
        [SerializeField] private List<LaneRect> barriers = new List<LaneRect>();

        [Header("Boxes")]
        [Tooltip("The middle of every box (the grey squares in the pockets): a row of 3 and a row of 2 in front of each spawn.")]
        [SerializeField] private List<Vector2> boxes = new List<Vector2>();

        [Tooltip("How wide and deep every box is, in metres (the drawing: 2.5 m).")]
        [SerializeField, Min(0.5f)] private float boxSizeMetres = 2.5f;

        [Tooltip("How tall every box is, in metres. Tall enough to hide behind.")]
        [SerializeField, Min(0.5f)] private float boxHeightMetres = 2.5f;

        [Tooltip("The smallest walking gap, in metres, between two boxes or between a box and a wall (the board: 2.75 m or more). Checked by the tests and by the scene measure.")]
        [SerializeField, Min(0f)] private float minimumGapMetres = 2.75f;

        [Header("Zones")]
        [Tooltip("The middle of each neutral zone (top and bottom of the middle hall).")]
        [SerializeField] private List<Vector2> zoneCentres = new List<Vector2>();

        [Tooltip("Which tier the two zones are (3 = Tier 3: its capture time and size come from the Territory Config).")]
        [SerializeField, Range(1, 4)] private int zoneTier = 3;

        [Tooltip("The radius the drawing gave the zone circles, in metres (130 pixels across). The tests compare it with the Tier 3 radius in the Territory Config.")]
        [SerializeField, Min(0.1f)] private float zoneDrawnRadiusMetres = 5.4167f;

        [Tooltip("How wide the body of each zone tower is, in metres (the board draws a 24 pixel square at 12 pixels to the metre = 2 m).")]
        [SerializeField, Min(0.5f)] private float zoneTowerSizeMetres = 2f;

        [Tooltip("How wide the body of each team's spawn tower is, in metres (the board draws a 46 pixel circle = 3.8 m).")]
        [SerializeField, Min(0.5f)] private float spawnTowerSizeMetres = 3.8f;

        [Header("Spawns")]
        [Tooltip("One row per team: spawn tower, spawn point and healing ground. The drawing has the Purple pocket on the left and the White pocket on the right.")]
        [SerializeField] private List<LaneSpawn> spawns = new List<LaneSpawn>();

        [Tooltip("The distance the drawing's title gives between the two spawns ('56 m spawn to spawn'), in metres: the straight line between the two teams' spawn points.")]
        [SerializeField, Min(1f)] private float spawnToSpawnMetres = 56f;

        [Tooltip("A team's two players appear this many metres to either side of the spawn point (one to the left, one to the right as the team faces the middle), so teammates do not stand on the same spot.")]
        [SerializeField, Min(0f)] private float spawnSideOffsetMetres = 2f;

        [Header("Outline and floor")]
        [Tooltip("The playable ground seen from above: the inner faces of the outer walls, in order round the edge (first number X, second Z). " +
                 "The player clamp, Blink and Portal checks and the spectator view use it.")]
        [SerializeField] private List<Vector2> outline = new List<Vector2>();

        [Tooltip("How big the floor slab is, in metres (first number along X, second along Z). A little larger than the map.")]
        [SerializeField] private Vector2 floorSize = new Vector2(96f, 56f);

        [Header("Minimap")]
        [Tooltip("Extra metres of ground the minimap picture shows beyond the farthest wall, on every side.")]
        [SerializeField, Min(0f)] private float minimapMarginMetres = 4f;

        [Header("Builder measures")]
        [Tooltip("How tall the yellow jersey barriers are, in metres. Waist high, like the triangle arena's barriers: players see over them and shots cross them.")]
        [SerializeField, Min(0.1f)] private float barrierHeightMetres = 1f;

        [Tooltip("How far, in metres, the unused 'capital under attack' spawn point stands from the normal spawn point. Dominion never respawns anyone there; the point only keeps the scene's spawn list whole.")]
        [SerializeField, Min(0f)] private float unusedUnderAttackOffsetMetres = 2f;

        [Tooltip("The name of the physics layer the lane's walls and boxes are put on. It must be the same layer the triangle arena's walls use, so shots, sight and movement treat them the same.")]
        [SerializeField] private string wallLayerName = "Building";

        public float BarrierHeightMetres => barrierHeightMetres;
        public float UnusedUnderAttackOffsetMetres => unusedUnderAttackOffsetMetres;
        public string WallLayerName => wallLayerName;
        public IReadOnlyList<LaneRect> Walls => walls;
        public IReadOnlyList<LaneRect> Barriers => barriers;
        public IReadOnlyList<Vector2> Boxes => boxes;
        public float BoxSizeMetres => boxSizeMetres;
        public float BoxHeightMetres => boxHeightMetres;
        public float MinimumGapMetres => minimumGapMetres;
        public IReadOnlyList<Vector2> ZoneCentres => zoneCentres;
        public int ZoneTier => zoneTier;
        public float ZoneDrawnRadiusMetres => zoneDrawnRadiusMetres;
        public float ZoneTowerSizeMetres => zoneTowerSizeMetres;
        public float SpawnTowerSizeMetres => spawnTowerSizeMetres;
        public float SpawnSideOffsetMetres => spawnSideOffsetMetres;

        /// <summary>True for the pieces of the H in the middle ("H Wall Left", "H Arm Right", ...): built as blocks, not boundary walls, so a Portal crosses them as it crosses the triangle's middle wall.</summary>
        public static bool IsBlockWall(string wallName) => wallName != null && wallName.StartsWith("H ", StringComparison.Ordinal);
        public IReadOnlyList<LaneSpawn> Spawns => spawns;
        public float SpawnToSpawnMetres => spawnToSpawnMetres;
        public IReadOnlyList<Vector2> Outline => outline;
        public Vector2 FloorSize => floorSize;
        public float MinimapMarginMetres => minimapMarginMetres;

        public LaneRect BoxRect(int index) => new LaneRect("Box " + (index + 1), boxes[index], new Vector2(boxSizeMetres, boxSizeMetres));

        public bool TryGetSpawn(int team, out LaneSpawn spawn)
        {
            foreach (LaneSpawn row in spawns)
            {
                if (row.team != team) continue;
                spawn = row;
                return true;
            }
            spawn = default;
            return false;
        }

#if UNITY_EDITOR
        /// <summary>Editor only (DominionLaneLayoutFactory): replaces every row.</summary>
        public void SetRows(List<LaneRect> newWalls, List<LaneRect> newBarriers, List<Vector2> newBoxes, List<Vector2> newZones, List<LaneSpawn> newSpawns,
                            List<Vector2> newOutline, float newZoneRadius)
        {
            walls = newWalls; barriers = newBarriers; boxes = newBoxes; zoneCentres = newZones; spawns = newSpawns; outline = newOutline;
            zoneDrawnRadiusMetres = newZoneRadius;
        }
#endif
    }

    /// <summary>The plan-view maths the lane tests and the scene measure share: gaps between footprints, and mirror images.</summary>
    public static class LaneGeometry
    {
        public static float Gap(LaneRect a, LaneRect b) => Gap(a.Min, a.Max, b.Min, b.Max);

        public static float Gap(Vector2 aMin, Vector2 aMax, Vector2 bMin, Vector2 bMax)
        {
            float dx = Mathf.Max(0f, Mathf.Max(aMin.x - bMax.x, bMin.x - aMax.x));
            float dy = Mathf.Max(0f, Mathf.Max(aMin.y - bMax.y, bMin.y - aMax.y));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>The point turned half a turn about the middle of the map (x and z both flipped).</summary>
        public static Vector2 Opposite(Vector2 p) => new Vector2(-p.x, -p.y);

        /// <summary>The point mirrored across the map's long axis (z flipped).</summary>
        public static Vector2 FlipZ(Vector2 p) => new Vector2(p.x, -p.y);

        /// <summary>The point mirrored across the map's short axis (x flipped).</summary>
        public static Vector2 FlipX(Vector2 p) => new Vector2(-p.x, p.y);
    }
}
