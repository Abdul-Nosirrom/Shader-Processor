# ShaderGen

A Unity URP shader preprocessing system that automatically generates auxiliary passes from a single authored pass. Write your Forward pass, hook in your custom logic, and let ShaderGen handle the boilerplate. The goal is to get the convenience of surface shaders while staying in normal HLSL.

## Features

- **Automatic Pass Generation**: Write your Forward pass once, get ShadowCaster, DepthOnly, DepthNormals, MotionVectors, and Meta passes generated automatically
- **Forward Body Injection**: Inject your entire vertex and fragment body into generated passes with no hooks needed. The system strips boilerplate, normalizes variable names, rewrites struct types, and swaps return statements per pass. Great for shaders where you just want everything to "work" without defining hooks
- **Hook System**: Define vertex displacement, interpolator transfer, alpha clip, and tessellation factor override functions that propagate to all generated passes
- **Shader Inheritance**: Child shaders inherit from a parent via `"Inherit" = "Parent/Name"`. Supports property overrides, pass exclusion, pass-level tag/render state overrides, HLSLINCLUDE appending, auto-generated CBUFFER/texture declarations for new properties, and InheritHook injection points for parent-defined extensibility
- **Pass Injectors**: Data-driven pass definitions. Each pass is a small class + a template. Adding a new pass type requires zero pipeline changes
- **Tag Processors**: Modular feature injection for things that modify existing passes (like tessellation). Supports Full and Pass scoping modes
- **Hardware Tessellation**: Full tessellation pipeline with multiple modes, phong smoothing, and culling. Injected per-pass via tags
- **Struct Preservation**: Works with any struct/function/variable naming. Your `Attributes`/`Interpolators` (or `VIn`/`VOut` or whatever you call them) are carried through correctly. Same goes for parameter names like `v`, `o`, `i` instead of `input`/`output`
- **HLSLINCLUDE Support**: Structs, CBUFFER, textures, hook pragmas, and hook function bodies can all live in HLSLINCLUDE and are handled correctly across all passes
- **Editor Tooling**: Inspector shows parsed shader info, active passes, active tag processors, and detected hooks. Docs window (Tools/ShaderProcessor/Docs) shows all registered hooks, passes, and processors
- **Automated Tests**: EditMode tests covering pass generation, hooks, tessellation, outlines, CBUFFER handling, forward body injection, variable name normalization, shader inheritance, and regression cases. Run via Window > General > Test Runner

## Installation

1. Open the `Package Manager` in Unity
2. Select `+` and `Install package from git URL...`
3. Input `https://github.com/Abdul-Nosirrom/Shader-Processor.git` as the URL and install, should show up as `com.abdulal.shaderprocessor`

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

## Forward Body Injection

For shaders where you want the generated passes to share the exact same vertex and fragment logic as your forward pass (interpolator transfers, displacement, normal mapping, alpha clip, etc.) without writing any hook functions, use the `InjectForwardBody` tag:

```hlsl
SubShader
{
    Tags
    {
        "RenderType" = "Opaque"
        "RenderPipeline" = "UniversalPipeline"
        "InjectForwardBody" = "On"
    }
    // ...
}
```

### What It Does

**Vertex:** The system extracts your forward vertex function body, strips the boilerplate that templates already handle (struct init, instance ID setup, positionCS assignment, return statement), normalizes variable names to `input`/`output` to match template conventions, rewrites struct type names, and injects the remainder via `{{FORWARD_VERTEX_BODY}}`. What survives is your interpolator assignments (normalWS, tangentWS, UVs, etc.) and any displacement or helper math they depend on. The HLSL compiler's dead code elimination will strip anything a particular pass doesn't actually use.

**Fragment:** The system extracts your forward fragment body, normalizes the input variable name to `input`, rewrites struct type names, and replaces every `return` statement with the pass-specific output. So `return color;` becomes `return input.positionCS.z;` in DepthOnly, `return 0;` in ShadowCaster, etc. Your `clip()` calls, texture samples, and intermediate calculations survive and do the right thing in each pass.

### fragmentOutput Pragmas

Some passes need to reference specific variables from your fragment body. For example, Meta needs your albedo and emission, and DepthNormals needs your computed normal. Declare these with `fragmentOutput` pragmas in your forward pass:

```hlsl
#pragma fragmentOutput:albedo albedo
#pragma fragmentOutput:normal computedNormal
#pragma fragmentOutput:emission emission
```

