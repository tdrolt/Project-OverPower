using UnityEngine;

/// <summary>
/// How one forced move ended - a dash, a knockback, a blink. Lives in Player, not Overpower.Combat:
/// Combat's pure-logic types stay free of physics/scene types so they test without a scene, and
/// Blocker below is a real Collider.
/// </summary>
public enum DisplaceOutcome
{
    /// <summary>Travelled the full requested distance.</summary>
    Completed,

    /// <summary>Stopped early by something solid in the way.</summary>
    Blocked,

    /// <summary>Cut short by an interrupt (stun, death, ability cancelled), not by anything physical.</summary>
    Cancelled
}

/// <summary>What a displacement ended up doing. Immutable, like DamageInfo: a report of what already happened.</summary>
public readonly struct DisplaceEnd
{
    public readonly DisplaceOutcome Outcome;
    public readonly Vector3 EndPosition;

    /// <summary>What stopped the move. Null unless Outcome is Blocked.</summary>
    public readonly Collider Blocker;

    public DisplaceEnd(DisplaceOutcome outcome, Vector3 endPosition, Collider blocker)
    {
        Outcome = outcome;
        EndPosition = endPosition;
        Blocker = blocker;
    }
}

/// <summary>
/// Something that can be forcibly moved in a straight line over time (dash, knockback, blink
/// travel). PlayerDisplacement is the only implementation, so every shove shares one movement and
/// one way of reporting what stopped it. Says nothing about WHO moves or WHY: an ability calls it
/// on whatever IDamageable it hit, the same "found generically, acts on itself" shape as
/// IStatusReceiver.
/// </summary>
public interface IDisplaceable
{
    /// <summary>
    /// Moves direction.normalized * distance metres at speed metres/second. onEnd fires exactly
    /// once, even when blocked or cancelled, so a caller never waits forever.
    /// </summary>
    void Displace(Vector3 direction, float distance, float speed, System.Action<DisplaceEnd> onEnd);
}
