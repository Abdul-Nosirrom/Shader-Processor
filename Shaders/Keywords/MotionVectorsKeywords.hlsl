#pragma once

// =============================================================================
// MOTION_VECTORS_KEYWORDS.HLSL
// Keywords for the MotionVectors pass.
// Use: #include_with_pragmas "Library/Keywords/MotionVectorsKeywords.hlsl"
// =============================================================================

#pragma target 4.5

// -----------------------------------------------------------------------------
// Precomputed Velocity
// -----------------------------------------------------------------------------
// For Alembic/blend shape animations that provide velocity data
#pragma shader_feature_local_vertex _ADD_PRECOMPUTED_VELOCITY

// -----------------------------------------------------------------------------
// LOD Crossfade
// -----------------------------------------------------------------------------
#pragma multi_compile _ LOD_FADE_CROSSFADE

// -----------------------------------------------------------------------------
// GPU Instancing
// -----------------------------------------------------------------------------
#include_with_pragmas "../Keywords/DOTSInstancing.hlsl"

// -----------------------------------------------------------------------------
// Foveated Rendering (VR)
// -----------------------------------------------------------------------------
// Uncomment if targeting VR
// #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
