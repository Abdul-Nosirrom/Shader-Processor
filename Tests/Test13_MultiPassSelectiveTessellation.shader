// =============================================================================
// TEST 13: Multiple Authored Passes with Selective Tessellation
// 
// Tests: 3 authored passes, tessellation enabled on 2 of them via pass-level tags.
// Expected: Pass 1 (Forward) and Pass 2 (CustomLit) get tessellation injected.
//           Pass 3 (Unlit) does NOT get tessellation.
//           All generated base passes inherit tessellation from SubShader scope
//           (since at least one pass enables it, properties/CBUFFER are injected).
// =============================================================================
Shader "Tests/13_MultiPassSelectiveTessellation"
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

        // =====================================================================
        // Pass 1: Forward — HAS tessellation
        // =====================================================================
        Pass
        {
            Name "Forward"
            Tags
            {
                "LightMode" = "UniversalForward"
                "ShaderGen" = "True"
                "Tessellation" = "On"
            }

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

        // =====================================================================
        // Pass 2: CustomLit — HAS tessellation
        // =====================================================================
        Pass
        {
            Name "CustomLit"
            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
                "Tessellation" = "On"
            }

            HLSLPROGRAM
            #pragma vertex Vert2
            #pragma fragment Frag2
            #pragma multi_compile_instancing

            Interpolators Vert2(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag2(Interpolators input) : SV_Target
            {
                return half4(input.normalWS * 0.5 + 0.5, 1);
            }
            ENDHLSL
        }

        // =====================================================================
        // Pass 3: Debug — NO tessellation
        // =====================================================================
        Pass
        {
            Name "Debug"
            Tags
            {
                "LightMode" = "Always"
            }

            HLSLPROGRAM
            #pragma vertex Vert3
            #pragma fragment Frag3

            Interpolators Vert3(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag3(Interpolators input) : SV_Target
            {
                return half4(1, 0, 0, 1); // Solid red debug
            }
            ENDHLSL
        }

        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
