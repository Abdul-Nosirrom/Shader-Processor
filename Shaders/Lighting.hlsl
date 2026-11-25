#pragma once

// =============================================================================
// LIGHT_LOOP.HLSL
// One loop over every realtime light that reaches a pixel, so lighting code is
// written once instead of "main light, then additional lights".
//
// Usage:
//   ALL_LIGHTS_LOOP_BEGIN(positionWS, positionCS, shadowCoord, shadowMask)
//       color += YourLightingFunction(light, ...);   // `light` and `isMainLight` are in scope
//   ALL_LIGHTS_LOOP_END
//
// Pass half4(1, 1, 1, 1) as shadowMask if the shader doesn't sample one.
// =============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Keywords/LightingKeywords.hlsl"

// -----------------------------------------------------------------------------
// Additional light iteration
// -----------------------------------------------------------------------------
// FS_ADDITIONAL_LIGHTS_INIT declares the iteration state. FS_ADDITIONAL_LIGHTS_NEXT
// writes the next additional light index, or breaks out of the enclosing loop when
// there are none left.

#if defined(_CLUSTER_LIGHT_LOOP)
    // Forward+ keeps additional directional lights out of the cluster. They sit at indices
    // [0, URP_FP_DIRECTIONAL_LIGHTS_COUNT) and URP lights them in a separate loop before it
    // walks the cluster (UniversalFragmentPBR in URP Lighting.hlsl). Cluster indices start
    // after them.
    #define FS_ADDITIONAL_LIGHTS_INIT(positionWS, positionCS) \
        uint _directionalLightCount = min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); \
        uint _nextDirectionalLight = 0u; \
        ClusterIterator _clusterIterator = ClusterInit(GetNormalizedScreenSpaceUV(positionCS), positionWS, 0);

    #define FS_ADDITIONAL_LIGHTS_NEXT(lightIndex) \
        if (_nextDirectionalLight < _directionalLightCount) { lightIndex = _nextDirectionalLight++; } \
        else if (ClusterNext(_clusterIterator, lightIndex)) { lightIndex += URP_FP_DIRECTIONAL_LIGHTS_COUNT; } \
        else { break; }

#elif defined(_ADDITIONAL_LIGHTS)
    // Forward: per-object light list
    #define FS_ADDITIONAL_LIGHTS_INIT(positionWS, positionCS) \
        uint _additionalLightCount = GetAdditionalLightsCount(); \
        uint _nextAdditionalLight = 0u;

    #define FS_ADDITIONAL_LIGHTS_NEXT(lightIndex) \
        if (_nextAdditionalLight < _additionalLightCount) { lightIndex = _nextAdditionalLight++; } \
        else { break; }

#else
    // No per-pixel additional lights (off, or per-vertex through DoVertexLighting)
    #define FS_ADDITIONAL_LIGHTS_INIT(positionWS, positionCS)
    #define FS_ADDITIONAL_LIGHTS_NEXT(lightIndex) { break; }
#endif

#if defined(_CLUSTER_LIGHT_LOOP) && defined(LIGHTMAP_ON) && defined(LIGHTMAP_SHADOW_MIXING)
    // Same as URP's CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK (RealtimeLights.hlsl): with
    // subtractive mixed lighting, lights already baked into the lightmap are flagged in the
    // colour alpha and would be counted twice
    #define FS_SKIP_BAKED_SUBTRACTIVE_LIGHT(lightIndex) if (_AdditionalLightsColor[lightIndex].a > 0.0h) { continue; }
#else
    #define FS_SKIP_BAKED_SUBTRACTIVE_LIGHT(lightIndex)
#endif

// -----------------------------------------------------------------------------
// All Lights Loop
// -----------------------------------------------------------------------------
// Lights come in URP's order: the main light, then the additional lights. The main light
// uses URP's 3-arg GetMainLight, which adds the shadow-distance fade, baked shadowmask mixing
// and the main light cookie on top of the realtime shadow.
//
// These names are our own rather than URP's LIGHT_LOOP_BEGIN/END (RealtimeLights.hlsl), so
// including this file after Lighting.hlsl doesn't redefine URP's macros.

#define ALL_LIGHTS_LOOP_BEGIN(positionWS, positionCS, shadowCoord, shadowMask) \
    { \
        FS_ADDITIONAL_LIGHTS_INIT(positionWS, positionCS) \
        bool _mainLightDone = false; \
        [loop] while (true) \
        { \
            Light light = (Light)0; \
            bool isMainLight = !_mainLightDone; \
            if (isMainLight) \
            { \
                _mainLightDone = true; \
                light = GetMainLight(shadowCoord, positionWS, shadowMask); \
            } \
            else \
            { \
                uint _lightIndex = 0u; \
                FS_ADDITIONAL_LIGHTS_NEXT(_lightIndex) \
                FS_SKIP_BAKED_SUBTRACTIVE_LIGHT(_lightIndex) \
                light = GetAdditionalLight(_lightIndex, positionWS, shadowMask); \
            }

#define ALL_LIGHTS_LOOP_END } }

// -----------------------------------------------------------------------------
// Vertex Lighting (Low-end / Mobile)
// -----------------------------------------------------------------------------

half3 DoVertexLighting(float3 positionWS, half3 normalWS)
{
    half3 vertexLight = half3(0, 0, 0);

    #if defined(_ADDITIONAL_LIGHTS_VERTEX)
        uint lightCount = GetAdditionalLightsCount();
        for (uint i = 0u; i < lightCount; ++i)
        {
            Light light = GetAdditionalLight(i, positionWS);
            vertexLight += light.color * light.distanceAttenuation * saturate(dot(normalWS, light.direction));
        }
    #endif

    return vertexLight;
}