The left side (`albedo`, `normal`, `emission`) is a well-known name that pass injectors reference. The right side (`albedo`, `computedNormal`, `emission`) is the actual variable name in your fragment body. This lets the Meta pass build a `MetaInput` using your `albedo` and `emission` variables, and DepthNormals can return your `computedNormal` instead of falling back to the geometric normal.

If the pragmas aren't present, passes fall back to safe defaults (white albedo for Meta, geometric normal for DepthNormals, etc.).

### Variable Naming

You can use whatever variable names you want in your forward pass. The system detects your parameter and output variable names and normalizes them to `input`/`output` before injection. So this works fine:

```hlsl
Interpolators Vert(Attributes v)
{
    Interpolators o = (Interpolators)0;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    // ...
}

half4 Frag(Interpolators i) : SV_Target
{
    half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
    // ...
}
```

### Using Body Injection with Hooks

Forward body injection and hooks can coexist. If your forward pass defines hook functions (via `#pragma vertexDisplacement` etc.) AND has `InjectForwardBody` enabled, the system strips hook calls from the injected body to prevent double-execution. The templates already emit hook calls via their own markers, so things stay clean.

This is useful when you want body injection for interpolator transfers and fragment logic, but also want a `vertexDisplacement` hook for proper motion vector support (see Limitations below).

## Hooks

Hooks let you define functions that automatically propagate to all generated passes. Declare them with pragma directives and ShaderGen extracts the function body, rewrites struct names per generated pass, and injects it with the appropriate call.

Both the pragma and the function body can live in the pass or in HLSLINCLUDE. Either location works for both.

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

### Tag Modes

Tags support three modes that control how far the feature propagates:

| Mode | Values | Behavior |
|------|--------|----------|
| **Full** | `On`, `True`, `Full` | Applies to the declaring pass(es) AND all generated passes |
| **Pass** | `Pass` | Applies only to the declaring pass. Generated passes are not affected |
| **Off** | `Off`, `False`, or absent | Feature disabled |

"Full" is the default and matches the behavior of `On`/`True`. "Pass" is useful when you want a feature on a specific authored pass (like an aura effect with tessellation) without it bleeding into ShadowCaster, DepthOnly, etc.

```hlsl
// Full mode: tessellation on this pass AND all generated passes
Pass { Tags { "Tessellation" = "On" } }

// Pass mode: tessellation on this pass ONLY, generated passes are clean
Pass { Tags { "Tessellation" = "Pass" } }
```

When declared at the SubShader level, the mode applies to all authored passes. Properties and CBUFFER entries are always injected regardless of mode (the material data is shared), only the pass modification and generated pass replacements are scoped.

### Tessellation

```hlsl
// SubShader level: applies to ALL passes and generated passes
Tags { "Tessellation" = "On" }

// Pass level, full mode: applies to that pass and generated passes
Pass { Tags { "Tessellation" = "On" } }

// Pass level, pass-only mode: applies to that pass only
Pass { Tags { "Tessellation" = "Pass" } }
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

## Shader Inheritance

Inheritance lets you create child shaders that reuse a parent's code without duplication. The child declares `"Inherit" = "Parent/ShaderName"` in its SubShader tags and can override or extend anything about the parent.

### Basic Usage

The simplest child just inherits everything and adds a tag:

```hlsl
Shader "MyShader/Tessellated"
{
    SubShader
    {
        Tags
        {
            "Inherit" = "MyShader/Base"
            "Tessellation" = "On"
        }
    }
}
```

That's a complete shader. It inherits all Properties, HLSLINCLUDE, passes, and pass injection markers from the parent, then adds the Tessellation tag on top.

### Property Overrides

Child can redeclare properties to change defaults, ranges, or attributes. Matching is by `_PropertyName`. Properties that don't exist in the parent are appended.

```hlsl
Properties
{
    // Override parent's default and range
    _AlphaCutoff("Alpha Cutoff", Range(0.1, 0.9)) = 0.3
    // Add a new property
    _RimPower("Rim Power", Range(0, 10)) = 3.0
}
```

### Pass Exclusion

Remove parent passes you don't need via the `ExcludePasses` tag. Comma-separated, matched by pass Name.

```hlsl
Tags
{
    "Inherit" = "MyShader/Base"
    "ExcludePasses" = "Meta,ExtraPass"
}
```

### Pass Overrides

Child Pass blocks without an HLSLPROGRAM are treated as metadata-only overrides. They match the parent pass by Name and merge tags and render state (Cull, ZWrite, ZTest, Blend, ColorMask, Offset).

```hlsl
// Override the parent's Forward pass LightMode and culling
Pass
{
    Name "Forward"
    Tags { "LightMode" = "VFXForward" }
    Cull Off
}
```

If the child Pass block contains an HLSLPROGRAM, it's treated as a full pass (not an override) and gets added as-is.

### HLSLINCLUDE Append

Child can add code to the parent's HLSLINCLUDE block. Content is appended before the parent's ENDHLSL. Hook functions, utility functions, and additional includes all go here.

```hlsl
HLSLINCLUDE
// This gets appended to the parent's HLSLINCLUDE

