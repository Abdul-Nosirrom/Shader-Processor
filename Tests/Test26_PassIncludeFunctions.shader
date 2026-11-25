// =============================================================================
// TEST 26b: Pass-Level Include - Vertex, Fragment and Hook Functions
//
// Tests: The forward pass's vertex and fragment functions and its
//        vertexDisplacement hook function all live in Test26_PassFunctions.hlsl,
//        which the pass #includes. The hook pragma is in the pass itself.
// Expected: The hook is registered and called in generated passes, the forward
//           vertex and fragment bodies are injected (InjectForwardBody), and the
//           fragment's extra SV_IsFrontFace parameter reaches generated passes.
// =============================================================================
Shader "Tests/26_PassIncludeFunctions"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "ShaderGen" = "True"
            "InjectForwardBody" = "On"
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" "ShaderGen" = "True" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma vertexDisplacement Test26Displace

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.abdulal.shaderprocessor/Tests/Test21_SharedInclude.hlsl"
            #include "Packages/com.abdulal.shaderprocessor/Tests/Test26_PassFunctions.hlsl"
            ENDHLSL
        }

        [InjectBasePasses]
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
