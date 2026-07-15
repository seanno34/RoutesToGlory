Shader "RTG/PlayerShipPresentation"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _RimColor ("Rim", Color) = (0.45, 0.85, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.4
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.38
        _TopLightStrength ("Top Light", Range(0, 1)) = 0.22
        _BottomShadeStrength ("Bottom Shade", Range(0, 1)) = 0.18
        _EmissiveBoost ("Emissive Boost", Range(0, 2)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half4 _RimColor;
                half _RimPower;
                half _RimStrength;
                half _TopLightStrength;
                half _BottomShadeStrength;
                half _EmissiveBoost;
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
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = normalize(_WorldSpaceCameraPos.xyz - positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(tex.a - 0.04h);

                half3 albedo = tex.rgb * _Color.rgb;
                half3 normalWS = normalize(input.normalWS);
                half3 viewDir = normalize(input.viewDirWS);

                half ndv = saturate(dot(normalWS, viewDir));
                half rim = pow(1.0h - ndv, _RimPower) * _RimStrength;
                half topLight = saturate(normalWS.y) * _TopLightStrength;
                half bottomShade = saturate(-normalWS.y) * _BottomShadeStrength;

                half3 lit = albedo;
                lit += _RimColor.rgb * rim;
                lit *= 1.0h + topLight - bottomShade;
                lit += albedo * tex.a * _EmissiveBoost * saturate(tex.b - tex.r * 0.35h);

                return half4(lit, tex.a * _Color.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
