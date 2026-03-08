// =============================================================================
// TEST 08: Custom Naming with HLSLINCLUDE
// 
// Tests: Non-standard struct/field names defined in HLSLINCLUDE.
//        Vertex function in HLSLINCLUDE too.
// Expected: Generated passes should use custom field names (pos, nrm, tc)
//           in semantic replacements (e.g. ShadowCaster uses input.pos.xyz).
// =============================================================================
Shader "Tests/08_CustomNamingHLSLINCLUDE"
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

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
        CBUFFER_END

        struct VS_IN
        {
            float4 pos : POSITION;
            float3 nrm : NORMAL;
            float2 tc : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct PS_IN
        {
            float4 clipPos : SV_POSITION;
            float3 wNormal : NORMAL;
            float2 tc : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        PS_IN BaseVert(VS_IN v)
        {
            PS_IN o = (PS_IN)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);
            o.clipPos = TransformObjectToHClip(v.pos.xyz);
            o.wNormal = TransformObjectToWorldNormal(v.nrm);
            o.tc = v.tc;
            return o;
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
            #pragma vertex BaseVert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            half4 Frag(PS_IN input) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }

        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
