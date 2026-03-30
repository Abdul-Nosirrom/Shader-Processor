// =============================================================================
// TEST 14: Hooks Defined in HLSLINCLUDE
// 
// Tests: Hook pragmas in the pass, but hook function BODIES in HLSLINCLUDE.
// This is a valid authoring pattern - shared code in HLSLINCLUDE, pass just
// declares which hooks to use.
//
// Expected: 
//   - vertexDisplacement function found in HLSLINCLUDE, extracted, rewritten
//     per generated pass (ShadowCasterAttributes, DepthOnlyAttributes, etc.)
//   - alphaClip function found in HLSLINCLUDE, same treatment
//   - interpolatorTransfer stays in pass (control case - should still work)
//   - Generated passes have all three hooks with correct struct names
//   - Forward pass compiles with original struct names from HLSLINCLUDE
// =============================================================================
Shader "Tests/14_HooksInHLSLINCLUDE"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        _HeightScale("Height Scale", Float) = 0.1
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
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
            float _HeightScale;
            float _Cutoff;
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
            float3 positionWS : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // =====================================================================
        // Hook function bodies in HLSLINCLUDE - shared across all passes
        // =====================================================================

        void HeightDisplace(inout Attributes input)
        {
            input.positionOS.y += _HeightScale * sin(input.uv.x * 6.28);
        }

        void AlphaClipFunction(Interpolators input)
        {
            half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
            clip(col.a - _Cutoff);
        }

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

            // Pragmas in pass, function bodies in HLSLINCLUDE above
            #pragma vertexDisplacement HeightDisplace
            #pragma alphaClip AlphaClipFunction

            // This hook has BOTH pragma and body in the pass (control case)
            #pragma interpolatorTransfer TransferWorldPos

            void TransferWorldPos(Attributes input, inout Interpolators output)
            {
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
            }

            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                HeightDisplace(input);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                TransferWorldPos(input, output);

                return output;
            }

            half4 Frag(Interpolators input) : SV_Target
            {
                AlphaClipFunction(input);
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
            }
            ENDHLSL
        }

        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
