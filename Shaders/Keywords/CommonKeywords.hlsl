#pragma once

// =============================================================================
// COMMON_KEYWORDS.HLSL
// Keywords needed by most/all passes: fog, LOD, debug.
// Use: #include_with_pragmas "Library/Keywords/CommonKeywords.hlsl"
// =============================================================================

// -----------------------------------------------------------------------------
// Shader Target
// -----------------------------------------------------------------------------
#pragma target 4.5

// -----------------------------------------------------------------------------
// LOD Crossfade
// -----------------------------------------------------------------------------
// Dithered LOD transitions
#pragma multi_compile _ LOD_FADE_CROSSFADE

// -----------------------------------------------------------------------------
// Fog
// -----------------------------------------------------------------------------
// Includes linear, exp, exp2 fog variants
#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

// -----------------------------------------------------------------------------
// Debug Display
// -----------------------------------------------------------------------------
// Rendering Debugger support (material views, overdraw, etc.)
#pragma multi_compile_fragment _ DEBUG_DISPLAY
