Shader "Custom/PortalCylinderGlow"
{
    Properties
    {
        [Header(Color and Gradient)]
        [HDR] _BottomColor ("Bottom Bright Color", Color) = (0.55, 0.90, 1.0, 1.0)
        [HDR] _TopColor ("Top Color", Color) = (0.12, 0.50, 1.0, 0.0)
        _GradientPower ("Fade Curve Exponent", Range(0.2, 5.0)) = 1.8
        _BottomOffset ("Bottom Height Offset", Range(-1.0, 1.0)) = 0.0
        
        [Header(Transparency and Glow)]
        _BaseAlpha ("Overall Alpha Multiplier", Range(0.0, 2.0)) = 0.9
        _EmissionGain ("Emission Multiplier", Range(0.5, 5.0)) = 2.2
        _RimPower ("Rim / Edge Glow Power", Range(0.5, 8.0)) = 2.5
        _RimIntensity ("Rim Glow Intensity", Range(0.0, 3.0)) = 1.4
        
        [Header(Energy Flow and Noise)]
        _FlowSpeed ("Vertical Flow Speed", Float) = 0.8
        _WaveFrequency ("Wave Frequency", Float) = 2.0
        _WaveStrength ("Wave Ripple Strength", Range(0.0, 1.0)) = 0.25
        
        [Header(Rendering Options)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 0 // Double-sided (Off)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5 // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1 // One (Additive)
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline" 
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PortalCylinderForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float2 uv           : TEXCOORD2;
                float  heightNorm   : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BottomColor;
                float4 _TopColor;
                float  _GradientPower;
                float  _BottomOffset;
                float  _BaseAlpha;
                float  _EmissionGain;
                float  _RimPower;
                float  _RimIntensity;
                float  _FlowSpeed;
                float  _WaveFrequency;
                float  _WaveStrength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // Height normalized from object-space Y (-1.0 to 1.0 for standard cylinder)
                float h = saturate((input.positionOS.y + 1.0 + _BottomOffset) * 0.5);
                output.heightNorm = h;

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normalize(normInputs.normalWS);
                output.uv = input.uv;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Invert height factor so bottom = 1.0 (bright) and top = 0.0 (fade out)
                float heightFactor = 1.0 - input.heightNorm;
                float fade = pow(saturate(heightFactor), _GradientPower);

                // Vertical moving energy wave
                float time = _Time.y * _FlowSpeed;
                float wave = sin((input.heightNorm * _WaveFrequency - time) * 6.2831853) * 0.5 + 0.5;
                float energyFlow = 1.0 + (wave - 0.5) * _WaveStrength;

                // Color interpolation from bottom to top
                float3 baseColor = lerp(_TopColor.rgb, _BottomColor.rgb, fade) * energyFlow;

                // Fresnel / Rim glow on cylinder edges
                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);
                float NdotV = abs(dot(input.normalWS, viewDirWS));
                float rim = pow(1.0 - saturate(NdotV), _RimPower) * _RimIntensity;

                // Final alpha: bright at bottom, softly fading out to 0 at top
                float alpha = lerp(_TopColor.a, _BottomColor.a, fade) * _BaseAlpha;
                alpha = saturate(alpha * (1.0 + rim * 0.5));

                // Add rim contribution to color
                float3 finalColor = (baseColor + _BottomColor.rgb * rim) * _EmissionGain;

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
