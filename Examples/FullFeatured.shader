// =============================================================================
// FreeSkies Example: Full Featured
// 
// Combines all features:
// - Outlines tag processor
// - All three hooks (vertexDisplacement, interpolatorTransfer, alphaClip)
// - Extra interpolators
// - Helper functions
// =============================================================================

Shader "FreeSkies/Examples/FullFeatured"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.5
        _Metallic("Metallic", Range(0, 1)) = 0
        
        [Header(Alpha)]
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Cutoff", Range(0, 1)) = 0.5
        
        [Header(Displacement)]
        _DisplacementMap("Displacement Map", 2D) = "gray" {}
        _DisplacementStrength("Strength", Range(0, 1)) = 0.1
        
        [Header(Rim Light)]
        _RimColor("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower("Rim Power", Range(1, 10)) = 3
        
        // Outline properties will be auto-injected
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "Outlines" = "On"
            "Tessellation" = "On"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _Smoothness;
            float _Metallic;
            float _Cutoff;
            float4 _DisplacementMap_ST;
            float _DisplacementStrength;
            float4 _RimColor;
            float _RimPower;
            // Outline CBUFFER entries auto-injected
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_DisplacementMap);
        SAMPLER(sampler_DisplacementMap);

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
            float4 tangentWS : TEXCOORD4;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD2;
            float3 viewDirWS : TEXCOORD3;
            float4 vertexColor : COLOR;
            float rimFactor : TEXCOORD5;
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
            
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_instancing

            // Hook declarations
            #pragma vertexDisplacement ApplyHeightmapDisplacement
            #pragma interpolatorTransfer TransferCustomData
            #pragma alphaClip PerformAlphaClip

            // =====================================================================
            // HELPER FUNCTIONS
            // =====================================================================
            
            float SampleHeight(float2 uv)
            {
                return SAMPLE_TEXTURE2D_LOD(_DisplacementMap, sampler_DisplacementMap, uv, 0).r;
            }
            
            float ComputeRimFactor(float3 normalWS, float3 viewDirWS)
            {
                float rim = 1.0 - saturate(dot(normalWS, viewDirWS));
                return pow(rim, _RimPower);
            }

            // =====================================================================
            // HOOK: Vertex Displacement
            // =====================================================================
            void ApplyHeightmapDisplacement(inout Attributes attr)
            {
                float height = SampleHeight(attr.uv);
                float displacement = (height - 0.5) * 2.0 * _DisplacementStrength;
                attr.positionOS.xyz += attr.normalOS * displacement;
            }

            // =====================================================================
            // HOOK: Transfer Custom Interpolators
            // =====================================================================
            void TransferCustomData(Attributes input, inout Interpolators output)
            {
                output.vertexColor = input.color;
                output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(output.positionWS);
                output.rimFactor = ComputeRimFactor(output.normalWS, output.viewDirWS);
            }

            // =====================================================================
            // HOOK: Alpha Clip
            // =====================================================================
            void PerformAlphaClip(Interpolators input)
            {
                #ifdef _ALPHATEST_ON
                float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
                clip(alpha - _Cutoff);
                #endif
            }

            // =====================================================================
            // VERTEX
            // =====================================================================
            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                // Apply displacement hook
                ApplyHeightmapDisplacement(input);
                
                // Standard transforms
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                
                // Custom data transfer
                TransferCustomData(input, output);
                
                return output;
            }

            // =====================================================================
            // FRAGMENT
            // =====================================================================
            half4 Frag(Interpolators input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                
                // Alpha clip
                PerformAlphaClip(input);
                
                // Sample textures
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                //albedo.rgb *= input.vertexColor.rgb;
                return albedo;
                // Setup lighting
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = normalize(input.viewDirWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo.rgb;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1;
                surfaceData.alpha = albedo.a;
                
                // PBR lighting
                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                
                // Add rim light
                color.rgb += _RimColor.rgb * input.rimFactor;
                
                return color;
            }
            ENDHLSL
        }

        [InjectBasePasses]
        
        // Outline pass auto-injected
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
