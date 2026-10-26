using Overpower.Combat;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>The mark where a Stun Gun bolt strikes a player: whether it shows, how the flash's spikes shoot out, how its light dies and how
    /// the crackling shock ring spreads over the floor (StunImpactView draws it).</summary>
    public static class StunImpactLook
    {
        /// <summary>Only on something a stun can land on (a wall gets the bolt's usual burst), and only where this client may see the shot.</summary>
        public static bool ShowsMark(bool struckSomethingStunnable, bool shownToMe) => struckSomethingStunnable && shownToMe;

        /// <summary>How far the spikes reach <paramref name="ageSeconds"/> in: most of the way at once, easing to <paramref name="reach"/>.</summary>
        public static float RayReach(float ageSeconds, float seconds, float reach)
        {
            float u = seconds > 0f ? Mathf.Clamp01(ageSeconds / seconds) : 1f;
            float left = 1f - u;
            return reach * (1f - left * left * left);
        }

        /// <summary>Spike <paramref name="index"/> of <paramref name="count"/>, spread evenly in the plane facing the camera (its
        /// <paramref name="right"/> and <paramref name="up"/>), the whole fan turned by <paramref name="turnDegrees"/>.</summary>
        public static Vector3 RayDirection(int index, int count, float turnDegrees, Vector3 right, Vector3 up)
        {
            float radians = (turnDegrees + (count > 0 ? index * 360f / count : 0f)) * Mathf.Deg2Rad;
            return (right.normalized * Mathf.Cos(radians) + up.normalized * Mathf.Sin(radians)).normalized;
        }

        /// <summary>The ring's size <paramref name="ageSeconds"/> in: from the bolt's own size, fast at first like a shock wave.</summary>
        public static float RingRadius(float ageSeconds, float seconds, float startRadius, float endRadius)
        {
            float u = seconds > 0f ? Mathf.Clamp01(ageSeconds / seconds) : 1f;
            float left = 1f - u;
            return Mathf.Lerp(startRadius, endRadius, 1f - left * left * left);
        }

        /// <summary>Point <paramref name="k"/> of <paramref name="n"/> round the ring, flat on the floor (y 0), each jumping in or out by up to
        /// <paramref name="jitter"/> of the radius, from the same integer hash as the stun arcs.</summary>
        public static Vector3 RingPoint(int seed, int k, int n, float radius, float jitter)
        {
            float r = radius * (1f + (StunArcsRule.Hash01(seed, k, 300) * 2f - 1f) * jitter);
            return AbilityVisualGeometry.CirclePoint(r, n > 0 ? k * 360f / n : 0f);
        }

        /// <summary>The light's strength: <paramref name="peak"/> at the hit, gone by <paramref name="seconds"/>, faster than a straight fade.</summary>
        public static float LightIntensity(float ageSeconds, float seconds, float peak)
        {
            float left = AbilityVisualGeometry.Fade01(ageSeconds, seconds);
            return peak * left * left;
        }
    }
}
