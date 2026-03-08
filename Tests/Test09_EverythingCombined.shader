// =============================================================================
// TEST 09: Everything Combined (Stress Test)
// 
// Tests: HLSLINCLUDE structs + custom names + all hooks + Tessellation +
//        Outlines + multiple authored passes with different structs.
// Expected: 
//   - Forward pass: HLSLINCLUDE structs (VIn/VOut), hooks, tessellation, outlines
//   - Debug pass: its own structs (DbgAttr/DbgInterp), tessellation (SubShader),
//                 NO hooks (hooks only from reference pass)
//   - Generated passes: use VIn/VOut from HLSLINCLUDE, hooks, tessellation, outlines
// =============================================================================
Shader "Tests/09_EverythingCombined"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        _HeightScale("Height Scale", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Tessellation" = "On"
            "Outlines" = "On"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _HeightScale;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        struct VIn
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            float4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct VOut
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : NORMAL;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD2;
            float4 vertexColor : COLOR;
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

            #pragma vertexDisplacement HeightDisplace
            #pragma interpolatorTransfer PassColor
            #pragma alphaClip DoClip

            void HeightDisplace(inout VIn attr)
            {
                attr.positionOS.y += sin(attr.uv.x * 10.0) * _HeightScale;
            }

            void PassColor(VIn input, inout VOut output)
            {
                output.vertexColor = input.color;
            }

            void DoClip(VOut input)
            {
                clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
            }

            VOut Vert(VIn input)
            {
                VOut output = (VOut)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                HeightDisplace(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                PassColor(input, output);
                return output;
            }

            half4 Frag(VOut input) : SV_Target
            {
                DoClip(input);
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
            }
            ENDHLSL
        }

        Pass // Debug pass with different structs - gets tessellation but not hooks
        {
            Name "Debug"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex DbgVert
            #pragma fragment DbgFrag

            struct DbgAttr
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct DbgInterp
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            DbgInterp DbgVert(DbgAttr input)
            {
                DbgInterp o = (DbgInterp)0;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 DbgFrag(DbgInterp input) : SV_Target
            {
                return half4(input.normalWS * 0.5 + 0.5, 1);
            }
            ENDHLSL
        }

        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
