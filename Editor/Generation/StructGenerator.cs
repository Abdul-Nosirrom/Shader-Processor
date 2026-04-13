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
        // Semantic Helpers
        //=============================================================================

        /// <summary>
        /// Returns an additional field entry for the given semantic if the user's
        /// Attributes struct doesn't already have it. Returns null if present.
        /// Used by passes whose templates reference a semantic that the user's
        /// struct might not declare (e.g., NORMAL for ShadowCaster shadow bias).
        /// </summary>
        public static string[] EnsureAttributeField(ShaderContext ctx, string semantic,
            string defaultType, string defaultName)
        {
            string resolvedName = ctx.Attributes?.GetField(semantic)?.Name ?? defaultName;

            if (ctx.Attributes?.Fields != null)
            {
                foreach (var field in ctx.Attributes.Fields)
                {
                    if (!field.IsMacro && field.Name == resolvedName)
                        return null;
                }
            }

            return new[] { $"{defaultType} {resolvedName} : {semantic};" };
        }

        /// <summary>
        /// Returns an additional field entry for the given semantic if the user's
        /// Interpolators struct doesn't already have it. Returns null if present.
        /// Tries multiple semantic conventions before falling back to the default name.
        /// </summary>
        public static string[] EnsureInterpolatorField(ShaderContext ctx, string defaultType,
            string defaultName, params string[] semanticCandidates)
        {
            // Resolve by trying each semantic candidate in order
            string resolvedName = defaultName;
            foreach (string semantic in semanticCandidates)
            {
                var field = ctx.Interpolators?.GetField(semantic);
                if (field != null)
                {
                    resolvedName = field.Name;
                    break;
                }
            }

            if (ctx.Interpolators?.Fields != null)
            {
                foreach (var field in ctx.Interpolators.Fields)
                {
                    if (!field.IsMacro && field.Name == resolvedName)
                        return null;
                }
            }

            // Use the first semantic candidate for the declaration
            return new[] { $"{defaultType} {resolvedName} : {semanticCandidates[0]};" };
        }

        //=============================================================================
        // Field Declaration Parsing
        //=============================================================================

        /// <summary>
        /// Parse an array of field declaration strings into StructField objects.
        /// Handles regular fields ("float3 normalOS : NORMAL;") and interleaved
        /// preprocessor guards ("#if defined(...)", "#endif").
        /// Used by TemplateEngine to build resolved structs for tag processors.
        /// </summary>
        public static List<StructField> ParseFieldDeclarations(string[] declarations)
        {
            if (declarations == null) return null;

            var fields = new List<StructField>();
            string currentGuard = null;

            foreach (string decl in declarations)
            {
                string trimmed = decl.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                if (trimmed.StartsWith("#endif"))
                {
                    currentGuard = null;
                    continue;
                }

                if (trimmed.StartsWith("#"))
                {
                    currentGuard = trimmed;
                    continue;
                }

                var match = System.Text.RegularExpressions.Regex.Match(trimmed,
                    @"^(\w+)\s+(\w+)\s*:\s*(\w+)\s*;?$");
                if (match.Success)
                {
                    fields.Add(new StructField
                    {
                        Type = match.Groups[1].Value,
                        Name = match.Groups[2].Value,
                        Semantic = match.Groups[3].Value,
                        PreprocessorGuard = currentGuard,
                        RawLine = trimmed
                    });
                }
            }

            return fields;
        }
    }
}
