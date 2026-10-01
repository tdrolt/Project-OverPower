using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// Every number behind the fog of war in one place: how far and wide a player sees, how dark the unseen world
    /// is, and how the centre scan behaves. It exists so Tudor can tune sight and the scan in the Inspector without
    /// touching code. Fields are [SerializeField] private with read-only properties, like the rest of the Data folder.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Vision Config", fileName = "VisionConfig")]
    public sealed class VisionConfig : ScriptableObject
    {
        [Header("Fog")]
        [Tooltip("Turns the whole vision system off: everyone sees everything, as before.")]
        [SerializeField] private bool fogEnabled = true;

        [Tooltip("The colour the unseen part of the arena is tinted. A lighter colour makes the fog look greyer.")]
        [SerializeField] private Color fogColour = new Color(0.102f, 0.110f, 0.133f, 1f);

        [Tooltip("How dark the unseen part of the arena is: 0 shows it fully, 1 hides it completely.")]
        [SerializeField, Range(0f, 1f)] private float fogDarkness = 0.5f;

        [Header("Sight")]
        [Tooltip("How wide the cone of sight toward your cursor is, in degrees. A wider cone shows more to the sides.")]
        [SerializeField, Range(1f, 360f)] private float coneAngleDegrees = 90f;

        [Tooltip("How far the cone of sight reaches, in metres. A longer cone lets you spot enemies from further away.")]
        [SerializeField, Min(0f)] private float coneLength = 22f;

        [Tooltip("How far you see in every direction around yourself, in metres, even behind you.")]
        [SerializeField, Min(0f)] private float circleRadius = 7f;

        [Tooltip("How high above the ground your eyes are, in metres. Sight is blocked by walls taller than this.")]
        [SerializeField, Min(0f)] private float eyeHeight = 1f;

        [Tooltip("How many metres of a wall face you see past its edge, so walls you look at do not look cut off.")]
        [SerializeField, Min(0f)] private float wallRevealDepth = 0.75f;

        [Tooltip("Metres between the spots checked along a line of sight. Smaller is more precise but costs more.")]
        [SerializeField, Min(0.1f)] private float lineSampleSpacing = 1f;

        [Tooltip("How many rays fan across the cone of sight (the circle around you uses the same spacing). More rays give smoother edges.")]
        [SerializeField, Min(8)] private int sightRayCount = 180;

        [Tooltip("Pixels along each side of the sight picture. Larger gives crisper fog edges and costs more memory. Read once when the match starts.")]
        [SerializeField, Range(64, 2048)] private int sightTextureSize = 512;

        [Header("Zones")]
        [Tooltip("When on, you always see everything about every zone (owner, capture, under attack). When off, you only learn about a zone while your team sees it, owns it, or your centre scan passes over it (a zone your team loses shows the loss); otherwise it stays as you last knew it.")]
        [SerializeField] private bool zoneOwnersVisibleWithoutSight = true;

        [Header("Minimap")]
        [Tooltip("The colour of enemy dots on the minimap. A different colour changes how enemies look on the map.")]
        [SerializeField] private Color minimapEnemyColour = new Color(0.898f, 0.282f, 0.302f, 1f);

        [Tooltip("How dark the minimap is drawn where your team cannot see. Higher hides the unseen map more.")]
        [SerializeField, Range(0f, 1f)] private float minimapFogDarkness = 0.6f;

        [Tooltip("How much lighter the parts of the minimap your team can see are drawn, so the lit holes read against the dark map")]
        [SerializeField, Range(0f, 1f)] private float minimapSeenLift = 0.2f;

        [Header("Centre scan")]
        [Tooltip("Seconds between centre scans. A shorter gap means zone states refresh more often. A new scan cuts off the previous wave, so a gap shorter than the wave's travel time (about 3 seconds at 40 m/s) means the outer arena is never scanned.")]
        [SerializeField, Min(5f)] private float scanIntervalSeconds = 30f;

        [Tooltip("How fast the scan wave spreads out from the centre, in metres per second. Faster means zones refresh sooner after a scan starts.")]
        [SerializeField, Min(1f)] private float scanWaveSpeed = 40f;

        [Tooltip("The colour of the scan wave as it crosses the arena.")]
        [SerializeField] private Color scanWaveColour = new Color(0.898f, 0.282f, 0.302f, 1f);

        [Tooltip("How thick the red ring on the ground is, in metres. Thicker is easier to see from the top-down camera.")]
        [SerializeField, Min(0.1f)] private float scanWaveWidth = 1.5f;

        [Tooltip("How many seconds the dots the scan reveals stay on the minimap. Longer keeps enemy positions visible for longer.")]
        [SerializeField, Min(0f)] private float scanDotSeconds = 4f;

        [Tooltip("How long the scan's red dots take to fade out at the end of Scan Dot Seconds.")]
        [SerializeField, Min(0f)] private float scanDotFadeSeconds = 1f;

        [Tooltip("Zones of this tier get their state refreshed by the centre scan.")]
        [SerializeField] private bool scanTier1 = true;

        [Tooltip("Zones of this tier get their state refreshed by the centre scan.")]
        [SerializeField] private bool scanTier2 = true;

        [Tooltip("Zones of this tier get their state refreshed by the centre scan.")]
        [SerializeField] private bool scanTier3 = true;

        [Tooltip("Zones of this tier get their state refreshed by the centre scan.")]
        [SerializeField] private bool scanTier4 = true;

        public bool FogEnabled => fogEnabled;
        public Color FogColour => fogColour;
        public float FogDarkness => fogDarkness;
        public float ConeAngleDegrees => coneAngleDegrees;
        public float ConeLength => coneLength;
        public float CircleRadius => circleRadius;
        public float EyeHeight => eyeHeight;
        public float WallRevealDepth => wallRevealDepth;
        public float LineSampleSpacing => lineSampleSpacing;
        public int SightRayCount => sightRayCount;
        public int SightTextureSize => sightTextureSize;
        public bool ZoneOwnersVisibleWithoutSight => zoneOwnersVisibleWithoutSight;
        public Color MinimapEnemyColour => minimapEnemyColour;
        public float MinimapFogDarkness => minimapFogDarkness;
        public float MinimapSeenLift => minimapSeenLift;
        public float ScanIntervalSeconds => scanIntervalSeconds;
        public float ScanWaveSpeed => scanWaveSpeed;
        public Color ScanWaveColour => scanWaveColour;
        public float ScanWaveWidth => scanWaveWidth;
        public float ScanDotSeconds => scanDotSeconds;
        public float ScanDotFadeSeconds => scanDotFadeSeconds;
        public bool ScanTier1 => scanTier1;
        public bool ScanTier2 => scanTier2;
        public bool ScanTier3 => scanTier3;
        public bool ScanTier4 => scanTier4;
    }
}
