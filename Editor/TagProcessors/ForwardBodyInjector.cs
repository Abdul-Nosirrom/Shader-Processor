using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Injects the forward pass's vertex and fragment bodies into generated passes.
    ///
    /// Vertex: Extracts the forward vertex body, strips boilerplate (struct init, instance
    /// setup, position assignment, return), normalizes variable names to match template
    /// conventions (input/output), rewrites struct names, and injects via
    /// {{FORWARD_VERTEX_BODY}}. What remains is interpolator assignments and any helper
    /// calculations they depend on. DCE strips anything the pass doesn't use.
    ///
    /// Fragment: Extracts the forward fragment body, normalizes the input variable name,
    /// rewrites struct names, swaps every return statement with the pass-specific output
    /// (from GetFragmentReturnExpression), and injects via {{FORWARD_FRAGMENT_BODY}}.
    /// DCE strips unused computations.
    ///
    /// Usage:
    ///   Tags { "InjectForwardBody" = "On" }
    ///
    /// Optional pragmas for passes that need specific surface data:
    ///   #pragma fragmentOutput:albedo myAlbedoVar
    ///   #pragma fragmentOutput:normal myNormalVar
    ///   #pragma fragmentOutput:emission myEmissionVar
    /// </summary>
    [ShaderTagProcessor("InjectForwardBody", priority: 200)]
    public class ForwardBodyInjector : ShaderTagProcessorBase
    {
        public override string TagName => "InjectForwardBody";
        public override int Priority => 200;
        
        //=============================================================================
        // Pass Replacements
        //=============================================================================
        
        public override Dictionary<string, string> GetPassReplacements(ShaderContext ctx, string passName)
        {
            var replacements = new Dictionary<string, string>();
            
            // Vertex body injection
            string vertexBody = BuildVertexBody(ctx, passName);
            if (vertexBody != null)
                replacements["FORWARD_VERTEX_BODY"] = vertexBody;
            
            // Fragment body injection
            string fragmentBody = BuildFragmentBody(ctx, passName);
            if (fragmentBody != null)
                replacements["FORWARD_FRAGMENT_BODY"] = fragmentBody;
            
            return replacements.Count > 0 ? replacements : null;
        }
        
        //=============================================================================
        // Vertex Body
        //=============================================================================
        
        /// <summary>
        /// Extract the forward vertex body, strip boilerplate, normalize variable names
        /// to match template conventions (input/output), rewrite struct names.
        /// Returns null if extraction fails.
        /// </summary>
        static string BuildVertexBody(ShaderContext ctx, string passName)
        {
            string source = ctx.ForwardPass?.HlslProgram;
            string funcName = ctx.ForwardVertexFunctionName;
            
            // Detect user's variable names before extraction
            string userInputName = DetectParameterName(source, funcName) ?? "input";
            
            string body = ExtractFunctionBody(source, funcName);
            if (body == null) return null;
            
            // Detect output variable name from struct init: Interpolators xxx = (Interpolators)0;
            string userOutputName = DetectOutputVariableName(body, ctx.InterpolatorsStructName) ?? "output";
            
            // Strip boilerplate that templates already handle
            body = StripVertexBoilerplate(body, ctx);
            
            // Normalize variable names to match templates (input/output)
            // Do this after stripping so removed lines won't interfere,
            // but before struct rewriting so names are settled first.
            body = NormalizeVariableNames(body, userInputName, userOutputName);
            
            // Rewrite struct names for this pass
            string attrName = passName + "Attributes";
            string interpName = passName + "Interpolators";
            body = HookProcessor.RewriteStructNames(body,
                ctx.AttributesStructName, attrName,
                ctx.InterpolatorsStructName, interpName);
            
            // If nothing meaningful remains after stripping, skip injection
            if (string.IsNullOrWhiteSpace(body)) return null;
            
            return body;
        }
        
        /// <summary>
        /// Strip lines from the vertex body that templates already handle:
        /// struct initialization, instance ID setup, position assignment, and return.
        /// </summary>
        static string StripVertexBoilerplate(string body, ShaderContext ctx)
        {
            string interpName = Regex.Escape(ctx.InterpolatorsStructName);
            string svPosition = ctx.Interpolators?.GetField("SV_POSITION")?.Name ?? "positionCS";
            
            // Strip: Interpolators output = (Interpolators)0;
            body = Regex.Replace(body,
                $@"{interpName}\s+\w+\s*=\s*\({interpName}\)\s*0\s*;",
                "");
            
            // Strip: UNITY_SETUP_INSTANCE_ID(input);
            body = Regex.Replace(body, @"UNITY_SETUP_INSTANCE_ID\s*\(\s*\w+\s*\)\s*;", "");
            
            // Strip: UNITY_TRANSFER_INSTANCE_ID(input, output);
            body = Regex.Replace(body, @"UNITY_TRANSFER_INSTANCE_ID\s*\([^)]*\)\s*;", "");
            
            // Strip: output.positionCS = ...; (single line position assignment)
            body = Regex.Replace(body,
                $@"\w+\.{Regex.Escape(svPosition)}\s*=[^;]+;",
                "");
            
            // Strip: hook calls in function body (prevents double-execution when
            // hooks and body injection coexist — templates emit hook calls separately)
            foreach (var entry in ctx.Hooks.Active)
            {
                body = Regex.Replace(body,
                    $@"\b{Regex.Escape(entry.Value.FunctionName)}\s*\([^)]*\)\s*;", "");
            }
            
            // Strip: return output;
            body = Regex.Replace(body, @"return\s+\w+\s*;", "");
            
            return body;
        }
        
        //=============================================================================
        // Fragment Body
        //=============================================================================
        
        /// <summary>
        /// Extract the forward fragment body, normalize variable names, swap returns,
        /// rewrite struct names. Returns null if extraction fails or the pass doesn't
        /// support injection.
        /// </summary>
        static string BuildFragmentBody(ShaderContext ctx, string passName)
        {
            // Look up the pass injector for its return expression
            var injector = ShaderPassInjectorRegistry.GetByName(passName);
            if (injector == null) return null;
            
            // Parse fragment output pragmas
            var outputMap = ParseFragmentOutputPragmas(ctx);
            
            // Get the resolved return expression
            string expression = ResolveReturnExpression(injector, ctx, outputMap);
            if (expression == null) return null;
            
            string source = ctx.ForwardPass?.HlslProgram;
            string funcName = ctx.ForwardFragmentFunctionName;
            
            // Detect user's parameter name before extraction
            string userInputName = DetectParameterName(source, funcName) ?? "input";
            
            // Extract fragment body
            string body = ExtractFunctionBody(source, funcName);
            if (body == null) return null;
            
            // Normalize input variable name to match templates.
            // Fragment has no output variable (returns directly), so only input matters.
            body = NormalizeVariableNames(body, userInputName);
            
            // Rewrite struct names for this pass
            string attrName = passName + "Attributes";
            string interpName = passName + "Interpolators";
            body = HookProcessor.RewriteStructNames(body,
                ctx.AttributesStructName, attrName,
                ctx.InterpolatorsStructName, interpName);
            
            // Strip: hook calls in function body
            foreach (var entry in ctx.Hooks.Active)
            {
                body = Regex.Replace(body,
                    $@"\b{Regex.Escape(entry.Value.FunctionName)}\s*\([^)]*\)\s*;", "");
            }
            
            // Swap return statements, skipping any that are inside line comments.
            // Without this, a commented return like "//return foo;" gets its replacement
            // injected after the "//", but only the first line is commented. Multi-line
            // replacements (like Meta's MetaInput block) spill out as live code.
            body = SwapReturnStatements(body, expression);
            
            // Strip: UNITY_SETUP_INSTANCE_ID(input);
            body = Regex.Replace(body, @"UNITY_SETUP_INSTANCE_ID\s*\(\s*\w+\s*\)\s*;", "");
            
            return body;
        }
        
        /// <summary>
        /// Replace all 'return ...;' statements with the given expression, but leave
        /// commented-out returns untouched. Uses match position to check if the return
        /// keyword falls after a '//' on the same line.
        /// </summary>
        static string SwapReturnStatements(string body, string expression)
        {
            return Regex.Replace(body, @"return\s+[^;]+;", match =>
            {
                // Find the start of the line containing this return
                int lineStart = body.LastIndexOf('\n', match.Index);
                if (lineStart < 0) lineStart = 0;
                
                // Check if there's a // between the line start and the return keyword
                string beforeReturn = body.Substring(lineStart, match.Index - lineStart);
                if (beforeReturn.Contains("//"))
                    return match.Value; // Leave commented returns unchanged
                
                return expression;
            });
        }
        
        //=============================================================================
        // Variable Name Detection & Normalization
        //=============================================================================
        
        /// <summary>
        /// Detect the parameter name of a function from its signature.
        /// For "Interpolators Vert(Attributes v)" returns "v".
        /// </summary>
        public static string DetectParameterName(string source, string funcName)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(funcName))
                return null;
            
            var match = Regex.Match(source,
                $@"\w+\s+{Regex.Escape(funcName)}\s*\(\s*\w+\s+(\w+)");
            return match.Success ? match.Groups[1].Value : null;
        }
        
        /// <summary>
        /// Detect the output variable name from a struct initialization line.
        /// For "Interpolators o = (Interpolators)0;" returns "o".
        /// </summary>
        public static string DetectOutputVariableName(string body, string interpStructName)
        {
            if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(interpStructName))
                return null;
            
            var match = Regex.Match(body,
                $@"{Regex.Escape(interpStructName)}\s+(\w+)\s*=\s*\({Regex.Escape(interpStructName)}\)\s*0\s*;");
            return match.Success ? match.Groups[1].Value : null;
        }
        
        /// <summary>
        /// Normalize user variable names to match template conventions (input/output).
        /// Uses word-boundary replacement so short names like "o" won't match inside
        /// identifiers like "float" or "color".
        /// </summary>
        public static string NormalizeVariableNames(string body, string userInputName, string userOutputName = null)
        {
            // Rename output first to avoid interaction if user named input "output" (pathological but safe)
            if (userOutputName != null && userOutputName != "output")
                body = Regex.Replace(body, $@"\b{Regex.Escape(userOutputName)}\b", "output");
            
            if (userInputName != "input")
                body = Regex.Replace(body, $@"\b{Regex.Escape(userInputName)}\b", "input");
            
            return body;
        }
        
        //=============================================================================
        // Return Expression Resolution
        //=============================================================================
        
        /// <summary>
        /// Get the fully resolved return expression for a pass injector.
        /// All {{output:X}} and {{SEMANTIC}} references are resolved to concrete names.
        /// This must happen here because TemplateEngine.Process() strips unresolved markers.
        /// </summary>
        static string ResolveReturnExpression(
            ShaderPassInjector injector, ShaderContext ctx, Dictionary<string, string> outputMap)
        {
            string expression = injector.GetFragmentReturnExpression(ctx, false);
            if (string.IsNullOrEmpty(expression)) return null;
            
            // Check if any {{output:X}} references can't be resolved
            if (HasUnresolvedOutputReferences(expression, outputMap))
            {
                expression = injector.GetFragmentReturnExpression(ctx, true);
                if (string.IsNullOrEmpty(expression)) return null;
            }
            
            // Resolve {{output:X}} → variable names from pragmas
            expression = Regex.Replace(expression, @"\{\{output:(\w+)\}\}", match =>
            {
                string name = match.Groups[1].Value;
                return outputMap.TryGetValue(name, out string varName) ? varName : match.Value;
            });
            
            // Resolve {{SEMANTIC}} → field names from structs (same lookup as AddSemanticReplacements)
            expression = ResolveSemanticMarkers(expression, ctx);
            
            return expression;
        }
        
        /// <summary>
        /// Check if an expression has {{output:X}} references that can't be resolved.
        /// </summary>
        static bool HasUnresolvedOutputReferences(string expression, Dictionary<string, string> outputMap)
        {
            var matches = Regex.Matches(expression, @"\{\{output:(\w+)\}\}");
            foreach (Match match in matches)
            {
                if (!outputMap.ContainsKey(match.Groups[1].Value))
                    return true;
            }
            return false;
        }
        
        /// <summary>
        /// Resolve bare {{SEMANTIC}} markers (like {{SV_POSITION}}, {{NORMAL_WS}}) to
        /// concrete field names. Uses the same lookup logic as AddSemanticReplacements
        /// in TemplateEngine so return expressions and templates resolve consistently.
        /// </summary>
        static string ResolveSemanticMarkers(string text, ShaderContext ctx)
        {
            return Regex.Replace(text, @"\{\{(\w+)\}\}", match =>
            {
                string semantic = match.Groups[1].Value;
                
                // NORMAL_WS: try multiple conventions
                if (semantic == "NORMAL_WS")
                {
                    return ctx.Interpolators?.GetField("NORMAL")?.Name
                        ?? ctx.Interpolators?.GetField("NORMALWS")?.Name
                        ?? ctx.Interpolators?.GetField("NORMAL_WS")?.Name
                        ?? "normalWS";
                }
                
                // Check interpolators first, then attributes
                string fieldName = ctx.Interpolators?.GetField(semantic)?.Name
                                ?? ctx.Attributes?.GetField(semantic)?.Name;
                if (fieldName != null) return fieldName;
                
                // Fallback defaults
                switch (semantic)
                {
                    case "SV_POSITION": return "positionCS";
                    case "POSITION":    return "positionOS";
                    case "NORMAL":      return "normalOS";
                    case "TANGENT":     return "tangentOS";
                    case "TEXCOORD0":   return "uv";
                    default:
                        Debug.LogWarning($"[ForwardBodyInjector] Unresolved marker: {match.Value}");
                        return match.Value;
                }
            });
        }
        
        //=============================================================================
        // Pragma Parsing
        //=============================================================================
        
        /// <summary>
        /// Parse #pragma fragmentOutput:name variableName from the forward pass.
        /// </summary>
        static Dictionary<string, string> ParseFragmentOutputPragmas(ShaderContext ctx)
        {
            var map = new Dictionary<string, string>();
            if (ctx.ForwardPass == null) return map;
            
            string source = (ctx.HlslIncludeBlock ?? "") + "\n" + (ctx.ForwardPass.HlslProgram ?? "");
            
            var matches = Regex.Matches(source, @"#pragma\s+fragmentOutput:(\w+)\s+(\w+)");
            foreach (Match match in matches)
            {
                map[match.Groups[1].Value] = match.Groups[2].Value;
            }
            
            return map;
        }
        
        //=============================================================================
        // Shared Helpers
        //=============================================================================
        
        /// <summary>
        /// Extract the inner body of a function (between the outermost braces).
        /// </summary>
        static string ExtractFunctionBody(string source, string funcName)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(funcName))
                return null;
            
            // Match: returnType FuncName(params) [: semantic] {
            var pattern = $@"\w+\s+{Regex.Escape(funcName)}\s*\([^)]*\)\s*(:\s*\w+\s*)?\{{";
            var match = Regex.Match(source, pattern, RegexOptions.Singleline);
            if (!match.Success) return null;
            
            int braceStart = match.Index + match.Length - 1;
            int depth = 1;
            int i = braceStart + 1;
            
            while (i < source.Length && depth > 0)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}') depth--;
                i++;
            }
            
            if (depth != 0) return null;
            
            return source.Substring(braceStart + 1, i - braceStart - 2).Trim();
        }
        
        /// <summary>
        /// Extracts the raw inner body of the vertex program function.
        /// Used by DepthNormalsPass to check if injection already writes normalWS.
        /// </summary>
        public static string ExtractForwardVertexBody(ShaderContext ctx)
        {
            return ExtractFunctionBody(ctx.ForwardPass?.HlslProgram, ctx.ForwardVertexFunctionName);
        }
    }
}
