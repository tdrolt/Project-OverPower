using NUnit.Framework;
using Overpower.Abilities;
using Overpower.Arena;
using Overpower.Weapons;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>The Zip Gun fires a small silver harpoon on a straight segmented silver cable whose chevrons run toward the target, in flight
    /// and during the pull; every colour from the Zip Gun's row in the look table. The harpoon is a look only: no collider, the hit size and
    /// the bullet's root scale stay; the harpoon is drawn Zip Harpoon Size times the hit sphere's width, its point at the sphere's front.</summary>
    public class ZipCableLookTests
    {
        private const System.Reflection.BindingFlags Any = MovementLookTestKit.Any;
        private const int ZipId = 18;
        private const string BulletPath = "Assets/_Project/Features/Weapons/Projectiles/Zip Gun Bullet.prefab";
        private const string HarpoonPath = "Assets/_Project/Shared/Art/HarpoonHead.fbx";
        private const string CablePath = "Assets/_Project/Shared/FX/Materials/Zip Cable.mat";
        private const string HarpoonMaterialPath = "Assets/_Project/Shared/FX/Materials/Zip Harpoon.mat";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        [Test]
        public void ChevronsRunTowardTheTarget()
        {
            Assert.AreEqual(0f, ZipCableLook.Scroll(0f, 8f, 0.6f), 1e-5f);
            float a = ZipCableLook.Scroll(0.01f, 8f, 0.6f), b = ZipCableLook.Scroll(0.02f, 8f, 0.6f);
            // A chevron drawn at texture position c sits (c - scroll) tiles from the shooter's end: a falling scroll moves it toward the target.
            Assert.Less(b, a, "the pattern slides from the shooter toward the target");
            Assert.AreEqual(0.08f, (a - b) * 0.6f, 1e-4f, "at Zip Chevron Speed (8 m/s over 0.01 s), one tile per Zip Chevron Spacing");
            for (float t = 0f; t < 3f; t += 0.0137f)
            {
                float s = ZipCableLook.Scroll(t, 8f, 0.6f);
                Assert.LessOrEqual(s, 0f);
                Assert.Greater(s, -1f, "kept within one tile, so it never grows large over a long match");
            }
            Assert.AreEqual(0f, ZipCableLook.Scroll(1f, 8f, 0f), "no spacing, no scroll");
        }

        [Test]
        public void TheBoltPaintsHarpoonAndCableFromTheLookTable()
        {
            using (var kit = new MovementLookTestKit(ZipId, LookShape.HarpoonCable))
            {
                Material cable = kit.Make(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
                kit.Look.zipCable = cable;
                kit.Look.zipCableWidth = 0.24f;
                kit.Look.zipChevronSpacing = 0.6f;
                kit.Look.zipChevronSpeed = 8f;
                kit.Look.abilityShotGlow = 0.5f;
                GameObject bolt = kit.NewObject("zip bolt test");
                bolt.transform.position = new Vector3(4f, 500f, 2f);
                var head = new GameObject("Head");
                head.transform.SetParent(bolt.transform, false);
                head.transform.localPosition = new Vector3(0f, 0f, -0.2f);
                MeshRenderer headRenderer = head.AddComponent<MeshRenderer>();
                head.AddComponent<MeshFilter>();
                var ropeObject = new GameObject("Rope");
                ropeObject.transform.SetParent(bolt.transform, false);
                LineRenderer rope = ropeObject.AddComponent<LineRenderer>();
                rope.positionCount = 2;
                ZipBoltView view = bolt.AddComponent<ZipBoltView>();
                var so = new SerializedObject(view);
                so.FindProperty("theme").objectReferenceValue = kit.Theme;
                so.FindProperty("head").objectReferenceValue = headRenderer;
                so.FindProperty("rope").objectReferenceValue = rope;
                so.ApplyModifiedPropertiesWithoutUndo();

                view.OnSpawned(null, new ProjectileContext(ZipId, 40f, 0.225f, 15f, 0f, -1, 1, Vector3.forward, bolt.transform.position));

                var block = new MaterialPropertyBlock();
                headRenderer.GetPropertyBlock(block);
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, block.GetColor(BaseColorId), "the harpoon wears the Zip Gun's row in the look table");
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold * 0.5f, block.GetColor(EmissionColorId), "and glows at Ability Shot Glow");
                Assert.AreSame(cable, rope.sharedMaterial, "the cable material from the look");
                Assert.AreEqual(0.24f, rope.widthMultiplier, 1e-5f, "Zip Cable Width");
                Assert.AreEqual(LineTextureMode.Tile, rope.textureMode, "the segment-and-chevron pattern repeats along the cable");
                Assert.AreEqual(1f / 0.6f, rope.textureScale.x, 1e-4f, "one pattern every Zip Chevron Spacing metres");
                rope.GetPropertyBlock(block);
                MovementLookTestKit.AssertRgb(MovementLookTestKit.Gold, block.GetColor(BaseColorId), "the cable too");
                Assert.AreEqual(1f, block.GetVector(BaseMapStId).x, 1e-5f, "the pattern's size comes from the line, not the material");
                Assert.AreEqual(head.transform.position, rope.GetPosition(1), "the cable ends at the back of the harpoon");
            }
        }

        [Test]
        public void TheHarpoonIsDrawnZipHarpoonSizeTimesTheHitWithItsPointAtTheHitsFront()
        {
            Assert.AreEqual(0.45f, ZipCableLook.HarpoonLength(0.225f, 1f), 1e-6f, "size 1 spans exactly the hit");
            Assert.AreEqual(0.675f, ZipCableLook.HarpoonLength(0.225f, 1.5f), 1e-6f, "half as long again");
            Assert.AreEqual(-0.225f, ZipCableLook.HarpoonBack(0.225f, 0.45f), 1e-6f, "the old placement: back at the hit's back");
            Assert.AreEqual(-0.45f, ZipCableLook.HarpoonBack(0.225f, 0.675f), 1e-6f, "a longer harpoon reaches further back; its point stays at the hit's front");

            using (var kit = new MovementLookTestKit(ZipId, LookShape.HarpoonCable))
            {
                kit.Look.zipHarpoonSize = 1.5f;
                GameObject bolt = kit.NewObject("zip harpoon size test");
                bolt.transform.position = new Vector3(4f, 500f, 2f);
                var head = new GameObject("Head");
                head.transform.SetParent(bolt.transform, false);
                head.transform.localScale = Vector3.one * 0.45f;
                MeshRenderer headRenderer = head.AddComponent<MeshRenderer>();
                Mesh spear = kit.Make(new Mesh { vertices = new[] { new Vector3(-0.2f, 0f, 0f), new Vector3(0.2f, 0f, 0f), new Vector3(0f, 0f, 2f) }, triangles = new[] { 0, 1, 2 } });
                head.AddComponent<MeshFilter>().sharedMesh = spear; // 2 m long from its back (z 0) to its point
                ZipBoltView view = bolt.AddComponent<ZipBoltView>();
                var so = new SerializedObject(view);
                so.FindProperty("theme").objectReferenceValue = kit.Theme;
                so.FindProperty("head").objectReferenceValue = headRenderer;
                so.ApplyModifiedPropertiesWithoutUndo();

                view.OnSpawned(null, new ProjectileContext(ZipId, 40f, 0.3f, 15f, 0f, -1, 1, Vector3.forward, bolt.transform.position));

                Assert.AreEqual(0.9f / 2f, head.transform.localScale.z, 1e-5f, "0.9 m long (1.5 x the 0.6 m hit) from a 2 m model");
                Assert.AreEqual(head.transform.localScale.z, head.transform.localScale.x, 1e-6f, "scaled evenly, the model's shape kept");
                Assert.AreEqual(-0.6f, head.transform.localPosition.z, 1e-5f, "its back 0.9 m behind the hit's front");
                Assert.AreEqual(0.3f, head.transform.localPosition.z + 2f * head.transform.localScale.z, 1e-5f, "its point at the hit's front");
            }
            Assert.IsTrue(IlWiring.Uses(typeof(ZipBoltView), nameof(ZipBoltView.OnSpawned), typeof(ZipBoltView).GetMethod("PlaceHarpoon", Any)), "every spawned bolt sizes its harpoon");
            Assert.IsTrue(IlWiring.Uses(typeof(ZipBoltView), "PlaceHarpoon", typeof(ArenaLook).GetField(nameof(ArenaLook.zipHarpoonSize))));
            Assert.IsTrue(IlWiring.Uses(typeof(ZipBoltView), "PlaceHarpoon", typeof(ZipCableLook).GetMethod(nameof(ZipCableLook.HarpoonLength))));
            Assert.IsTrue(IlWiring.Uses(typeof(ZipBoltView), "PlaceHarpoon", typeof(ZipCableLook).GetMethod(nameof(ZipCableLook.HarpoonBack))));
        }

        [Test]
        public void ThePullCableAndAnchorUseTheSameLookAndTheTestedRules()
        {
            System.Reflection.MemberInfo forAbility = typeof(LookColours).GetMethod(nameof(LookColours.ForAbility));
            System.Reflection.MemberInfo scroll = typeof(ZipCableLook).GetMethod(nameof(ZipCableLook.Scroll));
            System.Reflection.MemberInfo paint = typeof(ZipCableLook).GetMethod(nameof(ZipCableLook.Paint));
            System.Reflection.MemberInfo configure = typeof(ZipCableLook).GetMethod(nameof(ZipCableLook.Configure));
            Assert.IsTrue(IlWiring.Uses(typeof(ZipGunAbility), "CableColour", forAbility), "the anchor and pull cable read the Zip Gun's row");
            Assert.IsTrue(IlWiring.Uses(typeof(ZipGunAbility), "PlayTether", typeof(ZipGunAbility).GetMethod("CableColour", Any)));
            Assert.IsTrue(IlWiring.Uses(typeof(ZipGunAbility), "BuildRope", configure), "the pull cable is the same cable");
            Assert.IsTrue(IlWiring.Uses(typeof(ZipGunAbility), "UpdateRope", scroll), "its chevrons run toward the anchor");
            Assert.IsTrue(IlWiring.Uses(typeof(ZipGunAbility), "UpdateRope", paint));
            Assert.IsTrue(IlWiring.Uses(typeof(ZipBoltView), nameof(ZipBoltView.OnSpawned), forAbility), "the bolt reads the row");
            Assert.IsTrue(IlWiring.Uses(typeof(ZipBoltView), nameof(ZipBoltView.OnSpawned), configure));
            Assert.IsTrue(IlWiring.Uses(typeof(ZipBoltView), "UpdateRope", scroll));
            Assert.IsTrue(IlWiring.Uses(typeof(ZipBoltView), "UpdateRope", paint));
            Assert.IsNull(typeof(ZipBoltView).GetField("hookColor", Any), "no yellow hook colour (it read too close to the Shield's gold)");
            Assert.IsNull(typeof(ZipBoltView).GetProperty("HookColor", Any));
            Assert.IsNull(typeof(ZipGunAbility).GetField("ropeMaterial", Any), "the cable's material lives in the look");
            Assert.IsNull(typeof(ZipGunAbility).GetField("ropeWidth", Any), "and so does its width");
        }

        [Test]
        public void TheZipBulletIsAHarpoonWithNoCollider()
        {
            GameObject bullet = AssetDatabase.LoadAssetAtPath<GameObject>(BulletPath);
            Assert.IsNotNull(bullet);
            Assert.AreEqual(Vector3.one, bullet.transform.localScale, "the bullet's root scale is untouched");
            Assert.IsEmpty(bullet.GetComponentsInChildren<Collider>(true), "hits come from the motor's sphere cast, never a collider");
            Transform head = bullet.transform.Find("Head");
            Assert.IsNotNull(head);
            Mesh mesh = head.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(mesh);
            Assert.AreEqual(HarpoonPath, AssetDatabase.GetAssetPath(mesh), "the harpoon head from Blender");
            Assert.Less(mesh.triangles.Length / 3, 300, "a small low-poly model");
            Assert.Greater(mesh.bounds.size.z, mesh.bounds.size.x * 2f, "long along its flight (+Z), slim across");
            Assert.AreEqual(0f, mesh.bounds.min.z, 0.02f * mesh.bounds.size.z,
                "its origin is the back of the shaft, where the cable ties on (ZipBoltView.PlaceHarpoon takes bounds.max.z as the model's length)");
            Vector3 tip = Vector3.zero;
            foreach (Vector3 v in mesh.vertices)
                if (v.z > tip.z) tip = v;
            Assert.Less(Mathf.Abs(tip.x), 0.05f * mesh.bounds.size.z, "the spear point is at +Z, on the middle line");
            Assert.AreEqual(HarpoonMaterialPath, AssetDatabase.GetAssetPath(head.GetComponent<MeshRenderer>().sharedMaterial));

            ZipBoltView view = bullet.GetComponent<ZipBoltView>();
            Assert.AreEqual("Assets/_Project/Shared/Config/UiTheme.asset", AssetDatabase.GetAssetPath(new SerializedObject(view).FindProperty("theme").objectReferenceValue));
            Assert.AreEqual(CablePath, AssetDatabase.GetAssetPath(bullet.transform.Find("Rope").GetComponent<LineRenderer>().sharedMaterial));
            Assert.IsFalse(System.IO.File.ReadAllText(BulletPath).Contains("hookColor"), "the prefab holds no hook colour");
        }

        [Test]
        public void TheCableMaterialRepeatsItsPatternAlongTheCable()
        {
            var look = AssetDatabase.LoadAssetAtPath<ArenaLook>("Assets/_Project/Shared/Config/ArenaLook.asset");
            Assert.AreEqual(CablePath, AssetDatabase.GetAssetPath(look.zipCable), "ArenaLook > Zip Cable");
            Assert.AreEqual("Universal Render Pipeline/Unlit", look.zipCable.shader.name, "an Unlit that reads the texture's tiling and offset, so the chevrons can run");
            Assert.AreEqual(1f, look.zipCable.GetFloat("_Surface"), "see-through between the segments");
            Texture pattern = look.zipCable.GetTexture("_BaseMap");
            Assert.IsNotNull(pattern, "the segment-and-chevron pattern");
            Assert.AreEqual(TextureWrapMode.Repeat, pattern.wrapModeU, "repeats along the cable");
            Assert.IsFalse(System.IO.File.ReadAllText("Assets/_Project/Features/Abilities/Data/Zip Gun.prefab").Contains("ropeMaterial"), "Zip Gun.prefab holds no rope material");
        }
    }
}
