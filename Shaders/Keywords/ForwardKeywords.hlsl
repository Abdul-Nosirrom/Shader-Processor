#pragma once

// =============================================================================
// FORWARD_KEYWORDS.HLSL
// Complete keyword set for forward rendering passes.
// Use: #include_with_pragmas "Packages/com.abdulal.shaderprocessor/Shaders/Keywords/ForwardKeywords.hlsl"
//
// This is a convenience file that includes all keywords typically needed
// for a fully-featured forward lit pass. For simpler shaders, you can
// include individual keyword files instead.
// Can choose what to include by setting up certain defines prior. e.e
// #define SUPPORT_BAKED_LIGHTING
// #define SUPPORT_DECALS
// #define SUPPORT_DYNAMIC_LIGHTING
//
// ShaderGen copies the forward pass's pragmas into its generated passes, which
// define SHADERGEN_GENERATED_PASS. Those passes declare their own keyword set
// in their template (ShadowCasterKeywords.hlsl etc.), so this whole file stays
// out of them. Without the gate every lighting, GI and decal variant would be
// compiled again for shadow and depth passes that never read them.
// =============================================================================

#ifndef SHADERGEN_GENERATED_PASS

// Common (target, LOD, fog, debug)
#include_with_pragmas "CommonKeywords.hlsl"

// Realtime lighting and shadows
#include_with_pragmas "LightingKeywords.hlsl"

// Baked lighting (lightmaps, probes, mixed lighting)
#include_with_pragmas "BakedGIKeywords.hlsl"

// GPU instancing and DOTS
#include_with_pragmas "DOTSInstancing.hlsl"

// Decal support
#include_with_pragmas "DecalKeywords.hlsl"

// -----------------------------------------------------------------------------
// Rendering Layers (for light layers with instancing)
// -----------------------------------------------------------------------------
#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

// -----------------------------------------------------------------------------
// Foveated Rendering (VR optimization)
// -----------------------------------------------------------------------------
// Uncomment if targeting VR
// #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"

#endif // SHADERGEN_GENERATED_PASS