void MyHelper() { /* ... */ }
ENDHLSL
```

### Property Declarations

When a child adds new properties (ones that don't exist in the parent), the system automatically generates the matching HLSL declarations. Scalar types (Float, Range, Int, Color, Vector) get a CBUFFER entry inserted before the parent's `CBUFFER_END`. Texture types (2D, 3D, Cube) get `TEXTURE2D`/`SAMPLER` declarations outside the CBUFFER, plus a `float4 _Name_ST` entry inside it for 2D textures.

This means you just declare the property in the child's Properties block and use it in hook functions. No manual CBUFFER management needed.

```hlsl
// Child shader
Properties
{
    _RimPower("Rim Power", Range(0, 10)) = 3.0    // auto-generates: float _RimPower;
    _RimColor("Rim Color", Color) = (1,1,1,1)     // auto-generates: float4 _RimColor;
    _RimTex("Rim Texture", 2D) = "white" {}       // auto-generates: TEXTURE2D + SAMPLER + _ST
}
```

Properties that override existing parent properties (matched by `_Name`) don't generate duplicate declarations since the parent's CBUFFER already has them.

### InheritHook Injection Points

Parents can define named injection points in their shader code. Children bind functions to those points via pragmas. This is the most powerful inheritance feature since it lets the parent define exactly where and how children can extend behavior.

**Parent shader:**
```hlsl
half4 Frag(Interpolators input) : SV_Target
{
    half4 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
    {{InheritHook:ModifyColor(inout half4 col, float3 normalWS)}}
    return col;
}
```

The marker `{{InheritHook:Name(signature)}}` declares a hook point. The signature lists the available parameters with their types and qualifiers (`inout` for writable parameters). When the parent compiles standalone, the marker is stripped (resolves to nothing).

**Child shader:**
```hlsl
HLSLINCLUDE
#pragma InheritHook ModifyColor ApplyRim

void ApplyRim(inout half4 col, float3 normalWS)
{
    float rim = 1.0 - saturate(dot(normalWS, float3(0, 0, 1)));
    col.rgb += pow(rim, _RimPower) * 0.5;
}
ENDHLSL
```

The `#pragma InheritHook HookName FunctionName` binds a function to the hook. During inheritance resolution, the marker is replaced with `ApplyRim(col, normalWS);` using the argument names extracted from the signature. The function definition is appended to the parent's HLSLINCLUDE so it's visible at the call site.

Multiple hooks can be defined in the same parent, and a child can bind to any subset of them. Unbound hooks just resolve to nothing.

### How It Works

Inheritance is a pre-processing step that runs before the main ShaderGen pipeline. The `ShaderInheritance.Resolve()` method:

1. Loads the parent source
2. Replaces the shader name
3. Finds new child properties (not in parent)
4. Merges child properties (override or append)
5. Injects HLSL declarations for new properties (CBUFFER + textures)
6. Merges SubShader tags
7. Removes excluded passes
8. Applies pass-level overrides (tags, render state)
9. Appends child HLSLINCLUDE (hook functions, includes)
10. Resolves InheritHook markers
11. Appends pass injection markers
12. Rewrites relative `#include` paths

The result is a complete shader source that the rest of the pipeline processes as if it were hand-authored. The importer registers a dependency on the parent, so the child reimports when the parent changes.

### Limitations

- Single-level only. The parent cannot itself use Inherit.
- Pass overrides match by Name only. Unnamed passes can't be overridden.
- Property overrides replace a single line. If the parent's property has multi-line attribute chains above it, those are preserved (the declaration line itself is replaced).

## Processing Pipeline

