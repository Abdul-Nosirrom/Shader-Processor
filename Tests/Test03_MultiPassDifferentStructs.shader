// =============================================================================
// TEST 03: Multi-Pass with Different Struct Names
// 
// Tests: Two authored passes with completely different struct names.
//        Tessellation at SubShader level (applies to both).
// Expected: Each pass's tessellation code uses ITS OWN struct names/fields.
//           Pass 1 uses Attributes/Interpolators (4 fields).
//           Pass 2 uses SimpleIn/SimpleOut (1 field: positionOS only).
//           TessControlPoint for Pass 2 should only have positionOS.
// =============================================================================
Shader "Tests/03_MultiPassDifferentStructs"
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
            "Tessellation" = "On"
        }

        Pass
        {
            Name "FullPass"
            Tags
            {
                "LightMode" = "UniversalForward"
                "ShaderGen" = "True"
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

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
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Interpolators
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
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

        Pass
        {
            Name "MinimalPass"
            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
            }

            HLSLPROGRAM
            #pragma vertex MinVert
            #pragma fragment MinFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct SimpleIn
            {
                float4 positionOS : POSITION;
            };

            struct SimpleOut
            {
                float4 positionCS : SV_POSITION;
            };

            SimpleOut MinVert(SimpleIn input)
            {
                SimpleOut output = (SimpleOut)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 MinFrag(SimpleOut input) : SV_Target
            {
                return half4(1, 0, 0, 1);
            }
            ENDHLSL
        }

        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
