using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Utilities for finding, extracting, and modifying named blocks in shader
    /// source code (CBUFFER, Properties, SubShader, textures/samplers).
    ///
    /// Block operations use regex for marker detection and delegate to
    /// <see cref="ShaderSourceUtility"/> for brace matching where needed.
    /// </summary>
    public static class ShaderBlockUtility
    {
        //=============================================================================
        // Data Types
        //=============================================================================

        /// <summary>
        /// A located region in shader source, with positions and extracted content.
        /// </summary>
        public struct BlockRegion
        {
            /// <summary>Position of the first character of the block (the start marker).</summary>
            public int StartIndex;

            /// <summary>Position one past the last character of the block (the end marker).</summary>
            public int EndIndex;

            /// <summary>Inner content between the markers (trimmed).</summary>
            public string Content;
        }

        //=============================================================================
        // Pre-compiled Regex
        //=============================================================================

        static readonly Regex s_cbufferRegex = new Regex(
            @"CBUFFER_START\s*\(\s*UnityPerMaterial\s*\)(.*?)CBUFFER_END",
            RegexOptions.Compiled | RegexOptions.Singleline);

        static readonly Regex s_cbufferDetectRegex = new Regex(
            @"CBUFFER_START\s*\(\s*UnityPerMaterial\s*\)",
            RegexOptions.Compiled);

        static readonly Regex s_subShaderOpenRegex = new Regex(
            @"SubShader\s*\{", RegexOptions.Compiled);

        // Texture and sampler declaration patterns
        static readonly Regex s_texture2DRegex = new Regex(
            @"TEXTURE2D\s*\(\s*\w+\s*\)\s*;", RegexOptions.Compiled);
        static readonly Regex s_texture2DArrayRegex = new Regex(
            @"TEXTURE2D_ARRAY\s*\(\s*\w+\s*\)\s*;", RegexOptions.Compiled);
        static readonly Regex s_textureCubeRegex = new Regex(
            @"TEXTURECUBE\s*\(\s*\w+\s*\)\s*;", RegexOptions.Compiled);
        static readonly Regex s_texture3DRegex = new Regex(
            @"TEXTURE3D\s*\(\s*\w+\s*\)\s*;", RegexOptions.Compiled);
        static readonly Regex s_samplerRegex = new Regex(
            @"SAMPLER\s*\(\s*\w+\s*\)\s*;", RegexOptions.Compiled);

        //=============================================================================
        // CBUFFER
        //=============================================================================

        /// <summary>
        /// Check if source contains a CBUFFER_START(UnityPerMaterial) block.
        /// </summary>
        public static bool HasCBuffer(string source)
        {
            if (string.IsNullOrEmpty(source)) return false;
            return s_cbufferDetectRegex.IsMatch(source);
        }

        /// <summary>
        /// Find the CBUFFER_START(UnityPerMaterial)...CBUFFER_END block and return
        /// its region (positions and inner content). Returns null if not found.
        /// </summary>
        public static BlockRegion? FindCBuffer(string source)
        {
            if (string.IsNullOrEmpty(source)) return null;

            var match = s_cbufferRegex.Match(source);
            if (!match.Success) return null;

            return new BlockRegion
            {
                StartIndex = match.Index,
                EndIndex = match.Index + match.Length,
                Content = match.Groups[1].Value.Trim()
            };
        }

        /// <summary>
        /// Get the inner content of the CBUFFER (between CBUFFER_START and CBUFFER_END).
        /// Returns null if no CBUFFER is found.
        /// </summary>
        public static string GetCBufferContent(string source)
        {
            return FindCBuffer(source)?.Content;
        }

        /// <summary>
        /// Remove the entire CBUFFER block from source.
        /// Used during forward pass content extraction where the template provides
        /// its own CBUFFER via the {{CBUFFER}} marker.
        /// </summary>
        public static string StripCBuffer(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            return s_cbufferRegex.Replace(source, "");
        }

        /// <summary>
        /// Insert entries before CBUFFER_END. Used by tag processors and
        /// inheritance to add material property declarations.
        /// Returns null if CBUFFER_END is not found.
        /// </summary>
        public static string InsertBeforeCBufferEnd(string source, string entries)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(entries))
                return source;

            int endPos = source.IndexOf("CBUFFER_END");
            if (endPos < 0) return null;

            return source.Insert(endPos, entries);
        }

        //=============================================================================
        // Texture / Sampler Declarations
        //=============================================================================

        /// <summary>
        /// Remove all texture and sampler declarations from source.
        /// Handles TEXTURE2D, TEXTURE2D_ARRAY, TEXTURE3D, TEXTURECUBE, and SAMPLER.
        /// Used during forward pass content extraction where the template provides
        /// textures via the {{TEXTURES}} marker.
        /// </summary>
        public static string StripTextureDeclarations(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;

            source = s_texture2DRegex.Replace(source, "");
            source = s_texture2DArrayRegex.Replace(source, "");
            source = s_texture3DRegex.Replace(source, "");
            source = s_textureCubeRegex.Replace(source, "");
            source = s_samplerRegex.Replace(source, "");

            return source;
        }

        //=============================================================================
        // SubShader
        //=============================================================================

        /// <summary>
        /// Find the opening position of the SubShader block.
        /// Returns the index of 'S' in "SubShader", or -1 if not found.
        /// </summary>
        public static int FindSubShaderOpen(string source)
        {
            if (string.IsNullOrEmpty(source)) return -1;

            var match = s_subShaderOpenRegex.Match(source);
            return match.Success ? match.Index : -1;
        }

        /// <summary>
        /// Find the closing brace position of the SubShader block.
        /// Returns the index of the '}' character, or -1 if not found.
        /// Uses comment-aware brace matching.
        /// </summary>
        public static int FindSubShaderClose(string source)
        {
            if (string.IsNullOrEmpty(source)) return -1;

            var match = s_subShaderOpenRegex.Match(source);
            if (!match.Success) return -1;

            int end = ShaderSourceUtility.FindMatchingBrace(source, match.Index);
            if (end <= match.Index) return -1;

            // FindMatchingBrace returns one past the }, we want the } itself
            return end - 1;
        }

        //=============================================================================
        // Delimited Blocks
        //=============================================================================

        /// <summary>
        /// Extract the content between a start marker and end marker.
        /// For example, GetDelimitedContent(source, "HLSLINCLUDE", "ENDHLSL")
        /// returns the code between those two keywords.
        /// Returns null if the markers are not found.
        /// </summary>
        public static string GetDelimitedContent(string source, string startMarker, string endMarker)
        {
            if (string.IsNullOrEmpty(source)) return null;

            var pattern = $@"{Regex.Escape(startMarker)}\s*(.*?)\s*{Regex.Escape(endMarker)}";
            var match = Regex.Match(source, pattern, RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : null;
        }

        //=============================================================================
        // Tag Pair Parsing
        //=============================================================================

        static readonly Regex s_tagPairRegex = new Regex(
            @"""(\w+)""\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        /// <summary>
        /// Parse <c>"Key" = "Value"</c> pairs from a Tags block content string.
        /// Returns a case-insensitive dictionary of tag name to tag value.
        ///
        /// The input should be the inner content of a Tags block (between the braces),
        /// not the full shader source. For example:
        /// <c>"RenderPipeline" = "UniversalPipeline" "ShaderGen" = "True"</c>
        /// </summary>
        public static Dictionary<string, string> ParseTagPairs(string tagsContent)
        {
            var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(tagsContent)) return tags;

            foreach (Match match in s_tagPairRegex.Matches(tagsContent))
            {
                tags[match.Groups[1].Value] = match.Groups[2].Value;
            }

            return tags;
        }

        //=============================================================================
        // Pass Block Finding
        //=============================================================================

        /// <summary>
        /// A located Pass block in shader source.
        /// </summary>
        public struct PassBlockRegion
        {
            /// <summary>Position of the "P" in "Pass" keyword.</summary>
            public int StartIndex;

            /// <summary>Position one past the closing brace.</summary>
            public int EndIndex;

            /// <summary>Content between the outermost braces (exclusive, trimmed).</summary>
            public string Content;
        }

        // Comment-aware pass pattern. Handles inline comments between "Pass" and "{":
        //   Pass { ... }           - normal
        //   Pass // test\n { ... } - line comment before brace
        //   Pass /* x */ { ... }   - block comment before brace
        static readonly Regex s_passRegex = new Regex(
            @"Pass\s*(?://[^\n]*)?\s*(?:/\*.*?\*/)?\s*\{",
            RegexOptions.Compiled);

        /// <summary>
        /// Find all uncommented Pass blocks in source. Uses a comment-aware pattern
        /// that handles inline comments between "Pass" and the opening brace.
        /// Skips passes that are inside block or line comments.
        ///
        /// Each returned region includes the start position, end position, and inner
        /// content of the pass.
        /// </summary>
        public static List<PassBlockRegion> FindAllPassBlocks(string source)
        {
            var results = new List<PassBlockRegion>();
            if (string.IsNullOrEmpty(source)) return results;

            foreach (Match match in s_passRegex.Matches(source))
            {
                // Skip passes inside comments
                if (IsInComment(source, match.Index))
                    continue;

                // The regex ends at the opening brace. Find the matching close.
                int braceStart = match.Index + match.Length - 1;
                int braceEnd = ShaderSourceUtility.FindMatchingBrace(source, braceStart);
                if (braceEnd <= braceStart) continue;

                string content = source.Substring(braceStart + 1, braceEnd - braceStart - 2).Trim();

                results.Add(new PassBlockRegion
                {
                    StartIndex = match.Index,
                    EndIndex = braceEnd,
                    Content = content
                });
            }

            return results;
        }

        //=============================================================================
        // Comment Detection
        //=============================================================================

        /// <summary>
        /// Check if a position in source code is inside a comment.
        /// Handles both line comments (//) and block comments (/* */),
        /// including // inside closed block comments on the same line.
        ///
        /// Use this before processing matches from regex to skip commented-out
        /// blocks, passes, pragmas, etc.
        /// </summary>
        public static bool IsInComment(string source, int position)
        {
            // Check block comments first: find the last /* before position
            // and see if it's still open (no closing */ before position).
            int blockCommentStart = source.LastIndexOf("/*", position);
            if (blockCommentStart >= 0)
            {
                int blockCommentEnd = source.IndexOf("*/", blockCommentStart);
                if (blockCommentEnd < 0 || blockCommentEnd > position)
                    return true;
            }

            // Check line comments: scan the current line for //, but skip any //
            // that falls inside a /* */ block on the same line.
            int lineStart = source.LastIndexOf('\n', position);
            if (lineStart < 0) lineStart = 0;
            else lineStart++; // skip past the \n character

            bool inBlock = false;
            for (int i = lineStart; i < position; i++)
            {
                if (!inBlock && i + 1 < source.Length && source[i] == '/' && source[i + 1] == '*')
                {
                    inBlock = true;
                    i++; // skip the *
                }
                else if (inBlock && i + 1 < source.Length && source[i] == '*' && source[i + 1] == '/')
                {
                    inBlock = false;
                    i++; // skip the /
                }
                else if (!inBlock && i + 1 < source.Length && source[i] == '/' && source[i + 1] == '/')
                {
                    return true;
                }
            }

            return false;
        }
    }
}
