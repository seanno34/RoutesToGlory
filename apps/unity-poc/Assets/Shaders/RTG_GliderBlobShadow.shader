Shader "RTG/GliderBlobShadow"
{
    Properties
    {
        _ShadowColor ("Shadow", Color) = (0.02, 0.05, 0.12, 0.42)
        _Softness ("Softness", Range(0.5, 4)) = 2.2
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

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * 2.0 - 1.0;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half dist = length(input.uv);
                half alpha = saturate(1.0h - dist);
                alpha = pow(alpha, _Softness);
                return half4(_ShadowColor.rgb, _ShadowColor.a * alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
