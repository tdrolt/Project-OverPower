using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Vision;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The Blink leaves a burst of silver triangle shards where the player left and snaps a flat silver hexagon ring out where they
    /// land, on the other clients, each only where that client's team sees that spot.</summary>
    public class BlinkLookTests
    {
        private const BindingFlags Any = MovementLookTestKit.Any;
        private const int BlinkId = 15;

        [Test]
        public void ShardsSpreadAllRoundAndFlyOutward()
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < 8; i++)
            {
                Vector3 d = BlinkLook.ShardDirection(i, 8, 20f);
                Assert.AreEqual(1f, d.magnitude, 1e-4f, "a direction");
                Assert.AreEqual(Mathf.Sin(20f * Mathf.Deg2Rad), Mathf.Abs(d.y), 1e-4f, "tilted up or down by the tilt");
                sum += new Vector3(d.x, 0f, d.z);
                for (int j = 0; j < i; j++)
                    Assert.Greater(Vector3.Angle(d, BlinkLook.ShardDirection(j, 8, 20f)), 20f, "no two shards fly the same way");
            }
            Assert.Less(sum.magnitude, 1e-3f, "evenly round the spot, so the burst has no favourite side");
            Assert.Less(BlinkLook.ShardDirection(0, 8, 20f).y * BlinkLook.ShardDirection(1, 8, 20f).y, 0f, "neighbours alternate up and down");
        }

        [Test]
        public void ShardsReachTheirDistanceAndStop()
        {
            Assert.AreEqual(0f, BlinkLook.ShardReach(0f, 0.35f, 1.3f), 1e-5f);
            Assert.AreEqual(1.3f, BlinkLook.ShardReach(0.35f, 0.35f, 1.3f), 1e-5f);
            Assert.AreEqual(1.3f, BlinkLook.ShardReach(2f, 0.35f, 1.3f), 1e-5f, "never further");
            float last = -1f;
            for (float t = 0f; t <= 0.35f; t += 0.035f)
            {
                float r = BlinkLook.ShardReach(t, 0.35f, 1.3f);
                Assert.GreaterOrEqual(r, last, "always outward");
                last = r;
            }
            Assert.Greater(BlinkLook.ShardReach(0.0875f, 0.35f, 1.3f), 1.3f * 0.25f, "fast first, slowing down: a burst");
        }

        [Test]
        public void TheRingSnapsOutThenKeepsWideningWhileItFades()
        {
            Assert.AreEqual(0f, BlinkLook.RingRadius(0f, 0.12f, 0.3f, 0.9f, 0.4f), 1e-5f);
            Assert.AreEqual(0.9f, BlinkLook.RingRadius(0.12f, 0.12f, 0.3f, 0.9f, 0.4f), 1e-5f, "full size at the end of the snap");
            Assert.Greater(BlinkLook.RingRadius(0.04f, 0.12f, 0.3f, 0.9f, 0.4f), 0.9f / 3f, "a snap: most of the way out in the first third");
            Assert.AreEqual(0.9f * 1.2f, BlinkLook.RingRadius(0.27f, 0.12f, 0.3f, 0.9f, 0.4f), 1e-4f, "then keeps widening as it fades, so it reads as a burst");
            Assert.AreEqual(0.9f * 1.4f, BlinkLook.RingRadius(0.42f, 0.12f, 0.3f, 0.9f, 0.4f), 1e-4f, "Blink Ring Spread wider when it is gone");
            Assert.AreEqual(0.9f * 1.4f, BlinkLook.RingRadius(5f, 0.12f, 0.3f, 0.9f, 0.4f), 1e-4f, "and no further");
            Assert.AreEqual(0.9f, BlinkLook.RingRadius(0.3f, 0.12f, 0.3f, 0.9f, 0f), 1e-5f, "no spread: it holds");
            Assert.AreEqual(1f, BlinkLook.RingAlpha(0.1f, 0.12f, 0.3f), 1e-5f, "solid while it snaps");
            Assert.AreEqual(0.5f, BlinkLook.RingAlpha(0.27f, 0.12f, 0.3f), 1e-4f, "then fades");
            Assert.AreEqual(0f, BlinkLook.RingAlpha(0.5f, 0.12f, 0.3f), 1e-5f);
        }

        [Test]
        public void TheDepartureIsSilverShardsAndTheArrivalAFlatHexRingFromTheLookTable()
        {
            using (var kit = new MovementLookTestKit(BlinkId, LookShape.Shards))
            {
                kit.Look.blinkShardCount = 8;
                kit.Look.blinkShardLength = 0.35f;
                kit.Look.blinkShardWidth = 0.16f;
                kit.Look.blinkShardReach = 1.3f;
                kit.Look.blinkShardHeight = 0.8f;
                kit.Look.blinkShardSeconds = 0.35f;
                kit.Look.blinkRingRadius = 0.9f;
                kit.Look.blinkRingWidth = 0.1f;
                kit.Look.blinkRingSnapSeconds = 0.12f;
                kit.Look.blinkRingFadeSeconds = 0.3f;
                kit.Look.blinkRingSpread = 0.4f;
                kit.Look.blinkAlpha = 0.9f;
                GameObject go = kit.NewObject("blink view test");
                kit.GiveDefinition(go.AddComponent<BlinkAbility>(), BlinkId);
                BlinkMarkView view = go.AddComponent<BlinkMarkView>();
                MovementLookTestKit.SetTheme(view, kit.Theme);
                Vector3 left = new Vector3(2f, 500f, 1f), landed = new Vector3(8f, 500f, 1f); // far above the arena: no floor under them

                view.PlayDeparture(left, 1);
                view.PlayArrival(landed, 1);
                kit.Make(view.Shards);
                kit.Make(view.Ring);
                view.DrawShards(0.175f);
                view.DrawRing(0.2f);

                Assert.IsNull(view.Shards.transform.parent, "left behind where the blink started");
                LineRenderer[] shards = view.Shards.GetComponentsInChildren<LineRenderer>(true);
                Assert.AreEqual(8, shards.Length, "Blink Shard Count");
                Vector3 centre = left + Vector3.up * 0.8f;
                foreach (LineRenderer shard in shards)
                {
                    Assert.AreSame(kit.Fx, shard.sharedMaterial);
                    Assert.AreEqual(2, shard.positionCount);
                    Assert.AreEqual(0f, shard.widthCurve.Evaluate(1f), 1e-3f, "a triangle: wide at its base, a point at its tip");
                    Assert.AreEqual(0.16f, shard.widthMultiplier * shard.widthCurve.Evaluate(0f), 1e-3f, "Blink Shard Width at the base");
                    Vector3 b = shard.GetPosition(0), tip = shard.GetPosition(1);
                    Assert.AreEqual(0.35f, Vector3.Distance(b, tip), 1e-3f, "Blink Shard Length");
                    Assert.Greater(Vector3.Distance(tip, centre), Vector3.Distance(b, centre), "pointing outward");
                    Assert.AreEqual(BlinkLook.ShardReach(0.175f, 0.35f, 1.3f), Vector3.Distance(b, centre), 1e-3f, "flying out from where the player stood");
                    MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, shard.startColor, "the colour of the Blink's row in the look table");
                    Assert.AreEqual(0.9f * AbilityVisualGeometryFade(0.175f, 0.35f), shard.startColor.a, 1f / 255f, "fading as they fly");
                }

                Assert.IsNull(view.Ring.transform.parent);
                LineRenderer ring = view.Ring.GetComponentInChildren<LineRenderer>(true);
                Assert.AreSame(kit.Fx, ring.sharedMaterial);
                Assert.AreEqual(6, ring.positionCount, "a hexagon");
                Assert.IsTrue(ring.loop);
                Assert.AreEqual(LineAlignment.TransformZ, ring.alignment, "lying flat on the floor");
                float radius = BlinkLook.RingRadius(0.2f, 0.12f, 0.3f, 0.9f, 0.4f);
                Assert.Greater(radius, 0.9f, "past Blink Ring Radius and widening while it fades");
                for (int k = 0; k < 6; k++)
                {
                    Vector3 p = ring.transform.TransformPoint(ring.GetPosition(k));
                    Vector3 fromCentre = new Vector3(p.x - landed.x, 0f, p.z - landed.z);
                    Assert.AreEqual(radius, fromCentre.magnitude, 1e-3f, "the rule's size");
                    Assert.Less(p.y, 500.1f, "on the floor where the player landed");
                    float offGrid = Mathf.Repeat(Vector3.SignedAngle(Vector3.forward, fromCentre, Vector3.up), 60f);
                    Assert.AreEqual(30f, offGrid, 0.5f, "flat-topped: turned half a side from the floor's pointy-topped hex tiles, so it never reads as one");
                }
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, ring.startColor, "ring colour");
                Assert.AreEqual(0.9f * BlinkLook.RingAlpha(0.2f, 0.12f, 0.3f), ring.startColor.a, 1f / 255f);
            }
        }

        private static float AbilityVisualGeometryFade(float age, float seconds) => Overpower.Combat.AbilityVisualGeometry.Fade01(age, seconds);

        [Test]
        public void EachMarkShowsOnlyWhereTheTeamSeesItsSpot()
        {
            MethodInfo shown = typeof(TeamSight).GetMethod(nameof(TeamSight.ShotShownAt));
            MethodInfo cast = typeof(BlinkAbility).GetMethod(nameof(BlinkAbility.ExecuteCast));
            Assert.IsTrue(IlWiring.ResultDecidesABranch(typeof(BlinkAbility), nameof(BlinkAbility.ExecuteCast), shown), "the sight check decides whether a mark is drawn");
            var shownAt = IlWiring.CallOffsets(cast, shown);
            var leftAt = IlWiring.CallOffsets(cast, typeof(BlinkMarkView).GetMethod(nameof(BlinkMarkView.PlayDeparture)));
            var landedAt = IlWiring.CallOffsets(cast, typeof(BlinkMarkView).GetMethod(nameof(BlinkMarkView.PlayArrival)));
            Assert.AreEqual(2, shownAt.Count, "one check per spot");
            Assert.AreEqual(1, leftAt.Count);
            Assert.AreEqual(1, landedAt.Count);
            Assert.Less(shownAt[0], leftAt[0], "the start is checked before its shards");
            Assert.Less(leftAt[0], shownAt[1]);
            Assert.Less(shownAt[1], landedAt[0], "the landing is checked before its ring");
            Assert.IsTrue(IlWiring.Uses(typeof(BlinkMarkView), nameof(BlinkMarkView.DrawShards), typeof(BlinkLook).GetMethod(nameof(BlinkLook.ShardDirection))));
            Assert.IsTrue(IlWiring.Uses(typeof(BlinkMarkView), nameof(BlinkMarkView.DrawShards), typeof(BlinkLook).GetMethod(nameof(BlinkLook.ShardReach))));
            Assert.IsTrue(IlWiring.Uses(typeof(BlinkMarkView), nameof(BlinkMarkView.DrawRing), typeof(BlinkLook).GetMethod(nameof(BlinkLook.RingRadius))));
            Assert.IsTrue(IlWiring.Uses(typeof(BlinkMarkView), nameof(BlinkMarkView.DrawRing), typeof(BlinkLook).GetMethod(nameof(BlinkLook.RingAlpha))));
            Assert.IsTrue(IlWiring.Uses(typeof(BlinkMarkView), nameof(BlinkMarkView.DrawRing), typeof(VisualTint).GetMethod(nameof(VisualTint.FillFlatCircle))));
            Assert.IsTrue(IlWiring.Uses(typeof(BlinkMarkView), typeof(LookColours).GetMethod(nameof(LookColours.ForAbility))));
            Assert.IsNull(typeof(BlinkAbility).GetField("remoteVfxRadius", Any), "no sphere size: the marks are shards and a ring");
            Assert.IsFalse(IlWiring.Uses(typeof(BlinkAbility), typeof(GameObject).GetMethod(nameof(GameObject.CreatePrimitive))), "no primitive sphere");
        }

        [Test]
        public void DestroyFreesTheMarks()
        {
            int shardsBefore = MovementLookTestKit.Live(BlinkMarkView.ShardsName), ringBefore = MovementLookTestKit.Live(BlinkMarkView.RingName);
            using (var kit = new MovementLookTestKit(BlinkId, LookShape.Shards))
            {
                GameObject go = new GameObject("blink leak test");
                BlinkMarkView view = go.AddComponent<BlinkMarkView>();
                MovementLookTestKit.SetTheme(view, kit.Theme);
                view.PlayDeparture(new Vector3(0f, 500f, 0f), 0);
                view.PlayArrival(new Vector3(3f, 500f, 0f), 0);
                Assert.AreEqual(shardsBefore + 1, MovementLookTestKit.Live(BlinkMarkView.ShardsName));
                Assert.AreEqual(ringBefore + 1, MovementLookTestKit.Live(BlinkMarkView.RingName));
                MovementLookTestKit.DestroyLikeTheGame(go);
            }
            Assert.AreEqual(shardsBefore, MovementLookTestKit.Live(BlinkMarkView.ShardsName), "the shards leaked past their view");
            Assert.AreEqual(ringBefore, MovementLookTestKit.Live(BlinkMarkView.RingName), "the ring leaked past its view");
        }

        [Test]
        public void BlinkCarriesTheMarkView()
        {
            GameObject blink = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Abilities/Blink.prefab");
            BlinkMarkView view = blink.GetComponent<BlinkMarkView>();
            Assert.IsNotNull(view);
            Assert.AreEqual("Assets/Gameplay/Config/UiTheme.asset", AssetDatabase.GetAssetPath(new SerializedObject(view).FindProperty("theme").objectReferenceValue));
            Assert.IsEmpty(blink.GetComponentsInChildren<Collider>(true));
        }
    }
}
