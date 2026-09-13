using UnityEngine;
using Overpower.Combat;

namespace Overpower.Abilities
{
    /// <summary>
    /// An instant reposition toward the cursor - Tudor's decision (2026-09-13): blink lands EXACTLY
    /// on the cursor point when the cursor is within range, and the point `range` metres toward it
    /// otherwise [C]. Unlike a dash it never travels the space in between, so a wall between the
    /// caster and a valid spot on the far side does not stop it - only the destination itself has to
    /// be somewhere a player could actually stand.
    ///
    /// The destination search (clamp to range, then walk back toward the caster in fixed steps until
    /// a spot is clear) is pure logic in BlinkDestinationSearch (Assets/scripts/Combat), unit tested
    /// without a scene. This class only supplies the physics half of that search - the ValidityProbe
    /// - and the caster's own capsule to check it with, so the pure class never has to know what a
    /// Collider or a Physics call even is.
    ///
    /// Like Dash, the instant jump itself belongs to PlayerDisplacement (Owner.Displacement cast down
    /// to the concrete type, same reasoning as DashAbility's class comment): this module only decides
    /// WHERE the jump goes and whether it may happen at all.
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

        // Not a design tunable, like PlayerDisplacement's own blockMask: which layers a blink can
        // land on or be stopped by is fixed here rather than exposed for a designer to mis-set into
        // something that blinks through the arena floor. Computed in Awake, not a static field
        // initializer - the same LayerMask.GetMask crash-on-spawn PlayerDisplacement's class comment
        // documents.
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

            // Checked here, not at ExecuteCast time: a charge is already spent by the time
            // ExecuteCast runs (AbilityRunner.TryCast spends before sending), and a blink cannot be
            // un-spent after the fact. Refusing up front, while nothing has been spent yet, is the
            // only point this can safely be checked - see PlayerDisplacement.CanTeleport.
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

                // Ground is looked for within [maxStepUp above .. groundProbeDistance below] the
                // CASTER'S OWN current height, not the candidate's unknown height - the candidate
                // has no height of its own yet, that is exactly what this probe is trying to find.
                //
                // Starting the ray groundProbeDistance (2m) ABOVE the caster, as the first version
                // of this did, started the ray roughly at ceiling height - a house's roof is Building
                // layer now like everything else, so a candidate under a roof or overhang had its ray
                // start ABOVE the roof and hit the ROOF'S TOP first, blinking the caster onto the
                // building instead of the floor beneath it. Starting only maxStepUp (a curb, not a
                // ceiling) above the caster's own height means an ordinary roof is simply never in
                // the ray's reach at all - a downward ray cannot find something the search never
                // looks above. Blink is meant to stay at roughly the caster's own level; it is not a
                // way onto a rooftop. IsCapsuleBlocked below is a second, general backstop against
                // any solid geometry at the resolved spot (roof-related or not), not the primary fix
                // for this - this rule is about which HEIGHT counts as "ground" in the first place,
                // which is a physics concept the pure search below deliberately knows nothing about.
                //
                // RaycastAll, not Raycast: a candidate close to the caster (the search walks back
                // toward them) can put this ray's start-to-end span through the caster's OWN
                // capsule, which sits on the Default layer like any other ground. A single Raycast
                // would happily report that as "ground" and land the caster on top of their own
                // head instead of refusing - found by testing a blink with no real ground anywhere
                // in reach, which should refuse but instead landed a few steps back from the
                // cursor. Same self-exclusion PlayerDisplacement's own sweep already needs.
                Vector3 rayOrigin = new Vector3(candidateXZ.x, originHeight + maxStepUp, candidateXZ.z);
                RaycastHit[] groundHits = Physics.RaycastAll(rayOrigin, Vector3.down,
                    maxStepUp + groundProbeDistance, blockMask, QueryTriggerInteraction.Ignore);
                System.Array.Sort(groundHits, (a, b) => a.distance.CompareTo(b.distance));

                RaycastHit hit = default;
                bool foundGround = false;
                foreach (RaycastHit candidate in groundHits)
                {
                    if (candidate.collider.transform.IsChildOf(Owner.Root.transform))
                        continue; // never treat the caster's own body as ground to land on.

                    hit = candidate;
                    foundGround = true;
                    break;
                }

                if (!foundGround)
                    return false; // no ground within reach - off the map edge or over a void.

                if (hit.point.y <= killHeight)
                    return false; // ground exists but sits below the kill plane - not worth landing on.

                // Root position such that the capsule's OWN bottom sits exactly on the ground found
                // above - the same derivation TestRangeSpawner uses to stop dummies standing buried,
                // rather than a second hardcoded "player stands 0.5m above its root" number.
                float capsuleBottomOffset = capsule.center.y - capsule.height * 0.5f; // negative.
                Vector3 rootPosition = new Vector3(candidateXZ.x, hit.point.y - capsuleBottomOffset, candidateXZ.z);

                if (IsCapsuleBlocked(rootPosition))
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
                    // Only possible if a knockback started in the single frame between
                    // TryBuildCast's CanTeleport check and here (see DashAbility's own comment on
                    // the same race) - the charge is already spent and cannot be refunded.
                    Debug.LogWarning($"[BlinkAbility] {name}: teleport refused at execute time - " +
                                      "a knockback must have started after the cast was already sent.");
                }
                return;
            }

            PlayRemoteVfx(cast.Payload.Origin);
            PlayRemoteVfx(cast.Payload.Point);
        }

        /// <summary>
        /// True if a player-sized capsule rooted here would overlap a wall or another player's body.
        /// Uses OverlapCapsule rather than CheckCapsule so the caster's own colliders can be excluded
        /// from the result - CheckCapsule's bool answer has no way to say "except this one".
        /// Assumes the player prefab is not scaled and the capsule's direction is the Y axis, the
        /// same simplification PlayerDisplacement and the test-range dummy already make for this
        /// exact capsule.
        /// </summary>
        private bool IsCapsuleBlocked(Vector3 rootPosition)
        {
            Vector3 center = rootPosition + capsule.center;
            float halfSegment = Mathf.Max(capsule.height * 0.5f - capsule.radius, 0f);
            Vector3 top = center + Vector3.up * halfSegment;
            Vector3 bottom = center - Vector3.up * halfSegment;

            Collider[] overlaps = Physics.OverlapCapsule(top, bottom, capsule.radius, blockMask,
                                                          QueryTriggerInteraction.Ignore);
            foreach (Collider overlap in overlaps)
            {
                if (overlap.transform.IsChildOf(Owner.Root.transform))
                    continue; // never blocked by the caster's own body.

                return true;
            }

            return false;
        }

        private void PlayRemoteVfx(Vector3 point)
        {
            if (remoteVfxRadius <= 0f)
                return;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Blink VFX (cheap, remote only)";
            // Removed immediately, not with Destroy, which waits for end of frame - see
            // DebugPingAbility's identical trick: for that one frame the sphere would otherwise be a
            // solid object sitting in the world.
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
    }
}
