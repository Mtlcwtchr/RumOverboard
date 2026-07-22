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
        _Gloss ("Gloss", Range(0,1)) = 0.55
        _SpecularPower ("Specular Power", Range(4,256)) = 48
        _RippleScale ("Ripple Scale", Range(0.1,4)) = 1.2
        _RippleSpeed ("Ripple Speed", Range(0,4)) = 0.65
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #define MAX_WAVES 12

            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColorDeep;
            float4 _BaseColorShallow;
            float4 _FoamColor;
            float _ShallowDepthMax;
            float _DepthFadeDistance;
            float _TransparencyMin;
            float _TransparencyMax;
            float _FoamIntensity;
            float _CrestFoamThreshold;
            float _Gloss;
            float _SpecularPower;
            float _RippleScale;
            float _RippleSpeed;
            float _SeaLevel;
            float _OceanTime;
            float _ShallowColorBoost;
            int _WaveCount;
            CBUFFER_END

            float4 _WaveDirAmp[MAX_WAVES];
            float4 _WaveParams[MAX_WAVES];

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

            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(worldPos);
                o.worldPos = worldPos;
                o.worldNormal = TransformObjectToWorldNormal(input.normalOS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.uv = input.uv;
                return o;
            }

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

            float3 BuildStylizedNormal(float3 baseNormal, float2 waveSlope, float2 worldXZ)
            {
                float2 rippleUV = worldXZ * (0.03 * _RippleScale) + _OceanTime * _RippleSpeed;
                float rippleX = sin(rippleUV.x * 6.28318 + rippleUV.y * 1.73) * 0.12;
                float rippleZ = cos(rippleUV.y * 6.28318 + rippleUV.x * 1.37) * 0.12;

                float3 waveN = normalize(float3(-(waveSlope.x + rippleX), 1.0, -(waveSlope.y + rippleZ)));
                return normalize(lerp(baseNormal, waveN, 0.78));
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

                float2 slope;
                float crestFoam;
                EvaluateWaveSlope(i.worldPos.xz, slope, crestFoam);

                float3 normal = BuildStylizedNormal(normalize(i.worldNormal), slope, i.worldPos.xz);

                Light mainLight = GetMainLight();
                float3 lightDir = normalize(mainLight.direction);
                float3 viewDir = SafeNormalize(GetWorldSpaceViewDir(i.worldPos));
                float3 halfDir = normalize(lightDir + viewDir);

                float ndl = saturate(dot(normal, lightDir));
                float ndh = saturate(dot(normal, halfDir));
                float fresnel = pow(1.0 - saturate(dot(normal, viewDir)), 3.0);

                float3 shallowColor = _BaseColorShallow.rgb * (1.0 + _ShallowColorBoost * 0.35);
                float3 deepColor = _BaseColorDeep.rgb;
                float3 waterColor = lerp(shallowColor, deepColor, shallow01);

                float shoreFoam = (1.0 - depthFade) * _FoamIntensity;
                float foam = saturate(shoreFoam + crestFoam);
                float3 foamColor = _FoamColor.rgb * foam;

                float spec = pow(ndh, _SpecularPower) * _Gloss;
                float3 specColor = lerp(float3(0.4, 0.5, 0.65), float3(1.0, 1.0, 1.0), fresnel) * spec;

                float3 lit = waterColor * (0.45 + ndl * 0.55) + foamColor + specColor;

                float alpha = lerp(_TransparencyMin, _TransparencyMax, depthFade);
                alpha = saturate(alpha + foam * 0.15 + fresnel * 0.1);

                return half4(lit, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}

