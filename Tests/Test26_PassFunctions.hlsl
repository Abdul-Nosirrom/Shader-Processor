#pragma once
// Vertex, fragment and hook functions for Test26_PassIncludeFunctions, which
// includes this file from its forward pass. Structs, CBUFFER and textures come
// from Test21_SharedInclude.hlsl, included just before this file: in generated
// passes the forward pass's #includes sit above the generated structs and
// CBUFFER, so an included file has to get what it uses from earlier includes
// (or HLSLINCLUDE), not from code written inline in the pass.

void Test26Displace(inout Attributes input)
{
    input.positionOS.xyz += input.normalOS * 0.01;
}

Interpolators vert(Attributes input)
{
    Interpolators output = (Interpolators)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    Test26Displace(input);
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    return output;
}

// The extra SV_IsFrontFace parameter has to reach the generated fragment
// functions, since the injected body below reads it
half4 frag(Interpolators input, bool isFrontFace : SV_IsFrontFace) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
    half faceShade = isFrontFace ? 1.0 : 0.5;
    return col * _BaseColor * faceShade;
}
