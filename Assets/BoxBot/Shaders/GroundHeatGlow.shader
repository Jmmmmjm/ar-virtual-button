Shader "BoxBot/VFX/GroundHeatGlow"
{
    Properties
    {
        [HDR] _GlowColor ("Glow Color", Color) = (0.0, 0.6, 1.0, 1.0)
        [HDR] _HotCenterColor ("Hot Center Core", Color) = (1.0, 0.9, 0.7, 1.0)
        _Intensity ("Intensity", Float) = 4.0
        _BeamWidth ("Beam Lateral Width", Range(0.01, 0.5)) = 0.08
        _NoiseTiling ("Caustic Noise Tiling", Float) = 16.0
        _NoiseSpeed ("Caustic Scroll Speed", Float) = 3.0
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

        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "GroundHeatGlowPass"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _GlowColor;
                float4 _HotCenterColor;
                float _Intensity;
                float _BeamWidth;
                float _NoiseTiling;
                float _NoiseSpeed;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // U is lateral (across beam), V is longitudinal (along beam)
                float lateralDist = abs(input.uv.x - 0.5) * 2.0; // 0 center, 1 edge
                float lateralFalloff = exp(-lateralDist * lateralDist / max(_BeamWidth, 0.01));

                // Longitudinal fade: soft start at muzzle, soft end at max reach
                float longitudinalFade = smoothstep(0.0, 0.1, input.uv.y) * smoothstep(1.0, 0.85, input.uv.y);

                // Scrolling caustic heat ripples
                float ripple = 0.8 + 0.2 * sin(input.uv.y * _NoiseTiling - _Time.y * _NoiseSpeed + lateralDist * 4.0);

                // Incandescent gradient: White-gold along centerline, mood color along perimeter
                half3 composite = lerp(_GlowColor.rgb, _HotCenterColor.rgb, pow(saturate(1.0 - lateralDist), 4.0));
                half3 finalColor = composite * input.color.rgb * (_Intensity * lateralFalloff * longitudinalFade * ripple);

                return half4(finalColor, lateralFalloff * longitudinalFade);
            }
            ENDHLSL
        }
    }
}
