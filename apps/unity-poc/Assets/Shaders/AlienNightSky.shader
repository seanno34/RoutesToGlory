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

        [Header(PlanetA)]
        _PlanetADir ("Planet A Dir (xyz)", Vector) = (0.55, 0.62, 0.35, 0)
        _PlanetAColor ("Planet A Color", Color) = (0.78, 0.42, 0.58, 1)
        _PlanetASize ("Planet A Size", Range(0.004, 0.06)) = 0.026
        _PlanetABright ("Planet A Bright", Range(0, 2)) = 0.95
        _PlanetARing ("Planet A Ring", Range(0, 1)) = 1
        _PlanetARingWidth ("Planet A Ring Width", Range(0.5, 3)) = 1.55
        _PlanetARingBright ("Planet A Ring Bright", Range(0, 2)) = 0.75

        [Header(PlanetB)]
        _PlanetBDir ("Planet B Dir (xyz)", Vector) = (-0.72, 0.48, 0.22, 0)
        _PlanetBColor ("Planet B Color", Color) = (0.32, 0.48, 0.88, 1)
        _PlanetBSize ("Planet B Size", Range(0.004, 0.06)) = 0.022
        _PlanetBBright ("Planet B Bright", Range(0, 2)) = 0.9
        _PlanetBRing ("Planet B Ring", Range(0, 1)) = 0
        _PlanetBRingWidth ("Planet B Ring Width", Range(0.5, 3)) = 1.4
        _PlanetBRingBright ("Planet B Ring Bright", Range(0, 2)) = 0.6

        [Header(PlanetC)]
        _PlanetCDir ("Planet C Dir (xyz)", Vector) = (0.15, 0.42, -0.85, 0)
        _PlanetCColor ("Planet C Color", Color) = (0.55, 0.38, 0.72, 1)
        _PlanetCSize ("Planet C Size", Range(0.004, 0.06)) = 0.02
        _PlanetCBright ("Planet C Bright", Range(0, 2)) = 0.75
        _PlanetCRing ("Planet C Ring", Range(0, 1)) = 0
        _PlanetCRingWidth ("Planet C Ring Width", Range(0.5, 3)) = 1.4
        _PlanetCRingBright ("Planet C Ring Bright", Range(0, 2)) = 0.6

        [Header(PlanetD)]
        _PlanetDDir ("Planet D Dir (xyz)", Vector) = (-0.25, 0.78, -0.45, 0)
        _PlanetDColor ("Planet D Color", Color) = (0.48, 0.88, 0.72, 1)
        _PlanetDSize ("Planet D Size", Range(0.004, 0.06)) = 0.011
        _PlanetDBright ("Planet D Bright", Range(0, 2)) = 0.8
        _PlanetDRing ("Planet D Ring", Range(0, 1)) = 0
        _PlanetDRingWidth ("Planet D Ring Width", Range(0.5, 3)) = 1.3
        _PlanetDRingBright ("Planet D Ring Bright", Range(0, 2)) = 0.5

        [Header(PlanetE)]
        _PlanetEDir ("Planet E Dir (xyz)", Vector) = (0.82, 0.28, -0.35, 0)
        _PlanetEColor ("Planet E Color", Color) = (0.62, 0.55, 0.78, 1)
        _PlanetESize ("Planet E Size", Range(0.004, 0.06)) = 0.024
        _PlanetEBright ("Planet E Bright", Range(0, 2)) = 0.85
        _PlanetERing ("Planet E Ring", Range(0, 1)) = 1
        _PlanetERingWidth ("Planet E Ring Width", Range(0.5, 3)) = 1.7
        _PlanetERingBright ("Planet E Ring Bright", Range(0, 2)) = 0.7
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

            float4 _PlanetADir;
            float4 _PlanetAColor;
            float _PlanetASize;
            float _PlanetABright;
            float _PlanetARing;
            float _PlanetARingWidth;
            float _PlanetARingBright;

            float4 _PlanetBDir;
            float4 _PlanetBColor;
            float _PlanetBSize;
            float _PlanetBBright;
            float _PlanetBRing;
            float _PlanetBRingWidth;
            float _PlanetBRingBright;

            float4 _PlanetCDir;
            float4 _PlanetCColor;
            float _PlanetCSize;
            float _PlanetCBright;
            float _PlanetCRing;
            float _PlanetCRingWidth;
            float _PlanetCRingBright;

            float4 _PlanetDDir;
            float4 _PlanetDColor;
            float _PlanetDSize;
            float _PlanetDBright;
            float _PlanetDRing;
            float _PlanetDRingWidth;
            float _PlanetDRingBright;

            float4 _PlanetEDir;
            float4 _PlanetEColor;
            float _PlanetESize;
            float _PlanetEBright;
            float _PlanetERing;
            float _PlanetERingWidth;
            float _PlanetERingBright;

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

            // Soft lit disc; optional elliptical ring (ringEnable > 0).
            float3 SoftPlanet(
                float3 dir,
                float3 planetDir,
                float3 color,
                float size,
                float bright,
                float ringEnable,
                float ringWidth,
                float ringBright)
            {
                float3 n = normalize(dir);
                float3 p = normalize(planetDir);
                if (p.y < -0.05)
                    return 0;

                float ang = acos(saturate(dot(n, p)));
                float disc = smoothstep(size, size * 0.55, ang);

                // Soft limb darkening — subdued so discs read as planets, not suns.
                float limb = saturate(1.0 - ang / max(1e-4, size));
                float shade = pow(limb, 0.7);
                float3 lit = color * (0.28 + 0.55 * shade);

                // Thin atmospheric rim (cool, not solar flare).
                float rim = smoothstep(size * 1.25, size * 0.92, ang) - disc;
                rim = saturate(rim);
                float3 atmosphere = color * 0.85 * rim;

                // Tight, dim halo — avoid oversized bright sun discs.
                float halo = smoothstep(size * 2.1, size * 0.95, ang) * 0.08;

                float3 body = (lit * disc + atmosphere + color * halo) * bright;

                float3 rings = 0;
                if (ringEnable > 0.01)
                {
                    // Orthonormal basis in the ring plane; flatten one axis for a tilted ellipse.
                    float3 upHint = abs(p.y) > 0.92 ? float3(1, 0, 0) : float3(0, 1, 0);
                    float3 ringX = normalize(cross(p, upHint));
                    float3 ringY = cross(p, ringX);
                    float3 offset = n - p * dot(n, p);
                    float2 uv = float2(dot(offset, ringX), dot(offset, ringY) * 0.38);
                    float r = length(uv);

                    float inner = size * 1.15;
                    float outer = size * max(1.2, ringWidth);
                    float ringBand = smoothstep(inner, inner + size * 0.12, r)
                                   * smoothstep(outer + size * 0.1, outer, r);

                    // Hide ring where the planet disc occludes it; keep a soft gap.
                    ringBand *= 1.0 - disc;
                    // Prefer the near side of the sky so rings don't smear below the horizon.
                    ringBand *= saturate(dot(n, p) + 0.15);
                    ringBand *= saturate(n.y + 0.08);

                    float3 ringTint = lerp(color, float3(0.85, 0.8, 0.95), 0.35);
                    rings = ringTint * ringBand * ringBright * ringEnable * bright;
                }

                return body + rings;
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

                // Medium alien worlds + small moon; A and E carry procedural rings.
                skyCol += SoftPlanet(dir, _PlanetADir.xyz, _PlanetAColor.rgb, _PlanetASize, _PlanetABright,
                    _PlanetARing, _PlanetARingWidth, _PlanetARingBright);
                skyCol += SoftPlanet(dir, _PlanetBDir.xyz, _PlanetBColor.rgb, _PlanetBSize, _PlanetBBright,
                    _PlanetBRing, _PlanetBRingWidth, _PlanetBRingBright);
                skyCol += SoftPlanet(dir, _PlanetCDir.xyz, _PlanetCColor.rgb, _PlanetCSize, _PlanetCBright,
                    _PlanetCRing, _PlanetCRingWidth, _PlanetCRingBright);
                skyCol += SoftPlanet(dir, _PlanetDDir.xyz, _PlanetDColor.rgb, _PlanetDSize, _PlanetDBright,
                    _PlanetDRing, _PlanetDRingWidth, _PlanetDRingBright);
                skyCol += SoftPlanet(dir, _PlanetEDir.xyz, _PlanetEColor.rgb, _PlanetESize, _PlanetEBright,
                    _PlanetERing, _PlanetERingWidth, _PlanetERingBright);

                skyCol *= _Exposure;
                return float4(skyCol, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
