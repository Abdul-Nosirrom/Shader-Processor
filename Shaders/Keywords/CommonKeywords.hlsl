#pragma once

// =============================================================================
// COMMON_KEYWORDS.HLSL
// Forward pass keywords: target, LOD, fog, debug.
// Use: #include_with_pragmas "Packages/com.abdulal.shaderprocessor/Shaders/Keywords/CommonKeywords.hlsl"
// =============================================================================

// Forward pass only. ShaderGen's generated passes define SHADERGEN_GENERATED_PASS
// and declare their own keyword set, see ForwardKeywords.hlsl.
#ifndef SHADERGEN_GENERATED_PASS

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

#endif // SHADERGEN_GENERATED_PASS
