// =============================================================================
// TEST 06: Per-Pass Tessellation
// 
// Tests: Tessellation only on specific passes via pass-level tags (NOT SubShader).
// Expected: Only Pass 1 ("Tessellated") gets tessellation injected.
//           Pass 2 ("NoTess") should NOT get tessellation.
//           Generated base passes SHOULD get tessellation (feature is enabled).
// =============================================================================
Shader "Tests/06_PerPassTessellation"
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
            Name "Tessellated"
            Tags
            {
                "LightMode" = "UniversalForward"
                "ShaderGen" = "True"
                "Tessellation" = "On"
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
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Interpolators
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
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
            Name "NoTess"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex V2
            #pragma fragment F2

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attr2 { float4 positionOS : POSITION; };
            struct Interp2 { float4 positionCS : SV_POSITION; };

            Interp2 V2(Attr2 input)
            {
                Interp2 o = (Interp2)0;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 F2(Interp2 input) : SV_Target { return half4(0,0,1,1); }
            ENDHLSL
        }

        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
