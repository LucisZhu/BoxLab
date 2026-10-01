Shader "BoxLabArt/PermissionPortrait"
{
    Properties
    {
        _MainTex ("Model portrait", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Brightness ("Portrait brightness", Range(0,3)) = 1.5
        _Cutoff ("Transparent cutoff", Range(0,1)) = 0.025
        [HideInInspector] _ZWrite ("Depth write", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        Cull Off
        ZWrite [_ZWrite]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Brightness, _Cutoff;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 portrait = tex2D(_MainTex, i.uv);
                // Alpha is always honored, independent of Standard shader keyword stripping.
                clip(portrait.a - _Cutoff);
                portrait.rgb = saturate(portrait.rgb * _Color.rgb * _Brightness);
                portrait.a *= _Color.a;
                return portrait;
            }
            ENDCG
        }
    }
    Fallback Off
}