```
0. ShaderInheritance.Resolve()                          -- Pre-pass: merge parent + child if Inherit tag present
1. ShaderParser.Parse()                                 -- Extract structs, CBUFFER, textures, hooks, passes
2. ShaderPassInjectorRegistry.CollectMaterialEntries()  -- Gather material data from active pass injectors
3. ShaderTagProcessorRegistry.CollectTagProcessorEntries() -- Discover processors, collect material data
4. InjectProcessorProperties() + InjectProcessorCBuffer() -- Inject ALL accumulated material data into source
5. ReparseAllPasses() + ModifyTaggedPasses()            -- Reparse, then run ModifyPass (e.g., tessellation)
6. ProcessPassMarkers()                                 -- Generate and insert passes from [Inject] markers
7. StripInheritHookMarkers()                            -- Remove any remaining {{InheritHook:...}} markers
8. Validate()                                           -- Check for missing structs/semantics
```

Stages 2 and 3 both collect into the same fields (`ProcessorPropertiesEntries`, `ProcessorCBufferEntries`). Stage 4 injects everything in one shot, so pass injector and tag processor material data are treated uniformly.

Everything is discovered via TypeCache at editor startup. Hooks, passes, and tag processors are all registered automatically. The Docs window (Tools/ShaderProcessor/Docs) shows everything that's registered.

## Limitations and Things to Be Aware Of

Some things to keep in mind when writing shaders with this system. If something looks wrong in your generated output, check here first.

### Motion Vectors with Vertex Displacement (Forward Body Injection)

