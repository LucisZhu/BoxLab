Shader "BoxLabArt/RadialGoal"
{
    Properties
    {
        _Color ("Goal color", Color) = (1,0.78,0.20,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                float2 offset = (i.uv - 0.5) * 2.0;
                float radiusSquared = dot(offset, offset);
                clip(1.0 - radiusSquared);
                // Zero slope at the centre and the edge: no hard rim or rectangular border.
                float alpha = 1.0 - smoothstep(0.0, 1.0, radiusSquared);
                return fixed4(_Color.rgb, _Color.a * alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
