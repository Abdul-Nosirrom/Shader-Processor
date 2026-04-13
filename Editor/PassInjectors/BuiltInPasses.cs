using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FS.Shaders.Editor
{
    //=============================================================================
    // Base Passes (included in [InjectBasePasses])
    //=============================================================================
    
    /// <summary>
    /// ShadowCaster pass. Renders shadow map depth with shadow bias.
    /// Supports vertex displacement and alpha clip hooks.
    /// </summary>
    [ShaderPass]
    public class ShadowCasterPass : ShaderPassInjector
    {
        public override string PassName => "ShadowCaster";
        public override string TemplateName => "ShadowCaster";
        public override bool IsBasePass => true;

        // Template references input.{{NORMAL}} for shadow bias calculation.
        public override string[] GetAdditionalAttributeFields(ShaderContext ctx)
            => StructGenerator.EnsureAttributeField(ctx, "NORMAL", "float3", "normalOS");

        public override string GetFragmentReturnExpression(ShaderContext ctx, bool isFallback)
            => "return 0;";
    }
    
    /// <summary>
    /// DepthOnly pass. Writes depth for the depth prepass.
    /// Supports all standard hooks.
    /// </summary>
    [ShaderPass]
    public class DepthOnlyPass : ShaderPassInjector
    {
        public override string PassName => "DepthOnly";
        public override string TemplateName => "DepthOnly";
        public override bool IsBasePass => true;
        
        public override string GetFragmentReturnExpression(ShaderContext ctx, bool isFallback)
            => "return input.{{SV_POSITION}}.z;";
    }
    
    /// <summary>
    /// DepthNormals pass. Writes depth and world-space normals.
    /// Ensures normal fields exist in both Attributes and Interpolators structs,
    /// adding them if the user's structs don't already have them.
    /// </summary>
    [ShaderPass]
    public class DepthNormalsPass : ShaderPassInjector
    {
        public override string PassName => "DepthNormals";
        public override string TemplateName => "DepthNormals";
        public override bool IsBasePass => true;

        // Pass needs NORMAL semantic in Attributes for world-space normal calculation
        public override string[] GetAdditionalAttributeFields(ShaderContext ctx)
            => StructGenerator.EnsureAttributeField(ctx, "NORMAL", "float3", "normalOS");

        // Pass needs a normal field in Interpolators to output world-space normals
        public override string[] GetAdditionalInterpolatorFields(ShaderContext ctx)
            => StructGenerator.EnsureInterpolatorField(ctx, "float3", "normalWS", "NORMAL", "NORMALWS", "NORMAL_WS");
        
        public override Dictionary<string, string> GetAdditionalReplacements(ShaderContext ctx)
        {
            // If InjectForwardBody is active and the forward vertex body already writes
            // normalWS, skip the template's fallback assignment to avoid clobbering it.
            // If not, emit the geometric normal as a safe default.
            
            string normalField = ctx.Interpolators?.GetField("NORMAL")?.Name
                              ?? ctx.Interpolators?.GetField("NORMALWS")?.Name
                              ?? ctx.Interpolators?.GetField("NORMAL_WS")?.Name
                              ?? "normalWS";
            
            bool injectionWritesNormal = false;
            
            // Use HasFeature to match all truthy tag values (On, True, Enabled, 1, etc.)
            if (ctx.HasFeature("InjectForwardBody"))
            {
                // Check if the forward vertex function body assigns to the normalWS field.
                // Uses \w+ for the variable name since the raw body hasn't been normalized yet
                // the user may use 'output', 'o', 'result', etc.
                string vertexBody = ForwardBodyInjector.ExtractForwardVertexBody(ctx);
                if (vertexBody != null)
                {
                    // Normalize variable names to match what BuildVertexBody will produce,
                    // so we can reliably check for 'output.normalWS'
                    string userInputName = ForwardBodyInjector.DetectParameterName(
                        ctx.BuildSearchSource(ctx.ReferencePass?.HlslProgram),
                        ctx.ReferenceVertexFunctionName) ?? "input";
                    string userOutputName = ForwardBodyInjector.DetectOutputVariableName(
                        vertexBody, ctx.InterpolatorsStructName) ?? "output";
                    vertexBody = ForwardBodyInjector.NormalizeVariableNames(
                        vertexBody, userInputName, userOutputName);
    
                    injectionWritesNormal = Regex.IsMatch(vertexBody,
                        $@"\boutput\s*\.\s*{Regex.Escape(normalField)}\s*=");
                }
            }
            
            string normalAssign = injectionWritesNormal
                ? ""
                : $"output.{normalField} = TransformObjectToWorldNormal(input.{ctx.Attributes?.GetField("NORMAL")?.Name ?? "normalOS"});";
            
            return new Dictionary<string, string>
            {
                ["DEPTH_NORMALS_NORMAL_ASSIGNMENT"] = normalAssign
            };
        }
        
        public override string GetFragmentReturnExpression(ShaderContext ctx, bool isFallback)
            => isFallback
                ? "return half4(normalize(input.{{NORMAL_WS}}) * 0.5 + 0.5, input.{{SV_POSITION}}.z);"
                : "return half4(normalize({{output:normal}}) * 0.5 + 0.5, input.{{SV_POSITION}}.z);";
    }
    
    /// <summary>
    /// MotionVectors pass. Outputs per-pixel motion for temporal effects.
    /// Adds previous frame position fields to Attributes and clip-space
    /// position pairs to Interpolators.
    /// </summary>
    [ShaderPass]
    public class MotionVectorsPass : ShaderPassInjector
    {
        public override string PassName => "MotionVectors";
        public override string TemplateName => "MotionVectors";
        public override bool IsBasePass => true;

        // Previous frame vertex position (and optional Alembic velocity)
        public override string[] GetAdditionalAttributeFields(ShaderContext ctx) => new[]
        {
            "float3 positionOld : TEXCOORD4;",
            "#if defined(_ADD_PRECOMPUTED_VELOCITY)",
            "float3 alembic : TEXCOORD5;",
            "#endif"
        };

        // Current and previous clip-space positions for motion vector calculation
        public override string[] GetAdditionalInterpolatorFields(ShaderContext ctx) => new[]
        {
            "float4 curPositionCS : TEXCOORD8;",
            "float4 prevPositionCS : TEXCOORD9;"
        };
        
        // curPositionCS/prevPositionCS are always named this way (from struct override)
        public override string GetFragmentReturnExpression(ShaderContext ctx, bool isFallback)
            => @"
#ifdef LOD_FADE_CROSSFADE
        LODFadeCrossFade(input.{{SV_POSITION}});
#endif
        return float4(CalcNdcMotionVectorFromCsPositions(input.curPositionCS, input.prevPositionCS), 0, 0);";
    }
    
    /// <summary>
    /// Meta pass. Used for lightmap baking and GI.
    /// Adds uv1/uv2 fields to Attributes for lightmap coordinates.
    /// </summary>
    [ShaderPass]
    public class MetaPass : ShaderPassInjector
    {
        public override string PassName => "Meta";
        public override string TemplateName => "Meta";
        public override bool IsBasePass => true;

        // UnityMetaVertexPosition needs TEXCOORD1 (static lightmap UV) and
        // TEXCOORD2 (dynamic lightmap UV). Only added if not already present.
        public override string[] GetAdditionalAttributeFields(ShaderContext ctx)
        {
            var fields = new System.Collections.Generic.List<string>();

            if (ctx.Attributes?.HasField("TEXCOORD1") != true)
                fields.Add("float2 uv1 : TEXCOORD1;");

            if (ctx.Attributes?.HasField("TEXCOORD2") != true)
                fields.Add("float2 uv2 : TEXCOORD2;");

            return fields.Count > 0 ? fields.ToArray() : null;
        }
        
        public override string GetFragmentReturnExpression(ShaderContext ctx, bool isFallback)
        {
            if (isFallback)
            {
                return @"{
            MetaInput _mi = (MetaInput)0;
            _mi.Albedo = half3(1, 1, 1);
            _mi.Emission = half3(0, 0, 0);
            return UnityMetaFragment(_mi);
        }";
            }
            
            return @"{
            MetaInput _mi = (MetaInput)0;
            _mi.Albedo = {{output:albedo}}.rgb;
            _mi.Emission = {{output:emission}};
            return UnityMetaFragment(_mi);
        }";
        }
    }
    
    //=============================================================================
    // Feature Passes (activated by [InjectPass:Name], not included in base passes)
    //=============================================================================
    
    /// <summary>
    /// Outline pass. Renders inverted-hull outlines via front-face culling.
    /// Declares its own properties (_OutlineWidth, _OutlineColor) and CBUFFER entries.
    /// Activated by [InjectPass:Outline] in the shader.
    /// </summary>
    [ShaderPass]
    public class OutlinePass : ShaderPassInjector
    {
        public override string PassName => "Outline";
        public override string TemplateName => "Outline";
        public override bool IsBasePass => false;

        // Template references input.{{NORMAL}} for hull expansion.
        public override string[] GetAdditionalAttributeFields(ShaderContext ctx)
            => StructGenerator.EnsureAttributeField(ctx, "NORMAL", "float3", "normalOS");
        
        const string OutlineProperties = @"
        // Outlines (Auto-injected by FreeSkies)
        [SectionHeader(Outlines)]
        [Toggle(_ENABLE_OUTLINES)] _EnableOutlines(""Enable Outlines"", Float) = 0
        [ShowIf(_ENABLE_OUTLINES)] _OutlineWidth(""Outline Width"", Range(0,1)) = 0.1
        [ShowIf(_ENABLE_OUTLINES)] _OutlineColor(""Outline Color"", Color) = (0,0,0,1)";
        
        const string OutlineCBuffer = @"
        // Outlines (Auto-injected by FreeSkies)
        float _OutlineWidth;
        float4 _OutlineColor;";
        
        public override string GetPropertiesEntries(ShaderContext ctx)
        {
            if (ctx.PropertyExists("_OutlineWidth")) return null;
            return OutlineProperties;
        }
        
        public override string GetCBufferEntries(ShaderContext ctx)
        {
            if (ctx.CBufferEntryExists("_OutlineWidth")) return null;
            return OutlineCBuffer;
        }
        
        public override string GetFragmentReturnExpression(ShaderContext ctx, bool isFallback)
        {
            if (isFallback)
            {
                return @"
#ifdef _ENABLE_OUTLINES
        return _OutlineColor;
#else
        discard;
        return 0;
#endif";
            }
            
            return @"
#ifdef _ENABLE_OUTLINES
        return _OutlineColor * {{output:albedo}};
#else
        discard;
        return 0;
#endif";
        }
    }
}
