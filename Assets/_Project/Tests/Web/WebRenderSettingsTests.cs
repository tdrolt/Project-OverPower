using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// The browser build gets its own quality level (Web) with its own pipeline asset and a Forward renderer; the Windows build keeps
    /// the PC level, PC_RPAsset and its Forward+ renderer. Read through SerializedObject because the test assembly does not reference URP.
    /// </summary>
    public class WebRenderSettingsTests
    {
        private const string PcPipelinePath = "Assets/_Project/Settings/PC_RPAsset.asset";
        private const string WebPipelinePath = "Assets/_Project/Settings/Web_RPAsset.asset";
        private const int Forward = 0, ForwardPlus = 2;

        private static SerializedObject Quality() =>
            new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);

        private static SerializedProperty DefaultLevelFor(SerializedObject quality, string platform)
        {
            SerializedProperty defaults = quality.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < defaults.arraySize; i++)
            {
                SerializedProperty entry = defaults.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue == platform)
                    return quality.FindProperty("m_QualitySettings").GetArrayElementAtIndex(entry.FindPropertyRelative("second").intValue);
            }
            Assert.Fail("no default quality level for " + platform);
            return null;
        }

        private static Object PipelineOf(SerializedProperty level) => level.FindPropertyRelative("customRenderPipeline").objectReferenceValue;

        private static SerializedObject RendererOf(Object pipeline) =>
            new SerializedObject(new SerializedObject(pipeline).FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue);

        private static bool Excludes(SerializedProperty level, string platform)
        {
            SerializedProperty excluded = level.FindPropertyRelative("excludedTargetPlatforms");
            for (int i = 0; i < excluded.arraySize; i++)
                if (excluded.GetArrayElementAtIndex(i).stringValue == platform) return true;
            return false;
        }

        [Test]
        public void TheBrowserBuildUsesTheWebLevelWithAForwardRenderer()
        {
            SerializedProperty web = DefaultLevelFor(Quality(), "WebGL");
            Assert.AreEqual("Web", web.FindPropertyRelative("name").stringValue);
            Assert.AreEqual(WebPipelinePath, AssetDatabase.GetAssetPath(PipelineOf(web)));
            Assert.AreEqual(Forward, RendererOf(PipelineOf(web)).FindProperty("m_RenderingMode").intValue,
                "Forward+ has no WebGL2 support statement and a known freeze: the browser renders in Forward");
            Assert.IsTrue(Excludes(web, "Standalone"), "the Windows build must never pick the Web level");
        }

        [Test]
        public void TheWindowsBuildKeepsThePcLevelAndForwardPlus()
        {
            SerializedProperty pc = DefaultLevelFor(Quality(), "Standalone");
            Assert.AreEqual("PC", pc.FindPropertyRelative("name").stringValue);
            Assert.AreEqual(PcPipelinePath, AssetDatabase.GetAssetPath(PipelineOf(pc)));
            Assert.AreEqual(ForwardPlus, RendererOf(PipelineOf(pc)).FindProperty("m_RenderingMode").intValue);
            Assert.IsTrue(Excludes(pc, "WebGL"), "the browser build must not ship the Forward+ pipeline");
        }

        [Test]
        public void TheBrowserPlayerSettingsKeepTheMatchRunning()
        {
            Assert.IsTrue(PlayerSettings.runInBackground, "the host's tab runs the match logic for everyone");
            Assert.AreEqual(ManagedStrippingLevel.Minimal, PlayerSettings.GetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL),
                "Photon finds its browser socket class by name; higher stripping can remove it");
            Assert.AreNotEqual(WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly, PlayerSettings.WebGL.exceptionSupport,
                "without null checks a bug that Windows logs and survives crashes the browser build");
            Assert.AreEqual(WebGLCompressionFormat.Gzip, PlayerSettings.WebGL.compressionFormat);
            Assert.IsTrue(PlayerSettings.WebGL.decompressionFallback, "the build must load whether or not the host sends gzip headers");
        }

        [Test]
        public void TheWebRendererKeepsTheFogFeature()
        {
            SerializedProperty features = RendererOf(AssetDatabase.LoadMainAssetAtPath(WebPipelinePath)).FindProperty("m_RendererFeatures");
            bool fog = false;
            for (int i = 0; i < features.arraySize; i++)
            {
                Object feature = features.GetArrayElementAtIndex(i).objectReferenceValue;
                if (feature != null && feature.GetType().Name == "FogRenderFeature") fog = true;
            }
            Assert.IsTrue(fog, "without FogRenderFeature the fog of war is missing in the browser");
        }
    }
}
