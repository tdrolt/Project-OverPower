using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// The three Tier III health packs: how much they heal, how long they stay gone, how close you have to
    /// stand (where each pack stands is in ArenaLayout), and what they look like (their green is the UI Theme's healing
    /// green). One asset, shared by every pack.
    /// </summary>
    [CreateAssetMenu(menuName = "OverPower/Health Pack Config", fileName = "HealthPackConfig")]
    public sealed class HealthPackConfig : ScriptableObject
    {
        [Tooltip("Health a pack gives back when you walk over it hurt. You never go above your maximum health.")]
        [SerializeField, Min(0f)] private float healAmount = 50f;

        [Tooltip("Seconds a pack stays gone after someone takes it, before it comes back for everyone.")]
        [SerializeField, Min(1f)] private float respawnSeconds = 30f;

        [Tooltip("How close, in metres, your centre must be to the middle of the pack to take it.")]
        [SerializeField, Min(0.1f)] private float pickupRadius = 1.2f;

        [Tooltip("Extra metres of leeway the host allows when it checks you were standing on the pack, " +
                 "because it sees your position a moment late.")]
        [SerializeField, Min(0f)] private float hostSlackMetres = 1.5f;

        [Tooltip("If the host has not answered your request after this many seconds, you ask again.")]
        [SerializeField, Min(0.1f)] private float retrySeconds = 0.2f;

        [Tooltip("A player must be missing at least this much health to take a pack, so a sliver of damage does not use it up. " +
                 "0 = any damage at all. Full health never takes it.")]
        [SerializeField, Min(0f)] private float minimumMissingHealth = 10f;

        [Tooltip("How many times you ask again for a pack you are standing on before giving up until you step off it and back on.")]
        [SerializeField, Min(1)] private int maxAttemptsPerVisit = 6;

        [Tooltip("How long, in seconds, the host waits for the room to confirm the pack it just gave out before it trusts its " +
                 "own record again. Only matters if the confirmation never arrives.")]
        [SerializeField, Min(0.1f)] private float pendingWriteSeconds = 1.5f;

        [Tooltip("Colour of a pack's cross on the minimap while the pack is gone and waiting to come back. A ready pack shows there in " +
                 "the healing green of the UI Theme, like the pack itself.")]
        [SerializeField] private Color takenColour = new Color(0.5f, 0.5f, 0.5f, 1f);

        [Tooltip("How brightly the floating cross and the edge of its pad glow while the pack is ready to take. Above 1 they catch a " +
                 "halo from the bloom. The green itself is the healing green of the UI Theme.")]
        [SerializeField, Min(0f)] private float readyGlow = 1.6f;

        [Tooltip("How bright the cross and the edge of its pad stay while the pack is gone and waiting to come back, as a share of the " +
                 "healing green (0 = black, 1 = the full green, unglowing). Low, so a waiting pack reads as off, but high enough that " +
                 "players still see where it will come back on the darkest floor.")]
        [SerializeField, Range(0f, 1f)] private float respawningBrightness = 0.45f;

        [Tooltip("Colour of the flat hexagon pad on the floor under the cross: dark, like the arena's walls, so the green edge round it " +
                 "stands out. The pad reaches exactly as far as the pickup radius, so standing anywhere on it takes the pack.")]
        [SerializeField] private Color padColour = new Color(0.05f, 0.055f, 0.065f, 1f);

        [Tooltip("How thick the pad on the floor is, in metres: a low plate, never something to step up onto (it has no collider).")]
        [SerializeField, Min(0.01f)] private float padThicknessMetres = 0.08f;

        [Tooltip("How wide the glowing green edge round the pad is, in metres.")]
        [SerializeField, Min(0.01f)] private float padEdgeWidthMetres = 0.09f;

        [Tooltip("How strongly the inside of the pad fills with green, from 0 (edge only) to 1 (solid green). Even a few hundredths " +
                 "already turn the dark pad clearly green, so keep it at 0 for a dark pad with a glowing edge.")]
        [SerializeField, Range(0f, 1f)] private float padFillAlpha = 0f;

        [Tooltip("Size of the cross in metres, tip to tip.")]
        [SerializeField, Min(0.2f)] private float crossSizeMetres = 1.2f;

        [Tooltip("How high above the ground the middle of the cross floats, in metres.")]
        [SerializeField, Min(0f)] private float floatHeightMetres = 1.2f;

        [Tooltip("How far, in metres, the cross bobs up and down from its resting height.")]
        [SerializeField, Min(0f)] private float bobHeightMetres = 0.15f;

        [Tooltip("How many full up-and-down bobs the cross makes each second.")]
        [SerializeField, Min(0f)] private float bobCyclesPerSecond = 0.6f;

        [Tooltip("How far the cross leans back from standing straight up, in degrees. 0 = perfectly upright. The game camera " +
                 "looks almost straight down, so a fully upright cross is seen edge-on as a bar; leaning it back lets the plus " +
                 "sign show. The lean turns with the cross, so below about 60 the cross is edge-on to the camera for part of every turn " +
                 "and reads as a green bar.")]
        [SerializeField, Range(0f, 80f)] private float crossLeanDegrees = 65f;

        [Tooltip("How fast the cross turns, in degrees per second.")]
        [SerializeField] private float spinDegreesPerSecond = 60f;

        public float MinimumMissingHealth => minimumMissingHealth;
        public int MaxAttemptsPerVisit => maxAttemptsPerVisit;
        public float PendingWriteSeconds => pendingWriteSeconds;
        public float HealAmount => healAmount;
        public float RespawnSeconds => respawnSeconds;
        public int RespawnMs => Mathf.RoundToInt(respawnSeconds * 1000f);
        public float PickupRadius => pickupRadius;
        public float HostSlackMetres => hostSlackMetres;
        public float RetrySeconds => retrySeconds;
        public Color TakenColour => takenColour;
        public float ReadyGlow => readyGlow;
        public float RespawningBrightness => respawningBrightness;
        public Color PadColour => padColour;
        public float PadThicknessMetres => padThicknessMetres;
        public float PadEdgeWidthMetres => padEdgeWidthMetres;
        public float PadFillAlpha => padFillAlpha;
        public float CrossSizeMetres => crossSizeMetres;
        public float FloatHeightMetres => floatHeightMetres;
        public float BobHeightMetres => bobHeightMetres;
        public float BobCyclesPerSecond => bobCyclesPerSecond;
        public float CrossLeanDegrees => crossLeanDegrees;
        public float SpinDegreesPerSecond => spinDegreesPerSecond;
    }
}
