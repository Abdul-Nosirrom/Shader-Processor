#pragma once

// =============================================================================
// LIGHTING_KEYWORDS.HLSL
// All keywords related to realtime lighting, shadows, and light features.
// Use: #include_with_pragmas "Library/Keywords/LightingKeywords.hlsl"
// =============================================================================

// -----------------------------------------------------------------------------
// Main Light Shadows
// -----------------------------------------------------------------------------
// Controls shadow map cascades and screen-space shadows for directional light
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

// -----------------------------------------------------------------------------
// Additional Lights
// -----------------------------------------------------------------------------
// VERTEX: Per-vertex lighting (mobile/low-end)
// ADDITIONAL_LIGHTS: Per-pixel lighting (default)
#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

// Shadows from point/spot lights
#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS

// -----------------------------------------------------------------------------
// Shadow Quality
// -----------------------------------------------------------------------------
// Soft shadow filtering quality levels
#pragma multi_compile _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

// -----------------------------------------------------------------------------
// Forward+ (Clustered Lighting)
// -----------------------------------------------------------------------------
// Enables clustered light loop for many lights
#pragma multi_compile _ _CLUSTER_LIGHT_LOOP

// -----------------------------------------------------------------------------
// Light Features
// -----------------------------------------------------------------------------
// Cookie textures on lights
#pragma multi_compile _ _LIGHT_COOKIES

// Per-light layer masking
#pragma multi_compile _ _LIGHT_LAYERS

// -----------------------------------------------------------------------------
// Screen-Space Effects
// -----------------------------------------------------------------------------
// Screen-space ambient occlusion
// #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

// Screen-space global illumination (if available)
// #pragma multi_compile_fragment _ _SCREEN_SPACE_IRRADIANCE

// -----------------------------------------------------------------------------
// Reflection Probes
// -----------------------------------------------------------------------------
// Blend between overlapping reflection probes
#pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING

// Box projection for indoor reflections
#pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

// Probe atlas optimization (Unity 6+)
// #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
