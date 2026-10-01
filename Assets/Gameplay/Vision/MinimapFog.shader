// The minimap's fog layer: a UI shader over the baked arena picture. Unseen parts get the fog colour at the darkness;
// seen parts are drawn white at the (small) lift, so the lit holes read against the near-black map. The sight picture
// (white = my team sees it) is the RawImage's texture, so the layer sits on the same rect as the arena picture. The
// vertex colour is (fog colour, alpha 1 * canvas group opacity). Darkness and lift are globals set by TeamSight (the
// Mask makes a stencil copy of the material, which later SetFloat calls on the original do not reach).
Shader "Overpower/Minimap Fog"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sight picture", 2D) = "black" {}
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "MinimapFog"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _VisionMinimapDarkness;
            float _VisionMinimapSeenLift;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float seen = tex2D(_MainTex, i.uv).r;
                float a = lerp(_VisionMinimapDarkness, _VisionMinimapSeenLift, seen);
                float3 rgb = lerp(i.color.rgb, float3(1, 1, 1), seen);
                return fixed4(rgb, i.color.a * a);
            }
            ENDCG
        }
    }
}
