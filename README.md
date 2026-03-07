# ShaderGen

A Unity shader preprocessing system for URP that automatically generates auxiliary passes (ShadowCaster, DepthOnly, etc.) from a single Forward pass definition. Supports hook-based customization, tag processors for feature injection, and hardware tessellation.

## Features

- **Automatic Pass Generation**: Write your Forward pass once, get ShadowCaster, DepthOnly, DepthNormals, MotionVectors, and Meta passes generated automatically
- **Hook System**: Define vertex displacement, interpolator transfer, and alpha clip functions that propagate to all passes
- **Tag Processors**: Modular system for injecting features like outlines and tessellation via SubShader tags
- **Struct Preservation**: Your custom Attributes and Interpolators are carried through to generated passes
- **Custom Naming Support**: Works with any struct/function names, not just hardcoded conventions

## Installation

1. Copy the `ShaderProcessing` folder into your Unity project's `Packages` directory
2. The package will be recognized as `com.abdulal.shaderprocessor`

## Quick Start

Add `"ShaderGen" = "True"` to your Forward pass tags:

```hlsl
Pass
{
    Name "Forward"
    Tags
    {
        "LightMode" = "UniversalForward"
        "ShaderGen" = "True"
    }
    
    HLSLPROGRAM
    #pragma vertex Vert
    #pragma fragment Frag
    
    // Your shader code...
    
    ENDHLSL
}

[InjectBasePasses]
```

The `[InjectBasePasses]` marker tells ShaderGen where to insert the generated passes.

### Individual Pass Injection

If you only need specific passes instead of all of them, use individual markers:

```hlsl
Pass
{
    Name "Forward"
    Tags { "LightMode" = "UniversalForward" "ShaderGen" = "True" }
    // ...
}

[InjectPass:ShadowCaster]
[InjectPass:DepthOnly]
```

Available passes:
- `[InjectPass:ShadowCaster]` - Shadow casting
- `[InjectPass:DepthOnly]` - Depth prepass
- `[InjectPass:DepthNormals]` - Depth and normals (for SSAO, etc.)
- `[InjectPass:MotionVectors]` - Motion vector generation
- `[InjectPass:Meta]` - Lightmap baking

Use `[InjectBasePasses]` to inject all of them at once, or pick only the ones you need.

## Hook System

Hooks let you define functions that automatically propagate to all generated passes.

### Available Hooks

| Hook | Purpose | Signature |
|------|---------|-----------|
| `vertexDisplacement` | Modify vertex position/attributes before transforms | `void FuncName(inout Attributes attr)` |
| `interpolatorTransfer` | Transfer custom data from vertex to fragment | `void FuncName(Attributes input, inout Interpolators output)` |
| `alphaClip` | Perform alpha testing in fragment | `void FuncName(Interpolators input)` |

### Usage

Declare hooks with pragma directives:

```hlsl
#pragma vertexDisplacement ApplyDisplacement
#pragma interpolatorTransfer TransferCustomData
#pragma alphaClip PerformAlphaClip

void ApplyDisplacement(inout Attributes attr)
{
    float height = SampleHeight(attr.uv);
    attr.positionOS.xyz += attr.normalOS * height * _Strength;
}

void TransferCustomData(Attributes input, inout Interpolators output)
{
    output.vertexColor = input.color;
    output.customData = ComputeCustomData(input);
}

void PerformAlphaClip(Interpolators input)
{
    #ifdef _ALPHATEST_ON
    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
    #endif
}
```

## Tag Processors

Tag processors inject features based on SubShader tags. Built-in processors:

### Outlines

```hlsl
Tags
{
    "Outlines" = "On"
}
```

Automatically injects:
- Outline properties (`_OutlineWidth`, `_OutlineColor`)
- CBUFFER entries
- Outline pass with front-face culling

### Tessellation

```hlsl
Tags
{
    "Tessellation" = "On"
}
```

Automatically injects:
- Tessellation properties and material drawer
- Hull and Domain shaders into all passes
- Multiple tessellation modes (Uniform, EdgeLength, Distance, EdgeDistance)
- Phong smoothing support
- Frustum and backface culling options

Tessellation properties are controlled via a custom material drawer that shows/hides options based on the enabled mode.

## File Structure

```
ShaderProcessing/
├── Editor/
│   ├── Core/
│   │   ├── ShaderContext.cs       # Shared state during processing
│   │   ├── ShaderParser.cs        # Parses shader source
│   │   ├── ShaderProcessor.cs     # Main processing pipeline
│   │   └── FSShaderImporter.cs    # ScriptedImporter integration
│   ├── Generation/
│   │   ├── TemplateEngine.cs      # Template loading and processing
│   │   ├── PassGenerator.cs       # Generates auxiliary passes
│   │   ├── StructGenerator.cs     # Generates Attributes/Interpolators
│   │   └── HookProcessor.cs       # Processes hook functions
│   ├── TagProcessors/
│   │   ├── ShaderTagProcessor.cs  # Base class and registry
│   │   ├── OutlinesProcessor.cs   # Outline feature injection
│   │   └── TessellationProcessor.cs # Tessellation injection
│   └── Templates/
│       ├── ShadowCaster.hlsl
│       ├── DepthOnly.hlsl
│       ├── DepthNormals.hlsl
│       ├── MotionVectors.hlsl
│       ├── Meta.hlsl
│       ├── Outline.hlsl
│       └── Tessellation.hlsl
└── Examples/
    ├── Basic.shader
    ├── WithOutlines.shader
    ├── AllHooks.shader
    ├── CustomNaming.shader
    └── FullFeatured.shader
```

