// =============================================================================
// TEST 23: Cull Copied From the Forward Pass
//
// Tests: generated passes take the reference pass's Cull line ({{CULL}} marker).
// Expected:
//   - ShadowCaster, DepthOnly, DepthNormals and MotionVectors all say
//     "Cull [_Cull]", the same as the forward pass, and none say "Cull Back"
//   - Meta keeps its own "Cull Off"
// =============================================================================
Shader "Tests/23_CullFromForward"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
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
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Interpolators
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : NORMAL;
            float2 uv : TEXCOORD0;
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

            // Cull Back (commented out, must not be picked up)
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Interpolators input) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
            }
            ENDHLSL
        }

        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
