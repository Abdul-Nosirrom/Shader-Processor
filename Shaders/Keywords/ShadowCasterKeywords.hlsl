#pragma once

// =============================================================================
// SHADOW_CASTER_KEYWORDS.HLSL
// Keywords for the ShadowCaster pass.
// Use: #include_with_pragmas "Library/Keywords/ShadowCasterKeywords.hlsl"
// =============================================================================

#pragma target 4.5

// -----------------------------------------------------------------------------
// Shadow Type
// -----------------------------------------------------------------------------
// Distinguishes directional (orthographic) vs point/spot (perspective) shadows
// Affects shadow bias calculation
#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

// -----------------------------------------------------------------------------
// LOD Crossfade
// -----------------------------------------------------------------------------
#pragma multi_compile _ LOD_FADE_CROSSFADE

// -----------------------------------------------------------------------------
// GPU Instancing
// -----------------------------------------------------------------------------
#include_with_pragmas "../Keywords/DOTSInstancing.hlsl"
