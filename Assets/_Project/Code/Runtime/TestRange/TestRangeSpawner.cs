using UnityEngine;
using Overpower.Data;

namespace Overpower.TestRange
{
    /// <summary>
    /// Populates the practice range near team 0's spawn point when the test range is on, and does
    /// nothing when off (GameplayConfig.TestRangeEnabled). Dummies are placed relative to
    /// RoomManager.teamSpawnPoints[0], so moving that spawn point moves the range with it.
    /// </summary>
    public class TestRangeSpawner : MonoBehaviour
    {
        [SerializeField, Tooltip("Match tuning asset. Read for TestRangeEnabled (whether to spawn " +
                 "at all) and BaseMoveSpeed (how fast the strafing dummy moves, so it always " +
                 "matches a real player's speed instead of a number typed into this script).")]
        private GameplayConfig gameplayConfig;

        [SerializeField, Tooltip("Source of team 0's spawn point (teamSpawnPoints[0]). Dummies are " +
                 "placed relative to this, never a hardcoded position, so redesigning the arena's " +
                 "spawn moves the range with it.")]
        private RoomManager roomManager;

        [SerializeField, Tooltip("The dummy target prefab to spawn - must carry a DummyTarget " +
                 "component.")]
        private GameObject dummyTargetPrefab;

        [Header("Stationary dummies")]
        [SerializeField, Tooltip("Distance in front of the spawn point, in metres, for each " +
                 "stationary dummy. About 5m and about 25m by default, so both close-range falloff " +
                 "and full bullet travel time are testable at once. Add or remove entries to change " +
                 "how many stationary dummies spawn - nothing else needs editing.")]
        private float[] stationaryDistances = { 5f, 25f };

        [SerializeField, Tooltip("Sideways gap in metres between dummies that would otherwise stand " +
                 "on the same line.")]
        private float lateralSpacing = 4f;

        [SerializeField, Tooltip("Sideways offset in metres (along the spawn's right) for the whole " +
                 "range's baseline, before any of the distances above are applied. Team 0's spawn " +
                 "faces almost straight at its own capital's centre (measured: the capital sits " +
                 "about 0.6 m off that line, well inside the tower's own 2.6 m collider), so the " +
                 "'5 m' dummy used to spawn inside the capital tower. Shifting the whole line " +
                 "sideways by this much clears the tower (2.6 m radius + a dummy's own ~0.7 m + a " +
                 "safety margin) while keeping every configured distance meaningful, instead of " +
                 "eating into the close-range test by starting the line further out.")]
        private float rangeSidewaysOffsetMetres = 3.5f;

        [Header("Strafing dummy")]
        [SerializeField, Tooltip("How many strafing dummies to spawn, each patrolling " +
                 "independently. 1 is enough to practice leading a moving target.")]
        private int strafingDummyCount = 1;

        [SerializeField, Tooltip("How far left and right of its own patrol centre each strafing " +
                 "dummy travels, in metres. Total patrol width is twice this.")]
        private float strafeDistance = 6f;

        [SerializeField, Tooltip("Distance in front of the spawn point, in metres, for the " +
                 "strafing dummy's patrol line.")]
        private float strafeRowDistance = 15f;

        private void Start()
        {
            if (gameplayConfig == null)
            {
                Debug.LogError($"[TestRangeSpawner] {name}: GameplayConfig is not assigned - assuming the test range is off, spawning nothing.");
                enabled = false;
                return;
            }

            if (!gameplayConfig.TestRangeEnabled)
            {
                enabled = false; // The whole point: flip the asset's checkbox and this spawns nothing.
                return;
            }

            if (roomManager == null || dummyTargetPrefab == null || roomManager.teamSpawnPoints == null ||
                roomManager.teamSpawnPoints.Length == 0 || roomManager.teamSpawnPoints[0] == null)
            {
                Debug.LogError($"[TestRangeSpawner] {name}: missing Room Manager, Dummy Target " +
                                "Prefab, or team 0 has no spawn point - no dummies spawned.");
                enabled = false;
                return;
            }

            SpawnRange();
            enabled = false; // One-shot: nothing left to do every frame.
        }

        private void SpawnRange()
        {
            Transform spawn = roomManager.teamSpawnPoints[0];
            Vector3 forward = spawn.forward;
            Vector3 right = spawn.right;
            Vector3 baseline = Baseline(spawn.position, right, rangeSidewaysOffsetMetres);

            for (int i = 0; i < stationaryDistances.Length; i++)
            {
                Vector3 pos = Grounded(StationaryPoint(baseline, forward, right, stationaryDistances[i], lateralSpacing, i), dummyTargetPrefab);
                SpawnDummy(pos, spawn.rotation);
            }

            float moveSpeed = gameplayConfig.BaseMoveSpeed;
            for (int i = 0; i < strafingDummyCount; i++)
            {
                Vector3 center = Grounded(StrafeCentre(baseline, forward, right, strafeRowDistance, lateralSpacing, i), dummyTargetPrefab);
                GameObject dummy = SpawnDummy(center, spawn.rotation);
                dummy.AddComponent<Strafer>().Configure(center, right, strafeDistance, moveSpeed);
            }
        }

        // ---- Pure position math ----
        // Small static methods shared by SpawnRange and TestRangeSpawnerTests (which pins every one
        // clear of solid geometry), so the test cannot drift from what actually spawns.

