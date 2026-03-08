namespace FS.Shaders.Editor
{
    /// <summary>
    /// Processes the "Outlines" tag.
    /// Injects outline properties, CBUFFER entries, and the outline pass.
    /// </summary>
    [ShaderTagProcessor("Outlines", priority: 10)]
    public class OutlinesProcessor : ShaderTagProcessorBase
    {
        public override string TagName => "Outlines";
        public override int Priority => 10;
        
        //=============================================================================
        // Properties & CBUFFER Content
        //=============================================================================
        
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
        
        //=============================================================================
        // Processing Stages
        //=============================================================================
        
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
        
        public override void InjectPasses(ShaderContext ctx)
        {
            string outlinePass = PassGenerator.GenerateOutlinePass(ctx);
            QueuePass(ctx, outlinePass);
        }
    }
}
