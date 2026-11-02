using System.Collections.Generic;
using Overpower.Data;
using UnityEngine;
using UnityEngine.AI;

namespace Overpower.Bots
{
    /// <summary>
    /// The walkable map bots plan routes on. Neither scene ships one, so the host builds it when a match with bots starts, from the
    /// colliders that physically stop a player (floor, walls, blocks, barriers; never triggers or bodies), sized by BotConfig's agent
    /// numbers, and again when the arena changes shape (the shrink's wall is built at runtime). It always matches the current layout.
    /// Built asynchronously: routes are refused until it is ready.
    /// </summary>
    public sealed class BotNavMesh
    {
        private static readonly string[] SolidLayers = { "Default", "Building", "Barrier" };

        private NavMeshData data;
        private NavMeshDataInstance instance;
        private NavMeshBuildSettings settings;
        private AsyncOperation building;
        private bool hasSettings;

        public bool Ready { get; private set; }
        public int AgentTypeId => settings.agentTypeID;
        public int BuildCount { get; private set; }

        /// <summary>Starts a (re)build from the scene as it stands. A route asked meanwhile uses the previous map, if any.</summary>
        public void Build(BotConfig config)
        {
            if (!hasSettings)
            {
                settings = NavMesh.CreateSettings();
                settings.agentRadius = config != null ? config.AgentRadius : 1f;
                settings.agentHeight = config != null ? config.AgentHeight : 2.6f;
                settings.agentClimb = config != null ? config.AgentClimb : 0.4f;
                settings.agentSlope = 30f;
                hasSettings = true;
            }

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
                Debug.LogWarning("[BOTS] no solid colliders to build the walkable map from");
                return;
            }
            used.Expand(new Vector3(4f, 6f, 4f));

            if (data == null)
            {
                data = new NavMeshData(settings.agentTypeID);
                instance = NavMesh.AddNavMeshData(data);
            }
            building = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, sources, used);
            BuildCount++;
            Debug.Log($"[BOTS] building the walkable map from {sources.Count} colliders over {used.size.x:0}x{used.size.z:0} m");
        }

        /// <summary>Call once a frame: marks the map ready when the build finishes.</summary>
        public void Tick()
        {
            if (building == null || !building.isDone) return;
            building = null;
            Ready = true;
            Debug.Log("[BOTS] walkable map ready");
        }

        /// <summary>The nearest point of the map to a world point (within maxDistance), for a body's feet or a goal.</summary>
        public bool TrySnap(Vector3 point, float maxDistance, out Vector3 onMap)
        {
            onMap = point;
            if (!Ready && data == null) return false;
            var filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = NavMesh.AllAreas };
            if (!NavMesh.SamplePosition(point, out NavMeshHit hit, maxDistance, filter)) return false;
            onMap = hit.position;
            return true;
        }

        /// <summary>A route from one point to another; false while no map exists or when they are not connected.</summary>
        public bool TryPath(Vector3 from, Vector3 to, NavMeshPath path)
        {
            if (data == null || !TrySnap(from, 3f, out Vector3 start) || !TrySnap(to, 6f, out Vector3 end)) return false;
            var filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = NavMesh.AllAreas };
            return NavMesh.CalculatePath(start, end, filter, path) && path.status != NavMeshPathStatus.PathInvalid;
        }

        /// <summary>Whether nothing on the map blocks a straight walk between two points (the Zip and the Blink check it).</summary>
        public bool ClearWalk(Vector3 from, Vector3 to)
        {
            if (data == null || !TrySnap(from, 3f, out Vector3 start)) return false;
            var filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = NavMesh.AllAreas };
            return !NavMesh.Raycast(start, to, out _, filter);
        }

        public void Dispose()
        {
            if (instance.valid) instance.Remove();
            data = null;
            Ready = false;
            if (hasSettings) NavMesh.RemoveSettings(settings.agentTypeID);
            hasSettings = false;
        }
    }
}
