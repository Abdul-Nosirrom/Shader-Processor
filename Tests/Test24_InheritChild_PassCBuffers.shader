// =============================================================================
// TEST 24: Inheritance Child for Pass-Local CBUFFERs
//
// Adds one scalar and one 2D texture to Test24_InheritParent_PassCBuffers.
// Their declarations must reach every UnityPerMaterial CBUFFER in the parent.
// =============================================================================
Shader "Tests/24_InheritChild_PassCBuffers"
{
    Properties
    {
        _Test24Scale("Scale", Float) = 1
        _Test24Tex("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "Inherit" = "Tests/24_InheritParent_PassCBuffers"
        }
    }
}
