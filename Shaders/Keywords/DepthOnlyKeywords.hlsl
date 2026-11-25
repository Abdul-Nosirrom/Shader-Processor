#pragma once

// =============================================================================
// DEPTH_ONLY_KEYWORDS.HLSL
// Keywords for the DepthOnly pass.
// Use: #include_with_pragmas "Packages/com.abdulal.shaderprocessor/Shaders/Keywords/DepthOnlyKeywords.hlsl"
// The ShaderGen DepthOnly template includes this, so generated passes get it already.
// =============================================================================

#pragma target 4.5

// -----------------------------------------------------------------------------
// LOD Crossfade
// -----------------------------------------------------------------------------
#pragma multi_compile _ LOD_FADE_CROSSFADE

// -----------------------------------------------------------------------------
// GPU Instancing
// -----------------------------------------------------------------------------
#include_with_pragmas "../Keywords/DOTSInstancing.hlsl"
