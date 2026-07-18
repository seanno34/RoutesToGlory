Shader "RoutesToGlory/AlienTerrainBiome"
{
    Properties
    {
        [Header(CesiumCompat)]
        _baseColorFactor ("Base Color Factor", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _baseColorTexture ("Base Color Texture", 2D) = "white" {}

        [Header(BiomeColors)]
        _PlainColor ("Alien Plains", Color) = (0.09, 0.045, 0.14, 1)
        _WastelandColor ("Dust Expanse", Color) = (0.11, 0.06, 0.13, 1)
        _WetlandColor ("Fungal Marsh", Color) = (0.05, 0.04, 0.11, 1)
        _ForestColor ("Fungal Forest", Color) = (0.08, 0.055, 0.16, 1)
        _HighlandColor ("Crystal Highland", Color) = (0.16, 0.10, 0.24, 1)
        _RiftColor ("Volcanic Rift", Color) = (0.20, 0.05, 0.18, 1)
        _WaterColor ("Deep Violet Sea", Color) = (0.025, 0.02, 0.07, 1)

        [Header(NeonVeins)]
        _VeinColor ("Vein Color", Color) = (1.0, 0.22, 0.65, 1)
        _VeinCellSizeM ("Vein Cell Size (m)", Float) = 720
        _VeinWidthM ("Vein Width (m)", Float) = 9
        _VeinWarp ("Vein Warp", Range(0, 1)) = 0.48
        _VeinDensity ("Vein Density (higher=sparser)", Range(0, 1)) = 0.68
        _VeinSharpness ("Vein Sharpness", Range(1, 12)) = 8.5
        _VeinEmission ("Vein Emission", Range(0, 3)) = 0.38
        _VeinBlend ("Vein Blend", Range(0, 1)) = 0.52
        _VeinFilamentScaleM ("Filament Scale (m)", Float) = 140

        [Header(HeightSlope)]
        _HeightScaleM ("Height Band (world units)", Float) = 55
        _SlopeRiftStart ("Slope Rift Start", Range(0, 1)) = 0.36
        _SlopeBlend ("Slope Blend", Range(0.01, 4)) = 1.2
        _HighlandHeightCutoff ("Highland Cutoff", Float) = 0.22
        _WetlandHeightCutoff ("Wetland Cutoff", Float) = -0.12
        _WaterHeightCutoff ("Water Cutoff", Float) = -0.28

        [Header(MacroRegions)]
        _MacroRegionSizeM ("Macro Region Size (m)", Float) = 4200
        _MacroBorderSoftM ("Macro Border Softness (m)", Float) = 320
        _MacroWarpStrength ("Macro Warp Strength", Range(0, 1)) = 0.42

        [Header(WetBasins)]
        _WetBasinFraction ("Wet Basin Region Fraction", Range(0, 0.5)) = 0.18
        _WetlandWetnessMin ("Wetland Wetness Min", Range(0, 1)) = 0.45
        _WaterWetnessMin ("Water Wetness Min", Range(0, 1)) = 0.55

        [Header(Detail)]
        _DetailScale ("Fine Detail Scale", Float) = 0.06
        _DetailStrength ("Fine Detail Strength", Range(0, 1)) = 0.1
        _Saturation ("Color Saturation", Range(0.5, 2)) = 1.08
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

            #define ZONE_PLAINS 0.0
            #define ZONE_FOREST 1.0
            #define ZONE_WASTELAND 2.0
            #define ZONE_WET_BASIN 3.0

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

            struct MacroSample
            {
                float primaryZone;
                float secondaryZone;
                float borderBlend;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _baseColorFactor;
                float4 _PlainColor;
                float4 _WastelandColor;
                float4 _WetlandColor;
                float4 _ForestColor;
                float4 _HighlandColor;
                float4 _RiftColor;
                float4 _WaterColor;
                float4 _VeinColor;
                float _VeinCellSizeM;
                float _VeinWidthM;
                float _VeinWarp;
                float _VeinDensity;
                float _VeinSharpness;
                float _VeinEmission;
                float _VeinBlend;
                float _VeinFilamentScaleM;
                float _HeightScaleM;
                float _SlopeRiftStart;
                float _SlopeBlend;
                float _HighlandHeightCutoff;
                float _WetlandHeightCutoff;
                float _WaterHeightCutoff;
                float _MacroRegionSizeM;
                float _MacroBorderSoftM;
                float _MacroWarpStrength;
                float _WetBasinFraction;
                float _WetlandWetnessMin;
                float _WaterWetnessMin;
                float _DetailScale;
                float _DetailStrength;
                float _Saturation;
            CBUFFER_END

            float _RTG_HeightReferenceY;

            TEXTURE2D(_baseColorTexture);
            SAMPLER(sampler_baseColorTexture);

            float Hash21(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float2 Hash22(float2 p)
            {
                return float2(Hash21(p), Hash21(p + float2(17.7, 9.2)));
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

            float ZoneFromHash(float h, float wetFraction)
            {
                float forestCut = wetFraction + 0.28;
                float wasteCut = forestCut + 0.22;
                float zone = ZONE_PLAINS;
                if (h < wetFraction) zone = ZONE_WET_BASIN;
                else if (h < forestCut) zone = ZONE_FOREST;
                else if (h < wasteCut) zone = ZONE_WASTELAND;
                return zone;
            }

            float3 ZoneColor(float zone)
            {
                if (zone > 2.5) return _PlainColor.rgb;
                if (zone > 1.5) return _WastelandColor.rgb;
                if (zone > 0.5) return _ForestColor.rgb;
                return _PlainColor.rgb;
            }

            float VoronoiDist(float2 cellId, float2 cellUv, float2 offset)
            {
                float2 neighbor = cellId + offset;
                float2 feature = Hash22(neighbor) * 0.72 + 0.14;
                float2 diff = offset + feature - cellUv;
                return dot(diff, diff);
            }

            MacroSample SampleMacroRegion(float2 worldXZ)
            {
                MacroSample result;
                float cellSize = max(_MacroRegionSizeM, 400.0);
                float invCell = 1.0 / cellSize;

                float2 warp = (float2(
                    ValueNoise(worldXZ * invCell * 0.31 + 12.7),
                    ValueNoise(worldXZ * invCell * 0.31 + 51.2)) - 0.5) * _MacroWarpStrength * cellSize;
                float2 warpedXZ = worldXZ + warp;

                float2 cellId = floor(warpedXZ * invCell);
                float2 cellUv = frac(warpedXZ * invCell);

                float bestDist = 1e5;
                float secondDist = 1e5;
                float2 bestCell = cellId;
                float2 secondCell = cellId;

                float d;
                d = VoronoiDist(cellId, cellUv, float2(-1, -1)); if (d < bestDist) { secondDist = bestDist; secondCell = bestCell; bestDist = d; bestCell = cellId + float2(-1, -1); } else if (d < secondDist) { secondDist = d; secondCell = cellId + float2(-1, -1); }
                d = VoronoiDist(cellId, cellUv, float2(0, -1));  if (d < bestDist) { secondDist = bestDist; secondCell = bestCell; bestDist = d; bestCell = cellId + float2(0, -1); }  else if (d < secondDist) { secondDist = d; secondCell = cellId + float2(0, -1); }
                d = VoronoiDist(cellId, cellUv, float2(1, -1));  if (d < bestDist) { secondDist = bestDist; secondCell = bestCell; bestDist = d; bestCell = cellId + float2(1, -1); }  else if (d < secondDist) { secondDist = d; secondCell = cellId + float2(1, -1); }
                d = VoronoiDist(cellId, cellUv, float2(-1, 0));  if (d < bestDist) { secondDist = bestDist; secondCell = bestCell; bestDist = d; bestCell = cellId + float2(-1, 0); }  else if (d < secondDist) { secondDist = d; secondCell = cellId + float2(-1, 0); }
                d = VoronoiDist(cellId, cellUv, float2(0, 0));   if (d < bestDist) { secondDist = bestDist; secondCell = bestCell; bestDist = d; bestCell = cellId + float2(0, 0); }   else if (d < secondDist) { secondDist = d; secondCell = cellId + float2(0, 0); }
                d = VoronoiDist(cellId, cellUv, float2(1, 0));   if (d < bestDist) { secondDist = bestDist; secondCell = bestCell; bestDist = d; bestCell = cellId + float2(1, 0); }   else if (d < secondDist) { secondDist = d; secondCell = cellId + float2(1, 0); }
                d = VoronoiDist(cellId, cellUv, float2(-1, 1));  if (d < bestDist) { secondDist = bestDist; secondCell = bestCell; bestDist = d; bestCell = cellId + float2(-1, 1); }  else if (d < secondDist) { secondDist = d; secondCell = cellId + float2(-1, 1); }
                d = VoronoiDist(cellId, cellUv, float2(0, 1));   if (d < bestDist) { secondDist = bestDist; secondCell = bestCell; bestDist = d; bestCell = cellId + float2(0, 1); }   else if (d < secondDist) { secondDist = d; secondCell = cellId + float2(0, 1); }
                d = VoronoiDist(cellId, cellUv, float2(1, 1));   if (d < bestDist) { secondDist = bestDist; secondCell = bestCell; bestDist = d; bestCell = cellId + float2(1, 1); }   else if (d < secondDist) { secondDist = d; secondCell = cellId + float2(1, 1); }

                float bestHash = Hash21(bestCell * 2.17 + 9.4);
                float secondHash = Hash21(secondCell * 2.17 + 9.4);
                result.primaryZone = ZoneFromHash(bestHash, _WetBasinFraction);
                result.secondaryZone = ZoneFromHash(secondHash, _WetBasinFraction);

                float edgeDistM = sqrt(bestDist) * cellSize;
                float softM = max(_MacroBorderSoftM, 1.0);
                result.borderBlend = smoothstep(0.0, softM, edgeDistM);
                return result;
            }

            // Sparse organic rift veins in world XZ — Voronoi edges + ridge filaments.
            float SampleVeinMask(float2 worldXZ)
            {
                float cellSize = max(_VeinCellSizeM, 80.0);
                float invCell = 1.0 / cellSize;

                float2 warp = (float2(
                    ValueNoise(worldXZ * invCell * 0.37 + 41.3),
                    ValueNoise(worldXZ * invCell * 0.37 + 88.1)) - 0.5) * _VeinWarp * cellSize;
                float2 warpedXZ = worldXZ + warp;

                float2 cellId = floor(warpedXZ * invCell);
                float2 cellUv = frac(warpedXZ * invCell);

                float bestDist = 1e5;
                float secondDist = 1e5;
                float2 bestCell = cellId;

                float d;
                d = VoronoiDist(cellId, cellUv, float2(-1, -1)); if (d < bestDist) { secondDist = bestDist; bestDist = d; bestCell = cellId + float2(-1, -1); } else if (d < secondDist) secondDist = d;
                d = VoronoiDist(cellId, cellUv, float2(0, -1));  if (d < bestDist) { secondDist = bestDist; bestDist = d; bestCell = cellId + float2(0, -1); }  else if (d < secondDist) secondDist = d;
                d = VoronoiDist(cellId, cellUv, float2(1, -1));  if (d < bestDist) { secondDist = bestDist; bestDist = d; bestCell = cellId + float2(1, -1); }  else if (d < secondDist) secondDist = d;
                d = VoronoiDist(cellId, cellUv, float2(-1, 0));  if (d < bestDist) { secondDist = bestDist; bestDist = d; bestCell = cellId + float2(-1, 0); }  else if (d < secondDist) secondDist = d;
                d = VoronoiDist(cellId, cellUv, float2(0, 0));   if (d < bestDist) { secondDist = bestDist; bestDist = d; bestCell = cellId; }                  else if (d < secondDist) secondDist = d;
                d = VoronoiDist(cellId, cellUv, float2(1, 0));   if (d < bestDist) { secondDist = bestDist; bestDist = d; bestCell = cellId + float2(1, 0); }   else if (d < secondDist) secondDist = d;
                d = VoronoiDist(cellId, cellUv, float2(-1, 1));  if (d < bestDist) { secondDist = bestDist; bestDist = d; bestCell = cellId + float2(-1, 1); }  else if (d < secondDist) secondDist = d;
                d = VoronoiDist(cellId, cellUv, float2(0, 1));   if (d < bestDist) { secondDist = bestDist; bestDist = d; bestCell = cellId + float2(0, 1); }   else if (d < secondDist) secondDist = d;
                d = VoronoiDist(cellId, cellUv, float2(1, 1));   if (d < bestDist) { secondDist = bestDist; bestDist = d; bestCell = cellId + float2(1, 1); }   else if (d < secondDist) secondDist = d;

                float edgeM = (sqrt(secondDist) - sqrt(bestDist)) * cellSize;
                float halfWidth = max(_VeinWidthM, 1.0) * 0.5;
                float crack = 1.0 - smoothstep(0.0, halfWidth, edgeM);

                // Only light a subset of cell borders so the network stays sparse.
                float presence = smoothstep(_VeinDensity - 0.12, _VeinDensity + 0.05, Hash21(bestCell * 3.17 + 2.4));
                crack *= presence;

                // Secondary ridge filaments for organic scatter between major cracks.
                float filamentScale = 1.0 / max(_VeinFilamentScaleM, 20.0);
                float ridge = ValueNoise(worldXZ * filamentScale + float2(13.7, 29.1));
                ridge = 1.0 - abs(ridge * 2.0 - 1.0);
                float filament = pow(saturate(ridge), _VeinSharpness);
                float filamentMask = smoothstep(0.72, 0.92, ValueNoise(worldXZ * 0.00055 + float2(4.2, 19.8)));
                filament *= filamentMask * 0.28;

                return saturate(max(crack, filament));
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

                MacroSample macro = SampleMacroRegion(input.positionWS.xz);
                float3 macroColor = lerp(
                    ZoneColor(macro.secondaryZone),
                    ZoneColor(macro.primaryZone),
                    macro.borderBlend);

                float localWet = ValueNoise(input.positionWS.xz * 0.00035 + float2(23.1, 9.7));
                float wetBasin = step(2.5, macro.primaryZone) + step(2.5, macro.secondaryZone);
                wetBasin = saturate(wetBasin);
                float wetness = lerp(localWet * 0.25, saturate(localWet * 0.35 + macro.borderBlend * 0.45 + 0.35), wetBasin);

                float3 baseColor = macroColor;
                float riftMask = saturate(pow(slope / max(_SlopeRiftStart, 0.001), _SlopeBlend));

                if (riftMask > 0.45)
                {
                    baseColor = _RiftColor.rgb;
                }
                else if (
                    heightBand <= _WaterHeightCutoff &&
                    slope < 0.12 &&
                    wetBasin > 0.5 &&
                    wetness >= _WaterWetnessMin)
                {
                    baseColor = _WaterColor.rgb;
                }
                else if (
                    heightBand <= _WetlandHeightCutoff &&
                    wetBasin > 0.5 &&
                    wetness >= _WetlandWetnessMin)
                {
                    baseColor = _WetlandColor.rgb;
                }
                else if (heightBand >= _HighlandHeightCutoff)
                {
                    baseColor = _HighlandColor.rgb;
                }

                float detail = ValueNoise(input.positionWS.xz * _DetailScale);
                baseColor = lerp(baseColor, baseColor * (0.86 + detail * 0.28), _DetailStrength);
                baseColor = lerp(baseColor, _RiftColor.rgb, riftMask * 0.8);
                baseColor *= _baseColorFactor.rgb;
                baseColor = SaturateColor(baseColor, _Saturation);

                // Neon pink veins sit on top of the unified purple ground (single pass).
                float vein = SampleVeinMask(input.positionWS.xz);
                // Slightly denser veins on steep rift faces for fracture readability.
                vein = saturate(vein + riftMask * vein * 0.35);
                float3 veinRgb = _VeinColor.rgb;
                baseColor = lerp(baseColor, veinRgb, vein * _VeinBlend);
                baseColor += veinRgb * vein * _VeinEmission;

                return half4(saturate(baseColor), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
