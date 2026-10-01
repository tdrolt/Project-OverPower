// The fog: a full-screen pass that darkens everything the local team cannot see. Each pixel's world position is rebuilt
// from the camera depth texture; its x/z is looked up in the sight picture (_VisionSightTex, white = seen) through
// _VisionSightRect (min x, min z, size x, size z). Unseen (or off the picture) pixels are blended toward the fog colour by
// the darkness; the edge is hard (a step at 0.5). Sky pixels (no depth) are left alone. The colour and the darkness are
// globals that TeamSight sets from VisionConfig every frame, so there are no numbers in the material.
Shader "Overpower/Fog"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Fog"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_VisionSightTex);
            float4 _VisionSightRect;
            float4 _VisionFogColour;
            float _VisionFogDarkness;

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 screenUV = i.positionCS.xy * rcp(_ScaledScreenParams.xy);
                float depth = SampleSceneDepth(screenUV);
            #if UNITY_REVERSED_Z
                if (depth <= 0.0) return half4(0, 0, 0, 0); // sky: no depth, left alone
            #else
                if (depth >= 1.0) return half4(0, 0, 0, 0);
            #endif

                float3 world = ComputeWorldSpacePosition(screenUV, depth, UNITY_MATRIX_I_VP);
                float2 sightUV = (world.xz - _VisionSightRect.xy) / _VisionSightRect.zw;
                bool inside = all(sightUV >= 0.0) && all(sightUV <= 1.0);
                float seen = inside ? step(0.5, SAMPLE_TEXTURE2D_LOD(_VisionSightTex, sampler_LinearClamp, sightUV, 0).r) : 0.0;
                return half4(_VisionFogColour.rgb, _VisionFogDarkness * (1.0 - seen));
            }
            ENDHLSL
        }
    }
}
