using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Dominion
{
    /// <summary>
    /// Dominion Task 6: where a team's spawn heals its players, as a place in the scene. The 2v2 map has no capital circle, so its scene gets one of
    /// these per team around the pocket behind the boxes (Task 11); in 3v3v3 the team's own capital circle is the spawn without any of these
    /// (see InOwnSpawn). Only the flat ground position counts, not the height. Registered while enabled, so the lookup needs no scene search.
    /// </summary>
    public sealed class SpawnHealArea : MonoBehaviour
    {
        public enum AreaShape { Box, Circle }

        [Tooltip("Which team this spawn belongs to (0, 1 or 2). Only that team's players are healed here.")]
        [SerializeField, Min(0)] private int team;

        [Tooltip("Box: a rectangle on the ground that turns with this object. Circle: a round area around this object.")]
        [SerializeField] private AreaShape shape = AreaShape.Box;

        [Tooltip("For a box: how wide it is (first number, across this object's right) and how deep (second number, along its forward), in metres.")]
        [SerializeField] private Vector2 boxSizeMetres = new Vector2(10f, 10f);

        [Tooltip("For a circle: its radius in metres.")]
        [SerializeField, Min(0.1f)] private float radiusMetres = 5f;

        private static readonly List<SpawnHealArea> registered = new List<SpawnHealArea>();

        public int Team => team;

        private void OnEnable() => Register(this);
        private void OnDisable() => Unregister(this);

        public static void Register(SpawnHealArea area)
        {
            if (area != null && !registered.Contains(area)) registered.Add(area);
        }

        public static void Unregister(SpawnHealArea area) => registered.Remove(area);

        /// <summary>Describes the area in code (the tests, and scene setup scripts).</summary>
        public void Configure(int forTeam, AreaShape areaShape, Vector2 box, float radius)
        {
            team = forTeam; shape = areaShape; boxSizeMetres = box; radiusMetres = radius;
        }

        /// <summary>True when the position is inside this area, ignoring height and team.</summary>
        public bool ContainsPoint(Vector3 position)
        {
            Vector3 centre = transform.position;
            if (shape == AreaShape.Circle)
                return InCircle(centre.x, centre.z, radiusMetres, position.x, position.z);
            return InBox(centre.x, centre.z, transform.eulerAngles.y, boxSizeMetres.x, boxSizeMetres.y, position.x, position.z);
        }

        /// <summary>Flat circle test; the edge counts as inside.</summary>
        public static bool InCircle(float centreX, float centreZ, float radius, float x, float z)
        {
            float dx = x - centreX, dz = z - centreZ;
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>Flat box test for a rectangle turned by yawDegrees about the vertical axis (Unity's yaw: 0 = facing +Z, turning towards +X).
        /// The edge counts as inside.</summary>
        public static bool InBox(float centreX, float centreZ, float yawDegrees, float width, float depth, float x, float z)
        {
            float rad = yawDegrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            float dx = x - centreX, dz = z - centreZ;
            float right = dx * cos - dz * sin;   // along the box's own right axis (cos, -sin)
            float forward = dx * sin + dz * cos; // along its own forward axis (sin, cos)
            return Mathf.Abs(right) <= width * 0.5f + 1e-4f && Mathf.Abs(forward) <= depth * 0.5f + 1e-4f;
        }

        /// <summary>True when the position is inside a registered area of this team.</summary>
        public static bool Contains(int forTeam, Vector3 position)
        {
            for (int i = 0; i < registered.Count; i++)
            {
                SpawnHealArea area = registered[i];
                if (area != null && area.team == forTeam && area.ContainsPoint(position)) return true;
            }
            return false;
        }

        /// <summary>Is the position inside this team's own spawn: a registered area of the team (the 2v2 pocket), or, in a 3v3v3 match, the team's own
        /// capital circle (no scene object needed). An enemy's spawn is never this team's.</summary>
        public static bool InOwnSpawn(int forTeam, Vector3 position)
        {
            if (forTeam < 0) return false;
            bool inArea = Contains(forTeam, position);
            int teamCount = DominionMode.TeamCountOfCurrentRoom();
            bool inOwnCapital = false;
            if (!inArea && teamCount == 3) // the capital circle only matters in 3v3v3; skip the zone lookup otherwise
            {
                BuildingManager manager = BuildingManager.Instance;
                inOwnCapital = manager != null && manager.Map != null && manager.TryGetZoneAt(position, out int zone) && manager.Map.IsCapitalOf(zone, forTeam);
            }
            return DominionHealRules.InOwnSpawn(inArea, teamCount, inOwnCapital);
        }

        /// <summary>True when the scene has registered at least one healing area for this team.</summary>
        public static bool HasAreaFor(int forTeam)
        {
            for (int i = 0; i < registered.Count; i++)
                if (registered[i] != null && registered[i].team == forTeam) return true;
            return false;
        }
    }
}
