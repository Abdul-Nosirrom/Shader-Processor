using System.Collections.Generic;
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
            /// that need to appear before CBUFFER/structs so macros are available,
            /// inside the same #if/#ifdef conditions they were written under.
            /// </summary>
            public string Preprocessor;

            /// <summary>
            /// Texture and sampler declarations, helper functions and other code that
            /// reference CBUFFER variables, with their #if/#ifdef conditions.
            /// </summary>
            public string Content;
        }

        /// <summary>
        /// Extract reusable content from Forward pass HLSLPROGRAM, split into
        /// preprocessor directives and code. Preprocessor goes above CBUFFER in
        /// templates so macros used in CBUFFER/structs are defined. Code goes
        /// below CBUFFER/structs since it references those declarations; the
        /// pass's texture and sampler declarations travel with it.
        /// </summary>
        public static ForwardContentResult ExtractForwardPassContentSplit(
            ShaderContext ctx, string targetAttrName, string targetInterpName)
        {
            var result = new ForwardContentResult { Preprocessor = "", Content = "" };
            
            if (ctx.ReferencePass == null || string.IsNullOrEmpty(ctx.ReferencePass.HlslProgram))
                return result;

            string hlsl = ctx.ReferencePass.HlslProgram;

            // =====================================================================
            // CRITICAL: Strip the CBUFFER BEFORE function removal.
            // RemoveFunction's regex uses (\w+\s+)? for optional modifiers, which
            // can greedily match nearby keywords like CBUFFER_END across newlines,
            // eating them as part of the function removal. Stripping the CBUFFER
            // first eliminates this interaction.
            // =====================================================================

            // Remove CBUFFER (handled by {{CBUFFER}} in template)
            hlsl = ShaderBlockUtility.StripCBuffer(hlsl);

            // Texture and sampler declarations stay as written. They land in the code
            // half, which every template places above the hook functions and the
            // vertex/fragment bodies, and that half keeps their #if guards. Unlike the
            // CBUFFER, nothing adds textures to a generated pass, so there's nothing to
            // rebuild. Collecting them used to drop any declaration the parser didn't
            // recognise (a texture without its own sampler, texture arrays).

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
            //   These need to be above CBUFFER/structs so macros they
            //   define are available (e.g., COMBAT_PARAMETERS from an include).
            //
            // Code: everything else, including #undef/#error/#warning and texture
            //   and sampler declarations. Helper functions reference the CBUFFER, so
            //   they come after it; textures keep their order relative to the helpers.
            //
            // Conditionals (#if/#ifdef/#ifndef/#elif/#else/#endif) go to BOTH.
            //   A define hoisted out of an #ifdef/#else pair has to keep its
            //   condition, otherwise both branches become unconditional and the
            //   last definition wins. Mirroring every conditional keeps both halves
            //   balanced. A block that only wrapped code leaves an empty
            //   #ifdef/#endif pair in the preprocessor half, which is harmless.
            //
            // The split works on logical lines so a multi-line #define (lines
            // ending in '\') moves as one unit instead of leaving its body behind.
            // =====================================================================
            var preprocessorLines = new StringBuilder();
            var codeLines = new StringBuilder();

            foreach (string logicalLine in SplitLogicalLines(hlsl))
            {
                string trimmed = logicalLine.TrimStart();

                if (IsConditionalDirective(trimmed))
                {
                    preprocessorLines.AppendLine(logicalLine);
                    codeLines.AppendLine(logicalLine);
                }
                else if (IsPreprocessorSetupDirective(trimmed))
                {
                    preprocessorLines.AppendLine(logicalLine);
                }
                else
                {
                    codeLines.AppendLine(logicalLine);
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
        /// before CBUFFER/structs (defines macros, includes files, sets keywords).
        ///
        /// Returns true for: #include, #include_with_pragmas, #define, #pragma
        /// Returns false for everything else. Conditionals are handled separately by
        /// <see cref="IsConditionalDirective"/>; #undef, #error and #warning stay with the code.
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

            return false;
        }

        // \b requires a whole directive name, and \s* allows the legal "# if" spelling.
        static readonly Regex s_conditionalDirectiveRegex = new Regex(
            @"^#\s*(if|ifdef|ifndef|elif|else|endif)\b", RegexOptions.Compiled);

        /// <summary>
        /// Check if a line opens, switches or closes a conditional block
        /// (#if, #ifdef, #ifndef, #elif, #else, #endif). These are written to both the
        /// preprocessor and code halves of the split so each half keeps the conditions
        /// its lines were written under.
        /// </summary>
        static bool IsConditionalDirective(string trimmedLine)
        {
            return s_conditionalDirectiveRegex.IsMatch(trimmedLine);
        }

        /// <summary>
        /// Split source into logical lines. A physical line ending in '\' (ignoring
        /// trailing whitespace) is joined to the next one, the same way the HLSL
        /// preprocessor splices lines, so a multi-line #define comes back as one entry.
        ///
        /// Carriage returns are dropped from each physical line. Keeping them would put
        /// "\r" plus AppendLine's own "\r\n" after a trailing '\', and a splice is only
        /// guaranteed when the '\' is directly followed by a single line ending.
        /// </summary>
        static List<string> SplitLogicalLines(string source)
        {
            var logicalLines = new List<string>();
            var current = new StringBuilder();
            bool isContinuing = false;

            foreach (string rawLine in source.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');

                if (isContinuing)
                    current.Append('\n');
                current.Append(line);

                isContinuing = line.TrimEnd().EndsWith("\\");
                if (!isContinuing)
                {
                    logicalLines.Add(current.ToString());
                    current.Clear();
                }
            }

            // Source that ends on a '\' line still has a pending entry
            if (current.Length > 0)
                logicalLines.Add(current.ToString());

            return logicalLines;
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
