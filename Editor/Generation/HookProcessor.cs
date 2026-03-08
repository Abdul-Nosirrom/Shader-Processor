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
        /// When passName is provided, function names are prefixed (e.g., "HeightDisplace" →
        /// "DepthOnlyHeightDisplace") to avoid collisions with HLSLINCLUDE versions that
        /// share the same name but use the original struct types.
        /// </summary>
        public static string GenerateHookFunctions(ShaderContext ctx, string attrName, string interpName,
            string passName = "")
        {
            var sb = new StringBuilder();
            
            // Emit function bodies for all active hooks, with struct names rewritten
            foreach (var entry in ctx.Hooks.Active)
            {
                if (string.IsNullOrEmpty(entry.Value.FunctionBody))
                    continue;
                
                string rewritten = RewriteStructNames(entry.Value.FunctionBody,
                    ctx.AttributesStructName, attrName, ctx.InterpolatorsStructName, interpName);
                
                // Prefix function name to avoid collision with HLSLINCLUDE version.
                // HLSLINCLUDE functions are visible in all passes — if we emit a version with
                // rewritten struct types but the same name, HLSL can't resolve the overload
                // because structurally identical structs are treated as interchangeable.
                if (!string.IsNullOrEmpty(passName))
                {
                    string prefixedName = passName + entry.Value.FunctionName;
                    rewritten = Regex.Replace(rewritten,
                        $@"\b{Regex.Escape(entry.Value.FunctionName)}\b", prefixedName);
                }
                
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

            // =====================================================================
            // CRITICAL: Strip CBUFFER, textures, and includes BEFORE function removal.
            // RemoveFunction's regex uses (\w+\s+)? for optional modifiers, which
            // can greedily match nearby keywords like CBUFFER_END across newlines,
            // eating them as part of the function removal. Stripping the CBUFFER
            // first eliminates this interaction.
            // =====================================================================
            
            // Remove CBUFFER (handled by {{CBUFFER}} in template)
            hlsl = Regex.Replace(hlsl, @"CBUFFER_START\s*\(\s*UnityPerMaterial\s*\).*?CBUFFER_END", "", RegexOptions.Singleline);

            // Remove texture/sampler declarations (handled by {{TEXTURES}} in template)
            hlsl = Regex.Replace(hlsl, @"TEXTURE2D\s*\(\s*\w+\s*\)\s*;", "");
            hlsl = Regex.Replace(hlsl, @"SAMPLER\s*\(\s*\w+\s*\)\s*;", "");
            hlsl = Regex.Replace(hlsl, @"TEXTURE2D_ARRAY\s*\(\s*\w+\s*\)\s*;", "");
            hlsl = Regex.Replace(hlsl, @"TEXTURECUBE\s*\(\s*\w+\s*\)\s*;", "");

            // Remove #include statements (template has its own)
            hlsl = Regex.Replace(hlsl, @"#include\s+""[^""]+""\s*\n?", "");

            // Remove pragmas we'll replace
            hlsl = RemovePragmas(hlsl, "vertex", "fragment");

            // Remove struct declarations (we generate our own)
            hlsl = RemoveStructDeclaration(hlsl, ctx.AttributesStructName);
            hlsl = RemoveStructDeclaration(hlsl, ctx.InterpolatorsStructName);

            // Remove vertex and fragment functions
            hlsl = RemoveFunction(hlsl, ctx.ForwardVertexFunctionName);
            hlsl = RemoveFunction(hlsl, ctx.ForwardFragmentFunctionName);

            // Remove hook functions (they're output separately via HOOK_FUNCTIONS)
            foreach (var entry in ctx.Hooks.Active)
            {
                hlsl = RemoveFunction(hlsl, entry.Value.FunctionName);
            }

            // Remove hook pragma declarations for all registered hooks
            foreach (var hook in ShaderHookRegistry.All)
            {
                hlsl = Regex.Replace(hlsl, $@"#pragma\s+{Regex.Escape(hook.PragmaName)}\s+\w+\s*\n?", "");
            }

            // Rewrite struct names (for any remaining references in helper code)
            hlsl = RewriteStructNames(hlsl, ctx.AttributesStructName, targetAttrName,
                ctx.InterpolatorsStructName, targetInterpName);
            
            // Remove stray # or #pragma on their own lines
            hlsl = Regex.Replace(hlsl, @"^\s*#\s*(pragma)?\s*$", "", RegexOptions.Multiline);

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
    
            // Pattern: [modifier] return_type FunctionName(params) [: semantic] { body }
            // Must handle nested braces.
            // IMPORTANT: The optional modifier group uses [ \t]+ (not \s+) to prevent
            // matching words from unrelated lines. Without this, a word like CBUFFER_END
            // on a distant line could be grabbed as a "modifier", eating it from the source.
            var pattern = $@"
                (\w+[ \t]+)?                        # Optional modifier (inline, etc.) - same line only
                \w+\s+                              # Return type
                {Regex.Escape(functionName)}\s*     # Function name
                \([^)]*\)\s*                        # Parameters
                (:\s*\w+\s*)?                       # Optional semantic (: SV_Target)
                \{{                                 # Opening brace
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
