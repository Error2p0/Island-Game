// Procedural gradient skybox for the day/night cycle. Chosen over Unity's
// built-in Skybox/Procedural because every color here is a plain material
// property DayNightVisuals drives per frame — full control of the night
// palette and dusk horizon glow, which the built-in sky doesn't offer.
// Plain vertex/fragment skybox shader: the skybox pass renders the material
// directly (no pipeline pass tags), so this works identically under URP and
// the built-in pipeline. Cost: one gradient + one dot product per sky pixel.
//
// _CelestialDir points TOWARD the visible body; the disk doubles as the sun
// by day and the moon by night (the script swaps direction/color/size).
Shader "IslandGame/SkyGradient"
{
    Properties
    {
        _TopColor ("Zenith Color", Color) = (0.28, 0.48, 0.75, 1)
        _HorizonColor ("Horizon Color", Color) = (0.68, 0.80, 0.92, 1)
        _BottomColor ("Below-Horizon Color", Color) = (0.22, 0.26, 0.32, 1)
        _CelestialDir ("Celestial Direction (toward body)", Vector) = (0, 1, 0, 0)
        _CelestialColor ("Celestial Color", Color) = (1, 0.95, 0.85, 1)
        _DiskSize ("Disk Size", Range(0.0001, 0.05)) = 0.008
        _GlowStrength ("Glow Strength", Range(0, 3)) = 0.6
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _TopColor;
            fixed4 _HorizonColor;
            fixed4 _BottomColor;
            float4 _CelestialDir;
            fixed4 _CelestialColor;
            float _DiskSize;
            float _GlowStrength;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 viewDir : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.viewDir = v.vertex.xyz; // skybox mesh vertices ARE directions
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.viewDir);

                // Vertical gradient: horizon color hugs |y| ≈ 0, zenith takes
                // over with a soft curve; below the horizon darkens toward the
                // bottom color (reads as distant sea haze).
                fixed3 sky;
                if (dir.y >= 0.0)
                {
                    float t = pow(1.0 - saturate(dir.y), 2.2);
                    sky = lerp(_TopColor.rgb, _HorizonColor.rgb, t);
                }
                else
                {
                    float t = pow(saturate(-dir.y), 0.55);
                    sky = lerp(_HorizonColor.rgb, _BottomColor.rgb, t);
                }

                // Sun/moon disk + a tight halo. cosAngle is 1 straight at the
                // body; the disk edge is softened over half its own size.
                float cosAngle = dot(dir, normalize(_CelestialDir.xyz));
                float disk = smoothstep(1.0 - _DiskSize, 1.0 - _DiskSize * 0.5, cosAngle);
                float halo = pow(saturate(cosAngle), 48.0) * _GlowStrength * 0.35;
                sky += _CelestialColor.rgb * (disk + halo);

                return fixed4(sky, 1.0);
            }
            ENDCG
        }
    }
}
