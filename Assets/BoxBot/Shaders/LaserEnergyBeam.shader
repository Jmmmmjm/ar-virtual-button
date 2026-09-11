Shader "BoxBot/VFX/LaserEnergyBeam"
{
    Properties
    {
        [Header(Color and Intensity)]
        [HDR] _BaseColor ("Base Color", Color) = (0.0, 0.8, 1.0, 1.0)
        [HDR] _CoreColor ("Core Color (White-Hot)", Color) = (1.0, 1.0, 1.0, 1.0)
        _EmissionMult ("Emission Multiplier", Float) = 8.0
        
        [Header(Noise Turbulence)]
        _NoiseTex ("Noise Texture", 2D) = "white" {}
        _ScrollSpeedA ("Noise Scroll Speed A", Vector) = (0.2, 3.5, 0.0, 0.0)
        _ScrollSpeedB ("Noise Scroll Speed B", Vector) = (-0.15, -2.2, 0.0, 0.0)
        _NoiseDistortion ("Noise Distortion Strength", Range(0.0, 1.0)) = 0.35
        
        [Header(Fresnel Edge Glow)]
        _FresnelExp ("Fresnel Exponent", Range(0.5, 8.0)) = 2.5
        _FresnelPower ("Fresnel Glow Power", Range(0.0, 5.0)) = 2.0
        
        [Header(Beam Propagation)]
        _Progress ("Beam Progress (0 to 1)", Range(0.0, 1.0)) = 1.0
        _HeadFadeLength ("Head Fade Length", Range(0.01, 0.5)) = 0.1
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

        Blend One One             // Additive blending for searing energy
        ZWrite Off
        Cull Off                  // Double-sided for volumetric cylinder/ribbon

        Pass
        {
            Name "LaserEnergyBeamPass"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR; // Vertex Color: Supplies dynamic mood color from LineRenderer/Particles
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float2 uv           : TEXCOORD0;
                float4 color        : COLOR;
                float3 normalWS     : TEXCOORD1;
                float3 viewDirWS    : TEXCOORD2;
            };

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);

            // STRICT SRP BATCHER COMPLIANCE: Uniforms enclosed in UnityPerMaterial
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _CoreColor;
                float4 _NoiseTex_ST;
                float4 _ScrollSpeedA;
                float4 _ScrollSpeedB;
                float _EmissionMult;
                float _FresnelExp;
                float _FresnelPower;
                float _NoiseDistortion;
                float _Progress;
                float _HeadFadeLength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _NoiseTex);
                output.color = input.color;
                output.normalWS = normInputs.normalWS;
                output.viewDirWS = GetWorldSpaceViewDir(posInputs.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 1. Beam Progress Mask (Clipping / Head dissipation)
                float beamLength = input.uv.y;
                float progressMask = saturate((_Progress - beamLength) / max(_HeadFadeLength, 0.001));
                if (progressMask <= 0.001) discard;

                // 2. Dual Scrolling Noise Sampling for chaotic plasma turbulence
                float2 uvA = input.uv + _ScrollSpeedA.xy * _Time.y;
                float2 uvB = input.uv + _ScrollSpeedB.xy * _Time.y;
                half noiseA = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uvA).r;
                half noiseB = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uvB).r;
                half combinedNoise = lerp(noiseA, noiseB, 0.5);

                // 3. Fresnel / Cylindrical Edge Falloff
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                float NdotV = abs(dot(normalWS, viewDirWS));
                float fresnel = pow(saturate(1.0 - NdotV), _FresnelExp) * _FresnelPower;

                // 4. Lateral Center Core Mask (U coordinate: 0 at edge, 0.5 at center, 1 at edge)
                float coreDist = abs(input.uv.x - 0.5) * 2.0; // 0 at center, 1 at edge
                float coreMask = pow(saturate(1.0 - coreDist), 3.0);

                // 5. Color Composite: Vertex color overrides or modulates base color
                half4 moodTint = _BaseColor * input.color;
                half3 outerMantle = moodTint.rgb * (fresnel + combinedNoise * _NoiseDistortion);
                half3 innerCore = _CoreColor.rgb * coreMask;

                half3 finalRGB = (outerMantle + innerCore) * (_EmissionMult * progressMask);
                return half4(finalRGB, progressMask);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
