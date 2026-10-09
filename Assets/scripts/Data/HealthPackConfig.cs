using UnityEngine;

namespace Overpower.Data
{
    /// <summary>
    /// The three Tier III health packs: how much they heal, how long they stay gone, how close you have to
    /// stand (where each pack stands is in ArenaLayout), and what they look like. One asset, shared by every pack.
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

        [Tooltip("How high, in metres, the glow disc sits above the ground, just enough to not flicker against it.")]
        [SerializeField, Min(0f)] private float glowHeightMetres = 0.05f;

        [Tooltip("Colour of the floating cross while the pack is ready to take.")]
        [SerializeField] private Color readyColour = new Color(0.2f, 0.9f, 0.3f, 1f);

        [Tooltip("Colour of the soft glow on the ground under the cross while the pack is ready. Turned off while the pack is gone.")]
        [SerializeField] private Color glowColour = new Color(0.2f, 0.9f, 0.3f, 0.35f);

        [Tooltip("Colour of the cross while the pack is gone and waiting to come back. It stays in place, greyed out, with no glow.")]
        [SerializeField] private Color takenColour = new Color(0.5f, 0.5f, 0.5f, 1f);

        [Tooltip("Size of the cross in metres, tip to tip.")]
        [SerializeField, Min(0.2f)] private float crossSizeMetres = 1.2f;

        [Tooltip("Diameter in metres of the glow on the ground.")]
        [SerializeField, Min(0.2f)] private float glowDiameterMetres = 2.4f;

        [Tooltip("How high above the ground the middle of the cross floats, in metres.")]
        [SerializeField, Min(0f)] private float floatHeightMetres = 1.2f;

        [Tooltip("How far, in metres, the cross bobs up and down from its resting height.")]
        [SerializeField, Min(0f)] private float bobHeightMetres = 0.15f;

        [Tooltip("How many full up-and-down bobs the cross makes each second.")]
        [SerializeField, Min(0f)] private float bobCyclesPerSecond = 0.6f;

        [Tooltip("How far the cross leans back from standing straight up, in degrees. 0 = perfectly upright. The game camera " +
                 "looks almost straight down, so a fully upright cross is seen edge-on as a bar; leaning it back lets the plus " +
                 "sign show. The lean turns with the cross.")]
        [SerializeField, Range(0f, 80f)] private float crossLeanDegrees = 35f;

        [Tooltip("How fast the cross turns, in degrees per second.")]
        [SerializeField] private float spinDegreesPerSecond = 60f;

        public float MinimumMissingHealth => minimumMissingHealth;
        public int MaxAttemptsPerVisit => maxAttemptsPerVisit;
        public float PendingWriteSeconds => pendingWriteSeconds;
        public float GlowHeightMetres => glowHeightMetres;
        public float HealAmount => healAmount;
        public float RespawnSeconds => respawnSeconds;
        public int RespawnMs => Mathf.RoundToInt(respawnSeconds * 1000f);
        public float PickupRadius => pickupRadius;
        public float HostSlackMetres => hostSlackMetres;
        public float RetrySeconds => retrySeconds;
        public Color ReadyColour => readyColour;
        public Color GlowColour => glowColour;
        public Color TakenColour => takenColour;
        public float CrossSizeMetres => crossSizeMetres;
        public float GlowDiameterMetres => glowDiameterMetres;
        public float FloatHeightMetres => floatHeightMetres;
        public float BobHeightMetres => bobHeightMetres;
        public float BobCyclesPerSecond => bobCyclesPerSecond;
        public float CrossLeanDegrees => crossLeanDegrees;
        public float SpinDegreesPerSecond => spinDegreesPerSecond;
    }
}
