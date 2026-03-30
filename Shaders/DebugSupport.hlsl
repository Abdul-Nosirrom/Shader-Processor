#ifndef CUSTOM_DEBUG_SUPPORT_INCLUDED
#define CUSTOM_DEBUG_SUPPORT_INCLUDED

// =============================================================================
// DEBUG_SUPPORT.HLSL
// Integration with URP's Rendering Debugger (overdraw, material views, etc.)
// Without this, debug views won't work properly with custom shaders.
// =============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging3D.hlsl"

// Call this in fragment shader to set up debug data
void SetupDebugData(inout InputData inputData, float4 positionCS, float4 shadowCoord, float3 normalWS, float3 viewDirWS)
{
    #if defined(DEBUG_DISPLAY)
        inputData.positionCS = positionCS;
        inputData.shadowCoord = shadowCoord;
        inputData.viewDirectionWS = viewDirWS;
        inputData.normalWS = normalWS;
        inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    #endif
}

// Check if debug mode wants to override output. Returns true if debugColor should be used.
bool TryGetDebugColor(InputData inputData, half3 albedo, half alpha, half3 emission, out half4 debugColor)
{
    debugColor = half4(0, 0, 0, 1);
    
    #if defined(DEBUG_DISPLAY)
        SurfaceData surfaceData = (SurfaceData)0;
        surfaceData.albedo = albedo;
        surfaceData.alpha = alpha;
        surfaceData.emission = emission;
        
        if (CanDebugOverrideOutputColor(inputData, surfaceData, debugColor))
        {
            return true;
        }
    #endif
    
    return false;
}

// This is what to use
// Call setup goes like (right before final return):
// if (CanDebugOverrideOutputColor(color, params) return color;
// return whatever else:
bool CanDebugOverrideOutputColor(inout half3 color, float4 positionCS, float3 positionWS, float3 normalWS, half3 bakedGI = 0)
{
    #if defined(DEBUG_DISPLAY)
    InputData inputData = (InputData)0;
    inputData.bakedGI = bakedGI;
    inputData.positionCS = positionCS;
    inputData.positionWS = positionWS;
    inputData.normalWS = normalWS;
    inputData.shadowCoord = GetShadowCoord(positionWS, positionCS);
    inputData.viewDirectionWS = GetWorldSpaceViewDir(positionWS);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);

    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = color;
    half4 debugColor;
    if (CanDebugOverrideOutputColor(inputData, surfaceData, debugColor))
    {
        color = debugColor.rgb;
        return true;
    }
    color = surfaceData.albedo;
    #endif

    return false;
}

// This is what to use
// Call setup goes like (right before final return):
// if (CanDebugOverrideOutputColor(color, params) return color;
// return whatever else:
bool CanDebugOverrideOutputColor(inout half4 color, float4 positionCS, float3 positionWS, float3 normalWS, half3 bakedGI = 0)
{
    half3 colRGB = color.rgb;
    bool can = CanDebugOverrideOutputColor(colRGB, positionCS, positionWS, normalWS, bakedGI);
    color.rgb = colRGB;
    return can;
}

#endif
