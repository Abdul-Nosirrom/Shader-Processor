#pragma once

// =============================================================================
// DOTS_INSTANCING.HLSL
// Keywords for GPU instancing, DOTS, and GPU Resident Drawer support.
// Use: #include_with_pragmas "Library/Keywords/DOTSInstancing.hlsl"
// =============================================================================

// -----------------------------------------------------------------------------
// Standard GPU Instancing
// -----------------------------------------------------------------------------
#pragma multi_compile_instancing

// Per-instance rendering layer support (for light layers)
#pragma instancing_options renderinglayer

// -----------------------------------------------------------------------------
// DOTS Instancing (BatchRendererGroup / GPU Resident Drawer)
// -----------------------------------------------------------------------------
// Required for GPU Resident Drawer in Unity 6+ / URP 14+
// NOTE: DOTS.hlsl internally contains: #pragma multi_compile _ DOTS_INSTANCING_ON
// Do NOT add that pragma manually - it would duplicate variants
#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

// =============================================================================
// DOTS INSTANCING PROPERTY BLOCK TEMPLATE
// =============================================================================
// Add this in your shader's HLSLINCLUDE after CBUFFER to enable per-instance
// property overrides with GPU Resident Drawer:
//
// #ifdef UNITY_DOTS_INSTANCING_ENABLED
//     UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
//         UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
//         // Add other per-instance properties here
//     UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
//     
//     // Override property accessors
//     #define _BaseColor UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor)
// #endif
// =============================================================================
