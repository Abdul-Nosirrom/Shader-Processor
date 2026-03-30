using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Unified utilities for reading and removing #pragma directives in shader
    /// source code. Replaces the ad-hoc inline regex patterns that were scattered
    /// across ShaderParser, HookProcessor, ForwardBodyInjector, and SurfaceProcessor.
    ///
    /// All methods operate on raw source strings and do not modify ShaderContext.
    /// </summary>
    public static class ShaderPragmaUtility
    {
        //=============================================================================
        // Reading
        //=============================================================================

        /// <summary>
        /// Get the first argument value of a pragma directive.
        /// For <c>#pragma vertex Vert</c>, GetValue(source, "vertex") returns "Vert".
        /// Returns null if the pragma is not found.
        /// </summary>
        public static string GetValue(string source, string pragmaName)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(pragmaName))
                return null;

            var match = Regex.Match(source, $@"#pragma\s+{Regex.Escape(pragmaName)}\s+(\w+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Check whether a pragma directive exists in the source.
        /// Returns true if <c>#pragma pragmaName</c> is found (with at least one argument).
        /// </summary>
        public static bool HasPragma(string source, string pragmaName)
        {
            return GetValue(source, pragmaName) != null;
        }

        /// <summary>
        /// Parse <c>#pragma fragmentOutput:name variableName</c> directives.
        /// Returns a dictionary mapping output names to variable names.
        /// e.g. "#pragma fragmentOutput:albedo myAlbedo" yields { "albedo": "myAlbedo" }.
        /// </summary>
        public static Dictionary<string, string> ParseFragmentOutputs(string source)
        {
            var map = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(source)) return map;

            var matches = Regex.Matches(source, @"#pragma\s+fragmentOutput:(\w+)\s+(\w+)");
            foreach (Match match in matches)
            {
                map[match.Groups[1].Value] = match.Groups[2].Value;
            }

            return map;
        }

        //=============================================================================
        // Removal
        //=============================================================================

        /// <summary>
        /// Remove all occurrences of a pragma directive (including the trailing newline).
        /// For <c>#pragma vertex Vert</c>, Strip(source, "vertex") removes the entire line.
        /// </summary>
        public static string Strip(string source, string pragmaName)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(pragmaName))
                return source;

            return Regex.Replace(source,
                $@"#pragma\s+{Regex.Escape(pragmaName)}\s+\w+\s*\n?", "");
        }

        /// <summary>
        /// Remove all occurrences of multiple pragma directive types.
        /// Equivalent to calling Strip for each name, but in one pass per name.
        /// </summary>
        public static string StripAll(string source, params string[] pragmaNames)
        {
            foreach (var name in pragmaNames)
                source = Strip(source, name);
            return source;
        }

        /// <summary>
        /// Remove all <c>#pragma fragmentOutput:X Y</c> lines from source.
        /// These are custom directives consumed by ForwardBodyInjector and are
        /// not valid HLSL, so they must be stripped before shader compilation.
        /// </summary>
        public static string StripFragmentOutputs(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            return Regex.Replace(source, @"#pragma\s+fragmentOutput:\w+\s+\w+\s*\n?", "");
        }

        /// <summary>
        /// Remove stray <c>#</c> or <c>#pragma</c> tokens that sit alone on a line
        /// (left behind after other pragma stripping operations).
        /// </summary>
        public static string StripOrphanedPragmaLines(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            return Regex.Replace(source, @"^\s*#\s*(pragma)?\s*$", "", RegexOptions.Multiline);
        }
    }
}
