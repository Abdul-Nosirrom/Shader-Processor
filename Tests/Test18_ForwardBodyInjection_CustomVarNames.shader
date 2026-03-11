// =============================================================================
// TEST 18: Forward Body Injection with Custom Variable Names
//
// Tests: "InjectForwardBody" = "On" with non-standard variable naming.
// The vertex function uses 'v' for input and 'o' for output instead of
// the conventional 'input'/'output'. The fragment uses 'i' for input.
// Expected:
//   - Variable names normalized to input/output before injection
//   - Generated passes compile with template-standard variable names
//   - All Test17 behaviors still hold (UV transfer, normal transfer, etc.)
// =============================================================================
Shader "Tests/18_ForwardBodyInjection_CustomVarNames"
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
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #pragma fragmentOutput:albedo albedo
            #pragma fragmentOutput:normal computedNormal
            #pragma fragmentOutput:emission emission

            // NOTE: Uses 'v' for input and 'o' for output (non-standard)
            Interpolators Vert(Attributes v)
            {
                Interpolators o = (Interpolators)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                float3 tangentWS = TransformObjectToWorldDir(v.tangentOS.xyz);
                o.tangentWS = tangentWS;
                o.bitangentWS = cross(o.normalWS, tangentWS) * v.tangentOS.w;
                
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
            
                v.positionOS.xyz += v.normalOS * SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, o.uv, 0).r * 2.5f;
            
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            // NOTE: Uses 'i' for input (non-standard)
            half4 Frag(Interpolators i) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv));
                half3 emission = _EmissionColor.rgb * albedo.rgb;

                clip(albedo.a - _Cutoff);

                half3 computedNormal = normalize(
                    normalTS.x * i.tangentWS +
                    normalTS.y * i.bitangentWS +
                    normalTS.z * i.normalWS
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
