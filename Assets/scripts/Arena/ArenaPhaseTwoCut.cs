using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using Overpower.Abilities;
using Overpower.Data;
using Overpower.Match;

namespace Overpower.Arena
{
    /// <summary>
    /// Builds and removes the phase-two wall (GDD p.20-21 and p.27; Tudor, 2026-09-25) on every client from one
    /// replicated number, MatchDirector.CutTeam - so a late joiner and a new master get the same wall with no message
    /// of their own. Added at play by ArenaSymmetry.OnEnable: no scene object.
    ///
    /// On a cut: builds the wall boxes and the recess barrier as primitives with the outer walls' own material,
    /// thickness, height and layers (under Source/Boundry, so a portal's path check sees the wall); publishes the smaller
    /// outline (ArenaSymmetry.UsePlayableBounds) for blink, portals and the safety net; hides every block, barrier and
    /// scenery piece whose position is behind the wall; and destroys what this client placed there. Towers behind the
    /// wall hide themselves (BuildingCapture, from MatchDirector.IsOutOfPlay). On "no cut" (a new room), everything is
    /// put back exactly as it was.
    ///
    /// Polls once a frame instead of subscribing: the cut can appear on a join, a knockout or a host start and go away
    /// on leaving the room, and MatchDirector, BuildingManager and the towers start in no fixed order - one integer
    /// compare a frame catches every case.
    ///
    /// Review fix F5, 2026-09-25: while a cut stands, also checks THIS client's own player - once the frame it lands,
    /// then every half second - and sends them home if they are alive and behind the wall (a reconnect, a late join
    /// spawned into a corner someone else's knockout just closed, or a living player the phase-change trip home
    /// missed). Own client only; see ReturnOwnPlayerIfBehindWall.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaPhaseTwoCut : MonoBehaviour
    {
        public const string GroupName = "Phase Two Cut";
        private static readonly string[] HiddenGroups =
            { ArenaSymmetry.BlocksGroupName, ArenaSymmetry.BarriersGroupName, ArenaSymmetry.SceneryGroupName };

        /// <summary>The running arena's cut, or null outside Play Mode. The minimap reads Geometry from it.</summary>
        public static ArenaPhaseTwoCut Active { get; private set; }

        /// <summary>The team whose corner this client has closed, or PhaseTwoCutRules.NoCut.</summary>
        public int AppliedCutTeam { get; private set; } = PhaseTwoCutRules.NoCut;

        /// <summary>The standing wall's shape, or null while nothing is cut.</summary>
        public PhaseTwoCutGeometry Geometry { get; private set; }

        private ArenaSymmetry arena;
        private Transform built;
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private readonly List<Collider> hiddenColliders = new List<Collider>();
        private int failedCutTeam = PhaseTwoCutRules.NoCut; // logs a refused build once, not every frame

        // Extra step E2 (Task 4 review, 2026-09-25): a REAL refusal (failedCutTeam == the cut team - the capital's
        // tower IS registered, but the layout/geometry itself doesn't fit) can't fix itself between one frame and the
        // next, so recomputing every frame just burns CPU on the same answer. The TRANSIENT case (towers not
        // registered yet, so BuildGeometryFor returns null without ever calling LogRefusalOnce) is unaffected and
        // keeps retrying every frame - it resolves itself within a frame or two of scene load.
        private const float RefusedRetryIntervalSeconds = 1f;
        private float nextRetryTime;

        // Review fix F5, 2026-09-25 (the review's plan gap): a player can end up behind the wall in ways the
        // phase-change trip home (PlayerLifecycle.ReturnToSpawnForPhaseChange, fired once off MatchDirector's own
        // ThreeTeams -> TwoTeams edge) never touches - a reconnect or late join whose own D5 alternative spawn
        // lands inside a corner someone ELSE's knockout just closed, or a living player that trip home simply
        // missed. The out-of-arena safety net alone would only catch this once it next remembers a "safe" spot,
        // which can itself be behind the wall - so this checks THIS client's own player directly: once the frame
        // a cut lands, then every half second while one still stands.
        private const float OwnPlayerCheckIntervalSeconds = 0.5f;
        private float nextOwnPlayerCheck;

        private void OnEnable()
        {
            arena = GetComponent<ArenaSymmetry>();
            Active = this;
        }

        private void OnDisable()
        {
            Restore();
            AppliedCutTeam = PhaseTwoCutRules.NoCut;
            if (Active == this)
                Active = null;
        }

        private void Update()
        {
            int cut = MatchDirector.Instance != null ? MatchDirector.Instance.CutTeam : PhaseTwoCutRules.NoCut;
            if (cut != AppliedCutTeam)
                ApplyCutChange(cut);

            // F5: own client only - never move another player, which this component has no authority to do.
            if (AppliedCutTeam >= 0 && Geometry != null && Time.unscaledTime >= nextOwnPlayerCheck)
            {
                nextOwnPlayerCheck = Time.unscaledTime + OwnPlayerCheckIntervalSeconds;
                ReturnOwnPlayerIfBehindWall();
            }
        }

        private void ApplyCutChange(int cut)
        {
            if (cut < 0)
            {
                Restore();
                AppliedCutTeam = PhaseTwoCutRules.NoCut;
                return;
            }

            // E2: a real refusal for this same team waits out its cooldown instead of recomputing every frame - see
            // the field comments above.
            if (cut == failedCutTeam && Time.unscaledTime < nextRetryTime)
                return;

            PhaseTwoCutGeometry geometry = BuildGeometryFor(cut);
            if (geometry == null)
            {
                if (cut == failedCutTeam)
                    nextRetryTime = Time.unscaledTime + RefusedRetryIntervalSeconds;
                return; // towers not registered yet (try again next frame), or refused (wait out the cooldown above)
            }

            Restore();
            Apply(geometry);
            AppliedCutTeam = cut;
            nextOwnPlayerCheck = Time.unscaledTime; // F5: check the own player this same frame, not 0.5s later
            Debug.Log($"[Arena] phase two: team {cut}'s corner closed ({geometry.WallRuns.Count} wall boxes, " +
                      $"{hiddenRenderers.Count} renderers hidden).");
        }

        /// <summary>F5: this client's own player only, found the way MatchDirector.ReactToRoomState finds it. Moves
        /// them the same way the phase-change trip home does (SpawnCapitalFor, in-play only since review fix F1) if
        /// they are alive and standing behind the wall - the safety net alone could loop a player back to a spot it
        /// remembers as safe that is now behind it.</summary>
        private void ReturnOwnPlayerIfBehindWall()
        {
            PhotonView localView = PhotonNetwork.LocalPlayer != null
                ? PlayerLookup.GetPhotonViewFor(PhotonNetwork.LocalPlayer.ActorNumber) : null;
            PlayerLifecycle lifecycle = localView != null ? localView.GetComponent<PlayerLifecycle>() : null;
            if (lifecycle == null || !lifecycle.IsAlive || !Geometry.IsBehindWall(localView.transform.position))
                return;

            Debug.Log($"[Arena] phase two: {localView.Owner?.NickName} was behind the wall - sent home.");
            lifecycle.ReturnToSpawnForPhaseChange();
        }

        private PhaseTwoCutGeometry BuildGeometryFor(int cutTeam)
        {
            BuildingManager buildings = BuildingManager.Instance;
            if (arena == null || arena.FullBounds == null || buildings == null || buildings.Map == null)
                return null;
            int capital = buildings.Map.CapitalOf(cutTeam);
            if (capital < 0 || !buildings.TryGetZoneCentre(capital, out Vector3 capitalPosition))
                return null;

            ArenaLayout layout = arena.layout;
            if (layout == null)
            {
                LogRefusalOnce(cutTeam, "Arena Symmetry has no Layout assigned");
                return null;
            }

            var centre = new Vector2(arena.centre.x, arena.centre.z);
            PhaseTwoCutGeometry geometry = PhaseTwoCutGeometry.Build(arena.FullBounds.Polygon, centre,
                new Vector2(capitalPosition.x, capitalPosition.z) - centre, layout.PhaseTwoWallDistance,
                layout.PhaseTwoRecessWidth, layout.PhaseTwoRecessDepth, layout.WallThickness);
            if (geometry == null)
                LogRefusalOnce(cutTeam, "the Phase Two Cut values don't fit this arena (the wall must cross it once, and " +
                                        "the recess must fit between the outer walls)");
            return geometry;
        }

        private void LogRefusalOnce(int cutTeam, string why)
        {
            if (failedCutTeam == cutTeam)
                return;
            failedCutTeam = cutTeam;
            Debug.LogError($"[Arena] Can't build the phase-two wall for team {cutTeam}: {why}. The arena stays whole.");
        }

        private void Apply(PhaseTwoCutGeometry geometry)
        {
            Geometry = geometry;
            failedCutTeam = PhaseTwoCutRules.NoCut;
            ArenaLayout layout = arena.layout;

            Transform boundry = arena.source != null ? arena.source.Find(ArenaSymmetry.BoundaryGroupName) : null;
            if (boundry == null)
                Debug.LogWarning("[Arena] No Source/Boundry group: the phase-two wall is built, but a portal's path " +
                                 "check won't see it.");
            built = new GameObject(GroupName).transform;
            built.SetParent(boundry != null ? boundry : transform, false);

            float wallCentreY = (layout.WallBottomY + layout.WallTopY) * 0.5f;
            float wallHeight = layout.WallTopY - layout.WallBottomY;
            int buildingLayer = LayerMask.NameToLayer("Building");
            for (int i = 0; i < geometry.WallRuns.Count; i++)
            {
                ArenaWallPlan.Run run = geometry.WallRuns[i];
                Vector2 centreXZ = run.Centre(layout.WallThickness);
                GameObject wall = NewBox($"Phase Two Wall {i}", layout.WallMaterial, buildingLayer);
                wall.transform.SetPositionAndRotation(new Vector3(centreXZ.x, wallCentreY, centreXZ.y),
                    Quaternion.Euler(0f, run.UnityYawDegrees, 0f));
                wall.transform.localScale = new Vector3(run.Length, wallHeight, layout.WallThickness);
            }

            Vector3 barrierSize = layout.PhaseTwoRecessBarrierSize;
            if (geometry.HasRecess && barrierSize.x > 0f && barrierSize.y > 0f && barrierSize.z > 0f)
            {
                // Lifted by half its look, like every other barrier (ArenaPrimitiveBuilder): the look sits on the floor.
                var position = new Vector3(geometry.BarrierCentre.x, barrierSize.y * 0.5f, geometry.BarrierCentre.y);
                GameObject barrier = NewBox("Phase Two Recess Barrier", layout.BarrierMaterial,
                                            LayerMask.NameToLayer(ArenaLayers.BarrierLayerName));
                barrier.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, geometry.BarrierYawDegrees, 0f));
                barrier.transform.localScale = barrierSize;
                ArenaPieceShapes.BarrierBlockingBox(layout.BarrierBlockingBottomY, layout.BarrierBlockingTopY,
                    position.y, barrierSize.y, out Vector3 boxCentre, out Vector3 boxSize);
                BoxCollider box = barrier.GetComponent<BoxCollider>();
                box.center = boxCentre;
                box.size = boxSize;
            }

