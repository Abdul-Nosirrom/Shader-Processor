#pragma once

// =============================================================================
// DEPTH_NORMALS_KEYWORDS.HLSL
// Keywords for the DepthNormals pass.
// Use: #include_with_pragmas "Packages/com.abdulal.shaderprocessor/Shaders/Keywords/DepthNormalsKeywords.hlsl"
// The ShaderGen DepthNormals template includes this, so generated passes get it already.
// =============================================================================

#pragma target 4.5

// -----------------------------------------------------------------------------
// Normal Encoding
// -----------------------------------------------------------------------------
// Octahedral normal encoding for deferred renderer's G-buffer
// More precise normal storage in 2 channels
// #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

// -----------------------------------------------------------------------------
// Rendering Layers Output
// -----------------------------------------------------------------------------
// Writes mesh rendering layer to separate render target. Off because the generated
// DepthNormals pass only has one SV_Target, so these variants would write nothing.
// Turning it on means adding the second output to the DepthNormals template.
// #pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS
// #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

// -----------------------------------------------------------------------------
// LOD Crossfade
// -----------------------------------------------------------------------------
#pragma multi_compile _ LOD_FADE_CROSSFADE

// -----------------------------------------------------------------------------
// GPU Instancing
// -----------------------------------------------------------------------------
#include_with_pragmas "../Keywords/DOTSInstancing.hlsl"
