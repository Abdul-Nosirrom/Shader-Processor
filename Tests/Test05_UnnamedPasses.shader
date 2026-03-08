// =============================================================================
// TEST 05: Unnamed Passes
// 
// Tests: Passes without Name declarations. Tessellation at SubShader level.
// Expected: Both unnamed passes should be detected.
//           Tessellation should inject into both (SubShader scope).
//           IndexOf-based content matching should handle unnamed passes.
// =============================================================================
Shader "Tests/05_UnnamedPasses"
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
            "Tessellation" = "On"
        }

        Pass
        {
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
                float2 uv : TEXCOORD0;
            };

            struct Interpolators
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Interpolators input) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex V2
            #pragma fragment F2

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A2 { float4 positionOS : POSITION; };
            struct I2 { float4 positionCS : SV_POSITION; };

            I2 V2(A2 input)
            {
                I2 o = (I2)0;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 F2(I2 input) : SV_Target { return half4(1,0,0,1); }
            ENDHLSL
        }

        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
