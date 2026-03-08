using System.Text;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Generates pass-specific struct definitions.
    /// Copies user's struct fields and adds pass-specific fields if needed.
    /// </summary>
    public static class StructGenerator
    {
        //=============================================================================
        // Attributes Struct Generation
        //=============================================================================
        
        /// <summary>
        /// Generate a pass-specific Attributes struct based on user's struct.
        /// </summary>
        public static string GenerateAttributesStruct(ShaderContext ctx, string structName,
            string[] additionalFields = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"struct {structName}");
            sb.AppendLine("{");
            
            if (ctx.Attributes != null && ctx.Attributes.Fields.Count > 0)
            {
                foreach (var field in ctx.Attributes.Fields)
                {
                    if (field.IsMacro)
                    {
                        sb.AppendLine($"    {field.RawLine}");
                    }
                    else
                    {
                        sb.AppendLine($"    {field.Type} {field.Name} : {field.Semantic};");
                    }
                }
            }
            else
            {
                // Fallback minimal struct
                sb.AppendLine("    float4 positionOS : POSITION;");
                sb.AppendLine("    float3 normalOS : NORMAL;");
                sb.AppendLine("    float2 uv : TEXCOORD0;");
                sb.AppendLine("    UNITY_VERTEX_INPUT_INSTANCE_ID");
            }
            
            // Add any pass-specific fields
            if (additionalFields != null)
            {
                foreach (var field in additionalFields)
                {
                    sb.AppendLine($"    {field}");
                }
            }
            
            sb.AppendLine("};");
            return sb.ToString();
        }
        
        //=============================================================================
        // Interpolators Struct Generation
        //=============================================================================
        
        /// <summary>
        /// Generate a pass-specific Interpolators struct based on user's struct.
        /// </summary>
        public static string GenerateInterpolatorsStruct(ShaderContext ctx, string structName,
            string[] additionalFields = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"struct {structName}");
            sb.AppendLine("{");
            
            if (ctx.Interpolators != null && ctx.Interpolators.Fields.Count > 0)
            {
                foreach (var field in ctx.Interpolators.Fields)
                {
                    if (field.IsMacro)
                    {
                        sb.AppendLine($"    {field.RawLine}");
                    }
                    else
                    {
                        sb.AppendLine($"    {field.Type} {field.Name} : {field.Semantic};");
                    }
                }
            }
            else
            {
                // Fallback minimal struct
                sb.AppendLine("    float4 positionCS : SV_POSITION;");
                sb.AppendLine("    float2 uv : TEXCOORD0;");
                sb.AppendLine("    UNITY_VERTEX_INPUT_INSTANCE_ID");
            }
            
            // Add any pass-specific fields
            if (additionalFields != null)
            {
                foreach (var field in additionalFields)
                {
                    sb.AppendLine($"    {field}");
                }
            }
            
            sb.AppendLine("};");
            return sb.ToString();
        }
        
        //=============================================================================
        // Pass-Specific Struct Variants
        //=============================================================================
        
        /// <summary>
        /// Generate MotionVectors-specific Attributes with previous position.
        /// </summary>
        public static string GenerateMotionVectorsAttributes(ShaderContext ctx)
        {
            return GenerateAttributesStruct(ctx, "MotionVectorsAttributes", new[]
            {
                "float3 positionOld : TEXCOORD4;",
                "#if defined(_ADD_PRECOMPUTED_VELOCITY)",
                "float3 alembic : TEXCOORD5;",
                "#endif"
            });
        }
        
        /// <summary>
        /// Generate MotionVectors-specific Interpolators with current/previous clip positions.
        /// </summary>
        public static string GenerateMotionVectorsInterpolators(ShaderContext ctx)
        {
            return GenerateInterpolatorsStruct(ctx, "MotionVectorsInterpolators", new[]
            {
                "float4 curPositionCS : TEXCOORD8;",
                "float4 prevPositionCS : TEXCOORD9;"
            });
        }
        
        /// <summary>
        /// Generate Meta-specific Attributes with lightmap UVs.
        /// </summary>
        public static string GenerateMetaAttributes(ShaderContext ctx)
        {
            var additionalFields = new string[0];
            
            // Only add uv1/uv2 if not already present
            if (ctx.Attributes?.HasField("TEXCOORD1") != true)
            {
                additionalFields = new[] { "float2 uv1 : TEXCOORD1;", "float2 uv2 : TEXCOORD2;" };
            }
            
            return GenerateAttributesStruct(ctx, "MetaAttributes", additionalFields);
        }
        
        /// <summary>
        /// Generate DepthNormals-specific Attributes. Ensures NORMAL semantic exists
        /// since the pass fundamentally needs normals to function.
        /// </summary>
        public static string GenerateDepthNormalsAttributes(ShaderContext ctx)
        {
            string[] additionalFields = null;
            
            // Resolve what {{NORMAL}} will be (same logic as AddSemanticReplacements)
            string resolvedName = ctx.Attributes?.GetField("NORMAL")?.Name ?? "normalOS";
            
            // Check if a field with that name already exists (by any semantic)
            bool fieldExists = false;
            if (ctx.Attributes?.Fields != null)
            {
                foreach (var field in ctx.Attributes.Fields)
                {
                    if (!field.IsMacro && field.Name == resolvedName)
                    {
                        fieldExists = true;
                        break;
                    }
                }
            }
            
            if (!fieldExists)
            {
                additionalFields = new[] { $"float3 {resolvedName} : NORMAL;" };
            }
            
            return GenerateAttributesStruct(ctx, "DepthNormalsAttributes", additionalFields);
        }
        
        /// <summary>
        /// Generate DepthNormals-specific Interpolators. Ensures a normal field exists
        /// since the pass needs to output world-space normals.
        /// 
        /// Strategy: Resolve what {{NORMAL_WS}} will be (same logic as AddSemanticReplacements),
        /// then check if that field already exists by name. Only add if truly missing.
        /// This avoids duplicate field names when the user has e.g. "normalWS : TEXCOORD1".
        /// </summary>
        public static string GenerateDepthNormalsInterpolators(ShaderContext ctx)
        {
            string[] additionalFields = null;
            
            // Resolve the field name that {{NORMAL_WS}} will map to
            // (mirrors AddSemanticReplacements logic)
            string resolvedName = ctx.Interpolators?.GetField("NORMAL")?.Name
                               ?? ctx.Interpolators?.GetField("NORMALWS")?.Name
                               ?? ctx.Interpolators?.GetField("NORMAL_WS")?.Name
                               ?? "normalWS";
            
            // Check if a field with that name already exists in the struct (by any semantic)
            bool fieldExists = false;
            if (ctx.Interpolators?.Fields != null)
            {
                foreach (var field in ctx.Interpolators.Fields)
                {
                    if (!field.IsMacro && field.Name == resolvedName)
                    {
                        fieldExists = true;
                        break;
                    }
                }
            }
            
            if (!fieldExists)
            {
                additionalFields = new[] { $"float3 {resolvedName} : NORMAL;" };
            }
            
            return GenerateInterpolatorsStruct(ctx, "DepthNormalsInterpolators", additionalFields);
        }
    }
}
