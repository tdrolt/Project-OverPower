namespace Overpower.Abilities
{
    /// <summary>
    /// A visual-only component on a NetworkedDeployable prefab (ability visuals step 2): the mine's, portal's and fence's
    /// looks. NetworkedDeployable calls it on EVERY client, a late joiner's replay included, right after OnPlaced - so
    /// OwnerActor, OwnerTeam and anything OnPlaced unpacks (a portal's diameter) are already set - and before an
    /// already-expired copy hides its renderers. It must only build and colour visuals.
    /// </summary>
    public interface IDeployableView
    {
        void OnDeployablePlaced(NetworkedDeployable deployable);
    }
}
