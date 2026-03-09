// =============================================================================
// TEST 16: Pass-Only Tag Mode
//
// Tests: "Tessellation" = "Pass" on a specific pass.
// Expected:
//   - The "Aura" pass gets tessellation injected (ModifyPass runs)
//   - The "Forward" pass does NOT get tessellation (different pass, pass-level tag)
//   - Generated passes (ShadowCaster, DepthOnly, etc.) do NOT get tessellation
//     ("Pass" mode means only the declaring pass, not generated ones)
//   - Tessellation properties/CBUFFER still injected (material data is shared)
// =============================================================================
Shader "Tests/16_PassOnlyTagMode"
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

        Pass
        {
            Name "Aura"
            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
                "Tessellation" = "Pass"
            }

            HLSLPROGRAM
            #pragma vertex AuraVert
            #pragma fragment AuraFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            struct AuraAttr
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct AuraInterp
            {
                float4 positionCS : SV_POSITION;
            };

            AuraInterp AuraVert(AuraAttr input)
            {
                AuraInterp output = (AuraInterp)0;
                input.positionOS.xyz += input.normalOS * 0.1 * sin(_Time.y);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 AuraFrag(AuraInterp input) : SV_Target
            {
                return half4(_BaseColor.rgb, 0.3);
            }
            ENDHLSL
        }

        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
