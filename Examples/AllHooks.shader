// =============================================================================
// FreeSkies Example: All Hooks
// 
// Demonstrates all three hook pragmas:
// - #pragma vertexDisplacement
// - #pragma interpolatorTransfer  
// - #pragma alphaClip
// =============================================================================

Shader "FreeSkies/Examples/AllHooks"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        
        [Header(Alpha Clip)]
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        
        [Header(Displacement)]
        _DisplacementStrength("Displacement Strength", Range(0, 0.5)) = 0.1
        _DisplacementSpeed("Displacement Speed", Float) = 1.0
        
        [Header(Custom Data)]
        _CustomMultiplier("Custom Multiplier", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float _DisplacementStrength;
            float _DisplacementSpeed;
            float _CustomMultiplier;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            float4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Interpolators
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD2;
            float4 vertexColor : COLOR;
            float customData : TEXCOORD3;
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_instancing

            // =====================================================================
            // HOOK DECLARATIONS
            // =====================================================================
            #pragma vertexDisplacement ApplyDisplacement
            #pragma interpolatorTransfer TransferExtras
            #pragma alphaClip ClipAlpha

            // =====================================================================
            // HELPER FUNCTIONS (will be extracted for generated passes)
            // =====================================================================
            float ComputeCustomData(float2 uv, float time)
            {
                return sin(uv.x * 10.0 + time) * cos(uv.y * 10.0 + time) * _CustomMultiplier;
            }

            float3 ComputeDisplacement(float3 normal, float2 uv)
            {
                float wave = sin(_Time.y * _DisplacementSpeed + uv.x * 5.0);
                return normal * wave * _DisplacementStrength;
            }

            // =====================================================================
            // HOOK: Vertex Displacement
            // Signature: void FuncName(inout Attributes attr)
            // =====================================================================
            void ApplyDisplacement(inout Attributes attr)
            {
                float3 displacement = ComputeDisplacement(attr.normalOS, attr.uv);
                attr.positionOS.xyz += displacement;
            }

            // =====================================================================
            // HOOK: Interpolator Transfer
            // Signature: void FuncName(Attributes input, inout Interpolators output)
            // Only transfer EXTRA fields - NOT positionCS, normalWS, uv, positionWS
            // =====================================================================
            void TransferExtras(Attributes input, inout Interpolators output)
            {
                output.vertexColor = input.color;
                output.customData = ComputeCustomData(input.uv, _Time.y);
            }

            // =====================================================================
            // HOOK: Alpha Clip
            // Signature: void FuncName(Interpolators input)
            // =====================================================================
            void ClipAlpha(Interpolators input)
            {
                float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                clip(alpha - _Cutoff);
            }

            // =====================================================================
            // VERTEX
            // =====================================================================
            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                // Apply vertex displacement
                ApplyDisplacement(input);
                
                // Standard transforms
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                
                // Transfer extra interpolators
                TransferExtras(input, output);
                
                return output;
            }

            // =====================================================================
            // FRAGMENT
            // =====================================================================
            half4 Frag(Interpolators input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                // Alpha clip
                ClipAlpha(input);
                
                // Sample albedo
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                
                // Apply vertex color
                albedo.rgb *= input.vertexColor.rgb;
                
                // Add custom data visualization
                albedo.rgb += input.customData * 0.1;
                
                // Simple lighting
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half NdotL = saturate(dot(input.normalWS, mainLight.direction));
                half3 lighting = mainLight.color * NdotL * mainLight.shadowAttenuation;
                
                return half4(albedo.rgb * (lighting + 0.1), 1.0);
            }
            ENDHLSL
        }

        [InjectBasePasses]
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