This is the main one. When using `InjectForwardBody`, the MotionVectors pass gets your vertex displacement code for the current frame (it's in the injected body), but the previous frame position (`prevPosOS`) does NOT get displaced. The MotionVectors template has a `#ifdef FS_VERTEX_DISPLACEMENT` block that replays displacement on the previous position, but that only works with the hook system since it needs to call the displacement function a second time on different data. Inline body code can't be "called" like that.

**What this means in practice:** If your shader displaces vertices and you have TAA or motion blur enabled, you might see ghosting or smearing artifacts on the displaced geometry. Static geometry and non-displaced shaders are fine.

**The fix:** If you need correct motion vectors with displacement, use a `vertexDisplacement` hook instead of (or alongside) body injection. The hook path properly replays displacement on the previous frame position. Body injection and hooks can coexist, the system strips hook calls from the injected body to prevent double-execution.

Note that this only matters for displacement that moves vertices around. If your forward pass just transfers interpolators (normals, UVs, tangent basis) without any displacement, motion vectors will be fine with body injection alone.

### Displacement That Depends on Computed Interpolators

If you're using the `vertexDisplacement` hook for proper motion vector support, keep in mind that the hook runs before the injected body in the template ordering. So your displacement function only has access to raw input data (positionOS, normalOS, raw uv, etc.). If your displacement needs data that the forward body computes (like transformed UVs from `TRANSFORM_TEX`), you'd need to duplicate that computation inside the hook function or restructure so the displacement only depends on raw input fields.

Most displacement (heightmap sampling at raw UVs, procedural offsets from world position, wave animation) works fine with raw inputs. This only comes up if your displacement samples a texture at a transformed UV or depends on some other intermediate calculation from the vertex body.

### Dead Code in Generated Fragment Shaders

The full forward fragment body gets injected into every auxiliary pass. That means DepthOnly and ShadowCaster fragments will compute normal mapping, lighting, emission, etc. even though they don't use any of it. Only the `clip()` call and the pass-specific return actually matter.

This is fine. The HLSL compiler's dead code elimination strips all of it. The generated output looks noisy but the compiled shader is clean. Same story for the fallback code block after the injected body's return statement, the compiler sees it's unreachable and removes it.

### Fragment Hook Calls in Injected Bodies

If your forward pass uses hook functions (like `#pragma alphaClip DoClip`) and also has `InjectForwardBody` enabled, the system strips hook calls from the injected body. This is correct because the templates already emit hook calls via their own markers (`{{ALPHA_CLIP_CALL}}` etc.), so leaving them in the body would execute the hook twice.

If you see a hook being called twice in generated output, something went wrong with the stripping. Check that your hook function name matches what's in the pragma declaration.

### Variable Naming is Normalized

The system normalizes your vertex input/output parameter names to `input`/`output` and your fragment input parameter name to `input` before injection. This means if you use `v`, `o`, `i`, or any other names, they'll be rewritten in the generated passes. Your original forward pass is untouched.

This is usually invisible, but if you're reading the processed shader output (via "Show Processed Shader" in the inspector) and wondering why your variables changed names in the generated passes, that's why.

### DepthNormals Normal Assignment

The DepthNormals pass needs a world-space normal for the depth-normals buffer. When `InjectForwardBody` is active and your forward vertex body already writes `output.normalWS` (or whatever your normal field is called), the template skips its own geometric normal fallback to avoid clobbering your value. If your forward vertex body doesn't write normals, the template emits `TransformObjectToWorldNormal` as a fallback so the pass still works.

If you're getting flat/faceted normals in your depth normals buffer when you expect smooth or normal-mapped normals, check that your forward vertex body is actually assigning to the normalWS interpolator. If you're using the `fragmentOutput:normal` pragma, the DepthNormals fragment will use your computed normal-mapped normal from the fragment body instead of the vertex-interpolated geometric normal, which is usually what you want.

### fragmentOutput Pragmas Map to a Single Variable

Each `fragmentOutput` pragma maps a well-known name to exactly one variable in your fragment body. When the system swaps return statements for a generated pass, it references that single variable in every return path.

This means if your fragment has multiple variables that represent the same conceptual output and you return them conditionally, only the declared one will be used:
```hlsl
#pragma fragmentOutput:normal computedNormal

half4 Frag(Interpolators input) : SV_Target
{
    half3 computedNormal = /* ... */;
    half3 otherNormal = /* ... */;

    if (conditionA)
        return half4(computedNormal, 1);  // DepthNormals will use computedNormal here (correct)
    else
        return half4(otherNormal, 1);     // DepthNormals will STILL use computedNormal here (not otherNormal)
}
```

The DepthNormals pass replaces every `return` with `return half4(normalize(computedNormal) * 0.5 + 0.5, depth)` regardless of which branch you're in. It doesn't analyze which variable each return was originally using.

This applies to any pass whose return expression references a `fragmentOutput` variable (primarily Meta and DepthNormals). Passes with trivial returns like DepthOnly (`return depth`) and ShadowCaster (`return 0`) are unaffected since they don't reference any fragment variables.

If you need different normals returned conditionally, compute a single final normal before the return and declare that as your `fragmentOutput:normal`.

### Template Caching

Templates are cached in memory after first load. If you edit a `.hlsl` template file, the changes won't take effect until you either reimport a shader or use `Tools/ShaderProcessor/Reload Templates`. This is usually only relevant if you're developing new passes or modifying the templates themselves.

## Testing

The package includes 79 EditMode tests that run via the Unity Test Runner (Window > General > Test Runner, EditMode tab). Tests call `ShaderProcessor.Process()` on test shader files and assert properties of the output string. No GPU or rendering needed.

Tests cover pass generation, struct naming, CBUFFER duplication logic, hook function prefixing, tessellation injection, outline property injection, individual vs bulk pass injection, template marker cleanup, tag mode scoping, forward body injection, variable name normalization, fragmentOutput pragma resolution, and various regressions found during development.

## File Structure

```
Editor/
├── Core/
│   ├── ShaderContext.cs           # Shared state during processing
│   ├── ShaderParser.cs            # Parses shader source
│   ├── ShaderProcessor.cs         # Main processing pipeline
│   ├── ShaderInheritance.cs       # Inheritance pre-pass (Inherit tag resolution)
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
│   ├── TessellationProcessor.cs   # Tessellation injection
│   └── ForwardBodyInjector.cs     # Forward body injection tag processor
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
Tests/
├── FS.ShaderProcessor.Tests.asmdef
├── ShaderProcessorTests.cs
├── Test01 through Test19 shader files
```

## Notes

- The system uses Unity's ScriptedImporter, so shader changes trigger automatic reimport
- Generated passes appear in the imported shader asset, not in the source file
- Use "Show Processed Shader" in the inspector to see the full generated output
- Tag processors run in priority order (lower numbers first)
- Tessellation requires hardware support (DX11+, Metal, Vulkan)
- Hooks in HLSLINCLUDE are supported. The system extracts the body, rewrites struct names per pass, and prefixes function names to avoid collisions with the HLSLINCLUDE originals
- Hook pragmas can be declared in either the pass or HLSLINCLUDE
- Forward body injection runs at priority 200 (after tessellation at 100), so tessellation markers are available when the body gets injected
