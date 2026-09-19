using System.Collections.Generic;
using System.Linq;
using Overpower.Arena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Overpower.EditorTools
{
    /// <summary>
    /// Arena rebuild step 3: stamps Tower Look.prefab onto every BuildingCapture already in the scene, and hides
    /// (never removes) that tower's old house and flag carpet. The networked tower keeps its transform, PhotonView,
    /// capture trigger and BuildingCapture exactly as they are (base plan Decision 5) - only what a player SEES
    /// changes. Re-runnable: a second run replaces its own Tower Look child and re-hides the same pieces, so it is
    /// safe to call again after a tuning change to the prefab. Never runs in Play Mode (it would change a live,
    /// networked tower on this client only and desync the match), and never saves the scene itself - the caller
    /// looks at the report first, then saves.
    /// </summary>
    public static class ArenaPrimitiveBuilder
    {
        public const string TowerLookChildName = "Tower Look";

        public static List<string> BuildTowerLooks(Scene scene, GameObject towerLookPrefab)
        {
            var report = new List<string>();
            if (EditorApplication.isPlaying)
            {
                report.Add("Can't build tower looks in Play Mode: it would change a live, networked tower on this " +
                           "client only and desync the match. Stop Play Mode first.");
                return report;
            }
            if (towerLookPrefab == null)
            {
                report.Add("No Tower Look prefab given.");
                return report;
            }

            List<BuildingCapture> towers = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BuildingCapture>(true))
                .OrderBy(t => t.buildingID)
                .ToList();

            foreach (BuildingCapture tower in towers)
                BuildOne(tower, towerLookPrefab, report);

            EditorSceneManager.MarkSceneDirty(scene);
            return report;
        }

        private static void BuildOne(BuildingCapture tower, GameObject towerLookPrefab, List<string> report)
        {
            Transform root = tower.transform;

            // Hide the house: disable its own MeshRenderer and every non-trigger collider directly on it, then
            // deactivate every old child (windows, doors...). Every change is recorded against the prefab instance,
            // or it can be silently lost the next time this instance is saved (CODING-STANDARDS #6).
            var ownRenderer = tower.GetComponent<MeshRenderer>();
            if (ownRenderer != null && ownRenderer.enabled)
            {
                ownRenderer.enabled = false;
                RecordIfPrefab(ownRenderer);
            }
            foreach (Collider c in tower.GetComponents<Collider>())
            {
                if (c.isTrigger || !c.enabled) continue;
                c.enabled = false;
                RecordIfPrefab(c);
            }

            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child.name == TowerLookChildName)
                    continue; // handled below: destroyed and rebuilt fresh every run
                if (child.gameObject.activeSelf)
                {
                    child.gameObject.SetActive(false);
                    RecordIfPrefab(child.gameObject);
                }
            }

            // Hide the carpet (flag), never remove it: BuildingManager.ApplyOwnerVisual and the symmetry test both
            // still need flagRenderer assigned and its PhotonView untouched (Decision 10).
            if (tower.flagRenderer != null && tower.flagRenderer.enabled)
            {
                tower.flagRenderer.enabled = false;
                RecordIfPrefab(tower.flagRenderer);
            }

            // Replace any Tower Look from an earlier run, so this is safe to call twice.
            Transform existingLook = root.Find(TowerLookChildName);
            if (existingLook != null)
                Object.DestroyImmediate(existingLook.gameObject);

            var lookInstance = (GameObject)PrefabUtility.InstantiatePrefab(towerLookPrefab, root);
            lookInstance.name = TowerLookChildName;
            PrefabUtility.UnpackPrefabInstance(lookInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            Vector3 towerScale = root.lossyScale;
            bool uniform = Mathf.Abs(towerScale.x - towerScale.y) < 0.001f && Mathf.Abs(towerScale.y - towerScale.z) < 0.001f;
            if (!uniform)
                report.Add($"PROBLEM: tower {tower.buildingID}'s lossyScale {towerScale:F4} is not uniform - " +
                           "Tower Look would not be authored in true metres.");

            lookInstance.transform.localPosition = Vector3.zero;
            lookInstance.transform.localRotation = Quaternion.identity;
            float inverseScale = 1f / towerScale.x;
            lookInstance.transform.localScale = new Vector3(inverseScale, inverseScale, inverseScale);

            TowerLook look = lookInstance.GetComponent<TowerLook>();
            int columns = TowerLookRules.ColumnsForTier(tower.tier);
            look.ApplyColumns(tower.tier);

            report.Add($"tower id={tower.buildingID} tier={tower.tier} columns={columns} scale={inverseScale:F4}");
        }

        private static void RecordIfPrefab(Object target)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        // Temporary: folded into BuildAll's menu in arena step 5.
        [MenuItem("OverPower/Arena/Build tower looks")]
        private static void MenuBuildTowerLooks()
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Gameplay/Arena/Tower Look.prefab");
            List<string> report = BuildTowerLooks(scene, prefab);
            Debug.Log("[ArenaPrimitiveBuilder] Build tower looks:\n" + string.Join("\n", report));
        }
    }
}
