using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Vision;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The Teleport's marks (on a portal while someone channels, and at both ends of a trip on other screens) are flat hexagon rings
    /// in the movement look, not plain grey spheres, and they are gated exactly as the spheres were.</summary>
    public class TeleportRingTests
    {
        private const BindingFlags Any = MovementLookTestKit.Any;
        private const int TeleportId = 17;

        [Test]
        public void TheMarkIsAFlatHexRingOnTheFloor()
        {
            using (var kit = new MovementLookTestKit(TeleportId, LookShape.HexGate))
            {
                kit.Look.teleportRingWidth = 0.07f;
                kit.Look.teleportRingAlpha = 0.6f;
                var colour = new Color(0.31f, 0.62f, 0.44f, 1f);
                GameObject mark = kit.Make(TeleportRing.Build(new Vector3(3f, 600f, 4f), 0.8f, colour, kit.Look));
                Assert.IsNotNull(mark, "a mark is built");
                Assert.AreEqual(TeleportRing.MarkName, mark.name);
                Assert.IsEmpty(mark.GetComponentsInChildren<Collider>(true), "never a solid object, not even for one frame");
                Assert.IsEmpty(mark.GetComponentsInChildren<MeshRenderer>(true), "no sphere");
                LineRenderer[] lines = mark.GetComponentsInChildren<LineRenderer>(true);
                Assert.AreEqual(1, lines.Length, "one ring");
                LineRenderer ring = lines[0];
                Assert.AreEqual(6, ring.positionCount, "a hexagon");
                Assert.IsTrue(ring.loop);
                Assert.AreSame(kit.Look.fxAlpha, ring.sharedMaterial, "the see-through effect material");
                Assert.AreEqual(0.07f, ring.widthMultiplier, 1e-6f, "Teleport Ring Width");
                MovementLookTestKit.AssertRgb(colour, ring.startColor, "the colour it is given");
                Assert.AreEqual(0.6f, ring.startColor.a, 1e-4f, "Teleport Ring Alpha");
                Vector3 centre = new Vector3(3f, 600f, 4f);
                float y = 0f;
                for (int i = 0; i < 6; i++)
                {
                    Vector3 corner = ring.useWorldSpace ? ring.GetPosition(i) : ring.transform.TransformPoint(ring.GetPosition(i));
                    if (i == 0) y = corner.y;
                    Assert.AreEqual(y, corner.y, 1e-4f, "flat on the floor");
                    Assert.AreEqual(0.8f, Vector2.Distance(new Vector2(corner.x, corner.z), new Vector2(centre.x, centre.z)), 1e-4f, "corners on the mark's radius");
                }
                Assert.AreEqual(centre.y, y, 0.1f, "on the floor under the point (no floor here: at the point's own height)");
            }
        }

        [Test]
        public void TheMarkLiesOnTheFloorUnderAPointAboveIt()
        {
            using (var kit = new MovementLookTestKit(TeleportId, LookShape.HexGate))
            {
                GameObject floor = kit.Make(GameObject.CreatePrimitive(PrimitiveType.Cube));
                floor.transform.position = new Vector3(3f, 700f, 4f);
                floor.transform.localScale = new Vector3(4f, 1f, 4f); // top at 700.5
                Physics.SyncTransforms();
                // A Teleport's cast point is at the caster's root, about half a metre over the floor.
                GameObject mark = kit.Make(TeleportRing.Build(new Vector3(3f, 701f, 4f), 0.8f, Color.white, kit.Look));
                Assert.IsNotNull(mark);
                LineRenderer ring = mark.GetComponent<LineRenderer>();
                Vector3 corner = ring.useWorldSpace ? ring.GetPosition(0) : ring.transform.TransformPoint(ring.GetPosition(0));
                Assert.AreEqual(700.5f, corner.y, 0.05f, "snapped down onto the floor, not floating at the root's height");
            }
        }

        [Test]
        public void TheTeleportBuildsTheRingsInTheMovementLookAndGatesThemAsBefore()
        {
            System.Type teleport = typeof(TeleportAbility);
            MethodInfo spawn = teleport.GetMethod("SpawnMarker", Any);
            Assert.IsNotNull(spawn);
            Assert.IsTrue(IlWiring.Uses(teleport, "SpawnMarker", typeof(TeleportRing).GetMethod(nameof(TeleportRing.Build))), "the mark is the hex ring");
            Assert.IsFalse(IlWiring.CallsAcrossAssemblies(teleport, "SpawnMarker", typeof(GameObject).GetMethod(nameof(GameObject.CreatePrimitive))), "the grey sphere is gone");
            Assert.IsTrue(IlWiring.Uses(teleport, "SpawnMarker", typeof(LookColours).GetMethod(nameof(LookColours.For))), "coloured from the look's colour families");

            MethodInfo attach = typeof(VisibleWhenSeen).GetMethod(nameof(VisibleWhenSeen.Attach), new[] { typeof(GameObject), typeof(int) });
            Assert.IsNotNull(attach);
            MethodInfo execute = teleport.GetMethod(nameof(TeleportAbility.ExecuteCast));
            var spawnAt = IlWiring.CallOffsets(execute, spawn);
            var attachAt = IlWiring.CallOffsets(execute, attach);
            Assert.AreEqual(1, spawnAt.Count, "the channel mark");
            Assert.AreEqual(1, attachAt.Count, "follows my team's sight while it lasts");
            Assert.Less(spawnAt[0], attachAt[0], "built complete before the gate collects its renderers");
            Assert.IsTrue(IlWiring.ResultDecidesABranch(teleport, "PlayArrivalVfx", typeof(TeamSight).GetMethod(nameof(TeamSight.ShotShownAt))),
                "a trip's two marks only where my team sees them");
            Assert.IsTrue(IlWiring.Uses(teleport, "PlayArrivalVfx", spawn));
            Assert.IsTrue(IlWiring.Uses(teleport, "OnDestroy", teleport.GetMethod("ClearChannelVfx", Any)),
                "a player who leaves mid-channel takes the channel mark with them");
        }
    }
}
