Shader "Source/Gameplay/Ocean/StylizedURP"
{
    Properties
    {
        _BaseColorDeep ("Deep Color", Color) = (0.05, 0.32, 0.55, 1)
        _BaseColorShallow ("Shallow Color", Color) = (0.13, 0.62, 0.68, 1)
        _FoamColor ("Foam Color", Color) = (0.88, 0.95, 1.0, 1)
        _ShallowDepthMax ("Shallow Depth Max", Float) = 5
        _DepthFadeDistance ("Depth Fade Distance", Float) = 8
        _TransparencyMin ("Min Alpha", Range(0,1)) = 0.3
        _TransparencyMax ("Max Alpha", Range(0,1)) = 0.84
        _FoamIntensity ("Foam Intensity", Range(0,1)) = 0.4
        _CrestFoamThreshold ("Crest Foam Threshold", Range(0,1)) = 0.62

        [Header(Lighting)]
        _Gloss ("Sun Glint Gloss", Range(0,1)) = 0.55
        _SpecularPower ("Sun Glint Power", Range(4,256)) = 48
        _Roughness ("Water Roughness", Range(0.02,1)) = 0.12
        _ReflectionStrength ("Reflection Strength", Range(0,1)) = 0.4
        _SubsurfaceColor ("Subsurface Color", Color) = (0.16, 0.66, 0.58, 1)
        _SubsurfaceStrength ("Subsurface Strength", Range(0,3)) = 0.85

        [Header(Detail and Ripples)]
        _RippleScale ("Ripple Scale", Range(0.1,4)) = 1.2
        _RippleSpeed ("Ripple Speed", Range(0,4)) = 0.65
        [Normal] _DetailNormal ("Detail Normal", 2D) = "bump" {}
        _DetailNormalScale ("Detail Normal Scale", Range(0,2)) = 0.6
        _DetailTiling ("Detail Tiling", Float) = 0.15
        _DetailScroll ("Detail Scroll Speed", Float) = 0.04

        [Header(Refraction)]
        _RefractionStrength ("Refraction Strength", Range(0,0.12)) = 0.025

        [Header(Distance detail falloff)]
        _DetailFadeStart ("Detail Fade Start", Float) = 110
        _DetailFadeEnd ("Detail Fade End", Float) = 230

        _SeaLevel ("Sea Level", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            // Reflection-probe blending + main-light keywords so env reflection & lighting resolve.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            #define MAX_WAVES 12

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColorDeep;
            float4 _BaseColorShallow;
            float4 _FoamColor;
            float4 _SubsurfaceColor;
            float _ShallowDepthMax;
            float _DepthFadeDistance;
            float _TransparencyMin;
            float _TransparencyMax;
            float _FoamIntensity;
            float _CrestFoamThreshold;
            float _Gloss;
            float _SpecularPower;
            float _Roughness;
            float _ReflectionStrength;
            float _SubsurfaceStrength;
            float _RippleScale;
            float _RippleSpeed;
            float _DetailNormalScale;
            float _DetailTiling;
            float _DetailScroll;
            float _RefractionStrength;
            float _DetailFadeStart;
            float _DetailFadeEnd;
            float _SeaLevel;
            float _OceanTime;
            float _ShallowColorBoost;
            int _WaveCount;
            CBUFFER_END

            float4 _WaveDirAmp[MAX_WAVES];
            float4 _WaveParams[MAX_WAVES];

            TEXTURE2D(_DetailNormal);
            SAMPLER(sampler_DetailNormal);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            // Gerstner displacement on the GPU — same wave params (already baked with jitter/
            // choppiness/amplitude) the CPU physics uses, so the visible surface matches the
            // buoyancy surface. Runs per-vertex on the GPU: the CPU no longer touches vertices.
            float3 GerstnerDisplacement(float2 worldXZ, float fade)
            {
                float3 d = 0;
                int count = clamp(_WaveCount, 0, MAX_WAVES);
                [loop]
                for (int i = 0; i < count; i++)
                {
                    float2 dir = normalize(_WaveDirAmp[i].xy);
                    float amp = _WaveDirAmp[i].z;
                    float steep = _WaveDirAmp[i].w;

                    float k = _WaveParams[i].x;
                    float speed = _WaveParams[i].y;
                    float phase = _WaveParams[i].z;
                    float vw = _WaveParams[i].w;

                    float p = k * dot(dir, worldXZ) - k * speed * _OceanTime + phase;
                    float s = sin(p);
                    float c = cos(p);
                    float qa = steep * amp;

                    d.x += qa * dir.x * c * vw;
                    d.z += qa * dir.y * c * vw;
                    d.y += amp * s * vw;
                }
                return d * fade;
            }

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);

                // Waves fade to flat with distance so the huge far disk stays calm, single-colour and cheap.
                float camDist = distance(worldPos.xz, _WorldSpaceCameraPos.xz);
                float waveFade = 1.0 - smoothstep(_DetailFadeStart, _DetailFadeEnd, camDist);
                worldPos += GerstnerDisplacement(worldPos.xz, waveFade);

                o.positionCS = TransformWorldToHClip(worldPos);
                o.worldPos = worldPos;
                o.worldNormal = float3(0, 1, 0); // fragment rebuilds the normal from the wave slope
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.uv = input.uv;
                return o;
            }

            // Reconstructs the wave slope + crest-foam mask from the CPU-baked uniforms (which now
            // carry the same jitter/choppiness/amplitude the physics surface uses).
            void EvaluateWaveSlope(float2 worldXZ, out float2 slope, out float crest)
            {
                slope = 0;
                crest = 0;

                int count = clamp(_WaveCount, 0, MAX_WAVES);
                [loop]
                for (int i = 0; i < count; i++)
                {
                    float2 dir = normalize(_WaveDirAmp[i].xy);
                    float amp = _WaveDirAmp[i].z;
                    float steep = _WaveDirAmp[i].w;

                    float k = _WaveParams[i].x;
                    float speed = _WaveParams[i].y;
                    float phase = _WaveParams[i].z;
                    float visualWeight = _WaveParams[i].w;

                    float p = k * dot(dir, worldXZ) - k * speed * _OceanTime + phase;
                    float c = cos(p);
                    float s = sin(p);

                    float slopeMag = amp * k * visualWeight;
                    slope += dir * (slopeMag * c);

                    float localCrest = saturate((abs(s) * steep) - _CrestFoamThreshold);
                    crest += localCrest * visualWeight;
                }

                crest = saturate(crest * _FoamIntensity);
            }

            // Two counter-scrolling detail-normal layers, unpacked as an XZ slope perturbation
            // (no tangents needed — the water mesh is flat in UV).
            float2 SampleDetailSlope(float2 worldXZ)
            {
                float2 baseUV = worldXZ * _DetailTiling;
                float2 uv0 = baseUV + _OceanTime * _DetailScroll * float2(1.0, 0.6);
                float2 uv1 = baseUV * 1.7 - _OceanTime * _DetailScroll * float2(0.5, 1.0);

                float3 n0 = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, uv0), _DetailNormalScale);
                float3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, uv1), _DetailNormalScale);
                return (n0.xy + n1.xy) * 0.5;
            }

            float3 BuildStylizedNormal(float3 baseNormal, float2 waveSlope, float2 worldXZ, float detailFade)
            {
                float2 rippleUV = worldXZ * (0.03 * _RippleScale) + _OceanTime * _RippleSpeed;
                float rippleX = sin(rippleUV.x * 6.28318 + rippleUV.y * 1.73) * 0.12;
                float rippleZ = cos(rippleUV.y * 6.28318 + rippleUV.x * 1.37) * 0.12;

                float2 detail = SampleDetailSlope(worldXZ);
                // Fade the fine surface detail with distance so the far sea is smooth & single-colour.
                float2 s = waveSlope + (float2(rippleX, rippleZ) + detail) * detailFade;

                float3 waveN = normalize(float3(-s.x, 1.0, -s.y));
                return normalize(lerp(baseNormal, waveN, 0.82 * detailFade));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 screenUV = i.screenPos.xy / max(0.0001, i.screenPos.w);
                float rawSceneDepth = SampleSceneDepth(screenUV);
                float sceneEyeDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);
                float waterEyeDepth = LinearEyeDepth(i.positionCS.z / max(0.0001, i.positionCS.w), _ZBufferParams);
                float depthDiff = max(0, sceneEyeDepth - waterEyeDepth);

                float shallow01 = saturate(depthDiff / max(0.001, _ShallowDepthMax));
                float depthFade = saturate(depthDiff / max(0.001, _DepthFadeDistance));

                // Distance detail falloff: the far sea flattens to a calm single-colour surface.
                float camDist = distance(i.worldPos.xz, _WorldSpaceCameraPos.xz);
                float detailFade = 1.0 - smoothstep(_DetailFadeStart, _DetailFadeEnd, camDist);

                float2 slope;
                float crestFoam;
                EvaluateWaveSlope(i.worldPos.xz, slope, crestFoam);
                slope *= detailFade;
                crestFoam *= detailFade;

                float3 normal = BuildStylizedNormal(normalize(i.worldNormal), slope, i.worldPos.xz, detailFade);

                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);
                float3 viewDir = SafeNormalize(GetWorldSpaceViewDir(i.worldPos));
                float3 halfDir = normalize(lightDir + viewDir);

                float ndl = saturate(dot(normal, lightDir));
                float ndv = saturate(dot(normal, viewDir));
                float ndh = saturate(dot(normal, halfDir));
                float fresnel = pow(1.0 - ndv, 5.0);

                // --- Base water color (cozy deep/shallow ramp) ---
                float3 shallowColor = _BaseColorShallow.rgb * (1.0 + _ShallowColorBoost * 0.35);
                float3 deepColor = _BaseColorDeep.rgb;
                float3 waterColor = lerp(shallowColor, deepColor, shallow01);

                // --- Refraction: nudge the opaque scene behind the water, strongest in shallows ---
                float2 refractOffset = normal.xz * _RefractionStrength * (1.0 - depthFade);
                float3 refracted = SampleSceneColor(screenUV + refractOffset);
                waterColor = lerp(refracted, waterColor, saturate(0.35 + shallow01 * 0.65));

                // --- Ambient + wrapped diffuse main light ---
                float3 ambient = SampleSH(normal);
                float3 diffuse = mainLight.color * (0.5 + 0.5 * ndl);
                float3 lit = waterColor * (ambient + diffuse);

                // --- Environment reflection (sky / reflection probe), fresnel-weighted ---
                // Sample the probe cubemap directly — stable across URP versions.
                float3 reflDir = reflect(-viewDir, normal);
                half4 encodedRefl = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, reflDir, _Roughness * 5.0);
                float3 envColor = DecodeHDREnvironment(encodedRefl, unity_SpecCube0_HDR);
                float reflW = saturate(_ReflectionStrength * (0.15 + fresnel * 0.85));
                lit = lerp(lit, envColor, reflW);

                // --- Subsurface / translucency: crests glow when the sun is behind them ---
                float back = saturate(dot(viewDir, -lightDir));
                float sss = pow(back, 3.0) * (crestFoam + shallow01 * 0.5) * _SubsurfaceStrength;
                lit += _SubsurfaceColor.rgb * mainLight.color * sss;

                // --- Foam: shoreline + crest whitecaps on steep/tall wave faces, mottled by a
                //     moving breakup so the caps look churned rather than a flat wash ---
                float shoreFoam = (1.0 - depthFade) * _FoamIntensity;
                float slopeMag = length(slope);
                float whitecap = saturate((slopeMag - 0.30) * 1.6) * detailFade;
                float breakup = 0.55 + 0.45 * sin(i.worldPos.x * 0.7 + _OceanTime * 0.9) * cos(i.worldPos.z * 0.6 - _OceanTime * 0.6);
                whitecap *= breakup;
                float foam = saturate(shoreFoam + crestFoam + whitecap * _FoamIntensity);
                lit += _FoamColor.rgb * foam;

                // --- Sun glint ---
                float spec = pow(ndh, _SpecularPower) * _Gloss;
                lit += lerp(float3(0.4, 0.5, 0.65), float3(1.0, 1.0, 1.0), fresnel) * spec * mainLight.color;

                float alpha = lerp(_TransparencyMin, _TransparencyMax, depthFade);
                alpha = saturate(alpha + foam * 0.15 + fresnel * 0.1);

                return half4(lit, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
