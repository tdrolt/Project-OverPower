using System;
using System.Collections.Generic;
using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// The one home for the primitive arena's cover and barriers (arena step 4). The boundary walls themselves are
    /// NOT stored here: they are built fresh from ArenaSymmetry's own Source Outline every time (ArenaWallPlan), so
    /// the wall line and the "no gaps at the corners" rule can never drift apart from what Validate already checks.
    /// What lives here is everything else a designer might want to move: a block's box (a captured house, crate,
    /// rock or bush) and a barrier's row (Amendment 1's jersey barriers). A designer edits rows in the Inspector, or
    /// moves a block in the Scene view and presses "Capture layout from Source", then "Build primitive arena".
    ///
    /// Fields are [SerializeField] private with read-only accessors for the same reason as the rest of the Data
    /// folder (see GameplayConfig): a ScriptableObject is one shared instance per process, so writing to one at
    /// runtime quietly edits the asset in the Editor and does nothing in a build.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Arena Layout", fileName = "ArenaLayout")]
    public sealed class ArenaLayout : ScriptableObject
    {
        public enum PieceKind
        {
            /// <summary>A captured house, crate, rock or bush footprint: a plain Building-layer block.</summary>
            Block,

            /// <summary>Amendment 1's jersey barrier: GDD p.29, blocks walking only - every shot, dash, zip pull,
            /// blink and portal crosses it. Sits on its own Barrier layer, never Building.</summary>
            Barrier
        }

        [Serializable]
        public struct Piece
        {
            [Tooltip("For the designer's own reference only; never read by code.")]
            public string name;

            [Tooltip("Block = a plain wall-like footprint (a captured house, crate, rock or bush). Barrier = a " +
                     "jersey barrier: blocks walking, every shot and every movement ability crosses it.")]
            public PieceKind kind;

            [Tooltip("World position in the Source (Team 2) third, in metres.")]
            public Vector3 centre;

            [Tooltip("Turn about the vertical axis, in degrees (a Unity Y-Euler yaw).")]
            public float yawDegrees;

            [Tooltip("A Block's box size in world metres (x = along local X, y = height, z = along local Z). " +
                     "For a Barrier: x = length; y = how tall it looks (1.0 m, waist height: you see and shoot " +
                     "over it, but the blocking band below is set separately and reaches further than the look); " +
                     "z = thickness (0.6 m - never under 0.4 m, since walking is an unswept 0.1 m step and a " +
                     "thinner barrier could let a step land past it in one frame).")]
            public Vector3 size;
        }

        [Tooltip("Every captured block and barrier row, in the Source (Team 2) third. The boundary walls are not " +
                 "here - they are always built fresh from Arena Symmetry's Source Outline.")]
        [SerializeField] private List<Piece> pieces = new List<Piece>();

        [Header("Boundary walls (built from the outline, not captured)")]
        [Tooltip("The material every boundary wall box uses.")]
        [SerializeField] private Material wallMaterial;

        [Tooltip("How thick a boundary wall box is (its footprint's short side), in metres. Today's captured walls " +
                 "measure 0.724 m; kept the same so the play space doesn't shrink or grow.")]
        [SerializeField, Min(0.1f)] private float wallThickness = 0.724f;

        [Tooltip("A boundary wall box's bottom, in world Y metres. Buried well below the floor (floor top is Y 0) " +
                 "so there is never a crack under a wall, whatever the floor's own thickness.")]
        [SerializeField] private float wallBottomY = -1f;

        [Tooltip("A boundary wall box's top, in world Y metres. Matches today's wall height (6.036 m from its old " +
                 "0.06-6.04 m span), so the camera sees no more or less of what's behind a wall than it does today.")]
        [SerializeField] private float wallTopY = 6.036f;

        [Header("Blocks (captured, box for box)")]
        [Tooltip("The material every Block box uses.")]
        [SerializeField] private Material blockMaterial;

        [Header("Barriers (Amendment 1: jersey barriers)")]
        [Tooltip("The material every Barrier box's LOOK uses (the part a player sees and shoots over).")]
        [SerializeField] private Material barrierMaterial;

        [Tooltip("A barrier's BLOCKING collider bottom, in world Y metres. Reaches well below the floor, same " +
                 "reasoning as a wall: only a living player's body is ever on this layer, so extra height costs " +
                 "nothing and a body can never be lifted onto the barrier by standing on its own collider's edge.")]
        [SerializeField] private float barrierBlockingBottomY = -1f;

        [Tooltip("A barrier's BLOCKING collider top, in world Y metres - well above the 1.0 m look, so nobody can " +
                 "be knocked or dashed up onto the barrier and stand on it like a platform.")]
        [SerializeField] private float barrierBlockingTopY = 3f;

        [Header("Floor")]
        [Tooltip("The material the arena's single floor slab uses.")]
        [SerializeField] private Material floorMaterial;

        [Tooltip("The floor slab's width and depth in world metres, centred on Arena Symmetry's centre.")]
        [SerializeField] private Vector2 floorSize = new Vector2(220f, 220f);

        [Tooltip("The floor slab's thickness in world metres, with its TOP at world Y 0. Thick, so a player " +
                 "spawned a little into the floor is always pushed up, never through.")]
        [SerializeField, Min(0.1f)] private float floorThickness = 4f;

        public IReadOnlyList<Piece> Pieces => pieces;

        public Material WallMaterial => wallMaterial;
        public float WallThickness => wallThickness;
        public float WallBottomY => wallBottomY;
        public float WallTopY => wallTopY;

        public Material BlockMaterial => blockMaterial;

        public Material BarrierMaterial => barrierMaterial;
        public float BarrierBlockingBottomY => barrierBlockingBottomY;
        public float BarrierBlockingTopY => barrierBlockingTopY;

        public Material FloorMaterial => floorMaterial;
        public Vector2 FloorSize => floorSize;
        public float FloorThickness => floorThickness;
    }
}
