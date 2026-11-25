#pragma once

// =============================================================================
// DECAL_KEYWORDS.HLSL
// Keywords for DBuffer decal support.
// Use: #include_with_pragmas "Packages/com.abdulal.shaderprocessor/Shaders/Keywords/DecalKeywords.hlsl"
// =============================================================================

// Forward pass only. ShaderGen's generated passes define SHADERGEN_GENERATED_PASS
// and declare their own keyword set, see ForwardKeywords.hlsl.
#ifndef SHADERGEN_GENERATED_PASS

// -----------------------------------------------------------------------------
// DBuffer Decals
// -----------------------------------------------------------------------------
// MRT1: Albedo only
// MRT2: Albedo + Normal
// MRT3: Albedo + Normal + Metal/AO/Smoothness
#pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3

// =============================================================================
// USAGE IN FRAGMENT SHADER
// =============================================================================
// After sampling your base color and normal, apply decals:
//
// #if defined(_DBUFFER)
//     ApplyDecalToBaseColorAndNormal(input.positionCS, albedo, normalWS);
// #endif
//
// For full surface data (metallic, smoothness, AO):
// #if defined(_DBUFFER)
//     ApplyDecal(input.positionCS,
//         surfaceData.albedo,
//         surfaceData.specular,
//         inputData.normalWS,
//         surfaceData.metallic,
//         surfaceData.occlusion,
//         surfaceData.smoothness);
// #endif
// =============================================================================

#endif // SHADERGEN_GENERATED_PASS
