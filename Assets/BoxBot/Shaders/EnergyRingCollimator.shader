Shader "BoxBot/VFX/EnergyRingCollimator"
{
    Properties
    {
        [HDR] _RingColor ("Ring Color", Color) = (0.0, 0.9, 1.0, 1.0)
        _Intensity ("Intensity Multiplier", Float) = 6.0
        _RingFrequency ("Concentric Ring Frequency", Float) = 28.0
        _ExpansionSpeed ("Expansion Speed", Float) = 4.0
        _RingThickness ("Ring Thickness Edge", Range(0.1, 0.9)) = 0.5
        _RotationSpeed ("Angular Spin Speed", Float) = 1.5
        _ArcCount ("Radial Arc Segmentation", Float) = 6.0
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
            Name "CollimatorRingPass"
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
                float4 _RingColor;
                float _Intensity;
                float _RingFrequency;
                float _ExpansionSpeed;
                float _RingThickness;
                float _RotationSpeed;
                float _ArcCount;
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
                // Center UV to [-1, 1]
                float2 centeredUV = (input.uv - 0.5) * 2.0;
                float radius = length(centeredUV);
                if (radius > 1.0 || radius < 0.05) discard;

                float angle = atan2(centeredUV.y, centeredUV.x);

                // 1. Concentric Radial Wave
                float wave = sin(radius * _RingFrequency - _Time.y * _ExpansionSpeed);
                float ringPattern = smoothstep(_RingThickness, 1.0, wave);

                // 2. Magnetic Segment Arc Modulation
                float arcPattern = 0.6 + 0.4 * cos(angle * _ArcCount + _Time.y * _RotationSpeed);

                // 3. Feathered boundary falloff (soft inner and outer ring edges)
                float boundaryFade = smoothstep(1.0, 0.7, radius) * smoothstep(0.05, 0.2, radius);

                half3 finalColor = _RingColor.rgb * input.color.rgb * (_Intensity * ringPattern * arcPattern * boundaryFade);
                return half4(finalColor, boundaryFade);
            }
            ENDHLSL
        }
    }
}
