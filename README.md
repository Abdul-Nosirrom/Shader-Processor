# FreeSkies Shader Processing System

A Unity shader preprocessing system that automatically generates utility passes (ShadowCaster, DepthOnly, etc.) from a single Forward pass definition.

## Features

- **Tag-Based Processing**: Enable features via SubShader tags
- **Automatic Pass Generation**: Generate ShadowCaster, DepthOnly, DepthNormals, MotionVectors, Meta passes
- **Hook System**: Define vertex displacement, interpolator transfer, and alpha clip functions once
- **Extensible**: Add new features via tag processors
- **SRP Batching Compatible**: Shared CBUFFER across all passes

## Quick Start

1. Add `"FreeSkies" = "True"` tag to your SubShader
2. Place `[InjectBasePasses]` marker where passes should be generated
3. Define your structs and Forward pass as usual

```hlsl
Shader "MyShader"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "FreeSkies" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
            float3 normalWS : TEXCOORD1;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            Interpolators Vert(Attributes input) { /* ... */ }
            half4 Frag(Interpolators input) : SV_Target { /* ... */ }
            ENDHLSL
        }

        [InjectBasePasses]
    }
}
```

## Tag Processors

### Outlines

Enable with `"Outlines" = "On"`:

```hlsl
Tags
{
    "FreeSkies" = "True"
    "Outlines" = "On"
}
```

Automatically injects:
- `_EnableOutlines`, `_OutlineWidth`, `_OutlineColor` properties
- CBUFFER entries
- Outline pass

### Tessellation (Future)

Enable with `"Tessellation" = "On"` - transforms Forward pass and adds tessellation to all generated passes.

## Hook System

Define functions in your Forward pass and reference them via pragmas:

### Vertex Displacement

```hlsl
#pragma vertexDisplacement MyDisplacementFunc

void MyDisplacementFunc(inout Attributes attr)
{
    attr.positionOS.xyz += attr.normalOS * _DisplacementAmount;
}
```

### Interpolator Transfer

Transfer custom interpolator fields (NOT position, normal, UV - those are handled by templates):

```hlsl
#pragma interpolatorTransfer TransferExtras

void TransferExtras(Attributes input, inout Interpolators output)
{
    output.customData = ComputeCustomData(input.uv);
    output.vertexColor = input.color;
}
```

### Alpha Clip

```hlsl
#pragma alphaClip ClipAlpha

void ClipAlpha(Interpolators input)
{
    float alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a;
    clip(alpha - _Cutoff);
}
```

## Struct Naming

User defines structs as `Attributes` and `Interpolators`. Generated passes create pass-specific versions:
- `ShadowCasterAttributes`, `ShadowCasterInterpolators`
- `DepthOnlyAttributes`, `DepthOnlyInterpolators`
- etc.

Hook functions are automatically rewritten to use the pass-specific struct names.

## File Structure

```
Editor/
├── Core/
│   ├── ShaderContext.cs       # Parsed data container
│   ├── ShaderParser.cs        # Parsing logic
│   ├── ShaderProcessor.cs     # Main orchestrator
│   └── FSShaderImporter.cs    # Unity importer
│
├── Generation/
│   ├── TemplateEngine.cs      # Template processing
│   ├── PassGenerator.cs       # Pass generation
│   ├── StructGenerator.cs     # Struct generation
│   └── HookProcessor.cs       # Hook extraction
│
├── TagProcessors/
│   ├── ShaderTagProcessor.cs  # Base interface
│   └── OutlinesProcessor.cs   # Outlines feature
│
└── Templates/
    ├── ShadowCaster.hlsl
    ├── DepthOnly.hlsl
    ├── DepthNormals.hlsl
    ├── MotionVectors.hlsl
    ├── Meta.hlsl
    └── Outline.hlsl
```

## Template Markers

Templates use `{{MARKER}}` syntax:

| Marker | Description |
|--------|-------------|
| `{{ATTRIBUTES_STRUCT}}` | Generated attributes struct |
| `{{INTERPOLATORS_STRUCT}}` | Generated interpolators struct |
| `{{CBUFFER}}` | Material CBUFFER (if not in HLSLINCLUDE) |
| `{{TEXTURES}}` | Texture declarations (if not in HLSLINCLUDE) |
| `{{HOOK_DEFINES}}` | `#define FS_VERTEX_DISPLACEMENT` etc. |
| `{{HOOK_FUNCTIONS}}` | Extracted hook function bodies |
| `{{POSITION}}` | User's position field name (semantic-based) |
| `{{NORMAL}}` | User's normal field name |
| `{{TEXCOORD0}}` | User's UV field name |
| `{{SV_POSITION}}` | User's clip position field name |
| `{{VERTEX_DISPLACEMENT_CALL}}` | Hook call with `#ifdef` guard |
| `{{INTERPOLATOR_TRANSFER_CALL}}` | Hook call with `#ifdef` guard |
| `{{ALPHA_CLIP_CALL}}` | Hook call with `#ifdef` guard |

## Creating Custom Tag Processors

```csharp
[ShaderTagProcessor("MyFeature", priority: 50)]
public class MyFeatureProcessor : ShaderTagProcessorBase
{
    public override string TagName => "MyFeature";
    
    public override void InjectProperties(ShaderContext ctx)
    {
        if (PropertyExists(ctx, "_MyProperty")) return;
        InjectPropertiesContent(ctx, @"
        [Header(My Feature)]
        _MyProperty(""My Property"", Float) = 1");
    }
    
    public override void InjectCBuffer(ShaderContext ctx)
    {
        if (CBufferEntryExists(ctx, "_MyProperty")) return;
        InjectCBufferContent(ctx, @"
    float _MyProperty;");
    }
    
    public override void InjectPasses(ShaderContext ctx)
    {
        string passCode = GenerateMyPass(ctx);
        QueuePass(ctx, passCode);
    }
}
```

## Debug Tools

- **Tools > FreeSkies > Reload Templates**: Clear template cache
- **Tools > FreeSkies > Reload Tag Processors**: Re-discover processors
- **Inspector > Show Processed Shader**: View generated output

## Constraints

1. CBUFFER must be identical across all passes (SRP Batching)
2. Hook functions must be defined in Forward pass HLSLPROGRAM
3. Helper functions called by hooks must also be in Forward pass
4. Struct names detected from Forward pass vertex function signature

## License

MIT License
