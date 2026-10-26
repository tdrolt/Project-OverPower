using Overpower.Combat;
using UnityEngine;

/// <summary>The jolt a stunned player's own camera gives: only their own client, only for a stun, and only while the Arena Look's switch
/// is on. It starts at full strength and settles to still; the camera jumps a golden angle round each step, so it never slides one way.</summary>
public static class StunShakeRule
{
    private const float GoldenAngle = 137.5f; // each step turns this far from the last, so no two neighbours point alike; not a look number

    public static bool ShakesOnApply(StatusKind kind, bool victimIsMine, bool shakeOn, float strength) =>
        kind == StatusKind.Stun && victimIsMine && shakeOn && strength > 0f;

    /// <summary>How far the camera is off <paramref name="ageSeconds"/> into a shake: <paramref name="strength"/> at once, easing to 0 at
    /// <paramref name="seconds"/>.</summary>
    public static float Strength(float ageSeconds, float seconds, float strength)
    {
        if (ageSeconds < 0f || seconds <= 0f || ageSeconds >= seconds)
            return 0f;
        float left = 1f - ageSeconds / seconds;
        return strength * left * left;
    }

    /// <summary>The camera's offset in its own right (x) and up (y), in metres: a new direction <paramref name="frequency"/> times a second.</summary>
    public static Vector2 Offset(float ageSeconds, float seconds, float strength, float frequency)
    {
        float size = Strength(ageSeconds, seconds, strength);
        if (size <= 0f)
            return Vector2.zero;
        int step = Mathf.FloorToInt(ageSeconds * Mathf.Max(1f, frequency));
        float radians = step * GoldenAngle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * size;
    }
}
