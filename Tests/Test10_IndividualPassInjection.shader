// =============================================================================
// TEST 10: Individual Pass Injection Markers
// 
// Tests: Using [InjectPass:X] markers instead of [InjectBasePasses].
//        Only requesting ShadowCaster and DepthOnly (not all 5).
// Expected: Only ShadowCaster and DepthOnly passes generated.
//           No DepthNormals, MotionVectors, or Meta.
// =============================================================================
Shader "Tests/10_IndividualPassInjection"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Interpolators
            {
                float4 positionCS : SV_POSITION;
            };

            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Interpolators input) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }

        [InjectPass:ShadowCaster]
        [InjectPass:DepthOnly]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
