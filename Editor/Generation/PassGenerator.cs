using System.Collections.Generic;
using System.Text;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Generates shader passes from templates.
    /// Handles pass-specific struct generation, hook integration, and tessellation.
    /// </summary>
    public static class PassGenerator
    {
        //=============================================================================
        // All Base Passes
        //=============================================================================
        
        public static string GenerateAllBasePasses(ShaderContext ctx)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine(GenerateShadowCasterPass(ctx));
            sb.AppendLine();
            sb.AppendLine(GenerateDepthOnlyPass(ctx));
            sb.AppendLine();
            sb.AppendLine(GenerateDepthNormalsPass(ctx));
            sb.AppendLine();
            sb.AppendLine(GenerateMotionVectorsPass(ctx));
            sb.AppendLine();
            sb.AppendLine(GenerateMetaPass(ctx));
            
            return sb.ToString();
        }
        
        //=============================================================================
        // Individual Pass Generation
        //=============================================================================
        
        public static string GenerateShadowCasterPass(ShaderContext ctx)
        {
            string template = TemplateEngine.LoadTemplate("ShadowCaster");
            if (string.IsNullOrEmpty(template))
                return "// ERROR: ShadowCaster template not found";
            
            return TemplateEngine.ProcessPassTemplate(template, ctx, "ShadowCaster");
        }
        
        public static string GenerateDepthOnlyPass(ShaderContext ctx)
        {
            string template = TemplateEngine.LoadTemplate("DepthOnly");
            if (string.IsNullOrEmpty(template))
                return "// ERROR: DepthOnly template not found";
            
            return TemplateEngine.ProcessPassTemplate(template, ctx, "DepthOnly");
        }
        
        public static string GenerateDepthNormalsPass(ShaderContext ctx)
        {
            string template = TemplateEngine.LoadTemplate("DepthNormals");
            if (string.IsNullOrEmpty(template))
                return "// ERROR: DepthNormals template not found";
            
            // DepthNormals needs guaranteed normal fields in both structs
            var replacements = new Dictionary<string, string>
            {
                { "ATTRIBUTES_STRUCT", StructGenerator.GenerateDepthNormalsAttributes(ctx) },
                { "INTERPOLATORS_STRUCT", StructGenerator.GenerateDepthNormalsInterpolators(ctx) }
            };
            
            return TemplateEngine.ProcessPassTemplate(template, ctx, "DepthNormals", replacements);
        }
        
        public static string GenerateMotionVectorsPass(ShaderContext ctx)
        {
            string template = TemplateEngine.LoadTemplate("MotionVectors");
            if (string.IsNullOrEmpty(template))
                return "// ERROR: MotionVectors template not found";
            
            // Motion vectors needs special struct handling
            var replacements = new Dictionary<string, string>
            {
                { "ATTRIBUTES_STRUCT", StructGenerator.GenerateMotionVectorsAttributes(ctx) },
                { "INTERPOLATORS_STRUCT", StructGenerator.GenerateMotionVectorsInterpolators(ctx) }
            };
            
            return TemplateEngine.ProcessPassTemplate(template, ctx, "MotionVectors", replacements);
        }
        
        public static string GenerateMetaPass(ShaderContext ctx)
        {
            string template = TemplateEngine.LoadTemplate("Meta");
            if (string.IsNullOrEmpty(template))
                return "// ERROR: Meta template not found";
            
            // Meta pass needs special attributes with lightmap UVs
            var replacements = new Dictionary<string, string>
            {
                { "ATTRIBUTES_STRUCT", StructGenerator.GenerateMetaAttributes(ctx) }
            };
            
            return TemplateEngine.ProcessPassTemplate(template, ctx, "Meta", replacements);
        }
        
        public static string GenerateOutlinePass(ShaderContext ctx)
        {
            string template = TemplateEngine.LoadTemplate("Outline");
            if (string.IsNullOrEmpty(template))
                return "// ERROR: Outline template not found";
            
            return TemplateEngine.ProcessPassTemplate(template, ctx, "Outline");
        }
    }
}
