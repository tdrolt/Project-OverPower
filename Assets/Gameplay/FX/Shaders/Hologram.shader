// The Dominion respawn bubble: see-through team-colour glass, brighter at the rim, thin scan-lines drifting up, a slight flicker.
// Additive (Blend One One) so overlapping bubbles never need sorting; it only adds light, so it can never hide the player inside.
Shader "Overpower/Hologram"
{
    Properties
    {
        _BaseColor ("Team colour", Color) = (1, 1, 1, 1)
        _Rim ("Rim (0 even, 1 edge only)", Range(0, 1)) = 0.8
        _ScanDensity ("Scan-lines per metre", Float) = 4
        _ScanSpeed ("Scan-line drift (m/s)", Float) = 0.6
        _Flicker ("Flicker", Range(0, 1)) = 0.08
        _Intensity ("Intensity", Float) = 1
        _Seed ("Seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Rim;
                float _ScanDensity;
                float _ScanSpeed;
                half _Flicker;
                half _Intensity;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float Hash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float facing = abs(dot(normalize(input.normalWS), viewDir)); // abs: the inside faces glow like the outside ones
                float fresnel = pow(1.0 - saturate(facing), 3.0);
                float glass = lerp(1.0, fresnel, _Rim);

                // Distance to the nearest scan-line in line spacings; the lines move up the world, not with the bubble.
                float s = input.positionWS.y * _ScanDensity - _Time.y * _ScanSpeed * _ScanDensity;
                float d = abs(frac(s + 0.5) - 0.5);
                float aa = max(fwidth(s), 1e-4);
                float scan = 1.0 - smoothstep(0.06, 0.06 + 1.5 * aa, d);

                float t = _Time.y;
                float dip = step(0.93, Hash(floor(t * 18.0) + _Seed * 7.13));
                float flicker = saturate(1.0 - _Flicker * (0.5 + 0.5 * sin(t * 29.0 + _Seed * 3.1)) - 2.0 * _Flicker * dip);

                // The scan-lines are weighted by the glass too, so the rim, not the lines, leads in the middle of the bubble.
                float light = 0.04 + 0.7 * glass + 0.3 * scan * (0.25 + 0.75 * glass);
                half3 colour = _BaseColor.rgb * _BaseColor.a * _Intensity * flicker * light;
                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