            arena.UsePlayableBounds(geometry.Playable);
            HidePiecesBehind(geometry);
            NetworkedDeployable.DestroyOwnedWhere(geometry.IsBehindWall);
        }

        private GameObject NewBox(string boxName, Material material, int layer)
        {
            var go = new GameObject(boxName);
            go.transform.SetParent(built, false);
            go.layer = layer;
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.AddComponent<BoxCollider>();
            return go;
        }

        private void HidePiecesBehind(PhaseTwoCutGeometry geometry)
        {
            foreach (Transform third in new[] { arena.source, arena.generated120, arena.generated240 })
            {
                if (third == null)
                    continue;
                foreach (string groupName in HiddenGroups)
                {
                    Transform group = third.Find(groupName);
                    if (group == null)
                        continue;
                    foreach (Transform piece in group)
                    {
                        if (!geometry.IsBehindWall(piece.position))
                            continue;
                        foreach (Renderer r in piece.GetComponentsInChildren<Renderer>())
                            if (r.enabled) { r.enabled = false; hiddenRenderers.Add(r); }
                        foreach (Collider c in piece.GetComponentsInChildren<Collider>())
                            if (c.enabled) { c.enabled = false; hiddenColliders.Add(c); }
                    }
                }
            }
        }

        private void Restore()
        {
            if (built != null)
            {
                Destroy(built.gameObject);
                built = null;
            }
            foreach (Renderer r in hiddenRenderers)
                if (r != null) r.enabled = true;
            foreach (Collider c in hiddenColliders)
                if (c != null) c.enabled = true;
            hiddenRenderers.Clear();
            hiddenColliders.Clear();
            if (arena != null)
                arena.UsePlayableBounds(null);
            Geometry = null;
        }
    }
}
