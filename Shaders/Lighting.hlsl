#pragma once

// =============================================================================
// LIGHT_LOOP.HLSL
// Abstracts Forward vs Forward+ light iteration.
//
// Usage:
//   LIGHT_LOOP_BEGIN(positionWS, positionCS)
//       Light light = LIGHT_LOOP_GET_LIGHT(positionWS);
//       color += YourLightingFunction(light, ...);
//   LIGHT_LOOP_END
// =============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Keywords/LightingKeywords.hlsl"
// we overridin these
#undef LIGHT_LOOP_BEGIN
#undef LIGHT_LOOP_END

// -----------------------------------------------------------------------------
// Main Light Helper
// -----------------------------------------------------------------------------

Light GetMainLightData(float3 positionWS, float4 shadowCoord)
{
    Light light = GetMainLight(shadowCoord);
    return light;
}

// -----------------------------------------------------------------------------
// Additional Lights Loop Macros
// -----------------------------------------------------------------------------

#if defined(_ADDITIONAL_LIGHTS)

    #if defined(_CLUSTER_LIGHT_LOOP)
        // Forward+ (Clustered)
        #define LIGHT_LOOP_BEGIN(positionWS, positionCS) \
            { \
                uint _lightIdx; \
                ClusterIterator _iter = ClusterInit(GetNormalizedScreenSpaceUV(positionCS), positionWS, 0); \
                [loop] while (ClusterNext(_iter, _lightIdx)) { \
                    _lightIdx += URP_FP_DIRECTIONAL_LIGHTS_COUNT;

        #define LIGHT_LOOP_GET_LIGHT(positionWS) GetAdditionalLight(_lightIdx, positionWS)
        #define LIGHT_LOOP_GET_LIGHT_SHADOW(positionWS, shadowMask) GetAdditionalLight(_lightIdx, positionWS, shadowMask)
        #define LIGHT_LOOP_END } }
    #else
        // Standard Forward
        #define LIGHT_LOOP_BEGIN(positionWS, positionCS) \
            { \
                uint _lightCount = GetAdditionalLightsCount(); \
                for (uint _lightIdx = 0u; _lightIdx < _lightCount; ++_lightIdx) {

        #define LIGHT_LOOP_GET_LIGHT(positionWS) GetAdditionalLight(_lightIdx, positionWS)
        #define LIGHT_LOOP_GET_LIGHT_SHADOW(positionWS, shadowMask) GetAdditionalLight(_lightIdx, positionWS, shadowMask)
        #define LIGHT_LOOP_END } }
    #endif

#else
    // No additional lights
    #define LIGHT_LOOP_BEGIN(positionWS, positionCS) { if (false) {
    #define LIGHT_LOOP_GET_LIGHT(positionWS) (Light)0
    #define LIGHT_LOOP_GET_LIGHT_SHADOW(positionWS, shadowMask) (Light)0
    #define LIGHT_LOOP_END } }
#endif

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
