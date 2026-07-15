Shader "RoutesToGlory/AlienTerrainBiome"
{
    Properties
    {
        [Header(CesiumCompat)]
        _baseColorFactor ("Base Color Factor", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _baseColorTexture ("Base Color Texture", 2D) = "white" {}

        [Header(BiomeColors)]
        _WetlandColor ("Wetland / Low", Color) = (0.18, 0.28, 0.52, 1)
        _PlainColor ("Alien Plains", Color) = (0.72, 0.55, 0.22, 1)
        _ForestColor ("Fungal Forest", Color) = (0.14, 0.58, 0.30, 1)
        _HighlandColor ("Crystal Highland", Color) = (0.62, 0.78, 0.92, 1)
        _RiftColor ("Volcanic Rift", Color) = (0.92, 0.38, 0.12, 1)

        [Header(HeightSlope)]
        _HeightScaleM ("Height Band (world units)", Float) = 45
        _SlopeRiftStart ("Slope Rift Start", Range(0, 1)) = 0.38
        _SlopeBlend ("Slope Blend", Range(0.01, 4)) = 1.4

        [Header(Detail)]
        _NoiseScale ("Macro Noise Scale", Float) = 0.008
        _NoiseStrength ("Macro Noise Strength", Range(0, 1)) = 0.65
        _DetailScale ("Fine Detail Scale", Float) = 0.05
        _DetailStrength ("Fine Detail Strength", Range(0, 1)) = 0.22
        _Saturation ("Color Saturation", Range(0.5, 2)) = 1.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "AlienTerrainUnlit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _baseColorFactor;
                float4 _WetlandColor;
                float4 _PlainColor;
                float4 _ForestColor;
                float4 _HighlandColor;
                float4 _RiftColor;
                float _HeightScaleM;
                float _SlopeRiftStart;
                float _SlopeBlend;
                float _NoiseScale;
                float _NoiseStrength;
                float _DetailScale;
                float _DetailStrength;
                float _Saturation;
            CBUFFER_END

            // Global only (not in Properties) — Cesium clones materials per tile primitive.
            float _RTG_HeightReferenceY;

            TEXTURE2D(_baseColorTexture);
            SAMPLER(sampler_baseColorTexture);

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    v += a * ValueNoise(p);
                    p *= 2.03;
                    a *= 0.5;
                }
                return v;
            }

            float3 SaturateColor(float3 color, float amount)
            {
                float luma = dot(color, float3(0.299, 0.587, 0.114));
                return lerp(float3(luma, luma, luma), color, amount);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float slope = 1.0 - saturate(n.y);

                float heightBand = (input.positionWS.y - _RTG_HeightReferenceY) / max(_HeightScaleM, 1.0);
                float macro = Fbm(input.positionWS.xz * _NoiseScale);
                float detail = ValueNoise(input.positionWS.xz * _DetailScale);
                float patch = saturate((macro - 0.35) * 2.5);

                float3 lowToPlain = lerp(_WetlandColor.rgb, _PlainColor.rgb, saturate((heightBand + 0.08) * 3.0));
                float3 mid = lerp(lowToPlain, _ForestColor.rgb, patch * _NoiseStrength);
                float3 high = lerp(mid, _HighlandColor.rgb, saturate((heightBand - 0.12) * 2.0));
                float3 baseColor = lerp(high, _RiftColor.rgb,
                    saturate(pow(slope / max(_SlopeRiftStart, 0.001), _SlopeBlend)));

                baseColor = lerp(baseColor, baseColor * (0.82 + detail * 0.36), _DetailStrength);
                baseColor *= _baseColorFactor.rgb;
                baseColor = SaturateColor(baseColor, _Saturation);

                return half4(baseColor, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
