using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Unified utilities for finding, extracting, and removing HLSL function
    /// definitions in shader source code.
    ///
    /// Handles edge cases that individual implementations handled in isolation:
    /// - Optional modifiers (inline, static) with same-line-only whitespace
    ///   to prevent cross-line matching (from HookProcessor)
    /// - Nested parentheses in parameter lists from macros or default values
    ///   (from HookProcessor)
    /// - Output semantics (: SV_Target) between closing paren and opening
    ///   brace (from HookProcessor, ForwardBodyInjector)
    /// - Comment-aware brace matching via ShaderSourceUtility (from ShaderParser)
    /// - Multiple definitions behind #if guards, selecting the longest body
    ///   (from ForwardBodyInjector)
    /// </summary>
    public static class ShaderFunctionUtility
    {
        //=============================================================================
        // Data Types
        //=============================================================================

        /// <summary>
        /// Parsed representation of an HLSL function definition.
        /// </summary>
        public struct FunctionInfo
        {
            /// <summary>Return type (e.g. "void", "half4", "Interpolators").</summary>
            public string ReturnType;

            /// <summary>Function name.</summary>
            public string Name;

            /// <summary>
            /// Raw parameter list string between the parentheses, not including
            /// the parens themselves. e.g. "inout Attributes input, float3 offset"
            /// </summary>
            public string ParameterList;

            /// <summary>
            /// Output semantic if present (e.g. "SV_Target", "SV_POSITION"),
            /// or null if the function has no output semantic.
            /// </summary>
            public string Semantic;

            /// <summary>
            /// Inner body text between the outermost braces (exclusive of braces),
            /// trimmed of leading/trailing whitespace.
            /// </summary>
            public string Body;

            /// <summary>
            /// Complete function text from the start of the signature (including
            /// any modifier) through the closing brace.
            /// </summary>
            public string FullText;

            /// <summary>Position of the first character of the function in source.</summary>
            public int StartIndex;

            /// <summary>Position one past the closing brace (matches FindMatchingBrace convention).</summary>
            public int EndIndex;
        }

        /// <summary>
        /// A single parsed parameter from a function signature.
        /// </summary>
        public struct ParameterInfo
        {
            /// <summary>Type name (e.g. "Attributes", "float3", "Light").</summary>
            public string Type;

            /// <summary>Parameter name (e.g. "input", "offset").</summary>
            public string Name;

            /// <summary>Whether the parameter has an "inout" qualifier.</summary>
            public bool IsInOut;

            /// <summary>Whether the parameter has an "out" qualifier.</summary>
            public bool IsOut;

            /// <summary>Whether the parameter has an "in" qualifier.</summary>
            public bool IsIn;
        }

        //=============================================================================
        // Pre-compiled Regex
        //=============================================================================

        // Matches the start of a function signature up to the opening paren.
        // The optional modifier group uses [ \t]+ (not \s+) to prevent matching
        // words from unrelated lines above the function. This was a real bug
        // found when \s+ allowed CBUFFER_END to be eaten as a "modifier".
        //
        // Captures:
        //   Group 1 (optional): modifier (e.g. "inline")
        //   Group 2: return type (e.g. "void", "half4")
        //
        // The function name is injected via string interpolation.
        // Pattern ends at the opening paren '(' which begins the parameter list.
        const string SigPatternTemplate =
            @"(\w+[ \t]+)?" +      // Optional modifier, same line only
            @"(\w+)\s+" +          // Return type
            @"{0}\s*" +            // Function name (injected)
            @"\(";                 // Opening paren

        //=============================================================================
        // Finding Functions
        //=============================================================================

        /// <summary>
        /// Find a function definition by name in source code.
        /// Returns the first match, or null if the function is not found.
        ///
        /// Handles optional modifiers, nested parens in parameter lists,
        /// output semantics (: SV_Target), and comment-aware brace matching.
        /// </summary>
        public static FunctionInfo? FindFunction(string source, string funcName)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(funcName))
                return null;

            string sigPattern = string.Format(SigPatternTemplate, Regex.Escape(funcName));
            var match = Regex.Match(source, sigPattern,
                RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline);

            if (!match.Success)
                return null;

            return ParseFunctionFromSignatureMatch(source, match, funcName);
        }

        /// <summary>
        /// Find all definitions of a function by name. There may be multiple
        /// definitions behind #if/#else guards (e.g. a stub and a real
        /// implementation). Returns an empty list if none are found.
        /// </summary>
        public static List<FunctionInfo> FindAllFunctions(string source, string funcName)
        {
            var results = new List<FunctionInfo>();
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(funcName))
                return results;

            string sigPattern = string.Format(SigPatternTemplate, Regex.Escape(funcName));
            var matches = Regex.Matches(source, sigPattern,
                RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline);

            foreach (Match match in matches)
            {
                var info = ParseFunctionFromSignatureMatch(source, match, funcName);
                if (info != null)
                    results.Add(info.Value);
            }

            return results;
        }

        /// <summary>
        /// Find the "real" definition of a function, preferring the longest body.
        /// This handles the case where a function has multiple definitions behind
        /// #if guards (a short stub + the real implementation). Returns null if
        /// no definition is found.
        /// </summary>
        public static FunctionInfo? FindLongestFunction(string source, string funcName)
        {
            var all = FindAllFunctions(source, funcName);
            if (all.Count == 0) return null;
            if (all.Count == 1) return all[0];

            FunctionInfo longest = all[0];
            for (int i = 1; i < all.Count; i++)
            {
                if ((all[i].Body?.Length ?? 0) > (longest.Body?.Length ?? 0))
                    longest = all[i];
            }
            return longest;
        }

        //=============================================================================
        // Removing Functions
        //=============================================================================

        /// <summary>
        /// Remove the first definition of a function from source code.
        /// Returns the modified source, or the original source unchanged
        /// if the function is not found.
        /// </summary>
        public static string RemoveFunction(string source, string funcName)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(funcName))
                return source;

            var info = FindFunction(source, funcName);
            if (info == null)
                return source;

            return source.Substring(0, info.Value.StartIndex)
                 + source.Substring(info.Value.EndIndex);
        }

        //=============================================================================
        // Removing Struct Declarations
        //=============================================================================

        /// <summary>
        /// Remove a struct declaration (struct Name { ... };) from source code.
        /// Uses comment-aware brace matching. Includes the trailing semicolon
        /// if present.
        /// </summary>
        public static string RemoveStructDeclaration(string source, string structName)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(structName))
                return source;

            var match = Regex.Match(source, $@"struct\s+{Regex.Escape(structName)}\s*\{{");
            if (!match.Success) return source;

            int startIndex = match.Index;
            int endIndex = ShaderSourceUtility.FindMatchingBrace(source, startIndex);

            if (endIndex <= startIndex)
                return source;

            // Include trailing semicolon if present
            if (endIndex < source.Length && source[endIndex] == ';')
                endIndex++;

            return source.Remove(startIndex, endIndex - startIndex);
        }

        //=============================================================================
        // Parameter Parsing
        //=============================================================================

        /// <summary>
        /// Parse the parameter list string into structured parameter info.
        /// Handles qualifiers (inout, out, in) and nested parens in default values.
        /// </summary>
        public static List<ParameterInfo> ParseParameters(string parameterList)
        {
            var results = new List<ParameterInfo>();
            if (string.IsNullOrWhiteSpace(parameterList))
                return results;

            // Split on commas at depth 0 (handles nested parens in default values)
            var paramStrings = SplitParameterList(parameterList);

            foreach (string raw in paramStrings)
            {
                string trimmed = raw.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                var info = new ParameterInfo();

                // Split into tokens on whitespace
                var tokens = trimmed.Split(
                    new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length < 2)
                    continue; // Malformed, skip

                int typeIndex = 0;

                // Check for qualifiers
                if (tokens[0] == "inout")
                {
                    info.IsInOut = true;
                    typeIndex = 1;
                }
                else if (tokens[0] == "out")
                {
                    info.IsOut = true;
                    typeIndex = 1;
                }
                else if (tokens[0] == "in")
                {
                    info.IsIn = true;
                    typeIndex = 1;
                }

                if (typeIndex >= tokens.Length - 1)
                    continue; // Not enough tokens for type + name

                info.Type = tokens[typeIndex];

                // The name is the last token (might have a semantic after it,
                // but struct fields with semantics are a different context)
                info.Name = tokens[tokens.Length - 1];

                // Strip trailing semantic if present (e.g. "isFrontFace" from
                // "bool isFrontFace : SV_IsFrontFace" in extra frag params).
                // In a parameter list context, the colon and semantic are part of
                // a separate token, so the name is clean. But just in case:
                int colonPos = info.Name.IndexOf(':');
                if (colonPos >= 0)
                    info.Name = info.Name.Substring(0, colonPos).Trim();

                results.Add(info);
            }

            return results;
        }

        /// <summary>
        /// Count the number of parameters in a raw parameter list string.
        /// Handles nested parentheses in default values.
        /// Returns 0 for an empty parameter list.
        /// </summary>
        public static int CountParameters(string parameterList)
        {
            if (string.IsNullOrWhiteSpace(parameterList))
                return 0;

            return SplitParameterList(parameterList).Count;
        }

        //=============================================================================
        // Convenience Accessors
        //=============================================================================

        /// <summary>
        /// Extract just the return type and first parameter type from a function.
        /// Useful for detecting struct names from vertex function signatures
        /// (return type = Interpolators, first param type = Attributes).
        /// Returns null if the function is not found or has no parameters.
        /// </summary>
        public static (string returnType, string firstParamType)? GetSignatureTypes(
            string source, string funcName)
        {
            var info = FindFunction(source, funcName);
            if (info == null) return null;

            var parameters = ParseParameters(info.Value.ParameterList);
            if (parameters.Count == 0) return null;

            return (info.Value.ReturnType, parameters[0].Type);
        }

        //=============================================================================
        // Internal Helpers
        //=============================================================================

        /// <summary>
        /// Parse a function definition starting from a signature regex match.
        /// Walks the parameter list (handling nested parens), skips optional
        /// output semantics, and uses comment-aware brace matching for the body.
        /// Returns null if parsing fails at any phase.
        /// </summary>
        static FunctionInfo? ParseFunctionFromSignatureMatch(
            string source, Match sigMatch, string funcName)
        {
            // Phase 1: Extract return type from regex capture
            string modifier = sigMatch.Groups[1].Value; // may be empty
            string returnType = sigMatch.Groups[2].Value;

            // Phase 2: Walk the parameter list to find the matching close paren.
            // Can't use [^)]* because parameters may contain nested parens
            // (e.g. default values like max(0, 1) or macro arguments).
            int parenStart = sigMatch.Index + sigMatch.Length - 1; // the '(' char
            int pos = parenStart + 1;
            int parenDepth = 1;

            while (pos < source.Length && parenDepth > 0)
            {
                if (source[pos] == '(') parenDepth++;
                else if (source[pos] == ')') parenDepth--;
                pos++;
            }

            if (parenDepth != 0)
                return null; // Unbalanced parens

            // pos is now one past the closing ')'
            string paramList = source.Substring(parenStart + 1, pos - parenStart - 2);

            // Phase 3: Skip whitespace and optional output semantic (: SV_Target)
            int afterParams = pos;
            while (afterParams < source.Length && char.IsWhiteSpace(source[afterParams]))
                afterParams++;

            string semantic = null;
            if (afterParams < source.Length && source[afterParams] == ':')
            {
                afterParams++; // skip ':'
                while (afterParams < source.Length && char.IsWhiteSpace(source[afterParams]))
                    afterParams++;

                int semanticStart = afterParams;
                while (afterParams < source.Length &&
                       (char.IsLetterOrDigit(source[afterParams]) || source[afterParams] == '_'))
                    afterParams++;

                semantic = source.Substring(semanticStart, afterParams - semanticStart);

                while (afterParams < source.Length && char.IsWhiteSpace(source[afterParams]))
                    afterParams++;
            }

            // Phase 4: Expect opening brace, then find matching close
            if (afterParams >= source.Length || source[afterParams] != '{')
                return null;

            int braceEnd = ShaderSourceUtility.FindMatchingBrace(source, afterParams);
            if (braceEnd <= afterParams)
                return null;

            // Build the result
            int startIndex = sigMatch.Index;
            string body = source.Substring(afterParams + 1, braceEnd - afterParams - 2).Trim();
            string fullText = source.Substring(startIndex, braceEnd - startIndex);

            return new FunctionInfo
            {
                ReturnType = returnType,
                Name = funcName,
                ParameterList = paramList,
                Semantic = semantic,
                Body = body,
                FullText = fullText,
                StartIndex = startIndex,
                EndIndex = braceEnd
            };
        }

        /// <summary>
        /// Split a parameter list string on commas at depth 0.
        /// Handles nested parentheses from default values or macro arguments
        /// (e.g. "float a, float b = max(0, 1)" splits into two, not three).
        /// </summary>
        static List<string> SplitParameterList(string paramList)
        {
            var parts = new List<string>();
            int depth = 0;
            int start = 0;

            for (int i = 0; i < paramList.Length; i++)
            {
                char c = paramList[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(paramList.Substring(start, i - start));
                    start = i + 1;
                }
            }

            // Add the last segment
            if (start < paramList.Length)
                parts.Add(paramList.Substring(start));

            return parts;
        }
    }
}