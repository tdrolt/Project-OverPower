using UnityEngine;
using Overpower.Arena;
using Overpower.Combat;
using Overpower.Vision;
using Overpower.Match;

namespace Overpower.Abilities
{
    /// <summary>
    /// An instant reposition toward the cursor: lands EXACTLY on the cursor point when within range, otherwise `range` metres
    /// toward it [C]. It never travels the space in between, so only the destination has to be standable and inside the
    /// arena outline. The destination search is pure logic in BlinkDestinationSearch; this class supplies the physics half
    /// (the probe, using the caster's own capsule; ground via GroundProbe.TryFindGround, shared with Teleport's placement).
    /// The jump itself belongs to PlayerDisplacement: this module only decides WHERE it goes and whether it may happen.
    /// </summary>
    public sealed class BlinkAbility : AbilityModule
    {
        [Header("Range")]
        [SerializeField, Tooltip("How far a blink can reach, in metres, measured horizontally from " +
                 "the player. Lands exactly on the cursor when the cursor is within this range; " +
                 "beyond it, lands this full distance toward the cursor instead. Blink is instant, " +
                 "so a valid spot just past a thin wall is still reachable - only the destination is " +
                 "checked, never the path to it.")]
        private float range = 9f;

        [Header("Destination search")]
        [SerializeField, Tooltip("How far, in metres, the destination is walked back toward the " +
                 "player when the requested spot is inside geometry or off the map, before giving " +
                 "up and refusing the cast entirely. Smaller finds a tighter fit hugging a wall; " +
                 "larger is cheaper to compute but can skip past a narrow valid gap.")]
        private float searchStep = 0.25f;

        [SerializeField, Tooltip("How far, in metres, BELOW the player's OWN current height the " +
                 "ground is allowed to be for a candidate spot to count as solid ground. Too small " +
                 "refuses valid spots on a gentle slope or staircase down; too large can accept a " +
                 "spot far below the arena - past a thin floor - as if it were ground.")]
        private float groundProbeDistance = 2f;

        [SerializeField, Tooltip("How far, in metres, ABOVE the player's OWN current height the " +
                 "ground is allowed to be - a small step or curb, not a roof. Blink stays at roughly " +
                 "the caster's own level; it is not a way onto a rooftop. Keep this well under a " +
                 "typical building's floor-to-ceiling height, or a candidate under a roof will read " +
                 "the roof's top as ground and land the caster on top of the building instead of the " +
                 "floor beneath it.")]
        private float maxStepUp = 0.6f;

        [Header("Remote VFX (cosmetic only)")]
        [SerializeField, Tooltip("Radius in metres of the marker briefly shown at the start and end " +
                 "point, on every OTHER client's screen only - the caster's own screen already shows " +
                 "the real teleport. 0 shows nothing.")]
        private float remoteVfxRadius = 0.4f;

        [SerializeField, Tooltip("Seconds the remote marker stays up before it disappears on its own.")]
        private float remoteVfxSeconds = 0.3f;

        // Not a design tunable (like PlayerDisplacement's blockMask): a designer could mis-set it into a blink through the
        // arena floor. Computed in Awake, not a static initializer: LayerMask.GetMask there crashes on spawn.
        private int blockMask;

        // The player's own capsule, read once in OnEquip (Owner is not bound yet in Awake). Blink
        // validates against THIS shape rather than a separate Inspector radius, so retuning the
        // player's hitbox can never quietly disagree with what blink thinks it needs to fit into.
        private CapsuleCollider capsule;

        private void Awake()
        {
            blockMask = LayerMask.GetMask("Default", "Building");
        }

        public override void OnEquip()
        {
            capsule = Owner.Root.GetComponent<CapsuleCollider>();
            if (capsule == null)
                Debug.LogError($"[BlinkAbility] {name}: the player has no CapsuleCollider - blink " +
                                "cannot validate a destination and will always refuse to cast.");
        }

