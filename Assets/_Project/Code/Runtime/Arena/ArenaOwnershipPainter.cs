using System.Collections.Generic;
using Overpower.Match;
using Overpower.UI;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Arena
{
    /// <summary>Paints who owns the ground into one small texture (one texel per floor hexagon) that the arena shader reads for the floor
    /// tiles and the wall stripes. It reads every zone each frame without allocating, but repaints only when OwnershipSignature says a
    /// zone's state changed or an event marked it; before the warm-up (the room's lobby, no room) the arena draws plain. The owner is the minimap's
    /// (ZoneOwnerShown), so the floor and the minimap always agree. A repaint that flips a zone's shown owner also starts a ripple
    /// (RippleRule). Lives on MatchSystems, so neither scene file holds it.</summary>
    public sealed class ArenaOwnershipPainter : MonoBehaviour
    {
        public const string TextureName = "Arena Owner Tiles";

        [Tooltip("The theme the floor takes its owner colours and its neutral line colour from, and through it the Arena Look (tile size and how far past the ring the paint reaches).")]
        [SerializeField] private UiTheme theme;

        private BuildingManager manager;
        private MatchDirector director;
        private int[] zoneIds;
        private ZoneState[] painted, now;
        private ZoneBand[] bands;
        private bool marked = true;
        private int seenKnowledgeVersion = -1;
        private int paintedLookKey;
        private float unregisteredSince = -1f;
        private bool warnedUnregistered;
        private Texture2D tiles;
        private Color32[] pixels;
        private TileReach[] reachMask;
        private Vector2Int maskMin, maskExtent;
        private Vector2 maskOrigin;
        private float maskSize;
        private ArenaBounds maskBounds;
        private bool bound;
        private readonly Ripple[] ripples = new Ripple[RippleRule.Slots];
        private readonly Vector4[] rippleShape = new Vector4[RippleRule.Slots];
        private readonly Vector4[] rippleColour = new Vector4[RippleRule.Slots];
        private bool ripplesOn;

        private void Awake()
        {
            if (theme == null || theme.arenaLook == null)
            {
                Debug.LogError($"[Arena] {name}: the ownership painter has no UiTheme or the theme no Arena Look - the floor will not show who owns the ground.");
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            Hook();
            bool live = director != null && director.IsLive;
            if (!OwnershipSignature.ShowsOwnerTiles(manager != null && manager.Current != null, live, MatchDirector.LobbyStageNow))
            {
                if (bound)
                {
                    ArenaShaderGlobals.ClearOwnerTiles();
                    bound = false;
                }
                OwnershipSignature.Forget(painted);
                RippleRule.StopAll(ripples);
                ripplesOn = false;
                unregisteredSince = -1f;
                return;
            }

            ZoneKnowledge knowledge = ZoneKnowledge.Instance;
            if (knowledge != null && knowledge.Version != seenKnowledgeVersion)
            {
                seenKnowledgeVersion = knowledge.Version;
                marked = true;
            }
            int lookKey = FloorTileRule.PaintLookKey(theme.arenaLook, theme);
            if (lookKey != paintedLookKey)
                marked = true;

            ReadZones();
            if (OwnershipSignature.NeedsRepaint(painted, now, zoneIds.Length, marked))
            {
                RippleRule.StartFlips(painted, now, zoneIds.Length, RippleRule.NumbersOf(theme.arenaLook), theme, ripples, Time.time);
                Repaint(lookKey);
            }
            else
                WarnIfAZoneNeverRegisters();
            StepRipples();
        }

        // Globals are only written while a ripple runs, plus once as the last one ends.
        private void StepRipples()
        {
            RippleNumbers numbers = RippleRule.NumbersOf(theme.arenaLook);
            bool running = RippleRule.Pack(ripples, Time.time, numbers, rippleShape, rippleColour);
            if (!running && !ripplesOn)
                return;
            ArenaShaderGlobals.SetRipples(rippleShape, rippleColour, numbers.RingWidth, running);
            ripplesOn = running;
        }

        // A tower missing from the scene (or destroyed) holds every repaint back without a word, so name the missing zones once.
        private void WarnIfAZoneNeverRegisters()
        {
            bool anyMissing = false;
            for (int i = 0; i < zoneIds.Length && !anyMissing; i++)
                anyMissing = !now[i].Registered;
            if (!anyMissing)
            {
                unregisteredSince = -1f;
                return;
            }
            if (warnedUnregistered)
                return;
            if (unregisteredSince < 0f)
                unregisteredSince = Time.unscaledTime;
            if (Time.unscaledTime - unregisteredSince < 10f)
                return;
            var missing = new List<int>();
            for (int i = 0; i < zoneIds.Length; i++)
                if (!now[i].Registered)
                    missing.Add(zoneIds[i]);
            warnedUnregistered = true;
            Debug.LogWarning($"[Arena] {name}: zones {string.Join(", ", missing)} have no registered tower after 10 s - the floor shows no owners until every zone has one.");
        }

        private void Hook()
        {
            BuildingManager m = BuildingManager.Instance;
            if (m != manager)
            {
                if (manager != null)
                    manager.OwnershipChanged -= HandleOwnershipChanged;
                manager = m;
                zoneIds = null;
                if (manager != null)
                {
                    manager.OwnershipChanged += HandleOwnershipChanged;
                    var ids = new List<int>(manager.TowerDictionary.Keys);
                    ids.Sort();
                    zoneIds = ids.ToArray();
                    painted = new ZoneState[zoneIds.Length];
                    now = new ZoneState[zoneIds.Length];
                    bands = new ZoneBand[zoneIds.Length];
                }
                marked = true;
            }

            // Out of play changes on the live edge with no ownership event (see MinimapView.TryBuild).
            MatchDirector d = MatchDirector.Instance;
            if (d != director)
            {
                if (director != null)
                    director.LiveStateChanged -= HandleLiveStateChanged;
                director = d;
                if (director != null)
                    director.LiveStateChanged += HandleLiveStateChanged;
                marked = true;
            }
        }

        private void ReadZones()
        {
            TerritorySnapshot snapshot = manager.Current;
            for (int i = 0; i < zoneIds.Length; i++)
            {
                int zone = zoneIds[i];
                ref ZoneState s = ref now[i];
                s.Registered = manager.TryGetZoneCentre(zone, out Vector3 centre);
                s.Centre = new Vector2(centre.x, centre.z);
                s.Radius = manager.CaptureRadiusOf(zone);
                s.OwnerShown = ZoneOwnerShown.Of(snapshot, zone);
                s.Spawn = manager.IsSpawnZone(zone);
                s.OutOfPlay = director != null && director.IsOutOfPlay(zone);
            }
        }

        private void Repaint(int lookKey)
        {
            TerritoryMap map = manager.Map;
            for (int i = 0; i < zoneIds.Length; i++)
            {
                int spawnTeam = map != null ? map.CapitalTeamOf(zoneIds[i]) : TerritoryMap.Neutral;
                bands[i] = new ZoneBand(now[i].Centre, now[i].Radius,
                    FloorTileRule.FloorPaint(now[i].OwnerShown, now[i].Spawn, spawnTeam, now[i].OutOfPlay));
            }
            ArenaSymmetry arena = ArenaSymmetry.Active;
            Vector2 origin = arena != null
                ? new Vector2(arena.centre.x, arena.centre.z)
                : (Vector2)Shader.GetGlobalVector(ArenaShaderGlobals.OwnerGridId);
            PaintBands(bands, origin);

            System.Array.Copy(now, painted, now.Length);
            marked = false;
            paintedLookKey = lookKey;
        }

        /// <summary>Paints these zones into the texture and hands it to the arena shader. The origin is the grid's (the arena's centre).</summary>
        public void PaintBands(ZoneBand[] zones, Vector2 origin)
        {
            ArenaLook look = theme.arenaLook;
            float size = FloorTileRule.TileSize(look);
            float band = FloorTileRule.BandMetres(look);
            TileRange(zones, band, origin, size, out Vector2Int min, out Vector2Int extent);

            int count = extent.x * extent.y;
            if (tiles == null)
                tiles = new Texture2D(extent.x, extent.y, TextureFormat.RGBA32, false, true)
                {
                    name = TextureName,
                    hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
            else if (tiles.width != extent.x || tiles.height != extent.y)
                tiles.Reinitialize(extent.x, extent.y);
            if (pixels == null || pixels.Length != count)
                pixels = new Color32[count];

            // Which tiles lie inside the arena only changes with the texture's layout, and the outline test is the costly part of a repaint.
            ArenaSymmetry symmetry = ArenaSymmetry.Active;
            ArenaBounds arena = symmetry != null ? symmetry.FullBounds : null;
            if (reachMask == null || reachMask.Length != count || min != maskMin || extent != maskExtent || origin != maskOrigin || size != maskSize || arena != maskBounds)
            {
                if (reachMask == null || reachMask.Length != count)
                    reachMask = new TileReach[count];
                ArenaMask(reachMask, min, extent, origin, size, arena);
                maskMin = min;
                maskExtent = extent;
                maskOrigin = origin;
                maskSize = size;
                maskBounds = arena;
            }
            PaintTiles(pixels, min, extent, origin, size, zones, band, look, theme, reachMask);
            tiles.SetPixels32(pixels);
            tiles.Apply(false);
            ArenaShaderGlobals.SetOwnerTiles(tiles, new Vector4(origin.x, origin.y, size, 0f), new Vector4(min.x, min.y, extent.x, extent.y));
            bound = true;
        }

        /// <summary>The axial tiles (lowest q, r and how many of each) covering every zone's band, one tile of margin for rounding.</summary>
        public static void TileRange(ZoneBand[] zones, float band, Vector2 origin, float size,
                                     out Vector2Int min, out Vector2Int extent)
        {
            if (zones.Length == 0)
            {
                min = Vector2Int.zero;
                extent = Vector2Int.one;
                return;
            }
            var lo = new Vector2(float.MaxValue, float.MaxValue);
            var hi = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < zones.Length; i++)
            {
                float reach = zones[i].Radius + band;
                lo = Vector2.Min(lo, zones[i].Centre - new Vector2(reach, reach));
                hi = Vector2.Max(hi, zones[i].Centre + new Vector2(reach, reach));
            }
            // q and r are linear in x and z, so over the box they peak at its corners.
            Vector2Int a = HexGrid.TileAt(lo, origin, size), b = HexGrid.TileAt(new Vector2(hi.x, lo.y), origin, size);
            Vector2Int c = HexGrid.TileAt(new Vector2(lo.x, hi.y), origin, size), d = HexGrid.TileAt(hi, origin, size);
            Vector2Int low = Vector2Int.Min(Vector2Int.Min(a, b), Vector2Int.Min(c, d)) - Vector2Int.one;
            Vector2Int high = Vector2Int.Max(Vector2Int.Max(a, b), Vector2Int.Max(c, d)) + Vector2Int.one;
            min = low;
            extent = high - low + Vector2Int.one;
        }

        private static readonly Color32 PlainTexel = OwnerTileTexel.Encode(new TileLook(TileState.Plain, Color.black, 0f));

        /// <summary>Per texel, who may read its tile's owner (FloorTileRule.ReachOf: the floor inside the outline, the walls one tile past it).</summary>
        public static void ArenaMask(TileReach[] mask, Vector2Int min, Vector2Int extent, Vector2 origin, float size, ArenaBounds arena)
        {
            for (int y = 0; y < extent.y; y++)
                for (int x = 0; x < extent.x; x++)
                    mask[y * extent.x + x] = FloorTileRule.ReachOf(arena, HexGrid.CentreOf(new Vector2Int(min.x + x, min.y + y), origin, size), size);
        }

        /// <summary>Every texel through FloorTileRule.LookAt at its tile's centre, limited by the mask (null: floor and walls everywhere);
        /// texel (x, y) is tile (min.x + x, min.y + y).</summary>
        public static void PaintTiles(Color32[] pixels, Vector2Int min, Vector2Int extent, Vector2 origin, float size,
                                      ZoneBand[] zones, float band, ArenaLook look, UiTheme theme, TileReach[] reachMask)
        {
            for (int y = 0; y < extent.y; y++)
                for (int x = 0; x < extent.x; x++)
                {
                    int i = y * extent.x + x;
                    TileReach reach = reachMask != null ? reachMask[i] : TileReach.FloorAndWalls;
                    pixels[i] = reach == TileReach.None
                        ? PlainTexel
                        : OwnerTileTexel.Encode(FloorTileRule.LookAt(HexGrid.CentreOf(new Vector2Int(min.x + x, min.y + y), origin, size), zones, band, look, theme, reach));
                }
        }

        private void HandleOwnershipChanged(int zone, int oldOwner, int newOwner, TerritorySnapshot snapshot) => marked = true;

        private void HandleLiveStateChanged() => marked = true;

        private void OnDestroy()
        {
            if (manager != null)
                manager.OwnershipChanged -= HandleOwnershipChanged;
            if (director != null)
                director.LiveStateChanged -= HandleLiveStateChanged;
            if (tiles == null)
                return;
            if (Shader.GetGlobalTexture(ArenaShaderGlobals.OwnerTexId) == tiles)
                ArenaShaderGlobals.ClearOwnerTiles();
            if (Application.isPlaying) Destroy(tiles);
            else DestroyImmediate(tiles);
            tiles = null;
        }

        /// <summary>With domain reload on, a texture alive when Play Mode stopped lost its owner: free it by name.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void FreeLeftovers()
        {
#if UNITY_EDITOR
            foreach (Texture2D leftover in Resources.FindObjectsOfTypeAll<Texture2D>())
                if (leftover != null && leftover.name == TextureName && (leftover.hideFlags & HideFlags.DontSave) != 0 && !UnityEditor.EditorUtility.IsPersistent(leftover))
                    DestroyImmediate(leftover);
#endif
        }
    }
}
