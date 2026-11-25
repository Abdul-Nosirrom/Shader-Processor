using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Utilities for finding, extracting, and modifying named blocks in shader
    /// source code (CBUFFER, Properties, SubShader).
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
        /// Insert entries before the CBUFFER_END of every CBUFFER_START(UnityPerMaterial)
        /// block in source. Used by tag processors and inheritance to add material
        /// property declarations.
        ///
        /// Anchored to UnityPerMaterial blocks specifically - a plain IndexOf("CBUFFER_END")
        /// could hit a differently-named CBUFFER or a comment. All UnityPerMaterial blocks
        /// receive the entries because the SRP batcher requires every pass to declare an
        /// identical UnityPerMaterial layout, so pass-local CBUFFERs must stay in sync.
        /// Commented-out CBUFFER_START / CBUFFER_END tokens are skipped.
        ///
        /// Returns null if no UnityPerMaterial CBUFFER is found.
        /// </summary>
        public static string InsertBeforeCBufferEnd(string source, string entries)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(entries))
                return source;

            List<int> endPositions = FindMaterialCBufferEndPositions(source);
            if (endPositions.Count == 0) return null;

            // Insert back-to-front so earlier positions stay valid
            for (int i = endPositions.Count - 1; i >= 0; i--)
            {
                source = source.Insert(endPositions[i], entries);
            }

            return source;
        }

        /// <summary>
        /// Insert entries on the line after the CBUFFER_END of every
        /// CBUFFER_START(UnityPerMaterial) block in source. The counterpart of
        /// <see cref="InsertBeforeCBufferEnd"/> for declarations that belong next to the
        /// material CBUFFER but outside it (textures and samplers), so every pass with a
        /// pass-local CBUFFER also gets them. Commented-out tokens are skipped.
        ///
        /// Returns null if no UnityPerMaterial CBUFFER is found.
        /// </summary>
        public static string InsertAfterCBufferEnd(string source, string entries)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(entries))
                return source;

            List<int> endPositions = FindMaterialCBufferEndPositions(source);
            if (endPositions.Count == 0) return null;

            // Insert back-to-front so earlier positions stay valid
            for (int i = endPositions.Count - 1; i >= 0; i--)
            {
                int endOfLine = source.IndexOf('\n', endPositions[i]);
                int insertPos = endOfLine < 0 ? source.Length : endOfLine + 1;
                source = source.Insert(insertPos, entries);
            }

            return source;
        }

        /// <summary>
        /// Positions of the CBUFFER_END token closing each UnityPerMaterial block,
        /// in source order. Comment-aware on both ends: a CBUFFER_START inside a
        /// comment isn't a block, and a commented-out CBUFFER_END doesn't close one.
        /// s_cbufferRegex can't do this, because a match that starts in a comment
        /// would swallow the real block that follows it.
        /// </summary>
        static List<int> FindMaterialCBufferEndPositions(string source)
        {
            const string END_TOKEN = "CBUFFER_END";
            var endPositions = new List<int>();

            foreach (Match start in s_cbufferDetectRegex.Matches(source))
            {
                if (IsInComment(source, start.Index))
                    continue;

                int searchFrom = start.Index + start.Length;
                while (true)
                {
                    int end = source.IndexOf(END_TOKEN, searchFrom, StringComparison.Ordinal);
                    if (end < 0)
                        break;

                    if (!IsInComment(source, end))
                    {
                        endPositions.Add(end);
                        break;
                    }

                    searchFrom = end + END_TOKEN.Length;
                }
            }

            return endPositions;
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
        /// including // inside closed block comments on the same line, and
        /// /* openers that are dead because they sit inside a line comment.
        ///
        /// Use this before processing matches from regex to skip commented-out
        /// blocks, passes, pragmas, etc.
        /// </summary>
        public static bool IsInComment(string source, int position)
        {
            // Block comments: find the nearest preceding /* that is actually live.
            // A /* sitting inside a // line comment never opens a block comment -
            // e.g. "// see Assets/Shaders/* for examples" - and honoring it would
            // make everything after that line read as commented out (which, at the
            // importer activation callsite, silently unhooks the shader from the
            // pipeline). Skip dead openers and keep searching earlier.
            int searchFrom = position;
            while (searchFrom >= 0)
            {
                int blockCommentStart = source.LastIndexOf("/*", searchFrom);
                if (blockCommentStart < 0)
                    break;

                if (IsInLineComment(source, blockCommentStart))
                {
                    if (blockCommentStart == 0) break;
                    searchFrom = blockCommentStart - 1;
                    continue;
                }

                int blockCommentEnd = source.IndexOf("*/", blockCommentStart);
                if (blockCommentEnd < 0 || blockCommentEnd > position)
                    return true;

                // Nearest live opener closes before position, so position is not
                // inside a block comment. (An earlier unclosed opener would have
                // made this opener's IndexOf("*/") search fail too.)
                break;
            }

            return IsInLineComment(source, position);
        }

        /// <summary>
        /// Check if a position is preceded by a live // on its own line.
        /// A // inside a closed /* */ pair on the same line doesn't count.
        /// </summary>
        static bool IsInLineComment(string source, int position)
        {
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
