using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// A teleport gate placed on the ground - Tudor's Mobility spec: a 2.0m circle, up to two per
    /// player, usable by whoever placed them and their teammates. This class is only the networked object and the
    /// registry that lets an owner find their own pair; TeleportAbility owns the actual channel that
    /// uses them.
    ///
    /// VISIBLE TO EVERYONE, USABLE BY THE OWNER'S TEAM. Tudor's clarification: an enemy should be able to SEE a
    /// portal - it is a readable tell, "that lane has a gate somewhere" - but never step through it (Tudor, D15:
    /// a teammate may). That is why this object has no Collider and no trigger at all: entry is a plain XZ
    /// distance check (PortalUseRules.IsOnPortal) made by whoever wants to use it, on their own client - the owner
    /// in TeleportAbility.OwnerTick against Portal.ForOwner(Owner.ActorNumber), a teammate in AllyPortalTraveller
    /// against their teammates' entries. Enemies simply never run either check on it.
    ///
    /// PAIRING is a static PER-CLIENT registry keyed by owner actor number, ordered by placement
    /// (Seq) - not a paired view id carried in instantiationData, because the FIRST portal placed
    /// cannot know its partner's view id yet (it does not exist), and replacing the oldest of three
    /// re-pairs the remaining two automatically just by both still being registered. Every client
    /// keeps its OWN copy of this registry, built from its OWN locally-instantiated Portal objects -
    /// nothing here is itself networked state. That is also why a late joiner replaying the room's
    /// cached instantiations still ends up with a correct registry: OnPlaced runs for them exactly as
    /// it does for everyone else, even though only the actual owner will ever query it.
    /// </summary>
    public sealed class Portal : NetworkedDeployable
    {
        [SerializeField, Tooltip("Diameter of the portal circle, in metres - Tudor's spec (D16): 2.0m. The " +
                 "visual ring is scaled to match. This prefab's own value only previews the size in " +
                 "the Editor; the real value travels with the placement (see TeleportAbility) so " +
                 "every client agrees on the same size even if this prefab is retuned without every " +
                 "machine rebuilding first.")]
        private float portalDiameter = 2.0f;

        [SerializeField, Tooltip("The ring/disc visual, scaled sideways to Portal Diameter when this " +
                 "portal is placed - one prefab draws every size. Left empty draws nothing, which is " +
                 "still a legal (if invisible) portal.")]
        private Transform visual;

        /// <summary>Metres from centre - half of Portal Diameter.</summary>
        public float Radius => portalDiameter * 0.5f;

        /// <summary>The full circle width, in metres, as placed - not necessarily this prefab's own
        /// serialized default; see the field's tooltip.</summary>
        public float PortalDiameter => portalDiameter;

        /// <summary>This portal's place in its owner's placement order - the oldest of more than
        /// TeleportAbility's Max Portals is the one destroyed when a new one is placed. Arrives as
        /// instantiationData; never decided here.</summary>
        public int Seq { get; private set; }

        /// <summary>Seconds of standing on this portal that complete a trip - the owner's Teleport ability's Channel
        /// Seconds, sent along with the placement (its one home) so a teammate's client uses the same number without
        /// needing the ability equipped. 0 when the placement carried none: nobody can channel through it.</summary>
        public float ChannelSeconds { get; private set; }

        private static readonly List<Portal> EmptyList = new List<Portal>();
        private static readonly Dictionary<int, List<Portal>> byOwner = new Dictionary<int, List<Portal>>();

        /// <summary>Every portal THIS CLIENT currently believes belongs to the given actor, in no
        /// particular order. Empty, never null, for an actor with none.</summary>
        public static IReadOnlyList<Portal> ForOwner(int actorNumber) =>
            byOwner.TryGetValue(actorNumber, out List<Portal> list) ? list : EmptyList;

        protected override void OnPlaced(object[] data, PhotonMessageInfo info)
        {
            if (data != null && data.Length >= 3)
            {
                portalDiameter = (float)data[0];
                Seq = (int)data[1];
                ChannelSeconds = (float)data[2];
            }
            else
            {
                Debug.LogError($"[Portal] {name} was placed without its instantiation data, so it is " +
                                "falling back to the prefab's own numbers and may disagree with other clients.");
            }

            if (visual != null)
                visual.localScale = new Vector3(portalDiameter, visual.localScale.y, portalDiameter);

            Register(this);
            Debug.Log($"[Portal] placed by actor {OwnerActor} at {transform.position} - seq={Seq} diameter={portalDiameter:0.##}");
        }

        private void OnDestroy() => Unregister(this);

        private static void Register(Portal portal)
        {
            if (!byOwner.TryGetValue(portal.OwnerActor, out List<Portal> list))
            {
                list = new List<Portal>();
                byOwner.Add(portal.OwnerActor, list);
            }
            list.Add(portal);
        }

        private static void Unregister(Portal portal)
        {
            if (byOwner.TryGetValue(portal.OwnerActor, out List<Portal> list))
                list.Remove(portal);
        }
    }
}
