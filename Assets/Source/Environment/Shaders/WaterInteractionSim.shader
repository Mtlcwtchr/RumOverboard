// GPU interaction map for the ocean (RumOverboard ▸ WaterInteraction):
//   R = foam (0..1)            — stamped by hulls, spreads out and fades (wakes, bow foam, slams)
//   G = surface height (m)     — pushed up at the bow / down along the quarters
//   B = vertical velocity (m/s)— a damped 2D wave equation turns the pushes into ripples that run away
// One pass per frame (ping-pong): re-centre (the map follows the camera), diffuse/decay foam, step
// the waves, then apply this frame's stamps (oriented ellipses). Sampled by OceanStylizedURP.
Shader "Hidden/RumOverboard/WaterInteractionSim"
{
    Properties
    {
        _MainTex ("Previous", 2D) = "black" {}
    }

    SubShader
    {
        ZTest Always Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            #define MAX_STAMPS 64

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float2 _Shift;          // uv offset of the previous frame's map (re-centring)
            float4 _Rect;           // minX, minZ, size, 1/size
            float _Dt;
            float _FoamDecay;
            float _FoamSpread;
            float _WaveC2;          // (speed / texel)^2
            float _WaveDamping;
            float _HeightRestore;
            float _StampPush;
            int _StampCount;
            float4 _StampA[MAX_STAMPS]; // x, z, radius, foam
            float4 _StampB[MAX_STAMPS]; // dirX, dirZ, stretch, height

            float4 Prev(float2 uv)
            {
                if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1)
                    return 0;
                return tex2Dlod(_MainTex, float4(uv, 0, 0));
            }

            float4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv + _Shift;
                float2 t = _MainTex_TexelSize.xy;

                float4 c = Prev(uv);
                float4 l = Prev(uv - float2(t.x, 0));
                float4 r = Prev(uv + float2(t.x, 0));
                float4 d = Prev(uv - float2(0, t.y));
                float4 u = Prev(uv + float2(0, t.y));

                // Foam: spread + fade.
                float foamAvg = (l.r + r.r + d.r + u.r) * 0.25;
                float foam = lerp(c.r, max(c.r * 0.9, foamAvg), saturate(_FoamSpread * _Dt));
                foam *= exp(-_FoamDecay * _Dt);

                // Waves: damped wave equation (semi-implicit Euler).
                float lap = (l.g + r.g + d.g + u.g) - 4.0 * c.g;
                float v = c.b + _WaveC2 * lap * _Dt;
                v *= exp(-_WaveDamping * _Dt);
                float h = c.g + v * _Dt;
                h *= exp(-_HeightRestore * _Dt);

                // Stamps from hulls this frame.
                float2 world = _Rect.xy + i.uv * _Rect.z;
                int count = min(_StampCount, MAX_STAMPS);
                [loop]
                for (int k = 0; k < count; k++)
                {
                    float4 a = _StampA[k];
                    float4 b = _StampB[k];
                    float2 dp = world - a.xy;
                    float2 dir = b.xy;
                    float2 perp = float2(-dir.y, dir.x);
                    float along = dot(dp, dir) / max(0.01, a.z * b.z);
                    float across = dot(dp, perp) / max(0.01, a.z);
                    float q = along * along + across * across;
                    if (q >= 1.0)
                        continue;
                    float w = (1.0 - q);
                    w *= w;
                    foam = max(foam, w * a.w);
                    float push = w * _StampPush;
                    h = lerp(h, b.w, push);
                    v *= (1.0 - push);
                }

                // Fade to rest at the border so re-centring never leaves a hard edge.
                float2 e = min(i.uv, 1.0 - i.uv);
                float edge = saturate(min(e.x, e.y) * 24.0);
                return float4(saturate(foam) * edge, clamp(h, -4, 4) * edge, clamp(v, -8, 8) * edge, 1);
            }
            ENDCG
        }
    }
    FallBack Off
}

