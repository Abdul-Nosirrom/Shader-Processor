// =============================================================================
// TEST 24: Inheritance Parent with Pass-Local CBUFFERs
//
// Tests: where a child's new property declarations land (InjectPropertyDeclarations).
// Each pass declares its own CBUFFER_START(UnityPerMaterial), the way shaders
// without a shared HLSLINCLUDE CBUFFER do. Ahead of them sit two traps a plain
// IndexOf("CBUFFER_END") would hit first: the comment below and a CBUFFER that
// isn't UnityPerMaterial.
//   CBUFFER_END   <- trap: inside a comment
// Expected (with Test24_InheritChild_PassCBuffers):
//   - Both UnityPerMaterial blocks get the child's scalar and _ST entries
//   - Both passes get the child's texture and sampler after their CBUFFER
//   - The Test24Other CBUFFER gets nothing
// =============================================================================
Shader "Tests/24_InheritParent_PassCBuffers"
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Trap: not the material CBUFFER, must not receive the child's entries
        CBUFFER_START(Test24Other)
            float _Test24Other;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Interpolators
        {
            float4 positionCS : SV_POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

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

        Pass
        {
            Name "Second"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Interpolators input) : SV_Target
            {
                return _BaseColor * 0.5;
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
