using System.Collections.Generic;

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
        
        public override Dictionary<string, string> GetStructOverrides(ShaderContext ctx)
        {
            return new Dictionary<string, string>
            {
                ["ATTRIBUTES_STRUCT"] = StructGenerator.GenerateDepthNormalsAttributes(ctx),
                ["INTERPOLATORS_STRUCT"] = StructGenerator.GenerateDepthNormalsInterpolators(ctx)
            };
        }
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
        
        public override Dictionary<string, string> GetStructOverrides(ShaderContext ctx)
        {
            return new Dictionary<string, string>
            {
                ["ATTRIBUTES_STRUCT"] = StructGenerator.GenerateMotionVectorsAttributes(ctx),
                ["INTERPOLATORS_STRUCT"] = StructGenerator.GenerateMotionVectorsInterpolators(ctx)
            };
        }
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
        
        public override Dictionary<string, string> GetStructOverrides(ShaderContext ctx)
        {
            return new Dictionary<string, string>
            {
                ["ATTRIBUTES_STRUCT"] = StructGenerator.GenerateMetaAttributes(ctx)
            };
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
        
        const string OutlineProperties = @"
        // Outlines (Auto-injected by FreeSkies)
        [Header(Outlines)]
        [Toggle(_ENABLE_OUTLINES)] _EnableOutlines(""Enable Outlines"", Float) = 1
        _OutlineWidth(""Outline Width"", Range(0, 10)) = 1
        _OutlineColor(""Outline Color"", Color) = (0, 0, 0, 1)";
        
        const string OutlineCBuffer = @"
    // Outlines (Auto-injected by FreeSkies)
    float _OutlineWidth;
    float4 _OutlineColor;";
        
        public override string GetPropertiesEntries(ShaderContext ctx)
        {
            if (PropertyExists(ctx, "_OutlineWidth")) return null;
            return OutlineProperties;
        }
        
        public override string GetCBufferEntries(ShaderContext ctx)
        {
            if (CBufferEntryExists(ctx, "_OutlineWidth")) return null;
            return OutlineCBuffer;
        }
    }
}
