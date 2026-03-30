#pragma once

// =============================================================================
// FORWARD_KEYWORDS.HLSL
// Complete keyword set for forward rendering passes.
// Use: #include_with_pragmas "Library/Keywords/ForwardKeywords.hlsl"
//
// This is a convenience file that includes all keywords typically needed
// for a fully-featured forward lit pass. For simpler shaders, you can
// include individual keyword files instead.
// Can choose what to include by setting up certain defines prior. e.e
// #define SUPPORT_BAKED_LIGHTING
// #define SUPPORT_DECALS
// #define SUPPORT_DYNAMIC_LIGHTING
// =============================================================================

// Common (target, LOD, fog, debug)
#include_with_pragmas "Library/Keywords/CommonKeywords.hlsl"

// Realtime lighting and shadows
#include_with_pragmas "Library/Keywords/LightingKeywords.hlsl"

// Baked lighting (lightmaps, probes, mixed lighting)
#include_with_pragmas "Library/Keywords/BakedGIKeywords.hlsl"

// GPU instancing and DOTS
#include_with_pragmas "Library/Keywords/DOTSInstancing.hlsl"

// Decal support
#include_with_pragmas "Library/Keywords/DecalKeywords.hlsl"

// -----------------------------------------------------------------------------
// Rendering Layers (for light layers with instancing)
// -----------------------------------------------------------------------------
#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

// -----------------------------------------------------------------------------
// Foveated Rendering (VR optimization)
// -----------------------------------------------------------------------------
// Uncomment if targeting VR
// #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
