Shader "RoutesToGlory/AlienNightSky"
{
    Properties
    {
        [Header(NightGradient)]
        _ZenithColor ("Zenith Color", Color) = (0.02, 0.01, 0.05, 1)
        _HorizonColor ("Horizon Color", Color) = (0.08, 0.03, 0.14, 1)
        _GroundColor ("Ground Color", Color) = (0.02, 0.015, 0.04, 1)
        _HorizonGlow ("Horizon Glow", Range(0, 1)) = 0.35
        _Exposure ("Exposure", Range(0, 2)) = 1.0

        [Header(Stars)]
        _StarDensity ("Star Density", Range(20, 200)) = 95
        _StarThreshold ("Star Threshold", Range(0.85, 0.995)) = 0.965
        _StarBrightness ("Star Brightness", Range(0, 3)) = 1.35
        _StarTwinkle ("Star Twinkle", Range(0, 1)) = 0.15

        [Header(MilkyBand)]
        _BandColor ("Band Color", Color) = (0.22, 0.12, 0.38, 1)
        _BandStrength ("Band Strength", Range(0, 1)) = 0.22
        _BandWidth ("Band Width", Range(0.05, 0.6)) = 0.28
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float4 _ZenithColor;
            float4 _HorizonColor;
            float4 _GroundColor;
            float _HorizonGlow;
            float _Exposure;

            float _StarDensity;
            float _StarThreshold;
            float _StarBrightness;
            float _StarTwinkle;

            float4 _BandColor;
            float _BandStrength;
            float _BandWidth;

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            float Hash13(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float3 StarField(float3 dir, float density, float threshold, float brightness)
            {
                float3 n = normalize(dir);
                float3 cell = floor(n * density);
                float3 f = frac(n * density) - 0.5;

                float h = Hash13(cell);
                float starOn = step(threshold, h);
                float size = lerp(0.035, 0.012, saturate((h - threshold) / max(1e-4, 1.0 - threshold)));
                float d = length(f);
                float core = smoothstep(size, 0.0, d);
                float sparkle = pow(core, 2.5);

                // Sparse brighter stars from a second hash.
                float h2 = Hash13(cell + 17.13);
                float bright = lerp(0.55, 1.0, step(0.92, h2));

                float twinkle = 1.0;
                if (_StarTwinkle > 0.001)
                {
                    float t = sin(_Time.y * lerp(1.5, 4.5, h2) + h * 40.0) * 0.5 + 0.5;
                    twinkle = lerp(1.0, lerp(0.65, 1.0, t), _StarTwinkle);
                }

                float3 tint = lerp(float3(0.85, 0.9, 1.0), float3(1.0, 0.85, 0.7), step(0.7, h2));
                return tint * (sparkle * starOn * brightness * bright * twinkle);
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float up = dir.y;

                // Deep night gradient: blackish-purple zenith → violet horizon → near-black ground.
                float skyT = saturate(up);
                float3 skyCol = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(skyT, 0.65));
                float groundMask = saturate(-up * 3.0);
                skyCol = lerp(skyCol, _GroundColor.rgb, groundMask);

                // Faint alien dusk glow along the horizon (not a daytime sun).
                float horizon = 1.0 - abs(up);
                skyCol += _HorizonColor.rgb * pow(saturate(horizon), 4.0) * _HorizonGlow * 0.35;

                // Soft milky / nebula band across the sky.
                float3 bandAxis = normalize(float3(0.55, 0.15, 0.82));
                float bandDist = abs(dot(dir, bandAxis));
                float band = saturate(1.0 - bandDist / max(0.05, _BandWidth));
                band = pow(band, 1.8) * saturate(up + 0.15);
                float bandNoise = Hash12(dir.xz * 18.0 + dir.y * 7.0);
                skyCol += _BandColor.rgb * band * _BandStrength * (0.7 + 0.3 * bandNoise);

                // Dense starfield (two scales so it reads from glider altitude).
                float3 stars = 0;
                if (up > -0.05)
                {
                    stars += StarField(dir, _StarDensity, _StarThreshold, _StarBrightness);
                    stars += StarField(dir + 0.37, _StarDensity * 0.55, lerp(_StarThreshold, 0.99, 0.35), _StarBrightness * 0.55);
                    stars *= saturate(up * 4.0 + 0.2);
                }
                skyCol += stars;

                skyCol *= _Exposure;
                return float4(skyCol, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
