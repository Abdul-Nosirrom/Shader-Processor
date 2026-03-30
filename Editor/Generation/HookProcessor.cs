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
                // HLSLINCLUDE functions are visible in all passes - if we emit a version with
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
        /// Result of extracting forward pass content, split into preprocessor
        /// directives and code so they can be placed at different points in templates.
        /// </summary>
        public struct ForwardContentResult
        {
            /// <summary>
            /// Preprocessor directives (#include, #include_with_pragmas, #define, #pragma)
            /// that need to appear before CBUFFER/textures so macros are available.
            /// </summary>
            public string Preprocessor;
            
            /// <summary>
            /// Helper functions, code, and conditional compilation blocks (#ifdef etc.)
            /// that reference CBUFFER variables and textures.
            /// </summary>
            public string Content;
        }
        
        /// <summary>
        /// Extract reusable content from Forward pass HLSLPROGRAM, split into
        /// preprocessor directives and code. Preprocessor goes above CBUFFER in
        /// templates so macros used in CBUFFER/structs are defined. Code goes
        /// below CBUFFER/textures/structs since it references those declarations.
        /// </summary>
        public static ForwardContentResult ExtractForwardPassContentSplit(
            ShaderContext ctx, string targetAttrName, string targetInterpName)
        {
            var result = new ForwardContentResult { Preprocessor = "", Content = "" };
            
            if (ctx.ReferencePass == null || string.IsNullOrEmpty(ctx.ReferencePass.HlslProgram))
                return result;

            string hlsl = ctx.ReferencePass.HlslProgram;

            // =====================================================================
            // CRITICAL: Strip CBUFFER, textures BEFORE function removal.
            // RemoveFunction's regex uses (\w+\s+)? for optional modifiers, which
            // can greedily match nearby keywords like CBUFFER_END across newlines,
            // eating them as part of the function removal. Stripping the CBUFFER
            // first eliminates this interaction.
            // =====================================================================
            
            // Remove CBUFFER (handled by {{CBUFFER}} in template)
            hlsl = ShaderBlockUtility.StripCBuffer(hlsl);

            // Remove texture/sampler declarations (handled by {{TEXTURES}} in template)
            hlsl = ShaderBlockUtility.StripTextureDeclarations(hlsl);

            // Remove pragmas we'll replace
            hlsl = RemovePragmas(hlsl, "vertex", "fragment");

            // Remove struct declarations (we generate our own)
            hlsl = RemoveStructDeclaration(hlsl, ctx.AttributesStructName);
            hlsl = RemoveStructDeclaration(hlsl, ctx.InterpolatorsStructName);

            // Remove vertex and fragment functions
            hlsl = RemoveFunction(hlsl, ctx.ReferenceVertexFunctionName);
            hlsl = RemoveFunction(hlsl, ctx.ReferenceFragmentFunctionName);

            // Remove hook functions (they're output separately via HOOK_FUNCTIONS)
            foreach (var entry in ctx.Hooks.Active)
            {
                hlsl = RemoveFunction(hlsl, entry.Value.FunctionName);
            }

            // Remove hook pragma declarations for all registered hooks
            foreach (var hook in ShaderHookRegistry.All)
            {
                hlsl = ShaderPragmaUtility.Strip(hlsl, hook.PragmaName);
            }

            // Remove fragmentOutput pragmas (used by ForwardBodyInjector, not valid HLSL)
            hlsl = ShaderPragmaUtility.StripFragmentOutputs(hlsl);

            // Rewrite struct names (for any remaining references in helper code)
            hlsl = RewriteStructNames(hlsl, ctx.AttributesStructName, targetAttrName,
                ctx.InterpolatorsStructName, targetInterpName);
            
            // Remove stray # or #pragma on their own lines
            hlsl = ShaderPragmaUtility.StripOrphanedPragmaLines(hlsl);

            // =====================================================================
            // Split into preprocessor directives and code.
            //
            // Preprocessor: #include, #include_with_pragmas, #define, #pragma
            //   These need to be above CBUFFER/textures/structs so macros they
            //   define are available (e.g., COMBAT_PARAMETERS from an include).
            //
            // Code: everything else, including #ifdef/#endif/#if/#else/#elif/#undef
            //   These wrap function bodies and code that references CBUFFER/textures,
            //   so they must come after those declarations.
            // =====================================================================
            var preprocessorLines = new StringBuilder();
            var codeLines = new StringBuilder();
            
            foreach (var line in hlsl.Split('\n'))
            {
                string trimmed = line.TrimStart();
                
                if (IsPreprocessorSetupDirective(trimmed))
                {
                    preprocessorLines.AppendLine(line);
                }
                else
                {
                    codeLines.AppendLine(line);
                }
            }
            
            // Clean up multiple blank lines in both outputs
            string preprocessor = ShaderSourceUtility.CollapseBlankLines(preprocessorLines.ToString()).Trim();
            string content = ShaderSourceUtility.CollapseBlankLines(codeLines.ToString()).Trim();
            
            result.Preprocessor = preprocessor;
            result.Content = content;
            return result;
        }
        
        /// <summary>
        /// Check if a line is a preprocessor setup directive that needs to appear
        /// before CBUFFER/textures/structs (defines macros, includes files, sets keywords).
        /// 
        /// Returns true for: #include, #include_with_pragmas, #define, #pragma
        /// Returns false for: #ifdef, #endif, #if, #else, #elif, #undef, #error, #warning
        /// These conditional/control directives typically wrap code blocks that reference
        /// CBUFFER variables, so they need to stay with the code.
        /// </summary>
        static bool IsPreprocessorSetupDirective(string trimmedLine)
        {
            if (!trimmedLine.StartsWith("#"))
                return false;
            
            // These directives set up the compilation environment (defines, includes, keywords)
            // and must appear before declarations that use them.
            if (trimmedLine.StartsWith("#include"))      return true;  // covers #include and #include_with_pragmas
            if (trimmedLine.StartsWith("#define"))        return true;
            if (trimmedLine.StartsWith("#pragma"))        return true;
            
            // Everything else (#ifdef, #endif, #if, #else, #elif, #undef, #error, etc.)
            // stays with the code since it typically guards code blocks.
            return false;
        }
        
        /// <summary>
        /// Legacy method for backward compatibility. Returns just the content portion
        /// (helper functions and code). For new code, use ExtractForwardPassContentSplit.
        /// </summary>
        public static string ExtractForwardPassContent(ShaderContext ctx, string targetAttrName, string targetInterpName)
        {
            var split = ExtractForwardPassContentSplit(ctx, targetAttrName, targetInterpName);
            // Legacy behavior: return everything combined (preprocessor + content)
            // This preserves behavior for any external code calling this method.
            string combined = split.Preprocessor + "\n" + split.Content;
            return ShaderSourceUtility.CollapseBlankLines(combined).Trim();
        }
        
        //=============================================================================
        // Code Removal Helpers
        //=============================================================================
        
        static string RemovePragmas(string code, params string[] pragmaTypes)
        {
            return ShaderPragmaUtility.StripAll(code, pragmaTypes);
        }
        
        static string RemoveStructDeclaration(string code, string structName)
        {
            return ShaderFunctionUtility.RemoveStructDeclaration(code, structName);
        }
        
        static string RemoveFunction(string hlsl, string functionName)
        {
            return ShaderFunctionUtility.RemoveFunction(hlsl, functionName);
        }
    }
}
