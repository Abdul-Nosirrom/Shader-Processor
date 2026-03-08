# ShaderGen

A Unity URP shader preprocessing system that automatically generates auxiliary passes from a single authored pass. Write your Forward pass, hook in your custom logic, and let ShaderGen handle the boilerplate. The goal is to get the convenience of surface shaders while staying in normal HLSL.

## Features

- **Automatic Pass Generation**: Write your Forward pass once, get ShadowCaster, DepthOnly, DepthNormals, MotionVectors, and Meta passes generated automatically
- **Hook System**: Define vertex displacement, interpolator transfer, alpha clip, and tessellation factor override functions that propagate to all generated passes
- **Pass Injectors**: Data-driven pass definitions. Each pass is a small class + a template. Adding a new pass type requires zero pipeline changes
- **Tag Processors**: Modular feature injection for things that modify existing passes (like tessellation)
- **Hardware Tessellation**: Full tessellation pipeline with multiple modes, phong smoothing, and culling. Injected per-pass via tags
- **Struct Preservation**: Works with any struct/function naming. Your `Attributes`/`Interpolators` (or `VIn`/`VOut` or whatever you call them) are carried through correctly
- **HLSLINCLUDE Support**: Structs, CBUFFER, textures, and hook functions can live in HLSLINCLUDE and are handled correctly across all passes
- **Editor Tooling**: Inspector shows parsed shader info, active passes, active tag processors, and detected hooks. Docs window (Tools/ShaderProcessor/Docs) shows all registered hooks, passes, and processors

## Installation

1. Copy the package folder into your Unity project's `Packages` directory
2. The package will be recognized as `com.abdulal.shaderprocessor`

## Quick Start

Add `"ShaderGen" = "True"` to your Forward pass tags and place `[InjectBasePasses]` where you want the generated passes to appear:

```hlsl
Shader "MyShader"
{
    Properties
    {
        _BaseMap("Albedo", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
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
            float3 normalWS : NORMAL;
            float2 uv : TEXCOORD0;
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

            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
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

That's it. ShaderGen parses your Forward pass, extracts your structs and material data, and generates all the auxiliary passes URP needs.

### Individual Pass Injection

If you only need specific passes:

```hlsl
[InjectPass:ShadowCaster]
[InjectPass:DepthOnly]
```

Available base passes: `ShadowCaster`, `DepthOnly`, `DepthNormals`, `MotionVectors`, `Meta`.

Feature passes (not included in `[InjectBasePasses]`): `Outline`.

## Hooks

Hooks let you define functions that automatically propagate to all generated passes. Declare them with pragma directives in your pass, and ShaderGen extracts the function body, rewrites struct names per generated pass, and injects it with the appropriate call.

Hook function bodies can live either in the pass (next to the pragma) or in HLSLINCLUDE (shared across passes). Either way works.

### Built-in Hooks

| Hook | Purpose | Signature |
|------|---------|-----------|
| `vertexDisplacement` | Modify vertex position/attributes before transforms | `void Func(inout Attributes input)` |
| `interpolatorTransfer` | Transfer custom data from vertex to fragment | `void Func(Attributes input, inout Interpolators output)` |
| `alphaClip` | Alpha testing in fragment shader | `void Func(Interpolators input)` |
| `tessFactorOverride` | Override tessellation factor per vertex | `void Func(inout float factor, Attributes input)` |

### Usage

```hlsl
#pragma vertexDisplacement ApplyHeight
#pragma alphaClip DoClip

void ApplyHeight(inout Attributes input)
{
    float h = SAMPLE_TEXTURE2D_LOD(_HeightMap, sampler_HeightMap, input.uv, 0).r;
    input.positionOS.xyz += input.normalOS * h * _HeightScale;
}

void DoClip(Interpolators input)
{
    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a - _Cutoff);
}
```

### Adding Custom Hooks

Create a class inheriting `ShaderHookDefinition` with the `[ShaderHook]` attribute, then place a `{{MARKER}}` in your template:

```csharp
[ShaderHook]
public class MyCustomHook : ShaderHookDefinition
{
    public override string PragmaName => "myHook";
    public override string Define => "FS_MY_HOOK";
    public override string CallPattern => "{FuncName}(input);";
    public override string TemplateMarker => "MY_HOOK_CALL";
}
```

No pipeline code changes needed. The hook is discovered via TypeCache and works immediately.

## Pass Injectors

Each generated pass is defined by a `ShaderPassInjector` class that specifies its template and any material data it needs. Base passes are included in `[InjectBasePasses]`, feature passes are activated individually via `[InjectPass:Name]`.

### Adding Custom Passes

```csharp
[ShaderPass]
public class MyCustomPass : ShaderPassInjector
{
    public override string PassName => "MyCustom";
    public override string TemplateName => "MyCustom";  // loads Templates/MyCustom.hlsl
    public override bool IsBasePass => false;            // not in [InjectBasePasses]

