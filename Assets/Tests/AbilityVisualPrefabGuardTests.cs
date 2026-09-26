using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Weapons;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Ability visuals step 1 (Tudor, 2026-09-17: make the abilities clearer - visual only). Rewritten 2026-09-24
    /// (Tudor: "remove tests that are outdated" - he tunes every gameplay number from the Inspector, so a test
    /// pinning a literal here used to go red on every balance pass with nothing actually broken). Guards WIRING and
    /// STRUCTURE only, never a value Tudor can retune freely: prefab references point where they should
    /// (AssetAt), networked prefabs carry the PhotonView count they need, a required component exists
    /// (AbilityHitRelay), the falloff curve's SHAPE stays full-damage-at-zero to less-at-the-edge (every splash
    /// weapon's actual rule, not its damage/radius numbers), and that none of the touched prefabs carries a
    /// collider (they are visual/ability objects, not physical ones). Read-only: prefab assets are loaded, never
    /// instantiated or saved.
    /// </summary>
    public class AbilityVisualPrefabGuardTests
    {
        private static readonly string[] TouchedPrefabs =
        {
            "Assets/Resources/Mine.prefab",
            "Assets/Resources/Portal.prefab",
            "Assets/Resources/Electric Fence.prefab",
            "Assets/Resources/Fire Field.prefab",
            "Assets/Gameplay/Projectiles/Rocket.prefab",
            "Assets/Gameplay/Projectiles/Rocket Distance.prefab",
            "Assets/Gameplay/Projectiles/Rocket Cursor.prefab",
            "Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab",
            "Assets/Gameplay/Abilities/Mines.prefab",
            "Assets/Gameplay/Abilities/Teleport.prefab",
            "Assets/Gameplay/Abilities/Electric Fence.prefab",
            "Assets/Gameplay/Abilities/Flamethrower.prefab",
            "Assets/Gameplay/Abilities/Zip Gun.prefab",
        };

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);
            return prefab;
        }

        private static SerializedObject Fields<T>(string path) where T : Component
        {
            T component = Load(path).GetComponent<T>();
            Assert.IsNotNull(component, $"{path} has no {typeof(T).Name}");
            return new SerializedObject(component);
        }

        private static SerializedProperty Field(SerializedObject so, string name)
        {
            SerializedProperty property = so.FindProperty(name);
            Assert.IsNotNull(property, $"{so.targetObject.GetType().Name}.{name}");
            return property;
        }

        private static void AssetAt(SerializedObject so, string name, string expectedPath) =>
            Assert.AreEqual(expectedPath, AssetDatabase.GetAssetPath(Field(so, name).objectReferenceValue),
                $"{so.targetObject.GetType().Name}.{name}");

        private static void PhotonViews(string path, int expected) =>
            Assert.AreEqual(expected, Load(path).GetComponentsInChildren<PhotonView>(true).Length, path + " PhotonViews");

        [Test]
        public void NoTouchedPrefabHasACollider()
        {
            foreach (string path in TouchedPrefabs)
                Assert.IsEmpty(Load(path).GetComponentsInChildren<Collider>(true), path + " must have no collider");
        }

        [Test]
        public void MineIsWired()
        {
            PhotonViews("Assets/Resources/Mine.prefab", 1);

            SerializedObject ability = Fields<MineAbility>("Assets/Gameplay/Abilities/Mines.prefab");
            AssetAt(ability, "minePrefab", "Assets/Resources/Mine.prefab");
        }

        [Test]
        public void PortalIsWired()
        {
            PhotonViews("Assets/Resources/Portal.prefab", 1);

            SerializedObject ability = Fields<TeleportAbility>("Assets/Gameplay/Abilities/Teleport.prefab");
            AssetAt(ability, "portalPrefab", "Assets/Resources/Portal.prefab");
        }

        [Test]
        public void ElectricFenceIsWired()
        {
            PhotonViews("Assets/Resources/Electric Fence.prefab", 2); // two today - flagged for Tudor, not fixed here

            SerializedObject ability = Fields<ElectricFenceAbility>("Assets/Gameplay/Abilities/Electric Fence.prefab");
            AssetAt(ability, "fencePrefab", "Assets/Resources/Electric Fence.prefab");
        }

        [Test]
        public void ZipGunIsWired()
        {
            SerializedObject zip = Fields<ZipGunAbility>("Assets/Gameplay/Abilities/Zip Gun.prefab");
            AssetAt(zip, "projectilePrefab", "Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab");

            Assert.IsNotNull(Load("Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab").GetComponent<AbilityHitRelay>());
        }

        // The falloff curve's SHAPE (full damage at distance 0, less at the outer edge) is every splash weapon's
        // actual rule, not a value Tudor retunes - unlike splashDamage/splashRadius (his to change freely), which
        // this file no longer pins. hitMask/splashMask are dropped too: raw layer bitmasks are level-design/tuning
        // levers here, not enforced anywhere else as a structural invariant the way a PhotonView count is.
        [TestCase("Assets/Gameplay/Projectiles/Rocket.prefab")]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Distance.prefab")]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Cursor.prefab")]
        public void RocketSplashFalloffShapeIsUnchanged(string path)
        {
            SerializedObject explode = Fields<ExplodeOnImpact>(path);
            Keyframe[] keys = Field(explode, "falloff").animationCurveValue.keys;
            Assert.AreEqual(2, keys.Length, "falloff keys");
            Assert.AreEqual(0f, keys[0].time, 1e-5f);
            Assert.AreEqual(1f, keys[0].value, 1e-5f);
            Assert.AreEqual(1f, keys[1].time, 1e-5f);
            // Tudor, 2026-09-26: the blast now keeps 30% at the edge instead of 0 - so the edge value is his to
            // tune; the rule left is "less at the edge than at the centre, never negative".
            Assert.GreaterOrEqual(keys[1].value, 0f, "the edge never heals");
            Assert.Less(keys[1].value, keys[0].value, "the edge pays less than the centre");
        }

        [Test]
        public void CursorRocketFireFieldIsWired()
        {
            // A2 (Tudor 2026-09-17 evening, gameplay change) fact, recorded here rather than as a new assertion:
            // DetonateAtCursor.OnExpired spawns this prefab at the FLOOR point below the burst
            // (GroundSnap.TryFindGroundY on the shooter's own client, replicated by PhotonNetwork.Instantiate's own
            // position argument - no new RPC), not at the rocket's own muzzle-height position, and
            // FireField.BurnEveryoneInside burns an upright capsule over the drawn disc instead of a sphere at the
            // field's old floating centre (FireField.OverlapBurnZone, pinned in FireFieldBurnZoneTests against real
            // colliders - a capsule query needs a live PhysicsScene, which this asset-only pin test never opens).
            AssetAt(Fields<DetonateAtCursor>("Assets/Gameplay/Projectiles/Rocket Cursor.prefab"), "fireFieldPrefab",
                "Assets/Resources/Fire Field.prefab");
            PhotonViews("Assets/Resources/Fire Field.prefab", 1);
        }
    }
}
