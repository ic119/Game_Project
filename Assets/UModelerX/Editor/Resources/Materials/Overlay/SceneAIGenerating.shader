Shader "UModeler X/SceneAIGenerating"
{
    Properties
    {
        _ColorStart     ("Color Start (0%)",      Color)  = (0.3, 0.3, 1.0, 0.6)
        _ColorEnd       ("Color End (100%)",      Color)  = (0.3, 0.5, 0.8, 0.6)
        _Progress       ("Progress",              Range(0,1)) = 0.0
        _MaxProgress    ("Max Color Blend",       Range(0,1)) = 0.2
        _Speed          ("Move Speed",            Float)  = 0.2
        _Scale          ("Stripe Count",          Float)  = 5.0
        _WarpAmt        ("Warp Amount",           Float)  = 2.0
        _EditorTime     ("Editor Time",           Float)  = 0.0
        _Center         ("Box Center",            Vector) = (0, 0, 0, 0)
        _BoxHalfSize    ("Box Half Size",         Vector) = (0.5, 0.5, 0.5, 0)
        _ThicknessMod   ("Stripe Thickness Mod",  Float)  = 0.45
        _DriftSpeed     ("Wave Drift Speed",      Float)  = 0.4
        _EdgeWidth      ("Edge Glow Width",       Float)  = 0.07
        _EdgeBrightness ("Edge Glow Brightness",  Float)  = 2.0
        _PulseSpeed     ("Alpha Pulse Speed",     Float)  = 1.1
        _PulseAmt       ("Alpha Pulse Amount",    Float)  = 0.18
        _LayerBlend     ("Second Layer Blend",    Float)  = 0.30
        _BrightBias     ("Bright Bias",           Range(1.0, 10.0)) = 2.0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        ZWrite Off
        ZTest LEqual
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _ColorStart;
            fixed4 _ColorEnd;
            float  _Progress;
            float  _MaxProgress;
            float  _Speed;
            float  _Scale;
            float  _WarpAmt;
            float  _EditorTime;
            float4 _Center;
            float4 _BoxHalfSize;
            float  _ThicknessMod;
            float  _DriftSpeed;
            float  _EdgeWidth;
            float  _EdgeBrightness;
            float  _PulseSpeed;
            float  _PulseAmt;
            float  _LayerBlend;
            float  _BrightBias;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            // ─── 3D value noise ───────────────────────────────────────────────
            float hash3(float3 p)
            {
                p  = frac(p * float3(127.1, 311.7, 74.7));
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            float noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(hash3(i),                 hash3(i+float3(1,0,0)), f.x),
                         lerp(hash3(i+float3(0,1,0)),   hash3(i+float3(1,1,0)), f.x), f.y),
                    lerp(lerp(hash3(i+float3(0,0,1)),   hash3(i+float3(1,0,1)), f.x),
                         lerp(hash3(i+float3(0,1,1)),   hash3(i+float3(1,1,1)), f.x), f.y),
                    f.z);
            }

            // ─── 테두리 근접도: 0 = 면 중앙, 1 = 모서리/꼭짓점 ─────────────
            float edgeProximity(float3 localNorm01)
            {
                float3 nearest = min(localNorm01, 1.0 - localNorm01);
                float  minDist = min(nearest.x, min(nearest.y, nearest.z));
                return 1.0 - saturate(minDist / max(_EdgeWidth, 0.0001));
            }

            // ─── Vertex shader ────────────────────────────────────────────────
            v2f vert(appdata v)
            {
                v2f o;
                o.pos      = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // ─── Fragment shader ──────────────────────────────────────────────
            fixed4 frag(v2f i) : SV_Target
            {
                // ── 좌표 준비 ──────────────────────────────────────────────────
                float3 corner    = _Center.xyz - _BoxHalfSize.xyz;
                float3 localPos  = i.worldPos - corner;
                float3 fullDiag  = _BoxHalfSize.xyz * 2.0;
                float  invDiag   = 1.0 / max(length(fullDiag), 0.0001);
                float  rawTime   = _EditorTime * _Speed;
                // frac을 정수배로만 사용 → sin 주기와 일치하여 래핑 시 연속
                float  offset    = frac(rawTime);
                float  offset2   = frac(rawTime * 1.4);  // 두 번째 레이어용 (1.4배 속도)

                // 테두리 계산용 정규화 좌표 [0,1]^3
                float3 localNorm01 = localPos / max(fullDiag, 0.0001);

                // ── 서서히 기울어지는 파동 축 ──────────────────────────────────
                // _EditorTime 직접 사용 → _Speed 와 무관하게 독립 제어
                float  driftAngle = _EditorTime * _DriftSpeed;
                float3 drift = float3(
                    sin(driftAngle * 0.7 + 1.1),
                    cos(driftAngle * 0.5 + 2.3),
                    sin(driftAngle * 0.9 + 0.5)
                ) * 0.4;
                float3 driftedDiag = fullDiag + drift * length(fullDiag);
                float3 diagDir     = normalize(driftedDiag);
                float  maxProj     = dot(fullDiag, diagDir);
                float  distNorm    = dot(localPos, diagDir) / max(maxProj, 0.0001);

                // ── noise 왜곡 ─────────────────────────────────────────────────
                float3 nc   = localPos * invDiag * 3.0;
                float  warp = noise3(nc + float3( rawTime*0.7,  rawTime*0.5, -rawTime*0.4))
                            + noise3(nc + float3(-rawTime*0.4,  rawTime*0.6,  rawTime*0.3)) * 0.5;
                warp = (warp / 1.5) - 0.5;

                // ── 가변 줄무늬 굵기 ───────────────────────────────────────────
                float thicknessWarp = noise3(nc * 0.6 + float3(rawTime*0.2, -rawTime*0.15, rawTime*0.1));
                thicknessWarp = (thicknessWarp * 2.0 - 1.0) * _ThicknessMod;

                float distWarped  = distNorm + warp * _WarpAmt;
                float stripeCoord = (distWarped + thicknessWarp * 0.5 / max(_Scale, 0.001)) * _Scale;

                // ── 기본 줄무늬 ────────────────────────────────────────────────
                float phase      = (stripeCoord - offset) * (2.0 * UNITY_PI);
                float brightness = pow(sin(phase) * 0.5 + 0.5, _BrightBias);

                // ── 두 번째 레이어 (깊이감): offset2로 연속성 보장 ─────────────
                float phase2      = ((distWarped + thicknessWarp * 0.3) * _Scale * 0.65 - offset2) * (2.0 * UNITY_PI);
                float brightness2 = pow(sin(phase2) * 0.5 + 0.5, _BrightBias);
                float finalBright = lerp(brightness, brightness2, _LayerBlend);

                // ── 진행도에 따른 색상 보간 ────────────────────────────────────
                fixed4 baseColorFull = lerp(_ColorStart, _ColorEnd, min(_Progress, _MaxProgress));
                fixed3 baseColor     = baseColorFull.rgb;

                // ── 테두리 발광 ────────────────────────────────────────────────
                float edgeGlow = edgeProximity(localNorm01);

                // ── 최종 조합 ──────────────────────────────────────────────────
                fixed3 darkColor = baseColor * 0.35;
                fixed3 rgb = lerp(darkColor, baseColor, finalBright);
                rgb = saturate(rgb + edgeGlow * _EdgeBrightness * 0.25);

                // ── 호흡하는 알파 ──────────────────────────────────────────────
                float pulse = sin(rawTime * _PulseSpeed) * 0.5 + 0.5;
                float alpha = baseColorFull.a * (1.0 - _PulseAmt + _PulseAmt * pulse);

                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }

    FallBack Off
}
