// Paints the sight fans into the sight texture: the mesh vertices are world (x, z) positions, mapped straight to the
// texture through _SightRect (minX, minZ, sizeX, sizeZ) and filled white. Pipeline independent, no includes.
Shader "Hidden/Overpower/SightFill"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 _SightRect;
            float _SightFlipY; // -1 where texture row 0 is the top (Direct3D, Metal), else 1

            struct appdata { float4 position : POSITION; };
            struct v2f { float4 position : SV_POSITION; };

            v2f vert(appdata v)
            {
                v2f o;
                float2 uv = (v.position.xy - _SightRect.xy) / _SightRect.zw;
                float2 ndc = uv * 2.0 - 1.0;
                ndc.y *= _SightFlipY;
                o.position = float4(ndc, 0.0, 1.0);
                return o;
            }

            float4 frag(v2f i) : SV_Target { return float4(1.0, 1.0, 1.0, 1.0); }
            ENDHLSL
        }
    }
}
