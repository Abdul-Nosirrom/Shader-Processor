#pragma once

// =============================================================================
// COMMON.HLSL
// Core utilities and transform helpers. Include in HLSLINCLUDE block.
// =============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

// -----------------------------------------------------------------------------
// Vertex Transform Helper
// -----------------------------------------------------------------------------

struct VertexData
{
    float3 positionWS;
    float4 positionCS;
    float3 normalWS;
    float4 tangentWS;
    float3 viewDirWS;
};

VertexData GetVertexData(float3 positionOS, float3 normalOS, float4 tangentOS)
{
    VertexData o;
    VertexPositionInputs posInputs = GetVertexPositionInputs(positionOS);
    VertexNormalInputs normInputs = GetVertexNormalInputs(normalOS, tangentOS);
    
    o.positionWS = posInputs.positionWS;
    o.positionCS = posInputs.positionCS;
    o.normalWS = normInputs.normalWS;
    o.tangentWS = float4(normInputs.tangentWS, tangentOS.w);
    o.viewDirWS = GetWorldSpaceNormalizeViewDir(posInputs.positionWS);
    return o;
}

VertexData GetVertexDataSimple(float3 positionOS, float3 normalOS)
{
    return GetVertexData(positionOS, normalOS, float4(1, 0, 0, 1));
}

// -----------------------------------------------------------------------------
// Normal Mapping
// -----------------------------------------------------------------------------

half3 UnpackScaledNormal(half4 packedNormal, half scale)
{
    half3 normal;
    normal.xy = (packedNormal.wy * 2.0 - 1.0) * scale;
    normal.z = sqrt(1.0 - saturate(dot(normal.xy, normal.xy)));
    return normal;
}

half3 ApplyNormalMap(half3 normalTS, half3 tangentWS, half3 bitangentWS, half3 normalWS)
{
    return normalize(mul(normalTS, half3x3(tangentWS, bitangentWS, normalWS)));
}

// Samples a normal map, returning the new normals in worldspace TODO: adjust some funcs above for this
float3 SampleNormalMap(TEXTURE2D_PARAM(_normapTex, sampler_normalTex), float2 uv, half3 normalWS, half4 tangentWS, half normalStrength = 1)
{
    float3 normalTS = UnpackScaledNormal(SAMPLE_TEXTURE2D(_normapTex, sampler_normalTex, uv), normalStrength);
    float tangentSign = tangentWS.w;
    float3 bitangentWS = tangentSign * cross(normalWS.xyz, normalTS);
    half3x3 tangentToWorld = half3x3(tangentWS.xyz, bitangentWS, normalWS.xyz);
    return TransformTangentToWorld(normalTS, tangentToWorld, true);
}

// -----------------------------------------------------------------------------
// Shadow Coords
// -----------------------------------------------------------------------------

float4 GetShadowCoord(float3 positionWS, float4 positionCS)
{
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
        return ComputeScreenPos(positionCS);
    #else
        return TransformWorldToShadowCoord(positionWS);
    #endif
}

// -----------------------------------------------------------------------------
// LOD Crossfade
// -----------------------------------------------------------------------------

void ApplyLODCrossfade(float4 positionCS)
{
    #if defined(LOD_FADE_CROSSFADE)
        LODFadeCrossFade(positionCS);
    #endif
}