    // Optional: declare properties this pass needs
    public override string GetPropertiesEntries(ShaderContext ctx)
    {
        if (ctx.PropertyExists("_MyProperty")) return null;
        return "_MyProperty(\"My Property\", Float) = 1";
    }

    // Optional: declare CBUFFER entries
    public override string GetCBufferEntries(ShaderContext ctx)
    {
        if (ctx.CBufferEntryExists("_MyProperty")) return null;
        return "float _MyProperty;";
    }
}
```

Then use `[InjectPass:MyCustom]` in your shader. Properties and CBUFFER entries are auto-injected.

## Tag Processors

Tag processors modify existing passes based on SubShader or pass-level tags. They handle things like tessellation where you need to rewrite the vertex entry point and inject hull/domain shaders into every pass.

### Tessellation

```hlsl
// SubShader level: applies to ALL passes
Tags { "Tessellation" = "On" }

// Or pass level: applies to THAT pass only
Pass { Tags { "Tessellation" = "On" } }
```

Tessellation automatically injects properties, CBUFFER entries, a custom material drawer, and hull/domain shaders. Supports multiple modes (Uniform, EdgeLength, Distance, EdgeDistance), Phong smoothing, frustum culling, and backface culling.

The tessellation system dynamically reads your Attributes struct and generates matching TessControlPoint structs. All fields are carried through with appropriate interpolation (normalize for normals/tangents, barycentric for everything else).

### Adding Custom Tag Processors

```csharp
[ShaderTagProcessor("MyFeature", priority: 100)]
public class MyFeatureProcessor : ShaderTagProcessorBase
{
    public override string TagName => "MyFeature";

    public override string GetPropertiesEntries(ShaderContext ctx) => /* ... */;
    public override string GetCBufferEntries(ShaderContext ctx) => /* ... */;
    public override void ModifyPass(ShaderContext ctx, PassInfo pass) { /* ... */ }
    public override Dictionary<string, string> GetPassReplacements(ShaderContext ctx, string passName) => /* ... */;
}
```

## Processing Pipeline

```
1. ShaderParser.Parse()           -- Extract structs, CBUFFER, textures, hooks, passes
2. PassInjectorRegistry.Collect() -- Gather material data from active pass injectors
3. TagProcessorRegistry.Process() -- Run tag processors (properties, CBUFFER, ModifyPass)
4. ProcessPassMarkers()           -- Generate and insert passes from [Inject] markers
5. Validate()                     -- Check for missing structs/semantics
```

Everything is discovered via TypeCache at editor startup. Hooks, passes, and tag processors are all registered automatically. The Docs window (Tools/ShaderProcessor/Docs) shows everything that's registered.

## File Structure

```
Editor/
├── Core/
│   ├── ShaderContext.cs           # Shared state during processing
│   ├── ShaderParser.cs            # Parses shader source
│   ├── ShaderProcessor.cs         # Main processing pipeline
│   └── FSShaderImporter.cs        # ScriptedImporter + inspector
├── Generation/
│   ├── TemplateEngine.cs          # Template loading and marker replacement
│   ├── StructGenerator.cs         # Generates pass-specific structs
│   └── HookProcessor.cs           # Extracts and rewrites hook functions
├── PassInjectors/
│   ├── ShaderPassInjector.cs      # Base class + [ShaderPass] attribute
│   ├── ShaderPassInjectorRegistry.cs  # Discovery + generation
│   └── BuiltInPasses.cs           # ShadowCaster, DepthOnly, DepthNormals, etc.
├── ShaderHooks/
│   ├── ShaderHookDefinition.cs    # Base class + built-in hooks
│   └── ShaderHookRegistry.cs      # TypeCache discovery
├── TagProcessors/
│   ├── ShaderTagProcessor.cs      # Interface, base class, registry
│   └── TessellationProcessor.cs   # Tessellation injection
├── MaterialDrawers/
│   └── TessellationParameters.cs  # Custom material inspector for tess settings
├── ShaderProcessorDocsWindow.cs   # Tools/ShaderProcessor/Docs editor window
└── Templates/
    ├── ShadowCaster.hlsl
    ├── DepthOnly.hlsl
    ├── DepthNormals.hlsl
    ├── MotionVectors.hlsl
    ├── Meta.hlsl
    ├── Outline.hlsl
    └── Tessellation.hlsl
```

## Notes

- The system uses Unity's ScriptedImporter, so shader changes trigger automatic reimport
- Generated passes appear in the imported shader asset, not in the source file
- Use "Show Processed Shader" in the inspector to see the full generated output
- Tag processors run in priority order (lower numbers first)
- Tessellation requires hardware support (DX11+, Metal, Vulkan)
- Hooks in HLSLINCLUDE are supported. The system extracts the body, rewrites struct names per pass, and prefixes function names to avoid collisions with the HLSLINCLUDE originals
