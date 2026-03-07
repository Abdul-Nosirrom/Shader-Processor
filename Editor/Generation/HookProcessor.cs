using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Extracts hook functions from Forward pass and rewrites them for pass-specific structs.
    /// </summary>
    public static class HookProcessor
    {
        //=============================================================================
        // Hook Function Generation
        //=============================================================================
        
        /// <summary>
        /// Generate all hook functions with struct names rewritten for the target pass.
        /// </summary>
        public static string GenerateHookFunctions(ShaderContext ctx, string attrName, string interpName)
        {
            var sb = new StringBuilder();
            
            // Helper functions first (they might be called by hooks)
            // foreach (var helper in ctx.Hooks.HelperFunctions)
            // {
            //     string rewritten = RewriteStructNames(helper, ctx.AttributesStructName, attrName,
            //         ctx.InterpolatorsStructName, interpName);
            //     sb.AppendLine(rewritten);
            //     sb.AppendLine();
            // }
            
            // Vertex displacement hook
            if (ctx.Hooks.HasVertexDisplacement)
            {
                string rewritten = RewriteStructNames(ctx.Hooks.VertexDisplacementBody,
                    ctx.AttributesStructName, attrName, ctx.InterpolatorsStructName, interpName);
                sb.AppendLine(rewritten);
                sb.AppendLine();
            }
            
            // Interpolator transfer hook
            if (ctx.Hooks.HasInterpolatorTransfer)
            {
                string rewritten = RewriteStructNames(ctx.Hooks.InterpolatorTransferBody,
                    ctx.AttributesStructName, attrName, ctx.InterpolatorsStructName, interpName);
                sb.AppendLine(rewritten);
                sb.AppendLine();
            }
            
            // Alpha clip hook
            if (ctx.Hooks.HasAlphaClip)
            {
                string rewritten = RewriteStructNames(ctx.Hooks.AlphaClipBody,
                    ctx.AttributesStructName, attrName, ctx.InterpolatorsStructName, interpName);
                sb.AppendLine(rewritten);
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
        
        //=============================================================================
        // Struct Name Rewriting
        //=============================================================================
        
        /// <summary>
        /// Rewrite struct type names in a code block.
        /// Handles function parameters, local variables, and casts.
        /// </summary>
        public static string RewriteStructNames(string code, 
            string oldAttrName, string newAttrName,
            string oldInterpName, string newInterpName)
        {
            if (string.IsNullOrEmpty(code)) return code;
            
            string result = code;
            
            // Replace attribute struct name
            if (!string.IsNullOrEmpty(oldAttrName) && !string.IsNullOrEmpty(newAttrName))
            {
                result = ReplaceTypeName(result, oldAttrName, newAttrName);
            }
            
            // Replace interpolator struct name
            if (!string.IsNullOrEmpty(oldInterpName) && !string.IsNullOrEmpty(newInterpName))
            {
                result = ReplaceTypeName(result, oldInterpName, newInterpName);
            }
            
            return result;
        }
        
        /// <summary>
        /// Replace a type name, being careful to only match whole words.
        /// </summary>
        static string ReplaceTypeName(string code, string oldName, string newName)
        {
            // Match the type name as a whole word
            // Handles: function parameters, variable declarations, casts
            // Pattern: word boundary + name + word boundary
            return Regex.Replace(code, $@"\b{Regex.Escape(oldName)}\b", newName);
        }
        
        //=============================================================================
        // Forward Pass Content Extraction
        //=============================================================================
        
        /// <summary>
        /// Extract reusable content from Forward pass HLSLPROGRAM.
        /// Removes vertex/fragment functions, struct declarations, and specific pragmas.
        /// </summary>
        public static string ExtractForwardPassContent(ShaderContext ctx, string targetAttrName, string targetInterpName)
        {
            if (ctx.ForwardPass == null || string.IsNullOrEmpty(ctx.ForwardPass.HlslProgram))
                return "";

            string hlsl = ctx.ForwardPass.HlslProgram;

            // Remove pragmas we'll replace
            hlsl = RemovePragmas(hlsl, "vertex", "fragment");

            // Remove struct declarations (we generate our own)
            hlsl = RemoveStructDeclaration(hlsl, ctx.AttributesStructName);
            hlsl = RemoveStructDeclaration(hlsl, ctx.InterpolatorsStructName);

            // Remove vertex and fragment functions
            hlsl = RemoveFunction(hlsl, ctx.ForwardVertexFunctionName);
            hlsl = RemoveFunction(hlsl, ctx.ForwardFragmentFunctionName);

            // Remove hook functions (they're output separately via HOOK_FUNCTIONS)
            if (ctx.Hooks.HasVertexDisplacement)
                hlsl = RemoveFunction(hlsl, ctx.Hooks.VertexDisplacementName);
            if (ctx.Hooks.HasInterpolatorTransfer)
                hlsl = RemoveFunction(hlsl, ctx.Hooks.InterpolatorTransferName);
            if (ctx.Hooks.HasAlphaClip)
                hlsl = RemoveFunction(hlsl, ctx.Hooks.AlphaClipName);

            // Remove hook pragma declarations
            hlsl = Regex.Replace(hlsl, @"#pragma\s+(vertexDisplacement|interpolatorTransfer|alphaClip)\s+\w+\s*\n?", "");

            // Remove CBUFFER (handled by {{CBUFFER}} in template)
            hlsl = Regex.Replace(hlsl, @"CBUFFER_START\s*\(\s*UnityPerMaterial\s*\).*?CBUFFER_END", "", RegexOptions.Singleline);

            // Remove texture/sampler declarations (handled by {{TEXTURES}} in template)
            hlsl = Regex.Replace(hlsl, @"TEXTURE2D\s*\(\s*\w+\s*\)\s*;", "");
            hlsl = Regex.Replace(hlsl, @"SAMPLER\s*\(\s*\w+\s*\)\s*;", "");
            hlsl = Regex.Replace(hlsl, @"TEXTURE2D_ARRAY\s*\(\s*\w+\s*\)\s*;", "");
            hlsl = Regex.Replace(hlsl, @"TEXTURECUBE\s*\(\s*\w+\s*\)\s*;", "");

            // Remove #include statements (template has its own)
            hlsl = Regex.Replace(hlsl, @"#include\s+""[^""]+""\s*\n?", "");

            // Rewrite struct names
            hlsl = RewriteStructNames(hlsl, ctx.AttributesStructName, targetAttrName,
                ctx.InterpolatorsStructName, targetInterpName);
            
            // Remove stray # characters on their own lines
            hlsl = Regex.Replace(hlsl, @"^\s*#\s*$", "", RegexOptions.Multiline);

            // Clean up multiple blank lines
            hlsl = Regex.Replace(hlsl, @"\n{3,}", "\n\n");

            return hlsl.Trim();
        }
        
        //=============================================================================
        // Code Removal Helpers
        //=============================================================================
        
        static string RemovePragmas(string code, params string[] pragmaTypes)
        {
            foreach (var pragmaType in pragmaTypes)
            {
                code = Regex.Replace(code, $@"#pragma\s+{pragmaType}\s+\w+\s*\n?", "");
            }
            return code;
        }
        
        static string RemoveStructDeclaration(string code, string structName)
        {
            if (string.IsNullOrEmpty(structName)) return code;
            
            // Match: struct Name { ... };
            var match = Regex.Match(code, $@"struct\s+{Regex.Escape(structName)}\s*\{{");
            if (!match.Success) return code;
            
            int startIndex = match.Index;
            int braceCount = 0;
            int endIndex = startIndex;
            bool foundBrace = false;
            
            for (int i = startIndex; i < code.Length; i++)
            {
                if (code[i] == '{')
                {
                    braceCount++;
                    foundBrace = true;
                }
                else if (code[i] == '}')
                {
                    braceCount--;
                    if (foundBrace && braceCount == 0)
                    {
                        endIndex = i + 1;
                        // Include trailing semicolon if present
                        if (endIndex < code.Length && code[endIndex] == ';')
                            endIndex++;
                        break;
                    }
                }
            }
            
            if (endIndex > startIndex)
            {
                code = code.Remove(startIndex, endIndex - startIndex);
            }
            
            return code;
        }
        
        static string RemoveFunction(string hlsl, string functionName)
        {
            if (string.IsNullOrEmpty(functionName))
                return hlsl;
    
            // Pattern: return_type FunctionName(params) { body }
            // Must handle nested braces
            var pattern = $@"
                (\w+\s+)?                           # Optional return type modifiers (half4, void, etc)
                \w+\s+                              # Return type
                {Regex.Escape(functionName)}\s*     # Function name
                \([^)]*\)\s*                        # Parameters
                (:\s*\w+\s*)?                        # Optional semantic (: SV_Target)
                \{{                                  # Opening brace
            ";
    
            var match = Regex.Match(hlsl, pattern, RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline);
    
            if (!match.Success)
                return hlsl;
    
            // Find matching closing brace
            int braceStart = match.Index + match.Length - 1;
            int depth = 1;
            int i = braceStart + 1;
    
            while (i < hlsl.Length && depth > 0)
            {
                if (hlsl[i] == '{') depth++;
                else if (hlsl[i] == '}') depth--;
                i++;
            }
    
            if (depth == 0)
            {
                // Remove from match start to closing brace
                return hlsl.Substring(0, match.Index) + hlsl.Substring(i);
            }
    
            return hlsl;
        }
    }
}