        public override bool TryBuildCast(in CastContext ctx, out CastPayload payload)
        {
            payload = default;

            // Checked here, not in ExecuteCast: the charge is already spent by then (AbilityRunner.TryCast spends before
            // sending) and cannot be refunded, so this is the only safe point to refuse - see PlayerDisplacement.CanTeleport.
            var displacement = Owner.Displacement as PlayerDisplacement;
            if (displacement == null || !displacement.CanTeleport)
                return false;

            if (capsule == null || Owner.Motor == null)
                return false; // OnEquip already logged why; refuse rather than land somewhere unvalidated.

            float killHeight = Owner.Motor.KillHeight;
            float originHeight = ctx.Origin.y;

            bool Probe(Vector3 candidateXZ, out Vector3 landingPoint)
            {
                landingPoint = default;

                // Never outside the arena. The terrain carries on past the boundary walls, so the ground probe alone can't
                // tell; the search's own walk back toward the caster then finds the last spot inside. Blinking PAST a crate
                // or a house inside the arena is allowed.
                if (!Overpower.Arena.ArenaSymmetry.IsInsideArena(candidateXZ, capsule.radius))
                    return false;

                // See GroundProbe's own class comment for why the ray starts only maxStepUp above the
                // CASTER'S OWN current height (not the candidate's unknown one) rather than further up.
                if (!GroundProbe.TryFindGround(originHeight, candidateXZ, maxStepUp, groundProbeDistance,
                        killHeight, blockMask, Owner.Root.transform, out Vector3 ground))
                    return false; // no ground within reach, or it sits at/below the kill plane.

                // Root position such that the capsule's OWN bottom sits exactly on the ground found above - shared with
                // portal arrival, so the two can't drift apart.
                Vector3 rootPosition = PlayerSpaceProbe.RootOnGround(capsule, new Vector3(candidateXZ.x, ground.y, candidateXZ.z));

                // The fit check sees barriers (BodiesWallsAndBarriers) even though the ground probe above keeps blockMask
                // (a barrier's top is never floor): a blink aimed into one lands on the near side, as it does for a wall.
                if (PlayerSpaceProbe.IsCapsuleBlocked(capsule, rootPosition, ArenaLayers.BodiesWallsAndBarriers, Owner.Root.transform))
                    return false;

                landingPoint = rootPosition;
                return true;
            }

            BlinkDestinationSearch.Result result =
                BlinkDestinationSearch.Find(ctx.Origin, ctx.TargetPoint, range, searchStep, Probe);
            if (!result.Found)
                return false; // nothing valid anywhere, including the player's own spot - refuse, nothing spent.

            payload = new CastPayload { Origin = ctx.Origin, Point = result.Destination };
            LogBlink(HorizontalDistance(ctx.Origin, result.Destination),
                     HorizontalDistance(ctx.Origin, ctx.TargetPoint), result.Adjusted);
            return true;
        }

        public override void ExecuteCast(in CastEvent cast)
        {
            if (cast.IsCasterClient)
            {
                var displacement = Owner.Displacement as PlayerDisplacement;
                if (displacement == null || !displacement.TeleportTo(cast.Payload.Point))
                {
                    // Only possible if a knockback started between TryBuildCast's CanTeleport check and here (same race
                    // as DashAbility's); the charge is already spent and cannot be refunded.
                    Debug.LogWarning($"[BlinkAbility] {name}: teleport refused at execute time - " +
                                      "a knockback must have started after the cast was already sent.");
                }
                return;
            }

            // Vision: an enemy's Blink is seen leaving only if the start is in my team's sight, and arriving only if
            // the landing is (they can Blink into the fog and be gone). My own team's always show.
            PlayRemoteVfx(cast.Payload.Origin, cast.CasterTeam);
            PlayRemoteVfx(cast.Payload.Point, cast.CasterTeam);
        }

        private void PlayRemoteVfx(Vector3 point, int casterTeam)
        {
            if (remoteVfxRadius <= 0f || !TeamSight.ShotShownAt(casterTeam, point))
                return;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Blink VFX (cheap, remote only)";
            // Removed immediately, not with Destroy (end of frame): for that frame the sphere would be a solid object
            // in the world. Same trick as DebugPingAbility.
            DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.position = point;
            marker.transform.localScale = Vector3.one * remoteVfxRadius * 2f;
            Destroy(marker, remoteVfxSeconds);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            Vector3 flat = b - a;
            flat.y = 0f;
            return flat.magnitude;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogBlink(float dist, float requested, bool adjusted)
        {
            Debug.Log($"[BLINK] dist={dist:F2} requested={requested:F2} adjusted={adjusted}");
        }

        public override string ShopStatsText() => $"Range {ShopNumberFormat.Compact(range)}m";
    }
}
