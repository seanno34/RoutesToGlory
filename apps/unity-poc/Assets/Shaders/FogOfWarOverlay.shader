Shader "RoutesToGlory/FogOfWarOverlay"
{
    Properties
    {
        _FogColor ("Fog Color", Color) = (0.0588, 0.0902, 0.1647, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.92
        _NoiseScale ("Noise Scale", Float) = 0.015
        _PulseSpeed ("Pulse Speed", Float) = 0.35
        _EdgeShimmer ("Edge Shimmer", Range(0, 1)) = 0
        _PlayerLatLng ("Player LatLng", Vector) = (0, 0, 0, 0)
        _LiveRevealRadiusM ("Live Reveal Radius (m)", Float) = 35
        _TileBounds ("Tile Bounds SWNE", Vector) = (0, 0, 0, 0)
        _RevealMin ("Reveal Min SW", Vector) = (9999, 9999, 0, 0)
        _RevealMax ("Reveal Max NE", Vector) = (-9999, -9999, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+120"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "FogOfWar"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;
                float _Opacity;
                float _NoiseScale;
                float _PulseSpeed;
                float _EdgeShimmer;
                float4 _PlayerLatLng;
                float _LiveRevealRadiusM;
                float4 _TileBounds;
                float4 _RevealMin;
                float4 _RevealMax;
            CBUFFER_END

            float LatLngDistM(float2 a, float2 b)
            {
                const float latM = 111320.0;
                float avgLat = radians((a.x + b.x) * 0.5);
                float lngM = latM * cos(avgLat);
                float2 d = float2((b.x - a.x) * latM, (b.y - a.y) * lngM);
                return length(d);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(worldPos);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Per-fragment lat/lng across this 400 m tile (south, west, north, east).
                float fragLat = lerp(_TileBounds.x, _TileBounds.z, input.uv.y);
                float fragLng = lerp(_TileBounds.y, _TileBounds.w, input.uv.x);

                // Permanent reveal — areas the player has already uncovered stay clear.
                if (fragLat >= _RevealMin.x && fragLat <= _RevealMax.x &&
                    fragLng >= _RevealMin.y && fragLng <= _RevealMax.y)
                    discard;

                // Live reveal bubble around the pin.
                float dist = LatLngDistM(float2(fragLat, fragLng), _PlayerLatLng.xy);

                float edge = max(3.0, _LiveRevealRadiusM * 0.25);
                float clear = 1.0 - smoothstep(_LiveRevealRadiusM - edge, _LiveRevealRadiusM, dist);
                if (clear > 0.05)
                    discard;

                float2 noiseUv = float2(fragLng, fragLat) * _NoiseScale * 1000.0;
                float n = frac(sin(dot(noiseUv, float2(127.1, 311.7))) * 43758.5453);
                float pulse = 0.5 + 0.5 * sin(_Time.y * _PulseSpeed + n * 6.28318);
                float shimmer = lerp(1.0, 1.0 + pulse * 0.25, _EdgeShimmer);

                half4 col = _FogColor;
                col.a = saturate(_Opacity * (0.88 + n * 0.12) * shimmer);
                return col;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
