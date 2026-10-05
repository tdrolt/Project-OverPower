#if UNITY_EDITOR || DEVELOPMENT_BUILD // a check tool: it must not ship in a player's build
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Overpower.Dominion
{
    /// <summary>
    /// Dominion Task 11 check: walks the real player body along a list of routes by code (never the keyboard or mouse) and records its position every physics
    /// frame. It moves the body the way PlayerMotor does (Rigidbody.MovePosition at the motor's speed, with the motor's own movement switched off meanwhile), so
    /// walls, boxes and jersey barriers stop it exactly as they stop a player. A walk counts as stuck when the body gained under a quarter of the distance a
    /// step should give for half a second. Results: a CSV of every frame and a summary text, both written outside the project. Added to the player by a
    /// script in Play Mode; not part of the game.
    /// </summary>
    public sealed class LaneWalkRecorder : MonoBehaviour
    {
        public sealed class Walk
        {
            public string label;
            public Vector2 start;                 // world X, Z
            public List<Vector2> route = new List<Vector2>();
            public bool expectBlocked;
            // results
            public int frames, longestStuckRun;
            public bool blocked;
            public Vector2 stoppedAt;
            public float distanceWalked;
        }

        public const int StuckFramesToBlock = 25;

        public List<Walk> walks = new List<Walk>();
        public string csvPath, summaryPath;
        public bool finished;

        private Rigidbody body;
        private PlayerMotor motor;
        private PlayerDisplacement displacement;
        private int walkIndex = -1, waypoint;
        private Vector3 lastPosition;
        private int stuckRun, frame;
        private float time;
        private readonly StringBuilder csv = new StringBuilder("walk,frame,time,x,z,moved,expected\n");

        public void Begin()
        {
            body = GetComponent<Rigidbody>();
            motor = GetComponent<PlayerMotor>();
            displacement = GetComponent<PlayerDisplacement>();
            motor.ExternalMotionControl = true;
            NextWalk();
        }

        private void NextWalk()
        {
            walkIndex++;
            if (walkIndex >= walks.Count) { Finish(); return; }
            Walk w = walks[walkIndex];
            var start = new Vector3(w.start.x, body.position.y, w.start.y);
            if (!displacement.TeleportTo(start)) body.position = start;
            waypoint = 0; stuckRun = 0; frame = 0; time = 0f;
            lastPosition = start;
        }

        private void FixedUpdate()
        {
            if (finished || walkIndex < 0 || walkIndex >= walks.Count) return;
            Walk w = walks[walkIndex];
            Vector3 position = body.position;
            if (waypoint >= w.route.Count) { Close(w, position, false); NextWalk(); return; }

            Vector3 target = new Vector3(w.route[waypoint].x, position.y, w.route[waypoint].y);
            Vector3 to = target - position;
            float step = motor.CurrentSpeed * Time.deltaTime;
            float remaining = to.magnitude;
            if (remaining <= step) { body.MovePosition(target); waypoint++; }
            else body.MovePosition(position + to / remaining * step);

            float moved = (position - lastPosition).magnitude;
            time += Time.deltaTime; frame++;
            if (frame > 1)
            {
                w.distanceWalked += moved;
                bool stuckNow = moved < 0.25f * step && remaining > step;
                stuckRun = stuckNow ? stuckRun + 1 : 0;
                if (stuckRun > w.longestStuckRun) w.longestStuckRun = stuckRun;
            }
            csv.Append(w.label).Append(',').Append(frame).Append(',').Append(time.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
               .Append(position.x.ToString("0.000", CultureInfo.InvariantCulture)).Append(',').Append(position.z.ToString("0.000", CultureInfo.InvariantCulture)).Append(',')
               .Append(moved.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',').Append(step.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\n');
            lastPosition = position;

            if (stuckRun >= StuckFramesToBlock) { Close(w, position, true); NextWalk(); }
            else if (frame > 4000) { Close(w, position, true); NextWalk(); } // a walk that never ends
        }

        private void Close(Walk w, Vector3 position, bool blocked)
        {
            w.blocked = blocked;
            w.frames = frame;
            w.stoppedAt = new Vector2(position.x, position.z);
        }

        private void Finish()
        {
            finished = true;
            motor.ExternalMotionControl = false;
            File.WriteAllText(csvPath, csv.ToString());
            var sb = new StringBuilder();
            foreach (Walk w in walks)
            {
                bool pass = w.expectBlocked ? w.blocked : !w.blocked && w.longestStuckRun < 10;
                sb.AppendLine($"{(pass ? "PASS" : "FAIL")} {w.label}: expected {(w.expectBlocked ? "BLOCKED" : "free")}, got {(w.blocked ? "BLOCKED" : "free")}, frames {w.frames}, " +
                              $"walked {w.distanceWalked:0.0} m, longest stuck run {w.longestStuckRun} frames, ended at ({w.stoppedAt.x:0.00}, {w.stoppedAt.y:0.00})");
            }
            File.WriteAllText(summaryPath, sb.ToString());
        }
    }
}
#endif