        /// <summary>The range's baseline: the spawn point shifted sideways clear of team 0's own
        /// capital (rangeSidewaysOffsetMetres's tooltip).</summary>
        public static Vector3 Baseline(Vector3 spawnPosition, Vector3 right, float sidewaysOffsetMetres) =>
            spawnPosition + right * sidewaysOffsetMetres;

        public static Vector3 StationaryPoint(Vector3 baseline, Vector3 forward, Vector3 right, float distance, float lateralSpacing, int index) =>
            baseline + forward * distance + right * (index * lateralSpacing);

        public static Vector3 StrafeCentre(Vector3 baseline, Vector3 forward, Vector3 right, float strafeRowDistance, float lateralSpacing, int index) =>
            baseline + forward * strafeRowDistance + right * (index * lateralSpacing * 2f);

        /// <summary>
        /// Lifts a point on the ground to where the dummy's ROOT must sit for its collider to stand on
        /// it. The collider copies the player's capsule, whose bottom is below the root (local Y -0.5);
        /// placing the root ON the ground buried the dummy by 0.5m, so a real player's muzzle shot
        /// crossed the narrow top of the capsule (a ~0.4m-wide target, not 0.7m) and the shotgun
        /// spread was tuned against it. Derived from the collider, not a hardcoded 0.5. Also applied
        /// to the strafer's centre, which re-applies its position every frame.
        /// </summary>
        public static Vector3 Grounded(Vector3 groundPoint, GameObject dummyTargetPrefab)
        {
            CapsuleCollider capsule = dummyTargetPrefab.GetComponent<CapsuleCollider>();
            if (capsule == null)
                return groundPoint;

            float bottom = (capsule.center.y - capsule.height * 0.5f) * dummyTargetPrefab.transform.localScale.y;
            return groundPoint + Vector3.up * -bottom;
        }

        private GameObject SpawnDummy(Vector3 position, Quaternion rotation)
        {
            // A plain Instantiate, never PhotonNetwork.Instantiate: that would put a practice target
            // into every other client's match. Dummies are local scenery (see DummyTarget).
            return Instantiate(dummyTargetPrefab, position, rotation);
        }

        /// <summary>Moves a dummy left and right of a fixed centre at a fixed speed, for practicing
        /// leading a moving target. Nested: nothing outside this spawner creates one.
        ///
        /// SCALES WITH THE DUMMY'S STATUS: it rewrites transform.position every frame instead of
        /// driving a PlayerMotor, so no speed multiplier applies. Multiplying the PingPong clock's
        /// advance by (stunned ? 0 : 1 - Slow) has the same visible effect: a stunned dummy freezes
        /// and resumes at full speed the instant the stun lifts.
        ///
        /// KNOCKED BACK BY A SONIC PULSE: DummyTarget.Displace moves the same transform, so while
        /// dummy.IsDisplacing Update skips its own write AND stops advancing t, or the two would fight
        /// and the push would go nowhere. DummyTarget.Displaced then reports the net movement, added
        /// onto center, so the next frame's center(shifted) + axis * offset(unchanged) is exactly
        /// where the push ended and the patrol resumes with no snap.</summary>
        private sealed class Strafer : MonoBehaviour
        {
            private Vector3 center;

            // The row this strafer was calibrated to patrol, captured once in Configure: center drifts
            // with every push (OnDummyDisplaced), and this is what a dummy reset restores it to.
            private Vector3 originalCenter;

            private Vector3 axis;
            private float distance;
            private float speed;
            private float t;
            private DummyTarget dummy;

            public void Configure(Vector3 center, Vector3 axis, float distance, float speed)
            {
                this.center = center;
                this.originalCenter = center;
                this.axis = axis.normalized;
                this.distance = distance;
                this.speed = speed;
            }

            private void Awake()
            {
                // Same GameObject: TestRangeSpawner adds this component to the dummy it just spawned.
                dummy = GetComponent<DummyTarget>();
                if (dummy != null)
                {
                    dummy.Displaced += OnDummyDisplaced;
                    dummy.ResetOccurred += OnDummyReset;
                }
            }

            private void OnDestroy()
            {
                if (dummy != null)
                {
                    dummy.Displaced -= OnDummyDisplaced;
                    dummy.ResetOccurred -= OnDummyReset;
                }
            }

            private void OnDummyDisplaced(Vector3 worldDelta)
            {
                // The full 3D delta, not just its component along axis: a pulse rarely pushes exactly
                // along the patrol line, and dropping the sideways part would leave the dummy off its
                // center the instant the patrol clock starts reading from it again.
                center += worldDelta;
            }

            /// <summary>The dummy restored its position to its spawn point (DummyTarget.ResetToFull):
            /// the patrol centre must snap back, or the next frame's center + axis * offset would drag
            /// the dummy right back off it.</summary>
            private void OnDummyReset()
            {
                center = originalCenter;
            }

            private void Update()
            {
                if (dummy != null && dummy.IsDisplacing)
                    return; // A knockback owns this dummy's position right now - see the class comment.

                if (distance <= 0f)
                    return;

                float multiplier = dummy != null ? (dummy.IsStunned ? 0f : 1f - dummy.Slow) : 1f;

                // A round trip covers 4x distance (there and back); PingPong over 2x distance and
                // re-centring on zero turns that into a smooth back-and-forth with no snap at
                // either end, instead of a sawtooth that teleports at the turnaround.
                t += Time.deltaTime * speed * multiplier;
                float offset = Mathf.PingPong(t, distance * 2f) - distance;
                transform.position = center + axis * offset;
            }
        }
    }
}
