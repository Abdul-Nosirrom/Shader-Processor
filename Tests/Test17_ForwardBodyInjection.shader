// =============================================================================
// TEST 17: Forward Body Injection
//
// Tests: "InjectForwardBody" = "On" tag processor.
// Expected:
//   - Forward vertex body (minus boilerplate) injected into generated vertices
//   - Forward fragment body injected with return statements swapped
//   - Struct names rewritten in both vertex and fragment
//   - fragmentOutput pragmas map variable names for Meta/DepthNormals
//   - UVs, normals, tangents all transferred via vertex injection
//   - No manual hooks needed - tag handles everything
// =============================================================================
Shader "Tests/17_ForwardBodyInjection"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        _BumpMap("Normal Map", 2D) = "bump" {}
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        _EmissionColor("Emission", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "InjectForwardBody" = "On"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _Cutoff;
            float4 _EmissionColor;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);
        SAMPLER(sampler_BumpMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Interpolators
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : NORMAL;
            float3 tangentWS : TEXCOORD1;
            float3 bitangentWS : TEXCOORD2;
            float2 uv : TEXCOORD0;
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
                "Tessellation" = "On"
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #pragma fragmentOutput:albedo albedo
            #pragma fragmentOutput:normal computedNormal
            #pragma fragmentOutput:emission emission

            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
                output.tangentWS = tangentWS;
                output.bitangentWS = cross(output.normalWS, tangentWS) * input.tangentOS.w;
                
                // Offset by normals from base texture
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            
                input.positionOS.xyz += input.normalOS * SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, output.uv, 0).r * 2.5f;
            
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Interpolators input) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv));
                half3 emission = _EmissionColor.rgb * albedo.rgb;

                clip(albedo.a - _Cutoff);

                half3 computedNormal = normalize(
                    normalTS.x * input.tangentWS +
                    normalTS.y * input.bitangentWS +
                    normalTS.z * input.normalWS
                );

                half NdotL = saturate(dot(computedNormal, half3(0, 1, 0)));
                half4 color = half4(albedo.rgb * NdotL + emission, albedo.a);

                return color;
            }
            ENDHLSL
        }

        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
