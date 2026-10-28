using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Overpower.Tests
{
    /// <summary>
    /// The fog rebuilds each pixel's world position from the depth texture. On GL (the browser build) clip-space depth runs -1..1
    /// while the texture holds 0..1, so the value must be remapped before ComputeWorldSpacePosition or the fog edges slide away from
    /// walls and sight fans. Direct3D (Windows) has reversed 0..1 depth and takes the raw value, so its shader is unchanged.
    /// The sky test keeps the raw value: it compares against the texture's own far value.
    /// </summary>
    public class FogShaderDepthTests
    {
        private const string ShaderPath = "Assets/_Project/Features/Vision/Fog.shader";

        private static string Source() => System.IO.File.ReadAllText(ShaderPath).Replace("\r\n", "\n");

        [Test]
        public void TheWorldPositionIsRebuiltFromDepthInClipRangeOnGl()
        {
            string src = Source();
            StringAssert.Contains("#if !UNITY_REVERSED_Z\n                depthForPosition = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);", src);
            StringAssert.Contains("ComputeWorldSpacePosition(screenUV, depthForPosition, UNITY_MATRIX_I_VP)", src);
            StringAssert.DoesNotContain("ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP)", src);
        }

        [Test]
        public void TheSkyTestStillReadsTheRawDepth()
        {
            string src = Source();
            StringAssert.Contains("if (depth <= 0.0) return half4(0, 0, 0, 0);", src);
            StringAssert.Contains("if (depth >= 1.0) return half4(0, 0, 0, 0);", src);
        }

        [Test]
        public void TheFogShaderCompiles()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            Assert.IsNotNull(shader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "the fog shader has a compile error");
        }
    }
}
