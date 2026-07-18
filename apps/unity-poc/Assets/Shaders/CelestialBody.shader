Shader "RoutesToGlory/CelestialBody"
{
    Properties
    {
        [MainTexture] _BaseMap ("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Brightness ("Brightness", Range(0.05, 4)) = 1.15
        _RimStrength ("Rim Glow", Range(0, 1)) = 0.22
        _HorizonHaze ("Horizon Haze", Range(0, 1)) = 0.35
        _ElevationDegrees ("Elevation Degrees", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Geometry"
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "CelestialBody"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Brightness;
                half _RimStrength;
                half _HorizonHaze;
                half _ElevationDegrees;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS);
                output.positionCS = pos.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = nrm.normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(pos.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = SafeNormalize(input.viewDirWS);

                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(normalWS, mainLight.direction));
                // Soft night fill so the dark side isn't pure black under dim moonlight.
                half3 lit = albedo.rgb * (mainLight.color * (0.22h + 0.78h * ndotl));

                half rim = pow(saturate(1.0h - saturate(dot(normalWS, viewDirWS))), 2.5h);
                lit += albedo.rgb * rim * _RimStrength * 0.65h;

                // Horizon haze: lower elevation → pull toward fog/horizon violet, softer contrast.
                half haze = saturate(_HorizonHaze * saturate(1.0h - _ElevationDegrees / 8.0h));
                half3 hazeColor = half3(0.06h, 0.03h, 0.11h);
                lit = lerp(lit, lerp(lit, hazeColor, 0.55h), haze);
                lit *= _Brightness;

                return half4(lit, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
