using UnityEngine;

/// <summary>
/// How one forced move ended - a dash, a knockback, a blink. Kept in Player rather than
/// Overpower.Combat: every pure-logic type in Combat (see BeamResolver's BeamContact, which
/// reports a Collider hit as a plain distance/IDamageable/point struct instead of a raw Collider)
/// deliberately stays free of physics/scene types so it can be unit tested without a scene. Blocker
/// below is a real Collider - the thing a dash slammed into, which a HUD or VFX hook wants to
/// read - so this struct, and the interface that produces it, live next to PlayerMotor instead.
/// </summary>
public enum DisplaceOutcome
{
    /// <summary>Travelled the full requested distance.</summary>
    Completed,

    /// <summary>Stopped early by something solid in the way.</summary>
    Blocked,

    /// <summary>Cut short by an interrupt - a stun landing mid-dash, death, or the ability itself
    /// being cancelled - rather than by anything physical.</summary>
    Cancelled
}

/// <summary>What a displacement (dash, knockback, blink) ended up doing. Immutable, like DamageInfo,
/// for the same reason: it is a report of what already happened, not something a caller should be
/// able to mutate after the fact.</summary>
public readonly struct DisplaceEnd
{
    public readonly DisplaceOutcome Outcome;
    public readonly Vector3 EndPosition;

    /// <summary>What stopped the move. Null unless Outcome is Blocked - there is nothing to blame
    /// for a move that finished on its own or was cancelled by a status effect.</summary>
    public readonly Collider Blocker;

    public DisplaceEnd(DisplaceOutcome outcome, Vector3 endPosition, Collider blocker)
    {
        Outcome = outcome;
        EndPosition = endPosition;
        Blocker = blocker;
    }
}

/// <summary>
/// Something that can be forcibly moved in a straight line over time - a dash, a knockback, a
/// blink's travel phase. Interface only for now: PlayerDisplacement (Task 1.6) is the first and
/// only implementation, so every ability that needs to shove a player around shares one component
/// instead of each dash/knockback/blink re-deriving its own movement and its own way of reporting
/// what stopped it.
///
/// Deliberately excludes anything about WHO is moving or WHY - an equipment ability calls this on
/// whatever IDamageable it just hit, the same "found generically, acts on itself" shape as
/// IStatusReceiver.
/// </summary>
public interface IDisplaceable
{
    /// <summary>
    /// Starts moving this object direction.normalized * distance metres, covering it at speed
    /// metres/second. onEnd fires exactly once, whether the move finished, was blocked, or was
    /// cancelled - a caller that only cares about completion still needs to know a cancellation
    /// happened so it does not wait forever.
    /// </summary>
    void Displace(Vector3 direction, float distance, float speed, System.Action<DisplaceEnd> onEnd);
}
