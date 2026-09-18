using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Weapons;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// Ability visuals step 1 (Tudor, 2026-09-17: make the abilities clearer - visual only). Pins every gameplay number on
    /// the prefabs the visual work touches, at the values they had before it started, and that none of them carries a
    /// collider. If a visual change moves one of these, it changed the gameplay: stop, don't edit the number here.
    /// Read-only: prefab assets are loaded, never instantiated or saved.
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

        private static void Float(SerializedObject so, string name, float expected) =>
            Assert.AreEqual(expected, Field(so, name).floatValue, 1e-5f, $"{so.targetObject.GetType().Name}.{name}");

        private static void Int(SerializedObject so, string name, int expected) =>
            Assert.AreEqual(expected, Field(so, name).intValue, $"{so.targetObject.GetType().Name}.{name}");

        private static void Bool(SerializedObject so, string name, bool expected) =>
            Assert.AreEqual(expected, Field(so, name).boolValue, $"{so.targetObject.GetType().Name}.{name}");

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
        public void MineNumbersAreUnchanged()
        {
            SerializedObject mine = Fields<Mine>("Assets/Resources/Mine.prefab");
            Float(mine, "lifetimeSeconds", 45f);
            Float(mine, "damage", 20f);
            Float(mine, "slowMagnitude", 0.4f);
            Float(mine, "slowSeconds", 2f);
            Float(mine, "triggerRadius", 1.8f);
            Float(mine, "explosionRadius", 2.2f);
            Float(mine, "armDelaySeconds", 0.5f);
            Float(mine, "destroyDelaySeconds", 0.5f);
            Int(mine, "detectionMask", -1);
            // A5 (Tudor 2026-09-17 evening): mines turn invisible 1 s after placement, as the GDD always said - an
            // INTENDED gameplay change, so this pin is new on purpose (see the commit message).
            Float(mine, "invisibleAfterSeconds", 1f);
            PhotonViews("Assets/Resources/Mine.prefab", 1);

            SerializedObject ability = Fields<MineAbility>("Assets/Gameplay/Abilities/Mines.prefab");
            Float(ability, "cooldownSeconds", 10f);
            Int(ability, "charges", 2);
            Int(ability, "maxActiveMines", 4);
            AssetAt(ability, "minePrefab", "Assets/Resources/Mine.prefab");
            // A9 (Tudor 2026-09-17 evening): mines can be placed anywhere within 2 m of the player now, not only at
            // their feet - an INTENDED gameplay change, so this pin moves on purpose (see the commit message).
            Float(ability, "placementRange", 2f);
        }

        [Test]
        public void PortalNumbersAreUnchanged()
        {
            SerializedObject portal = Fields<Portal>("Assets/Resources/Portal.prefab");
            Float(portal, "lifetimeSeconds", 0f);
            Float(portal, "portalDiameter", 2.5f);
            PhotonViews("Assets/Resources/Portal.prefab", 1);

            SerializedObject ability = Fields<TeleportAbility>("Assets/Gameplay/Abilities/Teleport.prefab");
            Float(ability, "cooldownSeconds", 10f);
            Int(ability, "charges", 1);
            Float(ability, "placementRange", 5f);
            Int(ability, "maxPortals", 2);
            Float(ability, "channelSeconds", 3f);
            AssetAt(ability, "portalPrefab", "Assets/Resources/Portal.prefab");
        }

        [Test]
        public void ElectricFenceNumbersAreUnchanged()
        {
            SerializedObject fence = Fields<ElectricFence>("Assets/Resources/Electric Fence.prefab");
            Float(fence, "lifetimeSeconds", 8f);
            Float(fence, "radius", 6f);
            Float(fence, "ringThickness", 1f);
            Float(fence, "damagePerPass", 25f);
            Float(fence, "perTargetCooldownSeconds", 1f);
            Float(fence, "slowMagnitude", 0.5f);
            Float(fence, "slowSeconds", 1.5f);
            Bool(fence, "followsCaster", false);
            Int(fence, "detectionMask", -1);
            PhotonViews("Assets/Resources/Electric Fence.prefab", 2); // two today - flagged for Tudor, not fixed here

            SerializedObject ability = Fields<ElectricFenceAbility>("Assets/Gameplay/Abilities/Electric Fence.prefab");
            Float(ability, "cooldownSeconds", 0f);
            Int(ability, "charges", 1);
            AssetAt(ability, "fencePrefab", "Assets/Resources/Electric Fence.prefab");
        }

        [Test]
        public void FlamethrowerNumbersAreUnchanged()
        {
            SerializedObject flame = Fields<FlamethrowerAbility>("Assets/Gameplay/Abilities/Flamethrower.prefab");
            Float(flame, "cooldownSeconds", 13f);
            Int(flame, "charges", 1);
            Float(flame, "burnDamagePerSecond", 5f);
            Float(flame, "burnSeconds", 5f);
            Float(flame, "coneAngle", 45f);
            Float(flame, "coneRange", 7f);
            Float(flame, "spraySeconds", 1f);
            Int(flame, "detectionMask", -1);
        }

        [Test]
        public void ZipGunNumbersAreUnchanged()
        {
            SerializedObject zip = Fields<ZipGunAbility>("Assets/Gameplay/Abilities/Zip Gun.prefab");
            Float(zip, "cooldownSeconds", 15f);
            Int(zip, "charges", 1);
            Float(zip, "range", 15f);
            Float(zip, "projectileSpeed", 40f);
            // A4 (Tudor 2026-09-17 evening): the hook's hit size grows by 50% - an INTENDED gameplay change, so
            // this pin moves on purpose (see the commit message). 0.15 -> 0.225 m, the real authored field
            // (ProjectileMotor.cs:179 reads THIS value into its sphere cast; nothing else holds a copy).
            Float(zip, "projectileRadius", 0.225f);
            Float(zip, "pullSpeed", 25f);
            AssetAt(zip, "projectilePrefab", "Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab");

            SerializedObject motor = Fields<ProjectileMotor>("Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab");
            Int(motor, "hitMask", 9);
            Float(motor, "maxLifetimeSeconds", 5f);
            Assert.IsNotNull(Load("Assets/Gameplay/Projectiles/Zip Gun Bullet.prefab").GetComponent<AbilityHitRelay>());
        }

        // The Cursor rocket's blast is 2.5 m on purpose (Tudor, 2026-09-18): it matches the Fire Field it leaves on the
        // ground (radius 2.5), so the blast and the fire read as one area. The other two stay at 3 m.
        [TestCase("Assets/Gameplay/Projectiles/Rocket.prefab", 3f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Distance.prefab", 3f)]
        [TestCase("Assets/Gameplay/Projectiles/Rocket Cursor.prefab", 2.5f)]
        public void RocketSplashNumbersAreUnchanged(string path, float splashRadius)
        {
            SerializedObject explode = Fields<ExplodeOnImpact>(path);
            Float(explode, "splashDamage", 20f);
            Float(explode, "splashRadius", splashRadius);
            Float(explode, "occlusionNudge", 0.1f);
            Int(explode, "splashMask", 1);
            Keyframe[] keys = Field(explode, "falloff").animationCurveValue.keys;
            Assert.AreEqual(2, keys.Length, "falloff keys");
            Assert.AreEqual(0f, keys[0].time, 1e-5f);
            Assert.AreEqual(1f, keys[0].value, 1e-5f);
            Assert.AreEqual(1f, keys[1].time, 1e-5f);
            Assert.AreEqual(0f, keys[1].value, 1e-5f);

            SerializedObject motor = Fields<ProjectileMotor>(path);
            Int(motor, "hitMask", 9);
            Float(motor, "maxLifetimeSeconds", 5f);
        }

        [Test]
        public void CursorRocketFireFieldNumbersAreUnchanged()
        {
            // A2 (Tudor 2026-09-17 evening, gameplay change) fact, recorded here rather than as a new assertion:
            // DetonateAtCursor.OnExpired now spawns this prefab at the FLOOR point below the burst
            // (GroundSnap.TryFindGroundY on the shooter's own client, replicated by PhotonNetwork.Instantiate's own
            // position argument - no new RPC), not at the rocket's own muzzle-height position, and
            // FireField.BurnEveryoneInside now burns an upright capsule over the drawn disc instead of a sphere at
            // the field's old floating centre (FireField.OverlapBurnZone, pinned in FireFieldBurnZoneTests against
            // real colliders - a capsule query needs a live PhysicsScene, which this asset-only pin test never
            // opens). None of the serialized numbers below moved; only where the field spawns and what shape burns.
            AssetAt(Fields<DetonateAtCursor>("Assets/Gameplay/Projectiles/Rocket Cursor.prefab"), "fireFieldPrefab",
                "Assets/Resources/Fire Field.prefab");
            SerializedObject field = Fields<FireField>("Assets/Resources/Fire Field.prefab");
            Float(field, "duration", 3f);
            Float(field, "damagePerSecond", 12f);
            Float(field, "radius", 2.5f);
            Float(field, "tickInterval", 1f);
            Int(field, "burnMask", 1);
            PhotonViews("Assets/Resources/Fire Field.prefab", 1);
        }
    }
}
