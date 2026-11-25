// =============================================================================
// TEST 26a: Pass-Level Include - Structs and Material Data
//
// Tests: Structs, CBUFFER and textures come from a file the forward pass
//        #includes inside HLSLPROGRAM (Test21 does the same from HLSLINCLUDE).
// Expected: The pipeline reads the include and generates per-pass structs from
//           the real Attributes/Interpolators (not the fallback structs). It does
//           not emit its own CBUFFER or textures: generated passes get the
//           forward pass's #include lines, which bring them along.
// =============================================================================
Shader "Tests/26_PassIncludeStructs"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "ShaderGen" = "True"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" "ShaderGen" = "True" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.abdulal.shaderprocessor/Tests/Test21_SharedInclude.hlsl"

            Interpolators vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            half4 frag(Interpolators input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                return col * _BaseColor;
            }
            ENDHLSL
        }

        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
