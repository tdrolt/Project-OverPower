using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;

namespace Overpower.Abilities
{
    /// <summary>
    /// A teleport gate placed on the ground (2.0m circle, up to two per player), usable by its owner and their teammates (D15).
    /// Only the networked object and the registry that lets an owner find their pair; TeleportAbility owns the channel.
    /// VISIBLE TO EVERYONE (an enemy sees the tell), USABLE BY THE OWNER'S TEAM: no Collider or trigger at all, entry is a
    /// plain XZ check (PortalUseRules.IsOnPortal) that each user makes on their own client - the owner in TeleportAbility,
    /// a teammate in AllyPortalTraveller - so enemies never run it. PAIRING is a static per-client registry by owner actor,
    /// ordered by Seq, built from locally instantiated Portals (not networked state): the first portal cannot carry a
    /// partner's view id, and a late joiner replaying cached instantiations still ends up with a correct registry.
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
