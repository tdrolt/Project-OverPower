using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// A teleport gate placed on the ground - Tudor's Mobility spec: a 2.5m circle, up to two per
    /// player, personal to whoever placed them. This class is only the networked object and the
    /// registry that lets an owner find their own pair; TeleportAbility owns the actual channel that
    /// uses them.
    ///
    /// VISIBLE TO EVERYONE, USABLE BY ONE. Tudor's clarification: an enemy should be able to SEE a
    /// portal - it is a readable tell, "that lane has a gate somewhere" - but never step through it.
    /// That is why this object has no Collider and no trigger at all: TeleportAbility.OwnerTick
    /// checks entry with a plain XZ distance against Portal.ForOwner(Owner.ActorNumber), and
    /// OwnerTick only ever RUNS on the owner's own client (see AbilityModule's class comment on the
    /// owner/every-client split) - "personal" falls out of who is doing the asking, not anything
    /// this class has to enforce with a physics check of its own.
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
        [SerializeField, Tooltip("Diameter of the portal circle, in metres - Tudor's spec: 2.5m. The " +
                 "visual ring is scaled to match. This prefab's own value only previews the size in " +
                 "the Editor; the real value travels with the placement (see TeleportAbility) so " +
                 "every client agrees on the same size even if this prefab is retuned without every " +
                 "machine rebuilding first.")]
        private float portalDiameter = 2.5f;

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

        private static readonly List<Portal> EmptyList = new List<Portal>();
        private static readonly Dictionary<int, List<Portal>> byOwner = new Dictionary<int, List<Portal>>();

        /// <summary>Every portal THIS CLIENT currently believes belongs to the given actor, in no
        /// particular order. Empty, never null, for an actor with none.</summary>
        public static IReadOnlyList<Portal> ForOwner(int actorNumber) =>
            byOwner.TryGetValue(actorNumber, out List<Portal> list) ? list : EmptyList;

        protected override void OnPlaced(object[] data, PhotonMessageInfo info)
        {
            if (data != null && data.Length >= 2)
            {
                portalDiameter = (float)data[0];
                Seq = (int)data[1];
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
