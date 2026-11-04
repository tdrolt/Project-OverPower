using System.Collections.Generic;
using Overpower.Data;
using UnityEngine;
using UnityEngine.AI;

namespace Overpower.Bots
{
    /// <summary>
    /// The walkable maps bots plan routes on. Neither scene ships one, so the host builds them when a match with bots starts, from the
    /// colliders that physically stop a player (floor, walls, blocks, barriers; never triggers or bodies), and again when the arena changes
    /// shape (the shrink's wall is built at runtime). They always match the current layout. Two maps from the same colliders: a tight one
    /// exactly as wide as the body (every gap a player fits) and a wide one with BotConfig's margin off walls; TryRoute picks between their
    /// routes (BotPathRules.TakeWide). Built asynchronously: routes are refused until the tight map is ready.
    /// </summary>
    public sealed class BotNavMesh
    {
        private static readonly string[] SolidLayers = { "Default", "Building", "Barrier" };
        // How far from the map a body or a goal may be and still be put on it, in metres.
        private const float SnapBodyMetres = 3f, SnapGoalMetres = 6f;
        // A point of the tight map is on the wide map when the wide map is within its margin plus this: further away, the nearest wide
        // point can be on the other side of a wall.
        private const float WideSnapSlackMetres = 0.25f;

        /// <summary>One walkable map: its data in the scene, the agent it is built for, and the build running now.</summary>
        private sealed class Map
        {
            public readonly string Name;
            public NavMeshData Data;
            public NavMeshDataInstance Instance;
            public NavMeshBuildSettings Settings;
            public bool HasSettings;
            public AsyncOperation Building;
            public bool Ready;

            public Map(string name) { Name = name; }

            public NavMeshQueryFilter Filter => new NavMeshQueryFilter { agentTypeID = Settings.agentTypeID, areaMask = NavMesh.AllAreas };
        }

        private readonly Map narrow = new Map("tight");
        private readonly Map wide = new Map("wide");
        private float wideMargin;

        /// <summary>The tight map is built: routes can be planned (on the wide map too once it is built).</summary>
        public bool Ready => narrow.Ready;
        public int AgentTypeId => narrow.Settings.agentTypeID;
        public int BuildCount { get; private set; }

        /// <summary>Starts a (re)build of both maps from the scene as it stands, the tight one bodyRadius wide. A route asked meanwhile uses
        /// the previous maps, if any.</summary>
        public void Build(BotConfig config, float bodyRadius)
        {
            if (config == null) return;
            wideMargin = config.WideRouteMarginMetres;
            Configure(narrow, bodyRadius, config);
            Configure(wide, bodyRadius + wideMargin, config);

            var sources = new List<NavMeshBuildSource>();
            var bounds = new Bounds(Vector3.zero, Vector3.one * 4000f);
            NavMeshBuilder.CollectSources(bounds, LayerMask.GetMask(SolidLayers), NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);
            Bounds used = default;
            bool any = false;
            for (int i = sources.Count - 1; i >= 0; i--)
            {
                var collider = sources[i].component as Collider;
                Rigidbody body = collider != null ? collider.attachedRigidbody : null;
                if (collider == null)
                {
                    sources.RemoveAt(i);
                    continue;
                }
                if (!BotPathRules.IsWalkSource(collider.isTrigger, body != null && !body.isKinematic))
                {
                    sources.RemoveAt(i);
                    continue;
                }
                if (!any) { used = collider.bounds; any = true; }
                else used.Encapsulate(collider.bounds);
            }
            if (!any)
            {
                Debug.LogWarning("[BOTS] no solid colliders to build the walkable maps from");
                return;
            }
            used.Expand(new Vector3(4f, 6f, 4f));

            Start(narrow, sources, used);
            Start(wide, new List<NavMeshBuildSource>(sources), used);
            BuildCount++;
            Debug.Log($"[BOTS] building the walkable maps ({narrow.Settings.agentRadius:0.00} and {wide.Settings.agentRadius:0.00} m wide) " +
                      $"from {sources.Count} colliders over {used.size.x:0}x{used.size.z:0} m");
        }

        private static void Configure(Map map, float radius, BotConfig config)
        {
            if (map.HasSettings) return;
            map.Settings = NavMesh.CreateSettings();
            map.Settings.agentRadius = radius;
            map.Settings.agentHeight = config.AgentHeight;
            map.Settings.agentClimb = config.AgentClimb;
            map.Settings.agentSlope = 30f;
            map.HasSettings = true;
        }

        private static void Start(Map map, List<NavMeshBuildSource> sources, Bounds bounds)
        {
            if (map.Data == null)
            {
                map.Data = new NavMeshData(map.Settings.agentTypeID);
                map.Instance = NavMesh.AddNavMeshData(map.Data);
            }
            map.Building = NavMeshBuilder.UpdateNavMeshDataAsync(map.Data, map.Settings, sources, bounds);
        }

