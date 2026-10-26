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
            "Assets/_Project/Resources/Mine.prefab",
            "Assets/_Project/Resources/Portal.prefab",
            "Assets/_Project/Resources/Electric Fence.prefab",
            "Assets/_Project/Resources/Fire Field.prefab",
            "Assets/_Project/Features/Weapons/Projectiles/Rocket.prefab",
            "Assets/_Project/Features/Weapons/Projectiles/Rocket Distance.prefab",
            "Assets/_Project/Features/Weapons/Projectiles/Rocket Cursor.prefab",
            "Assets/_Project/Features/Weapons/Projectiles/Zip Gun Bullet.prefab",
            "Assets/_Project/Features/Abilities/Data/Mines.prefab",
            "Assets/_Project/Features/Abilities/Data/Teleport.prefab",
            "Assets/_Project/Features/Abilities/Data/Electric Fence.prefab",
            "Assets/_Project/Features/Abilities/Data/Flamethrower.prefab",
            "Assets/_Project/Features/Abilities/Data/Zip Gun.prefab",
            StunBulletPath,
            StunBurstPath,
            "Assets/_Project/Features/Abilities/Data/Sonic Pulse.prefab",
            FlameConePath,
        };

        private const string StunBulletPath = "Assets/_Project/Features/Weapons/Projectiles/Stun Gun Bullet.prefab";
        private const string StunBurstPath = "Assets/_Project/Shared/FX/Impacts/FX Stun Burst.prefab";
        private const string FlameConePath = "Assets/_Project/Features/Abilities/Data/Flamethrower Cone.prefab";
        private const string LookPath = "Assets/_Project/Shared/Config/ArenaLook.asset";
        private const string ThemePath = "Assets/_Project/Shared/Config/UiTheme.asset";
        private const int StunGunId = 22;

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
            PhotonViews("Assets/_Project/Resources/Mine.prefab", 1);

            SerializedObject ability = Fields<MineAbility>("Assets/_Project/Features/Abilities/Data/Mines.prefab");
            AssetAt(ability, "minePrefab", "Assets/_Project/Resources/Mine.prefab");
        }

        [Test]
        public void PortalIsWired()
        {
            PhotonViews("Assets/_Project/Resources/Portal.prefab", 1);

            SerializedObject ability = Fields<TeleportAbility>("Assets/_Project/Features/Abilities/Data/Teleport.prefab");
            AssetAt(ability, "portalPrefab", "Assets/_Project/Resources/Portal.prefab");
        }

        [Test]
        public void ElectricFenceIsWired()
        {
            PhotonViews("Assets/_Project/Resources/Electric Fence.prefab", 2); // two today - flagged for Tudor, not fixed here

            SerializedObject ability = Fields<ElectricFenceAbility>("Assets/_Project/Features/Abilities/Data/Electric Fence.prefab");
            AssetAt(ability, "fencePrefab", "Assets/_Project/Resources/Electric Fence.prefab");
        }

        [Test]
        public void ZipGunIsWired()
        {
            SerializedObject zip = Fields<ZipGunAbility>("Assets/_Project/Features/Abilities/Data/Zip Gun.prefab");
            AssetAt(zip, "projectilePrefab", "Assets/_Project/Features/Weapons/Projectiles/Zip Gun Bullet.prefab");

            Assert.IsNotNull(Load("Assets/_Project/Features/Weapons/Projectiles/Zip Gun Bullet.prefab").GetComponent<AbilityHitRelay>());
        }

        // The falloff curve's SHAPE (full damage at distance 0, less at the outer edge) is every splash weapon's
        // actual rule, not a value Tudor retunes - unlike splashDamage/splashRadius (his to change freely), which
        // this file no longer pins. hitMask/splashMask are dropped too: raw layer bitmasks are level-design/tuning
        // levers here, not enforced anywhere else as a structural invariant the way a PhotonView count is.
        [TestCase("Assets/_Project/Features/Weapons/Projectiles/Rocket.prefab")]
        [TestCase("Assets/_Project/Features/Weapons/Projectiles/Rocket Distance.prefab")]
        [TestCase("Assets/_Project/Features/Weapons/Projectiles/Rocket Cursor.prefab")]
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
            AssetAt(Fields<DetonateAtCursor>("Assets/_Project/Features/Weapons/Projectiles/Rocket Cursor.prefab"), "fireFieldPrefab",
                "Assets/_Project/Resources/Fire Field.prefab");
            PhotonViews("Assets/_Project/Resources/Fire Field.prefab", 1);
        }

        [Test]
        public void StunBulletIsNotTheBuiltInSphere()
        {
            GameObject bullet = Load(StunBulletPath);
            Assert.IsNull(bullet.GetComponent<MeshFilter>(), "the bolt is a child, so it can be drawn thicker across without touching the root");
            Transform bolt = bullet.transform.Find("Bolt");
            Assert.IsNotNull(bolt, "the bolt's mesh");
            Mesh mesh = bolt.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(mesh);
            Assert.AreNotEqual("Sphere", mesh.name, "the 0.6 m ball is gone");
            Assert.AreEqual("Assets/_Project/Shared/Art/StunBolt.fbx", AssetDatabase.GetAssetPath(mesh), "the faceted bolt from Blender");
            Assert.Less(mesh.triangles.Length / 3, 300, "a small low-poly model");
            // How big and how thick it is drawn is tuning (the Bolt child's scale); only its being a bolt along its flight is checked.
            Vector3 size = Vector3.Scale(mesh.bounds.size, Vector3.Scale(bullet.transform.localScale, bolt.localScale));
            Assert.Greater(size.z, Mathf.Max(size.x, size.y), "a bolt: longer along its flight (+Z) than across");
            Assert.IsEmpty(bullet.GetComponentsInChildren<Collider>(true), "hits come from the motor's sphere cast, never a collider");
        }

        [Test]
        public void StunBulletHasNoShotTeamVisuals()
        {
            GameObject bullet = Load(StunBulletPath);
            Assert.IsNull(bullet.GetComponent<ShotTeamVisuals>(), "an ability shot is not a team-coloured weapon shot");
            SerializedObject tint = Fields<LookTint>(StunBulletPath);
            Assert.AreEqual(StunGunId, Field(tint, "abilityId").intValue, "the Stun Gun row of the look table");
            AssetAt(tint, "theme", ThemePath);
            SerializedObject motor = Fields<ProjectileMotor>(StunBulletPath);
            AssetAt(motor, "fallbackImpactVfx", StunBurstPath);
            Assert.IsNotNull(bullet.GetComponentInChildren<ParticleSystem>(true), "a crackle behind the bolt");

            SerializedObject burstTint = Fields<LookTint>(StunBurstPath);
            Assert.AreEqual(StunGunId, Field(burstTint, "abilityId").intValue, "the burst is blue like the bolt");
            AssetAt(burstTint, "theme", ThemePath);
            Assert.IsNotNull(Load(StunBurstPath).GetComponent<ParticleSystem>(), "VFXManager times the burst from its particle systems");
        }

        [Test]
        public void PulseHasAView()
        {
            SerializedObject view = Fields<SonicPulseView>("Assets/_Project/Features/Abilities/Data/Sonic Pulse.prefab");
            AssetAt(view, "theme", ThemePath);
            Assert.IsNotNull(Load("Assets/_Project/Features/Abilities/Data/Sonic Pulse.prefab").GetComponent<SonicPulseAbility>());
        }

        [Test]
        public void FlameConeHasTongues()
        {
            GameObject cone = Load(FlameConePath);
            Transform tongues = cone.transform.Find(FlameConeVisual.TonguesChildName);
            Assert.IsNotNull(tongues, "a 'Flame Tongues' child on the cone");
            ParticleSystem system = tongues.GetComponent<ParticleSystem>();
            Assert.IsNotNull(system);
            Assert.AreSame(system, new SerializedObject(cone.GetComponent<FlameConeVisual>()).FindProperty("tongues").objectReferenceValue);
            var look = AssetDatabase.LoadAssetAtPath<Overpower.Arena.ArenaLook>(LookPath);
            var flames = tongues.GetComponent<ParticleSystemRenderer>();
            Assert.IsNotNull(look.flameJetMaterial, "Arena Look > Flame Jet Material");
            Assert.AreSame(look.flameJetMaterial, flames.sharedMaterial, "the flames are soft fire puffs that keep their colour over any floor (FX Flame Jet)");
            Assert.AreEqual("Universal Render Pipeline/Particles/Unlit", look.flameJetMaterial.shader.name, "draws the fire colours each flame is given");
            Assert.IsNotNull(look.flameJetMaterial.GetTexture("_BaseMap"), "the fire sheet");
            Assert.AreEqual(ParticleSystemRenderMode.Billboard, flames.renderMode, "round puffs facing the camera, never stretched into spikes");
            Assert.IsTrue(system.textureSheetAnimation.enabled, "each puff one shape of the fire sheet");
            foreach (ParticleSystemRenderer renderer in tongues.GetComponentsInChildren<ParticleSystemRenderer>(true))
                Assert.IsTrue(renderer.sharedMaterial == look.flameJetMaterial || renderer.sharedMaterial == look.fxAlpha || renderer.sharedMaterial == look.fxAdditive,
                    renderer.name + " is drawn with an FX material");
            Assert.GreaterOrEqual(tongues.GetComponentsInChildren<ParticleSystem>(true).Length, 2, "flames and embers");
            Assert.AreEqual("Assets/_Project/Features/Abilities/Data/Flame Cone.mat",
                AssetDatabase.GetAssetPath(cone.transform.Find("Fan").GetComponent<MeshRenderer>().sharedMaterial), "the fan is untouched");
        }

        [Test]
        public void TheFxMaterialsAreVertexColourParticlesOneAddingOneBlending()
        {
            var look = AssetDatabase.LoadAssetAtPath<Overpower.Arena.ArenaLook>(LookPath);
            Assert.IsNotNull(look.fxAdditive, "ArenaLook › Fx Additive");
            Assert.IsNotNull(look.fxAlpha, "ArenaLook › Fx Alpha");
            Assert.AreEqual("Assets/_Project/Shared/FX/Materials/FX Additive.mat", AssetDatabase.GetAssetPath(look.fxAdditive));
            Assert.AreEqual("Assets/_Project/Shared/FX/Materials/FX Alpha.mat", AssetDatabase.GetAssetPath(look.fxAlpha));
            Assert.IsNotNull(look.flameJetMaterial, "ArenaLook › Flame Jet Material");
            Assert.AreEqual("Assets/_Project/Shared/FX/Materials/FX Flame Jet.mat", AssetDatabase.GetAssetPath(look.flameJetMaterial));
            Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, look.flameJetMaterial.GetFloat("_DstBlend"),
                "the flames blend, so their shape's alpha keeps them inside their wedge (an opaque quad would draw outside the hit cone)");
            foreach (Material m in new[] { look.fxAdditive, look.fxAlpha, look.flameJetMaterial })
            {
                Assert.AreEqual("Universal Render Pipeline/Particles/Unlit", m.shader.name, m.name + ": reads vertex colours, a URP name");
                Assert.AreEqual(1f, m.GetFloat("_Surface"), m.name + " transparent");
                Assert.AreEqual(0f, m.GetFloat("_ZWrite"), m.name + " writes no depth");
            }
            Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.One, look.fxAdditive.GetFloat("_DstBlend"), "additive: adds light");
            Assert.AreEqual((float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, look.fxAlpha.GetFloat("_DstBlend"), "see-through: blends");
        }
    }
}
