// =============================================================================
// TEST 19: Inheritance Child (Combined)
// 
// Tests all inheritance features against Test19_InheritParent:
//   - Property override (_AlphaCutoff default/range changed)
//   - Property addition (_RimPower, auto-generates CBUFFER entry)
//   - Pass exclusion (ExtraPass removed)
//   - Pass override (Forward gets LightMode changed, Cull changed)
//   - SubShader tag override (Tessellation added)
//   - HLSLINCLUDE append with hook function
//   - InheritHook binding (ModifyColor -> ApplyRim)
// =============================================================================
Shader "Tests/19_InheritChild"
{
    Properties
    {
        _AlphaCutoff("Alpha Cutoff", Range(0.1, 0.9)) = 0.3
        _RimPower("Rim Power", Range(0, 10)) = 3.0
    }

    SubShader
    {
        Tags
        {
            "Inherit" = "Tests/19_InheritParent"
            "Tessellation" = "On"
            "ExcludePasses" = "ExtraPass"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "VFXForward" }
            Cull Off
        }

        HLSLINCLUDE
        #pragma InheritHook ModifyColor ApplyRim

        // Intellisense stub (stripped during inheritance, auto-generated in CBUFFER)
        float _RimPower;

        void ApplyRim(inout half4 col, float3 normalWS)
        {
            float rim = 1.0 - saturate(dot(normalWS, float3(0, 0, 1)));
            col.rgb += pow(rim, _RimPower) * 0.5;
        }
        ENDHLSL
    }
}
