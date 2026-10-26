using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Overpower.Vision
{
    /// <summary>
    /// One full-screen pass (Fog.shader) that darkens whatever my team's sight picture says is unseen. A RecordRenderGraph
    /// raster pass, because the project runs URP's Render Graph (not Compatibility Mode).
    /// It sits BEFORE transparents: world-space canvases (names, bars), ground rings and aim lines are transparent, so they
    /// stay bright on top of the fog; the HUD and minimap are screen-space overlay, drawn after the camera.
    /// Needs the camera depth texture (the pipeline asset has it on; the pass also asks for it).
    /// Does nothing when the fog flag is 0 (menus, name screen, fog off) or no TeamSight owner exists.
    /// </summary>
    public sealed class FogRenderFeature : ScriptableRendererFeature
    {
        [SerializeField, Tooltip("The fog material (Fog.mat). Colour and darkness come from Vision Config, not from here.")]
        private Material fogMaterial;

        private static readonly int FogEnabledId = Shader.PropertyToID("_VisionFogEnabled");
        private FogPass pass;

        public override void Create()
        {
            pass = new FogPass(fogMaterial) { renderPassEvent = RenderPassEvent.BeforeRenderingTransparents };
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (fogMaterial == null || pass == null)
                return;
            CameraType type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game || renderingData.cameraData.renderType == CameraRenderType.Overlay)
                return;
            if (renderingData.cameraData.camera.targetTexture != null)
                return; // a bake into a texture (the top-down renders) is never fogged
            if (TeamSight.Local == null || Shader.GetGlobalFloat(FogEnabledId) < 0.5f)
                return;
            renderer.EnqueuePass(pass);
        }

        private sealed class FogPass : ScriptableRenderPass
        {
            private sealed class PassData { public Material Material; }

            private readonly Material material;

            public FogPass(Material material)
            {
                this.material = material;
                profilingSampler = new ProfilingSampler("Vision Fog");
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                if (!resources.cameraDepthTexture.IsValid() || !resources.activeColorTexture.IsValid())
                    return;

                using (var builder = renderGraph.AddRasterRenderPass("Vision Fog", out PassData data, profilingSampler))
                {
                    data.Material = material;
                    builder.SetRenderAttachment(resources.activeColorTexture, 0);
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.Material, 0, MeshTopology.Triangles, 3));
                }
            }
        }
    }
}
