using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Generates pass-specific struct definitions.
    /// Copies user's struct fields and adds pass-specific fields if needed.
    /// Preserves preprocessor guards (#ifdef/#endif) around conditional fields.
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
            
            // Track emitted field names for deduplication
            var emittedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (ctx.Attributes != null && ctx.Attributes.Fields.Count > 0)
            {
                EmitFieldsWithGuards(sb, ctx.Attributes.Fields);
                foreach (var field in ctx.Attributes.Fields)
                {
                    if (!field.IsMacro && !string.IsNullOrEmpty(field.Name))
                        emittedNames.Add(field.Name);
                }
            }
            else
            {
                // Fallback minimal struct
                sb.AppendLine("    float4 positionOS : POSITION;");
                sb.AppendLine("    float3 normalOS : NORMAL;");
                sb.AppendLine("    float2 uv : TEXCOORD0;");
                sb.AppendLine("    UNITY_VERTEX_INPUT_INSTANCE_ID");
                emittedNames.Add("positionOS");
                emittedNames.Add("normalOS");
                emittedNames.Add("uv");
            }
            
            // Add any pass-specific fields, skipping duplicates
            if (additionalFields != null)
            {
                foreach (var field in additionalFields)
                {
                    // Extract field name: "float3 normalOS : NORMAL;" → "normalOS"
                    string name = ExtractFieldName(field);
                    if (name != null && emittedNames.Contains(name))
                        continue;
                    
                    sb.AppendLine($"    {field}");
                    if (name != null) emittedNames.Add(name);
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
            
            // Track emitted field names for deduplication
            var emittedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (ctx.Interpolators != null && ctx.Interpolators.Fields.Count > 0)
            {
                EmitFieldsWithGuards(sb, ctx.Interpolators.Fields);
                foreach (var field in ctx.Interpolators.Fields)
                {
                    if (!field.IsMacro && !string.IsNullOrEmpty(field.Name))
                        emittedNames.Add(field.Name);
                }
            }
            else
            {
                // Fallback minimal struct
                sb.AppendLine("    float4 positionCS : SV_POSITION;");
                sb.AppendLine("    float2 uv : TEXCOORD0;");
                sb.AppendLine("    UNITY_VERTEX_INPUT_INSTANCE_ID");
                emittedNames.Add("positionCS");
                emittedNames.Add("uv");
            }
            
            // Add any pass-specific fields, skipping duplicates
            if (additionalFields != null)
            {
                foreach (var field in additionalFields)
                {
                    string name = ExtractFieldName(field);
                    if (name != null && emittedNames.Contains(name))
                        continue;
                    
                    sb.AppendLine($"    {field}");
                    if (name != null) emittedNames.Add(name);
                }
            }
            
            sb.AppendLine("};");
            return sb.ToString();
        }
        
        //=============================================================================
        // Guard-Aware Field Emission
        //=============================================================================
        
        /// <summary>
        /// Emit struct fields with preprocessor guards. Groups consecutive fields
        /// with the same guard to avoid redundant #ifdef/#endif pairs.
        /// Fields with null guards are emitted directly.
        /// </summary>
        static void EmitFieldsWithGuards(StringBuilder sb, System.Collections.Generic.List<StructField> fields)
        {
            string activeGuard = null;
            
            foreach (var field in fields)
            {
                // Close previous guard if it changed
                if (activeGuard != null && activeGuard != field.PreprocessorGuard)
                {
                    sb.AppendLine("#endif");
                    activeGuard = null;
                }
                
                // Open new guard if needed
                if (field.PreprocessorGuard != null && activeGuard != field.PreprocessorGuard)
                {
                    sb.AppendLine(field.PreprocessorGuard);
                    activeGuard = field.PreprocessorGuard;
                }
                
                // Emit the field
                if (field.IsMacro)
                {
                    sb.AppendLine($"    {field.RawLine}");
                }
                else
                {
                    sb.AppendLine($"    {field.Type} {field.Name} : {field.Semantic};");
                }
            }
            
            // Close any trailing guard
            if (activeGuard != null)
            {
                sb.AppendLine("#endif");
            }
        }
        
        //=============================================================================
        // Field Name Extraction
        //=============================================================================
        
        /// <summary>
        /// Extract the field name from a struct field declaration string.
        /// Handles formats like "float3 normalOS : NORMAL;" → "normalOS"
        /// and preprocessor lines like "#if defined(X)" → null (skip).
        /// </summary>
        static readonly Regex s_fieldNameRegex = new Regex(
            @"^\s*\w+\s+(\w+)\s*:", RegexOptions.Compiled);
        
        static string ExtractFieldName(string fieldDecl)
        {
            if (string.IsNullOrEmpty(fieldDecl)) return null;
            
            // Skip preprocessor directives
            string trimmed = fieldDecl.TrimStart();
            if (trimmed.StartsWith("#")) return null;
            
            var match = s_fieldNameRegex.Match(fieldDecl);
            return match.Success ? match.Groups[1].Value : null;
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
        /// Generate Meta-specific Attributes.
        /// 
        /// The Meta pass needs TEXCOORD1 (static lightmap UV) and TEXCOORD2 (dynamic lightmap UV)
        /// for UnityMetaVertexPosition. The template references these via semantic markers
        /// ({{TEXCOORD1}}, {{TEXCOORD2}}) so existing fields are picked up by name automatically.
        /// We only add fields for semantics that are genuinely missing from the user's struct.
        /// </summary>
        public static string GenerateMetaAttributes(ShaderContext ctx)
        {
            var additionalFields = new System.Collections.Generic.List<string>();
            
            // Add uv1 if no TEXCOORD1 exists (basic shaders without lightmap UVs)
            if (ctx.Attributes?.HasField("TEXCOORD1") != true)
            {
                additionalFields.Add("float2 uv1 : TEXCOORD1;");
            }
            
            // Add uv2 if no TEXCOORD2 exists (most shaders won't have dynamic lightmap UVs)
            if (ctx.Attributes?.HasField("TEXCOORD2") != true)
            {
                additionalFields.Add("float2 uv2 : TEXCOORD2;");
            }
            
            return GenerateAttributesStruct(ctx, "MetaAttributes",
                additionalFields.Count > 0 ? additionalFields.ToArray() : null);
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
