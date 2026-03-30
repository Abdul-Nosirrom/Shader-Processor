#pragma once

// =============================================================================
// DEPTH_NORMALS_KEYWORDS.HLSL
// Keywords for the DepthNormals pass.
// Use: #include_with_pragmas "Library/Keywords/DepthNormalsKeywords.hlsl"
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
// Writes mesh rendering layer to separate render target
#pragma multi_compile_fragment _ _WRITE_RENDERING_LAYERS
#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

// -----------------------------------------------------------------------------
// LOD Crossfade
// -----------------------------------------------------------------------------
#pragma multi_compile _ LOD_FADE_CROSSFADE

// -----------------------------------------------------------------------------
// GPU Instancing
// -----------------------------------------------------------------------------
#include_with_pragmas "../Keywords/DOTSInstancing.hlsl"
