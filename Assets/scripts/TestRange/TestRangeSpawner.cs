using UnityEngine;
using Overpower.Data;

namespace Overpower.TestRange
{
    /// <summary>
    /// Populates the practice range near team 0's spawn point when the test range is switched on,
    /// and does nothing at all when it is off - see GameplayConfig.TestRangeEnabled.
    ///
    /// Dummies are placed relative to RoomManager.teamSpawnPoints[0] rather than a hardcoded
    /// position or a second direct reference to the same Transform, so moving the real spawn point
    /// in the Editor moves the range with it and there is exactly one place that decides where
    /// team 0 spawns.
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

            for (int i = 0; i < stationaryDistances.Length; i++)
            {
                Vector3 pos = Grounded(spawn.position + forward * stationaryDistances[i] + right * (i * lateralSpacing));
                SpawnDummy(pos, spawn.rotation);
            }

            float moveSpeed = gameplayConfig.BaseMoveSpeed;
            for (int i = 0; i < strafingDummyCount; i++)
            {
                Vector3 center = Grounded(spawn.position + forward * strafeRowDistance + right * (i * lateralSpacing * 2f));
                GameObject dummy = SpawnDummy(center, spawn.rotation);
                dummy.AddComponent<Strafer>().Configure(center, right, strafeDistance, moveSpeed);
            }
        }

        /// <summary>
        /// Lifts a point on the ground to where the dummy's ROOT must sit for its collider to stand on
        /// that ground. The dummy's collider copies the player's capsule, whose bottom is below the root
        /// (local Y -0.5), just as a player standing on the floor has its root 0.5m up. Placing the root
        /// ON the ground instead buried every dummy by 0.5m (measured 2026-09-13), so a shot fired from a
        /// real player's muzzle crossed the narrow top of the dummy's capsule: it read as a ~0.4m-wide
        /// target instead of 0.7m, and the shotgun spread was tuned against that buried target.
        ///
        /// Derived from the collider rather than a hardcoded 0.5, so it stays right if the capsule changes.
        /// Also applied to the strafer's centre, which re-applies its position every frame.
        /// </summary>
        private Vector3 Grounded(Vector3 groundPoint)
        {
            CapsuleCollider capsule = dummyTargetPrefab.GetComponent<CapsuleCollider>();
            if (capsule == null)
                return groundPoint;

            float bottom = (capsule.center.y - capsule.height * 0.5f) * dummyTargetPrefab.transform.localScale.y;
            return groundPoint + Vector3.up * -bottom;
        }

        private GameObject SpawnDummy(Vector3 position, Quaternion rotation)
        {
            // A plain Instantiate, never PhotonNetwork.Instantiate: PhotonNetwork.Instantiate would
            // create a networked object and put a practice target into every other client's match.
            // Dummies are local scenery for one Editor session - same reasoning as DummyTarget's
            // own class comment.
            return Instantiate(dummyTargetPrefab, position, rotation);
        }

        /// <summary>Moves a dummy left and right of a fixed centre at a fixed speed, for practicing
        /// leading a moving target. Nested here rather than a third file, since nothing outside this
        /// spawner ever needs to create one.
        ///
        /// SCALES WITH THE DUMMY'S OWN STATUS (Task 1.8): this rewrites transform.position directly
        /// every frame rather than driving a real PlayerMotor, so it cannot go through a speed
        /// multiplier the way a stunned or slowed player does - AddSpeedMultiplier has nothing to
        /// multiply here. Multiplying the PingPong clock's own advance by (stunned ? 0 : 1 - Slow)
        /// has the identical visible effect: a stunned dummy freezes in place (the clock stops
        /// advancing, so it snaps back to full speed the instant the stun lifts, exactly like a
        /// player's speed multiplier does) and a slowed one visibly crosses less ground per second.</summary>
        private sealed class Strafer : MonoBehaviour
        {
            private Vector3 center;
            private Vector3 axis;
            private float distance;
            private float speed;
            private float t;
            private DummyTarget dummy;

            public void Configure(Vector3 center, Vector3 axis, float distance, float speed)
            {
                this.center = center;
                this.axis = axis.normalized;
                this.distance = distance;
                this.speed = speed;
            }

            private void Awake()
            {
                // Same GameObject: TestRangeSpawner adds this component to the dummy it just spawned.
                dummy = GetComponent<DummyTarget>();
            }

            private void Update()
            {
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
