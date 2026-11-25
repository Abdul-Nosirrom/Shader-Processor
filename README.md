# ShaderGen

A Unity URP shader preprocessing system that automatically generates auxiliary passes from a single authored pass. Write your Forward pass, hook in your custom logic, and let ShaderGen handle the boilerplate. The goal is to get the convenience of surface shaders while staying in normal HLSL.

## Features

- **Automatic Pass Generation**: Write your Forward pass once, get ShadowCaster, DepthOnly, DepthNormals, MotionVectors, and Meta passes generated automatically
- **Forward Body Injection**: Inject your entire vertex and fragment body into generated passes with no hooks needed. The system strips boilerplate, normalizes variable names, rewrites struct types, and swaps return statements per pass. Great for shaders where you just want everything to "work" without defining hooks
- **Hook System**: Define vertex displacement, interpolator transfer, alpha clip, and tessellation factor override functions that propagate to all generated passes
- **Shader Inheritance**: Child shaders inherit from a parent via `"Inherit" = "Parent/Name"`. Supports property overrides, pass exclusion, pass-level tag/render state overrides, full pass additions, HLSLINCLUDE appending, auto-generated CBUFFER/texture declarations for new properties, and InheritHook injection points for parent-defined extensibility
- **Per-Pass Keywords**: Generated passes define `SHADERGEN_GENERATED_PASS`, `SHADERGEN_BASE_PASSES` and `SHADERGEN_PASS_<NAME>`, so forward-pass pragmas can be gated per pass. Each template brings its own pipeline keyword set, and the forward lighting keywords stay out of shadow and depth passes
- **Pass Injectors**: Data-driven pass definitions. Each pass is a small class + a template. Adding a new pass type requires zero pipeline changes
- **Tag Processors**: Modular feature injection for things that modify existing passes (like tessellation). Supports Full and Pass scoping modes
- **Hardware Tessellation**: Full tessellation pipeline with multiple modes, phong smoothing, and culling. Injected per-pass via tags
- **Struct Preservation**: Works with any struct/function/variable naming. Your `Attributes`/`Interpolators` (or `VIn`/`VOut` or whatever you call them) are carried through correctly. Same goes for parameter names like `v`, `o`, `i` instead of `input`/`output`
- **HLSLINCLUDE and Include Support**: Structs, CBUFFER, textures, hook pragmas, and hook function bodies can all live in HLSLINCLUDE and are handled correctly across all passes. Structs, the vertex and fragment functions, and hook function bodies can also live in a file that HLSLINCLUDE or the forward pass `#include`s. Texture and sampler declarations reach generated passes exactly as written, wherever they are (see [Material Data in Generated Passes](#material-data-in-generated-passes))
- **Editor Tooling**: Inspector shows parsed shader info, active passes, active tag processors, and detected hooks. Its Compiler Messages section lists the shader's compile errors and warnings like Unity's Shader inspector does, but pointed back at where each came from: your file and line (or the inherited parent's), or the generated pass it sits in. Double-click opens your file at the line, or a copy of the processed shader at the line for generated passes. The summary line splits counts into your code and generated code, so errors only in generated passes point at the processor. Compile All Passes compiles the default variant of every pass so its messages show up. Docs window (Tools/ShaderProcessor/Docs) shows all registered hooks, passes, and processors
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

If the pragmas aren't present, passes fall back to safe defaults (white albedo for Meta, geometric normal for DepthNormals, etc.). The fallback is per pass and all or nothing: Meta uses both `albedo` and `emission`, so with only `fragmentOutput:albedo` it still bakes plain white. A surface with no emission declares a zero variable for it (`half3 emission = 0;`).

ShaderGen blanks these pragma lines in your forward pass after it has read them, since Unity doesn't know the directive and would warn about it. The line stays, empty, so Compiler Messages line numbers still match your file.

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

Both the pragma and the function body can live in the pass or in HLSLINCLUDE. Either location works for both. The function body can also live in a file that either one `#include`s.

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

ShadowCaster, DepthOnly, DepthNormals and MotionVectors cull the same way as the forward pass. They copy its `Cull` line (`Cull Off`, `Cull [_Cull]`, ...). If the forward pass has no `Cull` line, they don't get one either, and they inherit the SubShader's `Cull` (or Unity's default, Back) the same way the forward pass does. Meta always uses `Cull Off` and Outline always uses `Cull Front`, because their culling is part of how they work.

ShadowCaster, DepthOnly, DepthNormals and MotionVectors apply LOD crossfade (`LODFadeCrossFade` under `LOD_FADE_CROSSFADE`) right before they return, after alpha clipping, the same as URP's own passes. That way an object fading between LODs dithers its shadow and depth prepass along with its forward pass. With `InjectForwardBody`, a forward fragment that already applies its own crossfade ends up clipping twice in the generated passes. The second clip removes the same pixels, so it only costs one extra texture sample in the crossfade variant.

The Meta pass also fills the data the Scene view's lighting draw modes (Baked Lightmap, Albedo, ...) read under `EDITOR_VISUALIZATION`, like URP's Meta pass does.

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

A custom template declares its own pipeline keywords, the way the built-in templates do (see [Keyword Pragmas and Pass Defines](#keyword-pragmas-and-pass-defines)). If it's an ordinary draw, include `Shaders/Keywords/DOTSInstancing.hlsl` with `#include_with_pragmas` for instancing, and add `LOD_FADE_CROSSFADE` if it crossfades. It gets the pass defines for free as long as it uses `{{FORWARD_PREPROCESSOR}}`.

## Material Data in Generated Passes

Generated passes need the same material data your forward pass uses. ShaderGen treats the two kinds differently:

- **The CBUFFER is rebuilt.** ShaderGen lifts your `UnityPerMaterial` block out of the forward pass and writes it at `{{CBUFFER}}` in each generated pass, adding the entries that pass injectors and tag processors declare (such as Outline's `_OutlineWidth`). Every pass ends up with the same layout, which the SRP Batcher needs. A CBUFFER in `HLSLINCLUDE` or in an included file isn't rebuilt; it reaches every pass from there.
- **Textures and samplers are copied as written.** Nothing adds textures to a generated pass, so there's nothing to rebuild. Declarations inline in the forward pass travel with the rest of its code into `{{FORWARD_CONTENT}}`, in their original order and under the same `#if` they were written under. Declarations in `HLSLINCLUDE` reach every pass because Unity puts `HLSLINCLUDE` in front of each one, and declarations in a file the forward pass includes come with the copied `#include` line. Any form works: `TEXTURE2D`, `TEXTURE2D_ARRAY`, `TEXTURE2D_X`, raw `Texture2D` and `SamplerState`, a texture with no sampler of its own, or your own declaration macros.

How to think about it: if the forward pass compiles, its textures are declared somewhere every generated pass also sees. A custom template gets them through `{{FORWARD_CONTENT}}`, the same marker that brings your helper functions, so a template that copies the forward pass's code has its textures too.

```hlsl
TEXTURE2D(_LayerAlbedo);    // own sampler not needed: sampled with an inline sampler below
TEXTURE2D(_LayerNormal);
#ifdef _DETAIL_MAP
TEXTURE2D(_DetailMap);      // keeps its #ifdef in every generated pass
SAMPLER(sampler_DetailMap);
#endif

half3 SampleLayer(float2 uv)
{
    return SAMPLE_TEXTURE2D(_LayerAlbedo, sampler_TrilinearRepeat, uv).rgb;
}
```

**Sharing a sampler across textures.** Unity binds a sampler named after a texture (`sampler_X`) by finding texture `X` in the same compiled pass and taking its filter and wrap settings. If a pass reads the sampler but never reads `X`, the compiler drops `X` and the pass fails with "Unrecognized sampler 'sampler_x'". With `InjectForwardBody` this happens easily: DepthNormals keeps only what feeds the normal, so a normal map sampled with its albedo's sampler breaks there while the forward pass is fine. Sample each texture with its own sampler, or share an inline sampler state such as `sampler_TrilinearRepeat` or `sampler_LinearRepeat` (declared by URP's `Core.hlsl`), which isn't tied to any texture. Inline samplers ignore the textures' import filter settings. A declared sampler that nothing uses is harmless.

## SRP Batcher and GPU Resident Drawer

The SRP Batcher keeps each material's `UnityPerMaterial` buffer on the GPU and only swaps per-object data between draws. It batches by shader variant, so different materials using the same variant still batch. The GPU Resident Drawer builds on the SRP Batcher: it draws compatible Mesh Renderers through the BatchRendererGroup API with GPU instancing and, optionally, GPU occlusion culling. Both decide per shader and per renderer whether they can take a draw. Anything they can't take falls back to the ordinary path, with no error in most cases.

ShaderGen guarantees one part of compatibility: every generated pass gets the same `UnityPerMaterial` layout as the forward pass (see [Material Data in Generated Passes](#material-data-in-generated-passes)). The rest is up to the shader and the scene:

- **Every `UnityPerMaterial` variable needs a Properties entry.** Unity builds a material's buffer from the material's property values, so a CBUFFER variable with no matching property can't be filled. That takes the shader off both the SRP Batcher and the GPU Resident Drawer, while the shader inspector still reports "SRP Batcher: compatible". It applies to values you only ever set from script or a MaterialPropertyBlock: give them a `[HideInInspector]` property. A texture property's `_ST`, `_TexelSize` and `_HDR` variables count as declared.
- **Defaults belong on the property.** HLSL ignores initializers on constant buffer members (`float _X = -100;` reads 0 when nothing sets it), so put the default on the Properties line.
- **No matrices or arrays in `UnityPerMaterial`.** No property type exists for them. Split a matrix into four `Vector` properties and rebuild it in the shader, use one `Vector` per element for a small array, or move the data to a global (`Shader.SetGlobalMatrix`), a texture or a buffer.
- **A renderer with a MaterialPropertyBlock drops out of both.** For per-renderer values, `Renderer.SetShaderUserValue` (read as `unity_RendererUserValue`) works with both. Under the GPU Resident Drawer it becomes a per-instance value automatically.
- **Samplers:** a sampler named after a texture breaks in any pass that doesn't read that texture (see the sampler note above).

```hlsl
Properties
{
    _BaseColor("Base Color", Color) = (1, 1, 1, 1)
    // Set from script on hit. Declared so the SRP Batcher can build the material buffer.
    [HideInInspector] _HitFlashTime("Hit Flash Time", Float) = -100
}
...
CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
    float _HitFlashTime;   // default comes from the property above, not from an initializer here
CBUFFER_END
```

**GPU Resident Drawer specifics.** Shaders need `DOTS_INSTANCING_ON` variants. Every shader that includes `ForwardKeywords.hlsl` or a pass keyword file gets them through `DOTSInstancing.hlsl`, which pulls in URP's `DOTS.hlsl`. Builds only keep those variants when Graphics settings > **BatchRendererGroup Variants** is **Keep All**. That setting is required for the GPU Resident Drawer, and it adds a `DOTS_INSTANCING_ON` copy of every variant to builds, so set it back to "Strip if Entities Graphics is not installed" if you don't use the GPU Resident Drawer. Static batching takes static meshes before the GPU Resident Drawer can, so turn it off when using it. Skinned meshes, objects under an Animator and renderers with a MaterialPropertyBlock are never drawn by it.

**How to check.** In the Frame Debugger, SRP Batcher draws sit under `RenderLoopNewBatcher.Draw` as **SRP Batch** events, and a batch only breaks when the shader variant changes. GPU Resident Drawer draws show as **Hybrid Batch Group**. A draw under `RenderLoop.Draw`, or a break cause such as "Objects have different materials", went down the ordinary path, and "SRP: Node is not compatible with SRP batcher" means the SRP Batcher looked at the draw and rejected it. The Game view Stats window's Draw Breakdowns give the totals per path.

## Keyword Pragmas and Pass Defines

Every `#pragma`, `#include` and `#define` in the forward pass is copied into every generated pass, keeping any `#if` they sit under. That's what keeps keyword-gated code compiling: if your forward pass clips under `#ifdef _ALPHATEST_ON`, the ShadowCaster that runs the same clip needs `_ALPHATEST_ON` declared too.

Not every keyword belongs everywhere, though. Lighting, GI, decal, fog and debug keywords change nothing in a shadow or depth pass, but each one multiplies that pass's variants. So each generated pass gets a few defines placed ahead of the copied lines, and pragmas can be gated on them with ordinary preprocessor code:

| Define | Defined in |
|---|---|
| `SHADERGEN_GENERATED_PASS` | Every generated pass. Never the forward pass. |
| `SHADERGEN_BASE_PASSES` | The passes `[InjectBasePasses]` generates (injectors with `IsBasePass`), whether they came from that marker or from `[InjectPass:X]`. |
| `SHADERGEN_PASS_<NAME>` | One pass. The name is the injector's `PassName` uppercased, with anything that isn't a letter or digit turned into `_`: `SHADERGEN_PASS_SHADOWCASTER`, `SHADERGEN_PASS_DEPTHONLY`, `SHADERGEN_PASS_DEPTHNORMALS`, `SHADERGEN_PASS_MOTIONVECTORS`, `SHADERGEN_PASS_META`, `SHADERGEN_PASS_OUTLINE`, or `SHADERGEN_PASS_MYCUSTOM` for a custom pass. |

How to think about it: an untagged pragma goes to every pass, so material keywords keep working in shadows and depth without you doing anything. You only write a gate when a keyword can't matter in some pass. "Forward only" is `!defined(SHADERGEN_GENERATED_PASS)`, and a new pass added later is automatically excluded from it. Use the per-pass define to let specific passes back in.

```hlsl
// Every pass: the injected body clips in ShadowCaster and DepthOnly too
#pragma shader_feature_local_fragment _ALPHATEST_ON

// Forward only: colour, no effect on depth or shadows
#if !defined(SHADERGEN_GENERATED_PASS)
#pragma shader_feature_local_fragment _RIM
#endif

// Forward plus DepthNormals, which writes the normal-mapped normal (fragmentOutput:normal)
#if !defined(SHADERGEN_GENERATED_PASS) || defined(SHADERGEN_PASS_DEPTHNORMALS)
#pragma shader_feature_local _NORMALMAP
#endif
```

This is the same mechanism URP uses (Shader Graph defines `HAVE_VFX_MODIFICATION` and `DOTS.hlsl` skips its pragma under it). Unity's pragma scanner only sees a `#define` that comes before the pragma, which is why the defines lead the copied block. They work in code too, the way BiRP surface shaders exposed `UNITY_PASS_SHADOWCASTER`: an injected forward body can skip work under `#ifdef SHADERGEN_PASS_SHADOWCASTER`.

**The keyword files gate themselves.** `ForwardKeywords.hlsl` and the files it pulls in (`CommonKeywords`, `LightingKeywords`, `BakedGIKeywords`, `DecalKeywords`) are wrapped in `#ifndef SHADERGEN_GENERATED_PASS`, so including `ForwardKeywords.hlsl` in the forward pass adds nothing to generated passes. Instead each template includes its own pass's keyword file: `ShadowCasterKeywords.hlsl` (punctual shadows, LOD crossfade, instancing), `DepthOnlyKeywords.hlsl`, `DepthNormalsKeywords.hlsl`, `MotionVectorsKeywords.hlsl` (also Alembic velocity) and `MetaKeywords.hlsl` (editor visualisation). Outline includes `DOTSInstancing.hlsl`. If you write an ungated pragma the template already declares, such as `multi_compile_instancing`, it's declared twice in generated passes. Gate it on `SHADERGEN_GENERATED_PASS` or use the keyword files.

**Where gates don't work:**

- `HLSLINCLUDE`. Unity places it ahead of every pass's program, before the pass defines, so pragmas there reach every pass. Put pragmas you want to gate in the forward pass.
- Inheritance. Gates in the parent's forward pass carry into the child unchanged. Pragmas a child appends to `HLSLINCLUDE` follow the rule above. Full passes a child adds are hand-written, so they get none of the defines.

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
[ShaderTagProcessor]
public class MyFeatureProcessor : ShaderTagProcessorBase
{
    public override string TagName => "MyFeature";
    public override int Priority => 100;

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

If the child Pass block contains an HLSLPROGRAM, it's treated as a full pass (not an override) and gets appended to the parent's SubShader, before any pass injection markers. If the child pass has the same Name as a parent pass you get a warning and both end up in the shader - add the parent's pass to `ExcludePasses` when the child is meant to replace it.

### HLSLINCLUDE Append

Child can add code to the parent's HLSLINCLUDE block. Content is appended before the parent's ENDHLSL. Hook functions, utility functions, and additional includes all go here.

```hlsl
HLSLINCLUDE
// This gets appended to the parent's HLSLINCLUDE

void MyHelper() { /* ... */ }
ENDHLSL
```

### Property Declarations

When a child adds new properties (ones that don't exist in the parent), the system automatically generates the matching HLSL declarations. Scalar types (Float, Range, Int, Color, Vector) get a CBUFFER entry inserted before the `CBUFFER_END` of every `CBUFFER_START(UnityPerMaterial)` block in the parent. Texture types (2D, 3D, Cube) get `TEXTURE2D`/`SAMPLER` declarations right after each of those blocks, plus a `float4 _Name_ST` entry inside them for 2D textures. A parent that declares its material CBUFFER once in `HLSLINCLUDE` gets the entries once; a parent that declares it in every pass gets them in every pass, which keeps the layouts identical for the SRP batcher. Other CBUFFERs and commented-out ones are left alone.

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

1. Loads the parent source, and rewrites the child's relative `#include` paths against the child's own folder
2. Replaces the shader name
3. Finds new child properties (not in parent)
4. Merges child properties (override or append)
5. Injects HLSL declarations for new properties (CBUFFER + textures)
6. Merges SubShader tags
7. Removes excluded passes
8. Applies pass-level overrides (tags, render state)
9. Appends child full passes (Pass blocks with an HLSLPROGRAM)
10. Appends child HLSLINCLUDE (hook functions, includes)
11. Resolves InheritHook markers
12. Appends pass injection markers
13. Rewrites the parent's relative `#include` paths against the parent's folder

Relative paths here means paths starting with `../`. They're turned into project paths (`Assets/...` or `Packages/...`), so they resolve no matter where the child shader lives.

The result is a complete shader source that the rest of the pipeline processes as if it were hand-authored. The importer registers a dependency on the parent, so the child reimports when the parent changes.

### Limitations

- Single-level only. The parent cannot itself use Inherit.
- Pass overrides match by Name only. Unnamed passes can't be overridden.
- Property overrides replace a single line. If the parent's property has multi-line attribute chains above it, those are preserved (the declaration line itself is replaced).

## Processing Pipeline

```
0. ShaderInheritance.Resolve()                          -- Pre-pass: merge parent + child if Inherit tag present
1. ShaderParser.Parse()                                 -- Extract structs, CBUFFER, hooks, passes
2. ShaderPassInjectorRegistry.CollectMaterialEntries()  -- Gather material data from active pass injectors
3. ShaderTagProcessorRegistry.CollectTagProcessorEntries() -- Discover processors, collect material data
4. InjectProcessorProperties() + InjectProcessorCBuffer() -- Inject ALL accumulated material data into source
5. ReparseAllPasses() + ModifyTaggedPasses()            -- Reparse, then run ModifyPass (e.g., tessellation)
6. ProcessPassMarkers()                                 -- Generate and insert passes from [Inject] markers
7. BlankFragmentOutputs() + StripInheritHookMarkers()   -- Blank fragmentOutput pragmas in your passes, remove remaining {{InheritHook:...}} markers
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

The generated pass writes the signed world normal with `w = 0`, the same as URP's own DepthNormals passes: `half4(NormalizeNormalPerPixel(normalWS), 0.0)`. URP renders `_CameraNormalsTexture` into a signed (SNorm) target and `SampleSceneNormals` returns it as is, so anything reading the normals texture (edge detection, SSR, decals) sees ShaderGen shaders and URP Lit the same way. Don't remap to `* 0.5 + 0.5`. URP's octahedral encoding (`_GBUFFER_NORMALS_OCT`) is only used by the deferred renderer with Accurate G-buffer Normals on, and the generated pass doesn't support it.

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

The DepthNormals pass replaces every `return` with `return half4(NormalizeNormalPerPixel(computedNormal), 0.0)` regardless of which branch you're in. It doesn't analyze which variable each return was originally using.

This applies to any pass whose return expression references a `fragmentOutput` variable (primarily Meta and DepthNormals). Passes with trivial returns like DepthOnly (`return depth`) and ShadowCaster (`return 0`) are unaffected since they don't reference any fragment variables.

If you need different normals returned conditionally, compute a single final normal before the return and declare that as your `fragmentOutput:normal`.

### Template Caching

Templates are cached in memory per import and every template file is registered as an import dependency, so editing a `.hlsl` template automatically reimports the shaders that go through the processor. `Tools/ShaderProcessor/Reload Templates` still exists to clear the in-memory cache manually. This is usually only relevant if you're developing new passes or modifying the templates themselves.

### Single SubShader Only

The parser, tag merging, and pass injection markers all assume one SubShader per shader. A second SubShader (e.g. a LOD fallback) won't have its markers replaced and its tags are ignored.

### Reserved Interpolator Semantics

Generated passes claim a few TEXCOORD slots for their own fields: MotionVectors uses TEXCOORD4/TEXCOORD5 (previous position, Alembic velocity) and TEXCOORD8/TEXCOORD9 (clip-space position pair), Meta uses TEXCOORD10/TEXCOORD11 (editor visualisation UV and light coordinate, only in its `EDITOR_VISUALIZATION` variant), and the tessellation factor override uses TEXCOORD7 in the control point struct. If your Attributes/Interpolators structs use those semantics, the generated passes will fail with duplicate-semantic errors.

### Untagged Keyword Pragmas Reach Every Pass

A forward-pass `multi_compile` or `shader_feature` with no gate is compiled into every generated pass as well, multiplying their variants. That's the safe default for material keywords, but a keyword that only affects colour is cheaper behind `#if !defined(SHADERGEN_GENERATED_PASS)`. See [Keyword Pragmas and Pass Defines](#keyword-pragmas-and-pass-defines).

### Variable Renames Are Whole-Word, Not Scope-Aware

Body injection renames your vertex/fragment parameter and output variable names to `input`/`output` with word-boundary matching. A local variable that shares a name with the parameter (e.g. parameter `i` plus a `for (int i ...)` loop) gets renamed too and breaks the generated pass. Avoid reusing the parameter name for locals.

## Testing

The package includes an EditMode test suite that runs via the Unity Test Runner (Window > General > Test Runner, EditMode tab). Most tests call `ShaderProcessor.Process()` on test shader files and assert properties of the output string. A separate compile test enumerates every test shader, processes it, and compiles the result via `ShaderUtil.CreateShaderAsset` with initial variants enabled - this validates ShaderLab plus the default-keyword HLSL (keyword-gated variants like `_TESSELLATION` only compile when a material enables them).

Tests cover pass generation, struct naming, CBUFFER duplication logic, hook function prefixing, tessellation injection, outline property injection, individual vs bulk pass injection, template marker cleanup, tag mode scoping, forward body injection, variable name normalization, fragmentOutput pragma resolution, shader inheritance, and various regressions found during development.

## File Structure

```
Editor/
├── Core/
│   ├── ShaderContext.cs           # Shared state during processing
│   ├── ShaderParser.cs            # Parses shader source
│   ├── ShaderProcessor.cs         # Main processing pipeline
│   ├── ShaderInheritance.cs       # Inheritance pre-pass (Inherit tag resolution)
│   ├── ShaderBlockUtility.cs      # CBUFFER/Properties/Pass block finding + comment detection
│   ├── ShaderFunctionUtility.cs   # HLSL function find/extract/remove
│   ├── ShaderIncludeResolver.cs   # #include flattening for parsing
│   ├── ShaderPragmaUtility.cs     # #pragma reading and stripping
│   ├── ShaderGenLog.cs            # Verbose logging gate (Tools/ShaderProcessor/Verbose Logging)
│   ├── FSShaderImporter.cs        # ScriptedImporter + importer override
│   ├── FSShaderImporterEditor.cs  # Importer inspector, incl. compiler messages
│   └── ProcessedShaderWindow.cs   # "Show Processed Shader" window
├── Diagnostics/
│   ├── LineDiff.cs                # Line diff used to map processed lines back
│   └── ShaderSourceMap.cs         # Processed line -> your file/line or generated pass
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
Shaders/                           # HLSL helpers for hand-written forward passes, included by path
├── Common.hlsl                    #   Vertex data, normal mapping, shadow coords, LOD crossfade helpers
├── DebugSupport.hlsl              #   Rendering Debugger hookup (CanDebugOverrideOutputColor)
├── Lighting.hlsl                  #   ALL_LIGHTS_LOOP_BEGIN/END: main + additional lights, Forward and Forward+
└── Keywords/                      #   Keyword sets per pass; ForwardKeywords.hlsl is the full forward set
Tests/
├── FS.ShaderProcessor.Tests.asmdef
├── ShaderProcessorTests.cs
├── Test01 through Test21 shader files
```

## Notes

- The system uses Unity's ScriptedImporter, so shader changes trigger automatic reimport
- Generated passes appear in the imported shader asset, not in the source file
- Use "Show Processed Shader" in the inspector to see the full generated output
- Tag processors run in priority order (lower numbers first)
- Tessellation requires hardware support (DX11+, Metal, Vulkan)
- Hooks in HLSLINCLUDE are supported. The system extracts the body, rewrites struct names per pass, and prefixes function names to avoid collisions with the HLSLINCLUDE originals. Prefixing applies to generated passes only - authored passes call your functions under their original names
- Hook pragmas can be declared in either the pass or HLSLINCLUDE. Commented-out pragmas are ignored
- `#include`s in HLSLINCLUDE and in each pass are read so the processor can find structs, functions and hooks defined in those files. The output keeps the `#include` lines as written and the compiler resolves them as usual. A CBUFFER or texture declarations in a file the forward pass includes are not copied into generated passes: those passes get the forward pass's `#include` lines, so the file brings them along. Those copied `#include` lines sit above the generated pass's CBUFFER and structs, so a file the forward pass includes has to get the structs, CBUFFER and textures it uses from HLSLINCLUDE or an earlier include, not from code written inline in the pass. Unity package includes (`Packages/com.unity.*`) are skipped, and so is a path that can't be found from the shader's folder
- Forward body injection runs at priority 200 (after tessellation at priority 5), so tessellation markers are available when the body gets injected
- Per-import progress logs are off by default; toggle them via `Tools/ShaderProcessor/Verbose Logging`
