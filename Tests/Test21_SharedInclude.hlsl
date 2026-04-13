#pragma once
// Shared include for Test21. Everything the shader needs (structs, CBUFFER,
// textures) lives here. If the include resolver works, the pipeline finds
// these and generates correct auxiliary passes.

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseColor;
CBUFFER_END

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Interpolators
{
    float4 positionCS : SV_POSITION;
    float3 normalWS : NORMAL;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
