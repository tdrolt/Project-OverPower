using System.Linq;
using NUnit.Framework;
using Overpower.Arena;
using Overpower.Dominion;
using Overpower.Match;
using Overpower.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.Tests
{
    public class SpawnLookRulesTests
    {
        [TestCase(true, true, true)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, false)]
        public void OnlyACapitalInDominionIsASpawn(bool dominion, bool capital, bool expected)
        {
            Assert.AreEqual(expected, DominionTerritoryRules.IsSpawn(dominion, capital));
        }

        [Test]
        public void ASpawnIsExactlyWhatCannotBeCaptured()
        {
            foreach (bool dominion in new[] { false, true })
                foreach (bool capital in new[] { false, true })
                    Assert.AreNotEqual(DominionTerritoryRules.IsSpawn(dominion, capital), DominionTerritoryRules.IsCapturable(dominion, capital));
        }

        [Test]
        public void ARadiusIsNeverAllowedPastTheCollider()
        {
            Assert.AreEqual(2.0f, SpawnTowerRules.WithinCollider(2.0f, 2.6f));
            Assert.AreEqual(2.6f, SpawnTowerRules.WithinCollider(9f, 2.6f));
            Assert.AreEqual(0f, SpawnTowerRules.WithinCollider(-1f, 2.6f));
        }

        [Test]
        public void AColumnStandsOnTheBodyCornerUnlessItsCapWouldPassTheCollider()
        {
            Assert.AreEqual(2.0f, SpawnTowerRules.ColumnRingRadius(2.0f, 0.45f, 2.6f), 1e-4f);
            Assert.AreEqual(2.6f - 0.45f, SpawnTowerRules.ColumnRingRadius(2.5f, 0.45f, 2.6f), 1e-4f);
            Assert.AreEqual(0f, SpawnTowerRules.ColumnRingRadius(2.0f, 3f, 2.6f));
        }

        [Test]
        public void TheColumnCountIsBetweenOneAndOnePerCorner()
        {
            Assert.AreEqual(1, SpawnTowerRules.ColumnCount(0));
            Assert.AreEqual(4, SpawnTowerRules.ColumnCount(4));
            Assert.AreEqual(6, SpawnTowerRules.ColumnCount(6));
            Assert.AreEqual(6, SpawnTowerRules.ColumnCount(40));
        }

        [Test]
        public void TheHexagonHasSixCornersTheFirstFacingFront()
        {
            Mesh mesh = HexPrismMesh.Create();
            try
            {
                float[] corners = mesh.vertices
                    .Where(v => Mathf.Abs(v.y - 1f) < 1e-4f && new Vector2(v.x, v.z).magnitude > 0.99f)
                    .Select(v => Mathf.Round(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg))
                    .Select(a => a < 0f ? a + 360f : a)
                    .Distinct().OrderBy(a => a).ToArray();
                CollectionAssert.AreEqual(new[] { 0f, 60f, 120f, 180f, 240f, 300f }, corners);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void TheHexagonFitsTheUnitCylinderItReplaces()
        {
            Mesh mesh = HexPrismMesh.Create();
            try
            {
                foreach (Vector3 v in mesh.vertices)
                {
                    Assert.LessOrEqual(new Vector2(v.x, v.z).magnitude, 1f + 1e-4f);
                    Assert.LessOrEqual(Mathf.Abs(v.y), 1f + 1e-4f);
                }
                Assert.AreEqual(1f, mesh.bounds.extents.y, 1e-4f);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void EveryFaceOfTheHexagonFacesOutward()
        {
            Mesh mesh = HexPrismMesh.Create();
            try
            {
                Vector3[] v = mesh.vertices;
                int[] t = mesh.triangles;
                Vector3[] n = mesh.normals;
                Assert.AreEqual(v.Length, n.Length);
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 centroid = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                    Vector3 geometric = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    Assert.Greater(Vector3.Dot(geometric, centroid), 0f, "triangle " + i / 3 + " is wound inward");
                    Assert.Greater(Vector3.Dot(n[t[i]], geometric), 0f, "triangle " + i / 3 + " normal disagrees with its winding");
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void EveryNumberOfTheSpawnTowerHasATooltip()
        {
            var fields = typeof(UiTheme).GetFields().Where(f => f.Name.StartsWith("spawnTower")).ToArray();
            Assert.GreaterOrEqual(fields.Length, 5);
            foreach (var f in fields)
            {
                var tip = (TooltipAttribute)System.Attribute.GetCustomAttribute(f, typeof(TooltipAttribute));
                Assert.IsNotNull(tip, f.Name);
                Assert.Greater(tip.tooltip.Length, 40, f.Name);
            }
        }
    }

    public class SpawnLookTowerTests
    {
        private const string PrefabPath = "Assets/Gameplay/Arena/Tower Look.prefab";
        private Scene scene;
        private GameObject instance;
        private TowerLook look;
        private UiTheme theme;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            SceneManager.MoveGameObjectToScene(instance, scene);
            look = instance.GetComponent<TowerLook>();
            theme = ScriptableObject.CreateInstance<UiTheme>();
            look.Bind(theme);
            look.ApplyColumns(1);
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(theme);
        }

        private float ColliderRadius => instance.GetComponent<CapsuleCollider>().radius;

        private float TopOfShown() => instance.GetComponentsInChildren<Renderer>(false).Max(r => r.bounds.max.y);

        [Test]
        public void TheSpawnBodyPlinthAndLidAreSixSided()
        {
            look.UseSpawnLook(true);
            foreach (Renderer r in new[] { look.drum, look.plinth, look.crown })
                Assert.AreEqual(HexPrismMesh.VertexCount, r.GetComponent<MeshFilter>().sharedMesh.vertexCount, r.name);
        }

        [Test]
        public void TheSpawnShowsItsOwnNumberOfColumnsOnTheCorners()
        {
            look.UseSpawnLook(true);
            Assert.AreEqual(SpawnTowerRules.ColumnCount(theme.spawnTowerColumnCount), look.ShownColumns);
            float[] yaws = look.columnSlots.Where(s => s.gameObject.activeSelf)
                .Select(s => Mathf.Round(s.localRotation.eulerAngles.y)).OrderBy(y => y).ToArray();
            CollectionAssert.AreEqual(new[] { 0f, 60f, 120f, 180f, 240f, 300f }, yaws);
        }

        [Test]
        public void NothingOfTheSpawnTowerPassesTheColliderOrRisesAboveTheCapitalItReplaces()
        {
            float topBefore = TopOfShown();
            look.UseSpawnLook(true);
            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(false))
            {
                Bounds b = r.bounds;
                float reach = Mathf.Max(Mathf.Abs(b.max.x), Mathf.Abs(b.min.x), Mathf.Abs(b.max.z), Mathf.Abs(b.min.z));
                Assert.LessOrEqual(reach, ColliderRadius + 1e-3f, r.transform.parent.name + "/" + r.name);
            }
            Assert.LessOrEqual(TopOfShown(), topBefore + 1e-3f);
        }

        [Test]
        public void TheSpawnLookDoesNotTouchTheCollider()
        {
            var capsule = instance.GetComponent<CapsuleCollider>();
            float radius = capsule.radius, height = capsule.height;
            Vector3 centre = capsule.center;
            look.UseSpawnLook(true);
            Assert.AreEqual(radius, capsule.radius);
            Assert.AreEqual(height, capsule.height);
            Assert.AreEqual(centre, capsule.center);
            Assert.AreEqual(1, instance.GetComponentsInChildren<Collider>(true).Length);
        }

        [Test]
        public void TheSpawnLookIsPaintedInTheTeamColour()
        {
            look.UseSpawnLook(true);
            look.Refresh(CaptureRingState.From(CaptureProgress.Idle, 1, false, 1000), 0f);
            Color painted = look.ShownColor;
            var block = new MaterialPropertyBlock();
            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(false).Where(r => r != look.drum))
            {
                r.GetPropertyBlock(block);
                Assert.AreEqual(painted.r, block.GetColor(Shader.PropertyToID("_BaseColor")).r, 1e-3f, r.transform.parent.name + "/" + r.name);
            }
        }

        [Test]
        public void AskingForTheRoundLookAgainRestoresTheCapital()
        {
            float topBefore = TopOfShown();
            Mesh drumBefore = look.drum.GetComponent<MeshFilter>().sharedMesh;
            Vector3 drumScale = look.drum.transform.localScale;
            look.UseSpawnLook(true);
            look.UseSpawnLook(false);
            Assert.AreSame(drumBefore, look.drum.GetComponent<MeshFilter>().sharedMesh);
            Assert.AreEqual(drumScale, look.drum.transform.localScale);
            Assert.AreEqual(1, look.ShownColumns);
            Assert.AreEqual(topBefore, TopOfShown(), 1e-3f);
        }

        [Test]
        public void ChangingTheTierWhileASpawnKeepsTheSpawnColumns()
        {
            look.UseSpawnLook(true);
            look.ApplyColumnsAtRuntime(1);
            Assert.AreEqual(SpawnTowerRules.ColumnCount(theme.spawnTowerColumnCount), look.ShownColumns);
        }
    }

    public class SpawnLookWiringTests
    {
        private static readonly System.Reflection.BindingFlags Any =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

        [Test]
        public void TheManagerAsksTheSpawnRule()
        {
            System.Reflection.MethodInfo rule = typeof(DominionTerritoryRules).GetMethod(nameof(DominionTerritoryRules.IsSpawn));
            Assert.IsNotNull(rule);
            Assert.IsNotNull(typeof(BuildingManager).GetMethod("IsSpawnZone", Any));
            Assert.IsTrue(IlWiring.Uses(typeof(BuildingManager), "IsSpawnZone", rule));
        }

        [Test]
        public void TheMapDecidesWhatToDrawFromTheSpawnZone()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapView), "RecolourOwnership", typeof(BuildingManager).GetMethod("IsSpawnZone", Any)));
        }

        [Test]
        public void TheTowerDecidesItsLookFromTheSpawnZone()
        {
            Assert.IsTrue(IlWiring.Uses(typeof(BuildingCapture), "RefreshRingView", typeof(BuildingManager).GetMethod("IsSpawnZone", Any)));
            Assert.IsTrue(IlWiring.Uses(typeof(BuildingCapture), "RefreshRingView", typeof(TowerLook).GetMethod(nameof(TowerLook.UseSpawnLook))));
        }

        [Test]
        public void TheMapDrawsNeitherTheSpawnBubbleNorItsLinksNorItsRingState()
        {
            System.Reflection.FieldInfo spawn = typeof(MinimapView).GetNestedType("ZoneUi", Any).GetField("IsSpawn");
            Assert.IsNotNull(spawn);
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapView), "ApplyLinkStyle", spawn), "links to a spawn are hidden");
            Assert.IsTrue(IlWiring.Uses(typeof(MinimapView), "UpdateZones", spawn), "a spawn bubble has no ring state to update");
            Assert.IsTrue(IlWiring.Stores(typeof(MinimapView), "RecolourOwnership", spawn));
        }
    }
}
