// The minimap's fog layer: a UI shader that paints the fog colour with alpha (1 - sight) * darkness over the baked arena
// picture. The sight picture (white = my team sees it) is the RawImage's texture, so the layer sits on the same rect as
// the arena picture. The colour and darkness come in as the Graphic's vertex colour (rgb = fog colour, a = darkness),
// which carries the canvas group opacity and respects masks like any other UI graphic.
Shader "Overpower/Minimap Fog"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sight picture", 2D) = "black" {}
        _Color ("Fog colour and darkness", Color) = (0,0,0,0.6)
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
                return fixed4(i.color.rgb, i.color.a * (1.0 - seen));
            }
            ENDCG
        }
    }
}
