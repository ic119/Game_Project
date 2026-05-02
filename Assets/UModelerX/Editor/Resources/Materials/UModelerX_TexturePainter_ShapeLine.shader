Shader "Hidden/UModelerX_TexturePainter_ShapeLine"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        LOD 100

        Pass
        {
            Cull Off
            ZTest Always
            Blend One OneMinusSrcAlpha
            ColorMask RGBA

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            float2 _LineStart;    // in UV space [0..1]
            float2 _LineEnd;      // in UV space [0..1]
            float _StrokeWidth;   // in pixel space
            float4 _StrokeColor;  // premultiplied alpha
            int _StrokeStyle;     // 0=Solid, 1=Dashed, 2=Dotted
            float _DashLen;       // in pixel space
            float _GapLen;        // in pixel space
            float2 _TexSize;      // (texWidth, texHeight)
            int _LineCap;         // 0=Round, 1=SquareExtend, 2=SquareFlush

            float periodicSignedOffset(float value, float pattern, float phase)
            {
                if (pattern <= 0.0)
                {
                    return 0.0;
                }

                float shifted = value - phase;
                return shifted - floor(shifted / pattern + 0.5) * pattern;
            }

            float intervalMask(float value, float startValue, float endValue, float feather)
            {
                float centerValue = (startValue + endValue) * 0.5;
                float halfExtent = max(endValue - startValue, 0.0) * 0.5;
                float edgeDistance = abs(value - centerValue) - halfExtent;
                return saturate(-edgeDistance / max(feather, 0.001) + 0.5);
            }

            float resolveOpenDashGap(float totalLength, float dashLen, float preferredGap)
            {
                if (totalLength <= dashLen || dashLen <= 0.0)
                {
                    return 0.0;
                }

                float preferredSpacing = max(dashLen + preferredGap, dashLen);
                float segmentCount = max(round((totalLength - dashLen) / preferredSpacing) + 1.0, 2.0);
                float actualSpacing = (totalLength - dashLen) / max(segmentCount - 1.0, 1.0);
                return max(actualSpacing - dashLen, 0.0);
            }

            float resolveOpenDotGap(float totalLength, float dotDiameter, float preferredGap)
            {
                if (totalLength <= 0.0 || dotDiameter <= 0.0)
                {
                    return max(preferredGap, 0.0);
                }

                float preferredSpacing = max(dotDiameter + preferredGap, dotDiameter);
                float dotCount = max(round(totalLength / preferredSpacing) + 1.0, 2.0);
                float actualSpacing = totalLength / max(dotCount - 1.0, 1.0);
                return max(actualSpacing - dotDiameter, 0.0);
            }

            float dashedMaskOpen(float arcLen, float totalLen, float dashLen, float gapLen, float capExtension, float feather)
            {
                float pattern = dashLen + gapLen;
                if (pattern <= 0.0 || dashLen <= 0.0)
                {
                    return 1.0;
                }

                float centerOffset = periodicSignedOffset(arcLen, pattern, dashLen * 0.5);
                float edgeDistance = abs(centerOffset) - dashLen * 0.5;
                float repeatedMask = saturate(-edgeDistance / max(feather, 0.001) + 0.5);
                float startMask = intervalMask(arcLen, -capExtension, dashLen, feather);
                float endMask = intervalMask(arcLen, totalLen - dashLen, totalLen + capExtension, feather);
                return max(repeatedMask, max(startMask, endMask));
            }

            float dotMaskFromCoordinates(float arcLen, float signedDistance, float dotDiameter, float gapLen, float feather, float phase)
            {
                float pattern = dotDiameter + gapLen;
                if (pattern <= 0.0 || dotDiameter <= 0.0)
                {
                    return 1.0;
                }

                float centerOffset = periodicSignedOffset(arcLen, pattern, phase);
                float radius = dotDiameter * 0.5;
                float dotDistance = length(float2(centerOffset, signedDistance));
                return saturate((radius - dotDistance) / max(feather, 0.001) + 0.5);
            }

            float dottedMaskOpen(float arcLen, float totalLen, float signedDistance, float dotDiameter, float gapLen, float feather)
            {
                float repeatedMask = dotMaskFromCoordinates(arcLen, signedDistance, dotDiameter, gapLen, feather, 0.0);
                float radius = dotDiameter * 0.5;
                float endDistance = length(float2(arcLen - totalLen, signedDistance));
                float endMask = saturate((radius - endDistance) / max(feather, 0.001) + 0.5);
                return max(repeatedMask, endMask);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 pixelPos = i.uv * _TexSize;

                float2 a = _LineStart * _TexSize;
                float2 b = _LineEnd * _TexSize;

                float2 ba = b - a;
                float segLenSq = dot(ba, ba);
                float segLen = sqrt(segLenSq);
                float2 pa = pixelPos - a;

                // 선분 위 투영 비율 t (클램프 전)
                float tRaw = dot(pa, ba) / max(segLenSq, 0.001);

                float fw = 1.0;
                float halfStroke = _StrokeWidth * 0.5;
                float2 dir = ba / max(segLen, 0.001);
                float2 perp = float2(-dir.y, dir.x);
                float along = dot(pa, dir);
                float signedAcross = dot(pa, perp);
                float across = abs(signedAcross);

                float dist;
                if (_LineCap == 0)
                {
                    // Round: 끝점에서 둥근 SDF (기존 sdSegment)
                    float tClamped = saturate(tRaw);
                    dist = length(pa - ba * tClamped);
                }
                else
                {
                    // Square caps: 회전된 박스 SDF로 정확한 직교 단면 계산
                    // SquareExtend(1): 양 끝에 halfStroke만큼 연장
                    // SquareFlush(2): 연장 없이 끝점에서 정확히 끝남
                    float extension = (_LineCap == 1) ? halfStroke : 0.0;
                    float halfLen = segLen * 0.5 + extension;

                    // 선분 중점 기준 로컬 좌표
                    float2 localP = float2(along - segLen * 0.5, across);
                    float2 d = abs(localP) - float2(halfLen, halfStroke);
                    dist = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
                }

                float strokeMask;
                if (_LineCap == 0)
                {
                    // Round: dist는 선분까지 거리 (≥0), halfStroke 이내이면 그림
                    strokeMask = saturate((halfStroke - dist) / fw + 0.5);
                }
                else
                {
                    // Square: Box SDF에서 dist<0이 내부
                    strokeMask = saturate(-dist / fw + 0.5);
                }

                float capExtension = _LineCap == 2 ? 0.0 : halfStroke;
                float resolvedDashGap = resolveOpenDashGap(segLen, _DashLen, _GapLen);
                float resolvedDotGap = resolveOpenDotGap(segLen, _DashLen, _GapLen);

                if (_StrokeStyle == 1 && strokeMask > 0.0)
                {
                    float arcLen = along;
                    float dMask = dashedMaskOpen(arcLen, segLen, _DashLen, resolvedDashGap, capExtension, fw);
                    strokeMask *= dMask;
                }
                else if (_StrokeStyle == 2 && strokeMask > 0.0)
                {
                    float arcLen = along;
                    float dMask = dottedMaskOpen(arcLen, segLen, signedAcross, _DashLen, resolvedDotGap, fw);
                    strokeMask *= dMask;
                }

                float4 col = _StrokeColor * strokeMask;
                return col;
            }
            ENDCG
        }
    }
}
