// =============================================================================
// TEST 04: Comments Around Pass Keyword
// 
// Tests: Various comment styles between Pass keyword and opening brace.
// Expected: All passes should be detected and processed correctly.
//           - Pass with // comment before brace
//           - Pass with /* */ comment before brace
//           - Pass with comment inside (after brace) - always worked
//           - Normal pass for reference
// =============================================================================
Shader "Tests/04_PassComments"
{
        
    // Adding some comment here
    /*
    * Another comment
    */

    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
    }
    // Adding some comment here
    /*
    * Another comment
    */

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass // Line comment after Pass keyword
        {
            Name "CommentedPass1"
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

        Pass /* Block comment after Pass keyword */
        {
            Name "CommentedPass2"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex Vert2
            #pragma fragment Frag2

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A2
            {
                float4 positionOS : POSITION;
            };
            struct I2
            {
                float4 positionCS : SV_POSITION;
            };

            I2 Vert2(A2 input)
            {
                I2 o = (I2)0;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 Frag2(I2 input) : SV_Target { return half4(0,1,0,1); }
            ENDHLSL
        }

        Pass
        // Comment on next line between Pass and brace
        {
            Name "CommentedPass3"
            Tags { "LightMode" = "Meta" }

            HLSLPROGRAM
            #pragma vertex Vert3
            #pragma fragment Frag3

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A3 { float4 positionOS : POSITION; };
            struct I3 { float4 positionCS : SV_POSITION; };

            I3 Vert3(A3 input)
            {
                I3 o = (I3)0;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }
            half4 Frag3(I3 input) : SV_Target { return 0; }
            ENDHLSL
        }

        [InjectPass:Outline]
        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
