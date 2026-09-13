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
}
