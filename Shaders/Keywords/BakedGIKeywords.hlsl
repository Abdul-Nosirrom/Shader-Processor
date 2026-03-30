#pragma once

// =============================================================================
// BAKED_GI_KEYWORDS.HLSL
// Keywords for baked lighting: lightmaps, light probes, mixed lighting.
// Use: #include_with_pragmas "Library/Keywords/BakedGIKeywords.hlsl"
// =============================================================================

// -----------------------------------------------------------------------------
// Lightmaps
// -----------------------------------------------------------------------------
// Object has baked lightmap UVs
#pragma multi_compile _ LIGHTMAP_ON

// Directional lightmaps (stores dominant light direction)
#pragma multi_compile _ DIRLIGHTMAP_COMBINED

// Higher quality lightmap filtering
#pragma multi_compile_fragment _ LIGHTMAP_BICUBIC_SAMPLING

// Legacy lightmap format support
#pragma multi_compile _ USE_LEGACY_LIGHTMAPS

// -----------------------------------------------------------------------------
// Mixed Lighting
// -----------------------------------------------------------------------------
// Baked indirect + realtime direct shadows
#pragma multi_compile _ LIGHTMAP_SHADOW_MIXING

// Shadowmask mode (baked shadows for static, realtime for dynamic)
#pragma multi_compile _ SHADOWS_SHADOWMASK

// -----------------------------------------------------------------------------
// Realtime GI
// -----------------------------------------------------------------------------
// Enlighten realtime lightmaps (if enabled)
#pragma multi_compile _ DYNAMICLIGHTMAP_ON

// -----------------------------------------------------------------------------
// Spherical Harmonics / Light Probes
// -----------------------------------------------------------------------------
// SH evaluation quality
// MIXED: Per-vertex L0L1, per-pixel L2
// VERTEX: All SH per-vertex (faster)
#pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX

// -----------------------------------------------------------------------------
// Probe Volumes (APV) - Unity 2023.1+ / Unity 6
// -----------------------------------------------------------------------------
// Uncomment if using Adaptive Probe Volumes
// #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
