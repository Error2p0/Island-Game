// Additive star-point shader for the procedural star dome (StarField).
// Queue Transparent-100: AFTER the skybox — URP draws the skybox late, over
// every pixel still at far depth, so anything in the Background queue that
// doesn't write depth would be overwritten by it — and BEFORE water/other
// transparents. ZWrite off + ZTest LEqual keeps terrain occluding the dome.
// _Fade is driven by DayNightVisuals: 0 by day, 1 deep at night.
Shader "IslandGame/Stars"
{
    Properties
    {
        _Fade ("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Fade;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Additive: black output = invisible, so fading the color to
                // zero IS the fade-out — no alpha blending needed.
                return fixed4(i.color.rgb * _Fade, 1.0);
            }
            ENDCG
        }
    }
}
