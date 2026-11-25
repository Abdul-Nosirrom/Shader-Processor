// =============================================================================
// TEST 22: Conditional and Multi-Line Defines
//
// Tests: #define hoisting out of the forward pass into generated passes.
// Mirrors VFXMaster's _PARTICLE_SYSTEM macro family: the same macro defined in
// both branches of an #ifdef/#else, with the "on" branch spanning several lines.
// Expected:
//   - Hoisted defines keep their #ifdef/#else/#endif in the preprocessor section
//   - The multi-line define moves as one unit, no body lines left in the code
//   - FS_TEST_GUARD is defined above the block, so the "on" branch is the live
//     one in the default variant the compile test builds. If the guard were
//     dropped, the #else definition would win (last one wins) and the generated
//     passes would fail on its undefined symbol.
// =============================================================================
Shader "Tests/22_ConditionalDefines"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "InjectForwardBody" = "On"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
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

            #define FS_TEST_GUARD

#ifdef FS_TEST_GUARD
            float TestGuardHelper(float x) { return x * 0.5; }

            #define TEST_SCALE(x) ( \
                TestGuardHelper(x) * 2.0 \
                + 0.0)
#else
            #define TEST_SCALE(x) (x * test_else_branch_must_not_compile)
#endif

            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Interpolators input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;

                // Statement before the return so it survives return swapping and the
                // macro is expanded inside every generated pass
                clip(albedo.a - TEST_SCALE(_Cutoff));

                return albedo;
            }
            ENDHLSL
        }

        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
