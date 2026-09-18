using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The maths behind CameraTracking's offset each frame - pulled out so CameraTracking.LateUpdate can stay a thin
/// caller (this project's pattern for anything worth unit testing without the engine's play loop; see
/// RaybeamGeometry, ChargeCountRule, ReactiveInvulnerabilityState for the same call).
///
/// THE ORDER IS THE WHOLE POINT (Scope ability, Tudor 2026-09-18). The scroll zoom is clamped FIRST, by
/// CameraTracking itself, to minZoomMultiplier/maxZoomMultiplier - that clamped value is what ApplyZoom below
/// receives as clampedZoom. Every active extra-zoom multiplier (Scope today; whatever else wants one tomorrow,
/// added through CameraTracking.AddZoomMultiplier) then multiplies the ALREADY-CLAMPED result, never the other
/// way around. If an extra-zoom ability instead bumped the clamped value itself, a player already at the scroll
/// wheel's own limit would gain nothing from it - the very next scroll tick would clamp the inflated number
/// straight back down.
/// </summary>
public static class CameraZoomStack
{
    /// <summary>baseOffset scaled by the already-clamped scroll zoom, then by every active extra-zoom
    /// multiplier's combined product (1 when nothing is active, so this is a true no-op with nothing scoping).</summary>
    public static Vector3 ApplyZoom(Vector3 baseOffset, float clampedZoom, float extraZoomMultiplierProduct) =>
        baseOffset * clampedZoom * extraZoomMultiplierProduct;

    /// <summary>The product of every active keyed multiplier - 1 for an empty stack, exactly like
    /// PlayerMotor.CurrentSpeed's own multiplier loop (PlayerMotor.cs:100-109).</summary>
    public static float Product(IEnumerable<float> multipliers)
    {
        float product = 1f;
        foreach (float multiplier in multipliers)
            product *= multiplier;
        return product;
    }
}
