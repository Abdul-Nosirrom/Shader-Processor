// =============================================================================
// FreeSkies Example: Custom Naming
// 
// Tests the semantic-based field name resolution.
// Uses non-standard names for structs and fields:
// - VertexInput instead of Attributes
// - Varyings instead of Interpolators  
// - pos instead of positionOS
// - nrm instead of normalOS
// - etc.
// =============================================================================

Shader "FreeSkies/Examples/CustomNaming"
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
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        // Custom struct names
        struct VertexInput
        {
            float4 pos : POSITION;      // Non-standard name
            float3 nrm : NORMAL;        // Non-standard name
            float2 texcoord : TEXCOORD0; // Non-standard name
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 clipPos : SV_POSITION;  // Non-standard name
            float3 worldNormal : TEXCOORD1; // Non-standard name
            float2 texcoord : TEXCOORD0;
            float3 worldPos : TEXCOORD2;
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
            #pragma vertex ForwardVert
            #pragma fragment ForwardFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile_instancing

            Varyings ForwardVert(VertexInput input)
            {
                Varyings output = (Varyings)0;
                
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                output.clipPos = TransformObjectToHClip(input.pos.xyz);
                output.worldPos = TransformObjectToWorld(input.pos.xyz);
                output.worldNormal = TransformObjectToWorldNormal(input.nrm);
                output.texcoord = TRANSFORM_TEX(input.texcoord, _BaseMap);
                
                return output;
            }

            half4 ForwardFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.texcoord) * _BaseColor;
                
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.worldPos));
                half NdotL = saturate(dot(input.worldNormal, mainLight.direction));
                
                return half4(albedo.rgb * (mainLight.color * NdotL + 0.1), albedo.a);
            }
            ENDHLSL
        }

        [InjectBasePasses]
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