## Processing Pipeline

1. **ShaderPreprocessor** detects `"ShaderGen" = "True"` tag and sets the custom importer
2. **FSShaderImporter** triggers on import and calls ShaderProcessor
3. **ShaderParser** extracts structs, functions, hooks, and tags from source
4. **Tag Processors** run in priority order:
   - `InjectProperties()` - Add material properties
   - `InjectCBuffer()` - Add CBUFFER entries
   - `ModifyMainPass()` - Modify the Forward pass
   - `InjectPasses()` - Queue additional passes
5. **PassGenerator** processes `[InjectBasePasses]` and individual `[InjectPass:Name]` markers
6. **TemplateEngine** loads templates and applies replacements including tag processor contributions

## Template Markers

Templates use `{{MARKER}}` syntax for replacements:

| Marker | Description |
|--------|-------------|
| `{{ATTRIBUTES}}` | Generated Attributes struct |
| `{{INTERPOLATORS}}` | Generated Interpolators struct |
| `{{CBUFFER}}` | Material CBUFFER content |
| `{{TEXTURES}}` | Texture/sampler declarations |
| `{{FORWARD_CONTENT}}` | Pragmas and helpers from Forward pass |
| `{{HOOK_FUNCTIONS}}` | Rewritten hook functions |
| `{{VERTEX_PRAGMA}}` | Vertex shader pragma (may be overridden by tessellation) |
| `{{TESSELLATION_CODE}}` | Tessellation shaders (when enabled) |

## Creating Custom Tag Processors

```csharp
[ShaderTagProcessor("MyFeature", priority: 10)]
public class MyFeatureProcessor : ShaderTagProcessorBase
{
    public override string TagName => "MyFeature";
    public override int Priority => 10;
    
    public override void InjectProperties(ShaderContext ctx)
    {
        if (PropertyExists(ctx, "_MyProperty")) return;
        InjectPropertiesContent(ctx, "_MyProperty(\"My Property\", Float) = 1");
    }
    
    public override void InjectCBuffer(ShaderContext ctx)
    {
        if (CBufferEntryExists(ctx, "_MyProperty")) return;
        InjectCBufferContent(ctx, "float _MyProperty;");
    }
    
    public override void ModifyMainPass(ShaderContext ctx)
    {
        // Modify the Forward pass source
    }
    
    public override void InjectPasses(ShaderContext ctx)
    {
        // Queue additional passes
        ctx.QueuedPasses.Add(new QueuedPass { /* ... */ });
    }
    
    public override Dictionary<string, string> GetPassReplacements(ShaderContext ctx, string passName)
    {
        // Provide replacements for generated passes
        return new Dictionary<string, string>
        {
            ["MY_MARKER"] = "replacement content"
        };
    }
}
```

## Tessellation Details

The tessellation system supports:

**Modes**
- Uniform: Fixed tessellation factor
- Edge Length: Screen-space edge length targeting
- Distance: Camera distance based falloff
- Edge Distance: Combination of edge length and distance

**Features**
- Phong smoothing for curved surfaces
- Frustum culling to skip offscreen patches
- Backface culling to skip away-facing patches

**Dynamic Field Handling**

The tessellation system dynamically reads your Attributes struct and generates matching `TessControlPoint` structs. All fields (position, normal, tangent, UVs, vertex colors, etc.) are properly carried through the tessellation stage with appropriate interpolation:

- POSITION: Barycentric interpolation with w=1
- NORMAL: Barycentric interpolation + normalize
- TANGENT: Barycentric interpolation + normalize, preserve w
- Everything else: Simple barycentric interpolation

## Example Shader

```hlsl
Shader "MyShader"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _DisplacementMap("Displacement", 2D) = "gray" {}
        _DisplacementStrength("Strength", Range(0, 1)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Tessellation" = "On"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _DisplacementStrength;
        CBUFFER_END

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_DisplacementMap);
        SAMPLER(sampler_DisplacementMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Interpolators
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags
            {
                "LightMode" = "UniversalForward"
                "ShaderGen" = "True"
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #pragma vertexDisplacement ApplyDisplacement

            void ApplyDisplacement(inout Attributes attr)
            {
                float h = SAMPLE_TEXTURE2D_LOD(_DisplacementMap, sampler_DisplacementMap, attr.uv, 0).r;
                attr.positionOS.xyz += attr.normalOS * (h - 0.5) * _DisplacementStrength;
            }

            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                ApplyDisplacement(input);
                
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Interpolators input) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
            }
            ENDHLSL
        }

        [InjectBasePasses]
    }

    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
```

## Notes

- The system uses Unity's ScriptedImporter, so shader changes trigger automatic reimport
- Generated passes appear in the imported shader asset, not the source file
- Tag processors run in priority order (lower numbers first)
- Tessellation requires hardware support (DX11+, Metal, Vulkan)