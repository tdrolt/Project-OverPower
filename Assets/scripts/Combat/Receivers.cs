namespace Overpower.Combat
{
    /// <summary>
    /// The status-effect equivalent of IDamageable: found the same way, with the same
    /// "GetComponentInParent, then let the callee decide" shape, so an equipment ability that
    /// applies a slow or a stun to whatever it hit does not need to know that thing is a
    /// PlayerStatusEffects specifically. DummyTarget has nothing to apply a status to and does not
    /// implement this - a dummy can be damaged, but it has no motor to slow or stun.
    ///
    /// Implementations are owner-only the same way ApplyDamage is (see IDamageable): every client
    /// finds this component and calls ApplyStatus on every hit, and the implementation itself
    /// decides whether IsMine before touching anything. "Every client calls, only the owner acts"
    /// is what keeps a status effect from being simulated twice - once by the victim, once by
    /// whichever remote copy the projectile also swept through.
    /// </summary>
    public interface IStatusReceiver
    {
        void ApplyStatus(in StatusEffectSpec spec, int sourceActor);
    }

    /// <summary>
    /// Marks an IDamageable as a STRUCTURE rather than a combatant - deployable cover today, a
    /// future barricade or turret later. Found the same way IStatusReceiver is (an `is`/`as` check
    /// on an IDamageable already in hand), so nothing that already found a target needs a second
    /// lookup: a mine's trigger and a beam's pierce both ask "is this thing I already have a
    /// structure", not "go find something else".
    ///
    /// WHY A MARKER, NOT A TeamId/ActorNumber RULE (Task 1.8b review finding). CoverWall reports
    /// ActorNumber/TeamId as -1/-1 so FriendlyFire fails open and every shot treats it as a wall -
    /// but DummyTarget's ActorNumber is ALSO -1 (it owns no Photon actor) and its TeamId is 99, a
    /// second value outside every real team for the identical fail-open reason. Neither field can
    /// tell "an unowned structure" apart from "a practice dummy with no real team" - a `TeamId >= 0`
    /// or `ActorNumber >= 0` check would silently stop dummies from triggering mines too, which
    /// MineTargetingTests already pins down (ADummyWithAnUnrecognisedTeamFailsOpenAsAnEnemy). A
    /// structure says so explicitly instead, and nothing about identity has to change.
    /// </summary>
    public interface IStructure
    {
    }
}