        /// <summary>Call once a frame: marks a map ready when its build finishes.</summary>
        public void Tick()
        {
            Finish(narrow);
            Finish(wide);
        }

        private static void Finish(Map map)
        {
            if (map.Building == null || !map.Building.isDone) return;
            map.Building = null;
            map.Ready = true;
            Debug.Log($"[BOTS] {map.Name} walkable map ready");
        }

        /// <summary>The nearest point of the tight map to a world point (within maxDistance), for a body's feet or a goal.</summary>
        public bool TrySnap(Vector3 point, float maxDistance, out Vector3 onMap) => Snap(narrow, point, maxDistance, out onMap);

        private static bool Snap(Map map, Vector3 point, float maxDistance, out Vector3 onMap)
        {
            onMap = point;
            if (map.Data == null) return false;
            if (!NavMesh.SamplePosition(point, out NavMeshHit hit, maxDistance, map.Filter)) return false;
            onMap = hit.position;
            return true;
        }

        /// <summary>A route from one point to another as its corners: the wide map's when BotPathRules.TakeWide says so, else the tight map's.
        /// False while no map exists or when the points are not connected. The wide route counts only when both ends lie on the wide map
        /// close to where the tight map puts them.</summary>
        public bool TryRoute(Vector3 from, Vector3 to, NavMeshPath narrowPath, NavMeshPath widePath, float maxRatio, out Vector3[] corners,
            out bool wideRoute)
        {
            corners = System.Array.Empty<Vector3>();
            wideRoute = false;
            if (!Snap(narrow, from, SnapBodyMetres, out Vector3 start) || !Snap(narrow, to, SnapGoalMetres, out Vector3 end)) return false;
            bool narrowFound = NavMesh.CalculatePath(start, end, narrow.Filter, narrowPath) && narrowPath.status != NavMeshPathStatus.PathInvalid;
            Vector3[] narrowCorners = narrowFound ? narrowPath.corners : null;

            float slack = wideMargin + WideSnapSlackMetres;
            bool wideFound = wide.Ready && Snap(wide, start, slack, out Vector3 wideStart) && Snap(wide, end, slack, out Vector3 wideEnd)
                && NavMesh.CalculatePath(wideStart, wideEnd, wide.Filter, widePath) && widePath.status == NavMeshPathStatus.PathComplete;
            Vector3[] wideCorners = wideFound ? widePath.corners : null;

            wideRoute = BotPathRules.TakeWide(wideFound, wideFound ? BotPathRules.RouteMetres(wideCorners) : 0f,
                narrowFound && narrowPath.status == NavMeshPathStatus.PathComplete, narrowFound ? BotPathRules.RouteMetres(narrowCorners) : 0f,
                maxRatio);
            if (wideRoute) corners = wideCorners;
            else if (narrowFound) corners = narrowCorners;
            else return false;
            return true;
        }

        /// <summary>A tight route's bend moved to the middle of the gap it passes (BotPathRules.GapMiddle), looking searchMetres to each side
        /// on the tight map; the bend itself when the way there or on from there would leave the map.</summary>
        public Vector3 Centred(Vector3 previous, Vector3 corner, Vector3 next, float searchMetres)
        {
            if (narrow.Data == null || searchMetres <= 0f) return corner;
            NavMeshQueryFilter filter = narrow.Filter;
            Vector3 across = BotPathRules.Across(previous, corner, next);
            float right = OpenMetres(corner, across, searchMetres, filter);
            float left = OpenMetres(corner, -across, searchMetres, filter);
            Vector3 middle = BotPathRules.GapMiddle(corner, across, left, right);
            if (NavMesh.Raycast(previous, middle, out _, filter) || NavMesh.Raycast(middle, next, out _, filter)) return corner;
            return middle;
        }

        // How far the map goes on from a point in a direction (flat), up to maxMetres.
        private static float OpenMetres(Vector3 from, Vector3 direction, float maxMetres, NavMeshQueryFilter filter)
        {
            if (!NavMesh.Raycast(from, from + direction * maxMetres, out NavMeshHit hit, filter)) return maxMetres;
            return Vector2.Distance(new Vector2(hit.position.x, hit.position.z), new Vector2(from.x, from.z));
        }

        /// <summary>Whether nothing on the tight map blocks a straight walk between two points (the Zip and the Blink check it).</summary>
        public bool ClearWalk(Vector3 from, Vector3 to)
        {
            if (!Snap(narrow, from, SnapBodyMetres, out Vector3 start)) return false;
            return !NavMesh.Raycast(start, to, out _, narrow.Filter);
        }

        public void Dispose()
        {
            Remove(narrow);
            Remove(wide);
        }

        private static void Remove(Map map)
        {
            if (map.Instance.valid) map.Instance.Remove();
            map.Data = null;
            map.Building = null;
            map.Ready = false;
            if (map.HasSettings) NavMesh.RemoveSettings(map.Settings.agentTypeID);
            map.HasSettings = false;
        }
    }
}
