using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Resolves <c>#include</c> directives by inlining the referenced file contents,
    /// producing a "flattened" source string for parsing purposes.
    ///
    /// The resolved source is only used for finding structs, functions, CBUFFERs, and
    /// pragmas during the parsing stage. The actual shader output keeps the original
    /// <c>#include</c> directives so the shader compiler handles them normally.
    ///
    /// Supports recursive resolution with cycle detection, relative and package paths,
    /// and a configurable filter for which includes should be resolved (so we can skip
    /// large Unity library includes we don't need to parse).
    /// </summary>
    public static class ShaderIncludeResolver
    {
        // Matches #include "path" and #include_with_pragmas "path"
        // Does NOT match angle-bracket includes (#include <file>) which are
        // Unity built-in system includes we never need to parse.
        static readonly Regex s_includeRegex = new Regex(
            @"^\s*#include(?:_with_pragmas)?\s+""([^""]+)""",
            RegexOptions.Compiled | RegexOptions.Multiline);

        //=============================================================================
        // Include Filter
        //=============================================================================

        /// <summary>
        /// Determines whether an include path should be resolved (inlined) or skipped.
        /// Override this to control which files get pulled into the resolved source.
        ///
        /// By default, Unity package includes (Packages/com.unity.*) are skipped since
        /// they're large and don't contain user-defined structs, functions, or CBUFFERs.
        /// Everything else (project Assets/, custom packages, relative paths) is resolved.
        /// </summary>
        public static Func<string, bool> ShouldResolve = DefaultShouldResolve;

        static bool DefaultShouldResolve(string includePath)
        {
            // Skip Unity's own package includes (URP, HDRP, Core RP, ShaderGraph, etc.)
            // These are massive and don't contain user code we need to parse.
            if (includePath.StartsWith("Packages/com.unity.", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        //=============================================================================
        // Public API
        //=============================================================================

        /// <summary>
        /// Resolve all #include directives in the source by inlining the referenced
        /// files' contents. Recursively resolves includes within included files.
        ///
        /// The result is a flattened string suitable for parsing. The original #include
        /// lines are replaced with the file contents wrapped in markers for debugging.
        ///
        /// Returns the original source unchanged if no resolvable includes are found
        /// or if the source is null/empty.
        /// </summary>
        /// <param name="source">The HLSL source text to resolve.</param>
        /// <param name="baseDirectory">
        /// The directory of the file containing the source, used for resolving
        /// relative include paths. For the main shader file, this is ctx.ShaderDirectory.
        /// </param>
        public static string Resolve(string source, string baseDirectory)
        {
            if (string.IsNullOrEmpty(source))
                return source;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return ResolveRecursive(source, baseDirectory, visited);
        }

        /// <summary>
        /// Resolve includes in a source string, providing the set of already-visited
        /// file paths (for use across multiple resolve calls that should share cycle
        /// detection, e.g. resolving HLSLINCLUDE and pass HLSL from the same shader).
        /// </summary>
        public static string Resolve(string source, string baseDirectory,
            HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(source))
                return source;

            return ResolveRecursive(source, baseDirectory, visited);
        }

        //=============================================================================
        // Path Resolution
        //=============================================================================

        /// <summary>
        /// Resolve an include path to an absolute file path on disk.
        /// Handles Unity package paths (Packages/...) and relative paths.
        /// Returns null if the file cannot be found.
        /// </summary>
        public static string ResolveIncludePath(string includePath, string baseDirectory)
        {
            if (string.IsNullOrEmpty(includePath))
                return null;

            // Package path (Packages/com.foo.bar/path/to/file.hlsl)
            // Unity's virtual filesystem maps Packages/ to the actual package location.
            // Path.GetFullPath handles this when run inside the Unity editor.
            if (includePath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                string resolved = Path.GetFullPath(includePath);
                if (File.Exists(resolved))
                    return resolved;

                // Fallback: some Unity versions need the path relative to the project root
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                resolved = Path.GetFullPath(Path.Combine(projectRoot, includePath));
                if (File.Exists(resolved))
                    return resolved;
            }

            // Project-relative path (Assets/Shaders/Shared/Common.hlsl)
            if (includePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string resolved = Path.GetFullPath(Path.Combine(projectRoot, includePath));
                if (File.Exists(resolved))
                    return resolved;
            }

            // Relative path (../Shared/Common.hlsl, ./Utils.hlsl, just Utils.hlsl)
            if (!string.IsNullOrEmpty(baseDirectory))
            {
                string resolved = Path.GetFullPath(Path.Combine(baseDirectory, includePath));
                if (File.Exists(resolved))
                    return resolved;
            }

            return null;
        }

        //=============================================================================
        // Internal
        //=============================================================================

        static string ResolveRecursive(string source, string baseDirectory,
            HashSet<string> visited)
        {
            // Process includes from bottom to top so inserting content doesn't shift
            // the positions of earlier matches. Collect all matches first.
            var matches = new List<Match>();
            foreach (Match match in s_includeRegex.Matches(source))
            {
                matches.Add(match);
            }

            if (matches.Count == 0)
                return source;

            var result = new StringBuilder(source);

            // Process in reverse order so earlier insertions don't shift later positions
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var match = matches[i];
                string includePath = match.Groups[1].Value;

                // Skip includes inside comments (line or block)
                if (ShaderBlockUtility.IsInComment(source, match.Index))
                    continue;

                // Check filter
                if (!ShouldResolve(includePath))
                    continue;

                // Resolve to absolute path
                string absolutePath = ResolveIncludePath(includePath, baseDirectory);
                if (absolutePath == null)
                    continue;

                // Normalize for cycle detection
                string normalizedPath = Path.GetFullPath(absolutePath);

                // Skip if already visited (cycle prevention / #pragma once equivalent)
                if (visited.Contains(normalizedPath))
                    continue;

                visited.Add(normalizedPath);

                // Read the included file
                string includeContent;
                try
                {
                    includeContent = File.ReadAllText(absolutePath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"[ShaderIncludeResolver] Failed to read '{includePath}': {e.Message}");
                    continue;
                }

                // Resolve includes within this file (recursive)
                string includeDirectory = Path.GetDirectoryName(absolutePath);
                includeContent = ResolveRecursive(includeContent, includeDirectory, visited);

                // Replace the #include line with the resolved content.
                // Keep the original line as a comment for debugging.
                string replacement =
                    $"// [Resolved] {match.Value.Trim()}\n{includeContent}\n// [/Resolved]";

                result.Remove(match.Index, match.Length);
                result.Insert(match.Index, replacement);
            }

            return result.ToString();
        }
    }
}
