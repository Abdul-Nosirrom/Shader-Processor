#pragma once

// =============================================================================
// DEPTH_ONLY_KEYWORDS.HLSL
// Keywords for the DepthOnly pass.
// Use: #include_with_pragmas "Library/Keywords/DepthOnlyKeywords.hlsl"
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
