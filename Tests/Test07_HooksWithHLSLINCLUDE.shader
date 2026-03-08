// =============================================================================
// TEST 07: Hooks with HLSLINCLUDE Structs
// 
// Tests: All three hooks declared in pass, but structs/CBUFFER in HLSLINCLUDE.
// Expected: Hook functions should propagate to generated passes.
//           Generated passes should use struct names from HLSLINCLUDE.
//           CBUFFER/textures not duplicated in generated passes.
// =============================================================================
Shader "Tests/07_HooksWithHLSLINCLUDE"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        _WaveStrength("Wave Strength", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _WaveStrength;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Interpolators
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : NORMAL;
            float2 uv : TEXCOORD0;
            float4 vertexColor : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags
            {
                "LightMode" = "UniversalForward"
                "ShaderGen" = "True"
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #pragma vertexDisplacement WaveDisplace
            #pragma interpolatorTransfer TransferColor
            #pragma alphaClip ClipIt

            void WaveDisplace(inout Attributes attr)
            {
                float wave = sin(_Time.y + attr.uv.x * 6.28);
                attr.positionOS.xyz += attr.normalOS * wave * _WaveStrength;
            }

            void TransferColor(Attributes input, inout Interpolators output)
            {
                output.vertexColor = input.color;
            }

            void ClipIt(Interpolators input)
            {
                float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                clip(alpha - _Cutoff);
            }

            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                WaveDisplace(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                TransferColor(input, output);
                return output;
            }

            half4 Frag(Interpolators input) : SV_Target
            {
                ClipIt(input);
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor * input.vertexColor;
            }
            ENDHLSL
        }

        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
