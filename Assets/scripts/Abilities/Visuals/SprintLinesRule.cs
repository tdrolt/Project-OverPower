using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// When a player shows Sprint speed-lines and where they stream. The sprinter sends a start phase each time its sprint really turns on
    /// and a stop phase each time it turns off, on the ability's one RPC, and another screen follows those. The key press's cast is not a
    /// start: a tap can start nothing, and a held key resumes a sprint after an overheat, a stun or a respawn without a new press. The
    /// sprinter's own screen uses its real state. Lines show only while the player actually moves. Each line slides back from behind the
    /// feet and fades, its own phase set by its index, so every client draws the same lines.
    /// </summary>
    public static class SprintLinesRule
    {
        private const float PhaseStep = 0.37f; // staggers the lines so they never slide back in step; not a look number

        public const byte StoppedPhase = 1;
        public const byte StartedPhase = 2;

        public static bool StartsNow(bool wasActive, bool nowActive) => nowActive && !wasActive;

        public static bool AnnouncedAfter(bool current, byte phase) =>
            phase == StartedPhase || (phase != StoppedPhase && current);

        public static bool Sprinting(bool isMine, bool ownerActive, bool announcedOn) => isMine ? ownerActive : announcedOn;

        public static bool Show(bool sprinting, float speed, float minSpeed) => sprinting && speed >= minSpeed;

        /// <summary>Flat ground speed between two positions; height changes are not running.</summary>
        public static float Speed(Vector3 previous, Vector3 now, float deltaTime)
        {
            if (deltaTime <= 0f)
                return 0f;
            Vector3 moved = now - previous;
            moved.y = 0f;
            return moved.magnitude / deltaTime;
        }

        /// <summary>Eases toward the raw speed over about <paramref name="responseSeconds"/>, so frames without a physics step (no movement)
        /// do not make the lines flicker.</summary>
        public static float Smooth(float previous, float raw, float deltaTime, float responseSeconds)
        {
            if (deltaTime <= 0f)
                return previous;
            if (responseSeconds <= 0f)
                return raw;
            return Mathf.Lerp(previous, raw, 1f - Mathf.Exp(-deltaTime / responseSeconds));
        }

        /// <summary>One end of line <paramref name="index"/> of <paramref name="count"/> in the run's own frame (x right, z forward, feet at
        /// zero): side by side <paramref name="spread"/> apart, starting <paramref name="behind"/> back and sliding <paramref name="travel"/>
        /// further back <paramref name="rate"/> times a second. The tail trails the head by <paramref name="length"/>.</summary>
        public static Vector3 LinePoint(int index, int count, float seconds, float rate, float spread, float behind, float length, float travel, bool tail)
        {
            float x = (index - (count - 1) * 0.5f) * spread;
            float z = -behind - Cycle(index, seconds, rate) * travel - (tail ? length : 0f);
            return new Vector3(x, 0f, z);
        }

        /// <summary>Full at the start of a line's slide, still strong halfway, gone at its end.</summary>
        public static float LineAlpha(int index, int count, float seconds, float rate)
        {
            float c = Cycle(index, seconds, rate);
            return 1f - c * c;
        }

        private static float Cycle(int index, float seconds, float rate)
        {
            float t = seconds * Mathf.Max(0f, rate) + index * PhaseStep;
            return t - Mathf.Floor(t);
        }
    }
}
