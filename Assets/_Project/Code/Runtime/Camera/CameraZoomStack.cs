using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The maths behind CameraTracking's offset each frame, pulled out so LateUpdate stays a thin caller and this
/// is testable without the play loop.
///
/// THE ORDER IS THE WHOLE POINT. CameraTracking clamps the scroll zoom FIRST, to minZoomMultiplier/
/// maxZoomMultiplier; every active extra-zoom multiplier (Scope, added through CameraTracking.AddZoomMultiplier)
/// then multiplies the ALREADY-CLAMPED result. If an extra-zoom ability bumped the clamped value itself, a player
/// already at the scroll wheel's limit would gain nothing: the next scroll tick would clamp the inflated number
/// straight back down.
/// </summary>
public static class CameraZoomStack
{
    /// <summary>baseOffset scaled by the already-clamped scroll zoom, then by every active extra-zoom
    /// multiplier's combined product (1 when nothing is active).</summary>
    public static Vector3 ApplyZoom(Vector3 baseOffset, float clampedZoom, float extraZoomMultiplierProduct) =>
        baseOffset * clampedZoom * extraZoomMultiplierProduct;

    /// <summary>The product of every active keyed multiplier; 1 for an empty stack, like
    /// PlayerMotor.CurrentSpeed's own multiplier loop.</summary>
    public static float Product(IEnumerable<float> multipliers)
    {
        float product = 1f;
        foreach (float multiplier in multipliers)
            product *= multiplier;
        return product;
    }

    /// <summary>Concrete-typed overload for CameraTracking's multiplier dictionary (zoomMultipliers.Values),
    /// called every LateUpdate plus every read of ZoomMultiplierProduct, so it must not allocate: this binds
    /// to ValueCollection's struct enumerator instead of boxing it through IEnumerable&lt;float&gt;. Same
    /// maths, kept as a literal copy because forwarding to the general overload would box right back.</summary>
    public static float Product(Dictionary<object, float>.ValueCollection multipliers)
    {
        float product = 1f;
        foreach (float multiplier in multipliers)
            product *= multiplier;
        return product;
    }
}
