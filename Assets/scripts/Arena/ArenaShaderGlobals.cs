using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>What the arena surface shader reads from ArenaLook, colours already in linear space.</summary>
    public struct ArenaGlobals
    {
        public float TileSize, FloorLineWidth, FloorGridGlow, OwnedEdgeGlow, OwnedFillGlow;
        public float StripeGlow, OutsideStripeGlow, CellSize, StripeWidth, NodeSize, TopEdgeWidth, AcrossChance, UpChance;
        public float Reflection;
        public Vector4 OutsideLine;
        /// <summary>Floor, wall, block, barrier: the order of the shader's surface index.</summary>
        public Vector4[] SurfaceBase;
        public float[] SurfaceGloss;
        public float[] SurfaceShine;
    }

    /// <summary>Hands the arena look to the "Overpower/Arena Surface" shader as shader globals only. The arena is Batching Static
    /// and shares four materials, so a property block would never show and a material edit would dirty a shared file; globals
    /// sit outside UnityPerMaterial, so the SRP Batcher keeps batching. The owner tiles (painted elsewhere) arrive through
    /// SetOwnerTiles; until then, and after ClearOwnerTiles, the shader draws the plain look and never samples them.</summary>
    public static class ArenaShaderGlobals
    {
        public static readonly int OwnerGridId = Shader.PropertyToID("_ArenaOwnerGrid");
        public static readonly int OwnerMinId = Shader.PropertyToID("_ArenaOwnerMin");
        public static readonly int OwnerTexId = Shader.PropertyToID("_ArenaOwnerTex");
        public static readonly int ReflectionId = Shader.PropertyToID("_ArenaReflection");
        public static readonly int LookReadyId = Shader.PropertyToID("_ArenaLookReady");
        public static readonly int SurfaceBaseId = Shader.PropertyToID("_ArenaSurfaceBase");
        public static readonly int SurfaceGlossId = Shader.PropertyToID("_ArenaSurfaceGloss");
        public static readonly int SurfaceShineId = Shader.PropertyToID("_ArenaSurfaceShine");
        public static readonly int FloorLineId = Shader.PropertyToID("_ArenaFloorLine");
        public static readonly int StripeId = Shader.PropertyToID("_ArenaStripe");
        public static readonly int StripeGlowId = Shader.PropertyToID("_ArenaStripeGlow");
        public static readonly int OutsideLineId = Shader.PropertyToID("_ArenaOutsideLine");

        // x, y: grid origin (world x, z); z: tile size; w: 1 while painted owner tiles are bound. Kept here so each setter changes only its part.
        private static Vector4 ownerGrid = new Vector4(0f, 0f, 3f, 0f);

        public static ArenaGlobals From(ArenaLook look) => new ArenaGlobals
        {
            TileSize = look.floorTileSize,
            FloorLineWidth = look.floorLineWidth,
            FloorGridGlow = look.floorGridGlow,
            OwnedEdgeGlow = look.ownedEdgeGlow,
            OwnedFillGlow = look.ownedFillGlow,
            StripeGlow = look.stripeGlow,
            OutsideStripeGlow = look.outsideStripeGlow,
            CellSize = look.circuitCellSize,
            StripeWidth = look.stripeWidth,
            NodeSize = look.nodeSize,
            TopEdgeWidth = look.topEdgeWidth,
            AcrossChance = look.circuitAcrossChance,
            UpChance = look.circuitUpChance,
            Reflection = look.reflectionStrength,
            OutsideLine = look.outsideLineColor.linear,
            SurfaceBase = new Vector4[]
            {
                look.floorSurface.baseTint.linear, look.wallSurface.baseTint.linear,
                look.blockSurface.baseTint.linear, look.barrierSurface.baseTint.linear,
            },
            SurfaceGloss = new[] { look.floorSurface.gloss, look.wallSurface.gloss, look.blockSurface.gloss, look.barrierSurface.gloss },
            SurfaceShine = new[] { look.floorSurface.sunShine, look.wallSurface.sunShine, look.blockSurface.sunShine, look.barrierSurface.sunShine },
        };

        public static void Push(ArenaLook look)
        {
            if (look == null)
                return;
            ArenaGlobals g = From(look);
            Shader.SetGlobalVectorArray(SurfaceBaseId, g.SurfaceBase);
            Shader.SetGlobalFloatArray(SurfaceGlossId, g.SurfaceGloss);
            Shader.SetGlobalFloatArray(SurfaceShineId, g.SurfaceShine);
            Shader.SetGlobalVector(FloorLineId, new Vector4(g.FloorLineWidth, g.FloorGridGlow, g.OwnedEdgeGlow, g.OwnedFillGlow));
            Shader.SetGlobalVector(StripeId, new Vector4(g.CellSize, g.StripeWidth, g.NodeSize, g.TopEdgeWidth));
            Shader.SetGlobalVector(StripeGlowId, new Vector4(g.OutsideStripeGlow, g.StripeGlow, g.AcrossChance, g.UpChance));
            Shader.SetGlobalVector(OutsideLineId, g.OutsideLine);
            Shader.SetGlobalFloat(ReflectionId, g.Reflection);
            ownerGrid.z = Mathf.Max(0.01f, g.TileSize);
            Shader.SetGlobalVector(OwnerGridId, ownerGrid);
            Shader.SetGlobalFloat(LookReadyId, 1f);
        }

        /// <summary>The tile at the origin is centred on it; the arena's centre keeps the three thirds' tiles identical.</summary>
        public static void SetGridOrigin(Vector2 origin)
        {
            ownerGrid.x = origin.x;
            ownerGrid.y = origin.y;
            Shader.SetGlobalVector(OwnerGridId, ownerGrid);
        }

        /// <summary>grid = (origin x, origin z, tile size, unused); min = (lowest q, lowest r, width, height) of the texture.</summary>
        public static void SetOwnerTiles(Texture2D tiles, Vector4 grid, Vector4 min)
        {
            ownerGrid = new Vector4(grid.x, grid.y, grid.z, tiles != null ? 1f : 0f);
            Shader.SetGlobalTexture(OwnerTexId, tiles != null ? tiles : Texture2D.blackTexture);
            Shader.SetGlobalVector(OwnerMinId, min);
            Shader.SetGlobalVector(OwnerGridId, ownerGrid);
        }

        public static void ClearOwnerTiles()
        {
            ownerGrid.w = 0f;
            Shader.SetGlobalTexture(OwnerTexId, Texture2D.blackTexture);
            Shader.SetGlobalVector(OwnerMinId, Vector4.zero);
            Shader.SetGlobalVector(OwnerGridId, ownerGrid);
        }
    }
}
