// =============================================================================
// TEST 25: Child "../" Include Resolved From the Child's Folder
//
// This child sits one folder below Tests/ and includes "../Test25_ChildInclude.hlsl",
// which is Tests/Test25_ChildInclude.hlsl from here. The parent (Test19) lives in
// Tests/, where the same relative path would point outside the package.
// Expected: the merged source includes
//   "Packages/com.abdulal.shaderprocessor/Tests/Test25_ChildInclude.hlsl"
// and compiles.
// =============================================================================
Shader "Tests/25_InheritChild_RelInclude"
{
    SubShader
    {
        Tags
        {
            "Inherit" = "Tests/19_InheritParent"
        }

        HLSLINCLUDE
        #include "../Test25_ChildInclude.hlsl"
        ENDHLSL
    }
}
