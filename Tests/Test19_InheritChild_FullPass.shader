// =============================================================================
// TEST 19 (FullPass child): Child adds a complete Pass with an HLSLPROGRAM.
//
// Tests: Child Pass blocks containing HLSLPROGRAM are appended to the parent's
//        SubShader as full passes (not treated as metadata overrides).
// Expected: Merged output contains the parent's passes AND the ChildExtra pass.
// =============================================================================
Shader "Tests/19_InheritChild_FullPass"
{
    SubShader
    {
        Tags
        {
            "Inherit" = "Tests/19_InheritParent"
        }

        Pass
        {
            Name "ChildExtra"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex ChildVert
            #pragma fragment ChildFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct ChildAttr
            {
                float4 positionOS : POSITION;
            };

            struct ChildInterp
            {
                float4 positionCS : SV_POSITION;
            };

            ChildInterp ChildVert(ChildAttr input)
            {
                ChildInterp output = (ChildInterp)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 ChildFrag(ChildInterp input) : SV_Target
            {
                return half4(1, 0, 1, 1);
            }
            ENDHLSL
        }
    }
}
