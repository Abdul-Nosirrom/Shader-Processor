using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Resolves shader inheritance. When a child shader declares
    /// <c>"Inherit" = "Parent/ShaderName"</c> in its SubShader tags, this
    /// loads the parent source, replaces the shader name, and merges the
    /// child's overrides on top.
    ///
    /// The result is a complete shader source that the rest of the pipeline
    /// processes normally - it doesn't know inheritance happened.
    ///
    /// <para><b>Features:</b></para>
    /// <list type="bullet">
    /// <item>SubShader tag merging (insert-or-replace by key)</item>
    /// <item>Property overrides and additions (with auto-generated HLSL declarations)</item>
    /// <item>Pass exclusion via "ExcludePasses" tag</item>
    /// <item>Pass-level tag and render state overrides (matched by Name)</item>
    /// <item>HLSLINCLUDE appending for hook functions and utilities</item>
    /// <item>InheritHook injection points for parent-defined extensibility</item>
    /// <item>Pass injection markers ([InjectBasePasses], [InjectPass:X])</item>
    /// </list>
    ///
    /// <para><b>Usage:</b></para>
    /// <code>
    /// Shader "VFX/Tessellated"
    /// {
    ///     Properties
    ///     {
    ///         _AlphaCutoff("Alpha Cutoff", Range(0.1, 0.9)) = 0.5
    ///         _RimPower("Rim Power", Range(0, 10)) = 3.0  // auto-declares float _RimPower in CBUFFER
    ///     }
    ///     SubShader
    ///     {
    ///         Tags
    ///         {
    ///             "Inherit" = "VFX/Master"
    ///             "Tessellation" = "On"
    ///             "ExcludePasses" = "Meta"
    ///         }
    ///
    ///         Pass
    ///         {
    ///             Name "Forward"
    ///             Tags { "LightMode" = "UniversalForward" }
    ///             Cull Back
    ///         }
    ///
    ///         HLSLINCLUDE
    ///         #pragma InheritHook ModifyColor ApplyRim
    ///         void ApplyRim(inout float4 col, float3 nrm)
    ///         {
    ///             col.rgb += pow(1 - saturate(dot(nrm, _ViewDir)), _RimPower);
    ///         }
    ///         ENDHLSL
    ///
    ///         [InjectBasePasses]
    ///     }
    /// }
    /// </code>
    ///
    /// <para><b>Limitations:</b></para>
    /// <list type="bullet">
    /// <item>Single-level only - the parent cannot itself use Inherit.</item>
    /// <item>Pass overrides match by Name only. Unnamed passes can't be overridden.</item>
    /// <item>Property overrides work per-line. Multi-line attribute chains on the
    ///   parent property are not replaced, only the declaration line itself.</item>
    /// </list>
    /// </summary>
    public static class ShaderInheritance
    {
        //=============================================================================
        // Result
        //=============================================================================
        
        /// <summary>
        /// Result of inheritance resolution.
        /// </summary>
        public struct ResolveResult
        {
            /// <summary>The resolved shader source (merged parent + child overrides).</summary>
            public string Source;
            
            /// <summary>
            /// Asset path of the parent shader, or null if no inheritance was used.
            /// Used by the importer to register a dependency so the child is
            /// reimported when the parent changes.
            /// </summary>
            public string ParentPath;
            
            /// <summary>
            /// Shader name of the parent (e.g., "VFX/VFXMaster"), or null if no inheritance.
            /// Used by the editor inspector to display inheritance info.
            /// </summary>
            public string ParentName;
        }
        
        //=============================================================================
        // Pre-compiled Regex
        //=============================================================================
        
        static readonly Regex s_shaderNameRegex = new Regex(
            @"Shader\s*""([^""]+)""", RegexOptions.Compiled);
        
        static readonly Regex s_inheritTagRegex = new Regex(
            @"""Inherit""\s*=\s*""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        
        static readonly Regex s_subShaderTagsBlockRegex = new Regex(
            @"(SubShader\s*\{[^{]*?)(Tags\s*\{)([^}]*)\}",
            RegexOptions.Compiled | RegexOptions.Singleline);
        
        static readonly Regex s_subShaderOpenRegex = new Regex(
            @"SubShader\s*\{", RegexOptions.Compiled);
        
        static readonly Regex s_markerRegex = new Regex(
            @"\[Inject(?:BasePasses|Pass:\w+)\]", RegexOptions.Compiled);
        
        static readonly Regex s_relativeIncludeRegex = new Regex(
            @"(#include(?:_with_pragmas)?\s+"")(\.\./[^""]+)("")",
            RegexOptions.Compiled);
        
        static readonly Regex s_propertiesOpenRegex = new Regex(
            @"Properties\s*\{", RegexOptions.Compiled);
        
        static readonly Regex s_propertyDeclRegex = new Regex(
            @"(_\w+)\s*\(\s*""", RegexOptions.Compiled);
        
        static readonly Regex s_passNameRegex = new Regex(
            @"Name\s*""([^""]+)""", RegexOptions.Compiled);
        
        static readonly Regex s_passTagsBlockRegex = new Regex(
            @"Tags\s*\{([^}]*)\}", RegexOptions.Compiled);
        
        static readonly Regex s_hlslIncludeBlockRegex = new Regex(
            @"^\s*HLSLINCLUDE\s*(.*?)\s*ENDHLSL",
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.Multiline);
        
        static readonly Regex s_cbufferAppendRegex = new Regex(
            @"CBUFFER_APPEND\s*(.*?)\s*CBUFFER_END",
            RegexOptions.Compiled | RegexOptions.Singleline);
        
        static readonly Regex s_propertyTypeRegex = new Regex(
            @"_(\w+)\s*\(\s*""[^""]*""\s*,\s*(Float|Range\s*\([^)]*\)|Int|Color|Vector|2D|3D|Cube)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        
        static readonly Regex s_inheritHookPragmaRegex = new Regex(
            @"#pragma\s+InheritHook\s+(\w+)\s+(\w+)", RegexOptions.Compiled);
        
        static readonly Regex s_inheritHookMarkerRegex = new Regex(
            @"\{\{InheritHook:(\w+)\(([^)]*)\)\}\}", RegexOptions.Compiled);
        
        static readonly Regex s_renderStateRegex = new Regex(
            @"^\s*(Cull|ZWrite|ZTest|Blend|ColorMask|Offset)\s+(.+)$",
            RegexOptions.Compiled | RegexOptions.Multiline);
        
        //=============================================================================
        // Public API
        //=============================================================================
        
        /// <summary>
        /// Check if a shader source uses the Inherit tag.
        /// Used by the preprocessor to decide whether to activate the custom importer.
        /// </summary>
        public static bool UsesInheritance(string source)
        {
            return s_inheritTagRegex.IsMatch(source);
        }
        
        /// <summary>
        /// Resolve shader inheritance. If the source contains an "Inherit" tag,
        /// loads the parent shader, replaces the name, merges tags, and appends
        /// any pass injection markers from the child.
        /// Returns the source unchanged if no "Inherit" tag is found.
        /// </summary>
        public static ResolveResult Resolve(string childSource, string childPath)
        {
            var result = new ResolveResult { Source = childSource, ParentPath = null };
            
            // Check for Inherit tag
            var inheritMatch = s_inheritTagRegex.Match(childSource);
            if (!inheritMatch.Success)
                return result;
            
            string parentName = inheritMatch.Groups[1].Value;
            
            // Extract child shader name
            var childNameMatch = s_shaderNameRegex.Match(childSource);
            if (!childNameMatch.Success)
            {
                Debug.LogError($"[ShaderInheritance] Could not extract Shader name from {childPath}");
                return result;
            }
            string childName = childNameMatch.Groups[1].Value;
            
            // Find parent shader asset
            string parentPath = FindShaderAssetPath(parentName);
            if (parentPath == null)
            {
                Debug.LogError($"[ShaderInheritance] Parent shader \"{parentName}\" not found " +
                    $"(referenced by {childPath}). Try reimporting the parent shader first.");
                return result;
            }
            
            // Guard: self-inheritance
            string normalizedChild = Path.GetFullPath(childPath);
            string normalizedParent = Path.GetFullPath(parentPath);
            if (string.Equals(normalizedChild, normalizedParent, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError($"[ShaderInheritance] Shader \"{childName}\" cannot inherit from itself.");
                return result;
            }
            
            string parentSource = File.ReadAllText(parentPath);
            
            // Guard: chained inheritance
            if (s_inheritTagRegex.IsMatch(parentSource))
            {
                Debug.LogError($"[ShaderInheritance] Parent shader \"{parentName}\" also uses Inherit. " +
                    "Chained inheritance is not supported.");
                return result;
            }
            
            // Build merged source
            string merged = parentSource;
            
            // Step 1: Replace shader name (first occurrence only)
            merged = s_shaderNameRegex.Replace(merged, $"Shader \"{childName}\"", 1);
            
            // Step 2: Find new child properties (before merge, so we can compare against parent)
            var newPropertyLines = FindNewChildProperties(merged, childSource);
            
            // Step 3: Merge child properties into parent (override defaults, add new)
            merged = MergeProperties(merged, childSource);
            
            // Step 4: Inject HLSL declarations for newly added properties
            // Scalars go in CBUFFER, textures go outside with TEXTURE2D/SAMPLER
            if (newPropertyLines.Count > 0)
                merged = InjectPropertyDeclarations(merged, newPropertyLines);
            
            // Step 5: Collect child's SubShader tags (excluding Inherit)
            var childTags = ParseChildTags(childSource);
            
            // Step 6: Extract ExcludePasses before tag merge (not a real ShaderLab tag)
            string excludePasses = null;
            if (childTags.TryGetValue("ExcludePasses", out excludePasses))
                childTags.Remove("ExcludePasses");
            
            // Step 7: Merge child tags into parent's SubShader Tags block
            merged = MergeSubShaderTags(merged, childTags);
            
            // Step 8: Remove excluded passes
            if (!string.IsNullOrEmpty(excludePasses))
                merged = ExcludePasses(merged, excludePasses);
            
            // Step 9: Apply child pass overrides (tags, render state)
            merged = ApplyPassOverrides(merged, childSource);
            
            // Step 10: Append child HLSLINCLUDE content (hook functions, includes)
            merged = AppendChildHlslInclude(merged, childSource);
            
            // Step 11: Resolve InheritHook markers using child's pragma bindings
            merged = ResolveInheritHooks(merged, childSource);
            
            // Step 12: Append any pass injection markers from the child
            merged = AppendChildMarkers(merged, childSource);
            
            // Step 13: Rewrite relative #include paths so they resolve correctly
            // from any child location. Relative paths (../../foo.hlsl) are resolved
            // against the parent's directory and converted to project-relative paths.
            merged = RewriteRelativeIncludes(merged, parentPath);
            
            result.Source = merged;
            result.ParentPath = parentPath;
            result.ParentName = parentName;
            
            Debug.Log($"[ShaderInheritance] Resolved \"{childName}\" from parent \"{parentName}\"");
            return result;
        }
        
        //=============================================================================
        // Tag Parsing
        //=============================================================================
        
        /// <summary>
        /// Parse tag key-value pairs from the child's SubShader Tags block,
        /// excluding the "Inherit" tag itself.
        /// </summary>
        static Dictionary<string, string> ParseChildTags(string childSource)
        {
            var tagsMatch = s_subShaderTagsBlockRegex.Match(childSource);
            if (!tagsMatch.Success)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            var tags = ShaderBlockUtility.ParseTagPairs(tagsMatch.Groups[3].Value);
            
            // Remove the Inherit tag itself (it's metadata, not a real shader tag)
            tags.Remove("Inherit");
            
            return tags;
        }
        
        //=============================================================================
        // Tag Merging
        //=============================================================================
        
        /// <summary>
        /// Merge child tags into the parent's SubShader Tags block.
        /// Existing tags with the same key are replaced; new tags are appended.
        /// If the parent has no Tags block in its SubShader, one is inserted.
        /// </summary>
        static string MergeSubShaderTags(string source, Dictionary<string, string> childTags)
        {
            if (childTags.Count == 0) return source;
            
            var tagsMatch = s_subShaderTagsBlockRegex.Match(source);
            
            if (tagsMatch.Success)
            {
                string tagsContent = tagsMatch.Groups[3].Value;
                
                foreach (var kvp in childTags)
                {
                    // Check if this tag key already exists in the parent
                    var existingTag = Regex.Match(tagsContent,
                        $@"""(?i:{Regex.Escape(kvp.Key)})""\s*=\s*""[^""]+""");
                    
                    if (existingTag.Success)
                    {
                        // Replace the existing tag value
                        string newTag = $"\"{kvp.Key}\" = \"{kvp.Value}\"";
                        tagsContent = tagsContent.Substring(0, existingTag.Index)
                            + newTag
                            + tagsContent.Substring(existingTag.Index + existingTag.Length);
                    }
                    else
                    {
                        // Append new tag
                        tagsContent = tagsContent.TrimEnd()
                            + $"\n            \"{kvp.Key}\" = \"{kvp.Value}\"\n        ";
                    }
                }
                
                // Reconstruct: prefix + "Tags {" + modified content + "}"
                string replacement = tagsMatch.Groups[1].Value
                    + tagsMatch.Groups[2].Value
                    + tagsContent + "}";
                
                source = source.Substring(0, tagsMatch.Index)
                    + replacement
                    + source.Substring(tagsMatch.Index + tagsMatch.Length);
            }
            else
            {
                // Parent has no Tags block - insert one after "SubShader {"
                var subShaderMatch = s_subShaderOpenRegex.Match(source);
                if (subShaderMatch.Success)
                {
                    int insertPos = subShaderMatch.Index + subShaderMatch.Length;
                    
                    var sb = new StringBuilder();
                    sb.AppendLine();
                    sb.AppendLine("        Tags");
                    sb.AppendLine("        {");
                    foreach (var kvp in childTags)
                    {
                        sb.AppendLine($"            \"{kvp.Key}\" = \"{kvp.Value}\"");
                    }
                    sb.Append("        }");
                    
                    source = source.Substring(0, insertPos)
                        + sb.ToString()
                        + source.Substring(insertPos);
                }
            }
            
            return source;
        }
        
        //=============================================================================
        // Property Merging
        //=============================================================================
        
        /// <summary>
        /// Merge child property declarations into the parent's Properties block.
        /// Matching properties (by _Name) are replaced. New properties are appended.
        /// </summary>
        static string MergeProperties(string merged, string childSource)
        {
            // Find child Properties block
            var childPropsMatch = s_propertiesOpenRegex.Match(childSource);
            if (!childPropsMatch.Success) return merged;
            
            int childBrace = childSource.IndexOf('{', childPropsMatch.Index);
            string childContent = ShaderSourceUtility.ExtractBraceContent(
                childSource, childBrace, out _);
            if (string.IsNullOrEmpty(childContent)) return merged;
            
            // Find parent Properties block
            var parentPropsMatch = s_propertiesOpenRegex.Match(merged);
            if (!parentPropsMatch.Success) return merged;
            
            int parentBrace = merged.IndexOf('{', parentPropsMatch.Index);
            
            // Process each child property
            var childLines = childContent.Split('\n');
            var newProperties = new List<string>();
            
            foreach (var rawLine in childLines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;
                
                // Find property name: _Name("
                var nameMatch = s_propertyDeclRegex.Match(line);
                if (!nameMatch.Success) continue;
                
                string propName = nameMatch.Groups[1].Value;
                
                // Find matching property in parent (within Properties block only)
                int parentEnd = ShaderSourceUtility.FindMatchingBrace(merged, parentBrace);
                string propsRegion = merged.Substring(parentBrace, parentEnd - parentBrace);
                
                var parentPropMatch = Regex.Match(propsRegion,
                    @"^[^\n]*" + Regex.Escape(propName) + @"\s*\([^\n]*$",
                    RegexOptions.Multiline);
                
                if (parentPropMatch.Success)
                {
                    // Replace the parent's property line
                    int absIndex = parentBrace + parentPropMatch.Index;
                    merged = merged.Remove(absIndex, parentPropMatch.Length)
                                  .Insert(absIndex, "        " + line);
                }
                else
                {
                    newProperties.Add("        " + line);
                }
            }
            
            // Append new properties before closing } of Properties
            if (newProperties.Count > 0)
            {
                int propsEnd = ShaderSourceUtility.FindMatchingBrace(merged, parentBrace);
                int closePos = propsEnd - 1; // the } character
                string insertion = "\n" + string.Join("\n", newProperties) + "\n";
                merged = merged.Insert(closePos, insertion);
            }
            
            return merged;
        }
        
        //=============================================================================
        // Property Declaration Injection
        //=============================================================================
        
        /// <summary>
        /// Find child properties that don't exist in the parent's Properties block.
        /// Returns the raw declaration lines for new additions only (not overrides).
        /// Must be called before MergeProperties so the parent source is unmodified.
        /// </summary>
        static List<string> FindNewChildProperties(string parentSource, string childSource)
        {
            var newProps = new List<string>();
            
            // Find child Properties block
            var childPropsMatch = s_propertiesOpenRegex.Match(childSource);
            if (!childPropsMatch.Success) return newProps;
            
            int childBrace = childSource.IndexOf('{', childPropsMatch.Index);
            string childContent = ShaderSourceUtility.ExtractBraceContent(
                childSource, childBrace, out _);
            if (string.IsNullOrEmpty(childContent)) return newProps;
            
            // Find parent Properties block for comparison
            var parentPropsMatch = s_propertiesOpenRegex.Match(parentSource);
            if (!parentPropsMatch.Success) return newProps;
            
            int parentBrace = parentSource.IndexOf('{', parentPropsMatch.Index);
            int parentEnd = ShaderSourceUtility.FindMatchingBrace(parentSource, parentBrace);
            string parentPropsRegion = parentSource.Substring(parentBrace, parentEnd - parentBrace);
            
            var childLines = childContent.Split('\n');
            foreach (var rawLine in childLines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;
                
                var nameMatch = s_propertyDeclRegex.Match(line);
                if (!nameMatch.Success) continue;
                
                string propName = nameMatch.Groups[1].Value;
                
                // Check if this property already exists in parent
                bool existsInParent = Regex.IsMatch(parentPropsRegion,
                    Regex.Escape(propName) + @"\s*\(");
                
                if (!existsInParent)
                    newProps.Add(line);
            }
            
            return newProps;
        }
        
        /// <summary>
        /// Generate and inject HLSL declarations for new child properties.
        /// Scalar types (Float, Range, Int, Color, Vector) go into the parent's CBUFFER.
        /// Texture types (2D, 3D, Cube) get TEXTURE2D/SAMPLER declarations outside CBUFFER,
        /// plus a float4 _Name_ST entry inside CBUFFER for 2D textures.
        /// </summary>
        static string InjectPropertyDeclarations(string merged, List<string> newProperties)
        {
            var cbufferEntries = new List<string>();
            var textureEntries = new List<string>();
            
            foreach (var propLine in newProperties)
            {
                var match = s_propertyTypeRegex.Match(propLine);
                if (!match.Success) continue;
                
                string name = "_" + match.Groups[1].Value;
                string type = match.Groups[2].Value;
                string typeLower = type.Split('(')[0].Trim().ToLowerInvariant();
                
                if (typeLower == "2d" || typeLower == "3d" || typeLower == "cube")
                {
                    string texMacro = typeLower == "2d" ? "TEXTURE2D"
                                   : typeLower == "3d" ? "TEXTURE3D"
                                   : "TEXTURECUBE";
                    textureEntries.Add($"{texMacro}({name});");
                    textureEntries.Add($"SAMPLER(sampler{name});");
                    
                    if (typeLower == "2d")
                        cbufferEntries.Add($"float4 {name}_ST;");
                }
                else
                {
                    string hlslType = (typeLower == "color" || typeLower == "vector")
                        ? "float4" : "float";
                    cbufferEntries.Add($"{hlslType} {name};");
                }
            }
            
            // Insert CBUFFER entries before CBUFFER_END
            if (cbufferEntries.Count > 0)
            {
                int cbufferEnd = merged.IndexOf("CBUFFER_END");
                if (cbufferEnd >= 0)
                {
                    var sb = new StringBuilder();
                    foreach (var entry in cbufferEntries)
                        sb.AppendLine("            " + entry);
                    merged = merged.Insert(cbufferEnd, sb.ToString());
                }
            }
            
            // Insert texture declarations after CBUFFER_END line
            if (textureEntries.Count > 0)
            {
                int cbufferEnd = merged.IndexOf("CBUFFER_END");
                if (cbufferEnd >= 0)
                {
                    int endOfLine = merged.IndexOf('\n', cbufferEnd);
                    if (endOfLine < 0) endOfLine = merged.Length;
                    
                    var sb = new StringBuilder();
                    sb.AppendLine();
                    foreach (var entry in textureEntries)
                        sb.AppendLine("        " + entry);
                    merged = merged.Insert(endOfLine + 1, sb.ToString());
                }
            }
            
            return merged;
        }
        
        //=============================================================================
        // Pass Exclusion
        //=============================================================================
        
        /// <summary>
        /// Remove passes whose Name matches any entry in a comma-separated list.
        /// e.g., "ExcludePasses" = "Meta,DepthNormals" removes those passes.
        /// </summary>
        static string ExcludePasses(string merged, string excludeList)
        {
            var names = excludeList.Split(',');
            foreach (var rawName in names)
            {
                string name = rawName.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                merged = RemovePassByName(merged, name);
            }
            return merged;
        }
        
        /// <summary>
        /// Remove the first Pass block whose Name tag matches the given name.
        /// </summary>
        static string RemovePassByName(string merged, string passName)
        {
            var passBlocks = ShaderBlockUtility.FindAllPassBlocks(merged);
            
            foreach (var block in passBlocks)
            {
                var nameMatch = s_passNameRegex.Match(block.Content);
                if (!nameMatch.Success) continue;
                if (!nameMatch.Groups[1].Value.Equals(passName, StringComparison.OrdinalIgnoreCase))
                    continue;
                
                // Remove from the start of the line containing "Pass" to end of block
                int lineStart = merged.LastIndexOf('\n', block.StartIndex);
                if (lineStart < 0) lineStart = 0;
                
                merged = merged.Remove(lineStart, block.EndIndex - lineStart);
                Debug.Log($"[ShaderInheritance] Excluded pass \"{passName}\"");
                return merged;
            }
            
            Debug.LogWarning($"[ShaderInheritance] ExcludePasses: pass \"{passName}\" not found");
            return merged;
        }
        
        //=============================================================================
        // Pass Overrides
        //=============================================================================
        
        /// <summary>
        /// Apply child pass overrides to matching parent passes.
        /// Child Pass blocks without HLSLPROGRAM are treated as metadata-only overrides.
        /// Matched by pass Name. Tags and render state lines are merged.
        /// </summary>
        static string ApplyPassOverrides(string merged, string childSource)
        {
            var childPassBlocks = ShaderBlockUtility.FindAllPassBlocks(childSource);
            
            foreach (var block in childPassBlocks)
            {
                // Skip passes with HLSLPROGRAM (those are full pass replacements, not overrides)
                if (block.Content.Contains("HLSLPROGRAM")) continue;
                
                // Get the pass name
                var nameMatch = s_passNameRegex.Match(block.Content);
                if (!nameMatch.Success) continue;
                string passName = nameMatch.Groups[1].Value;
                
                // Find and apply override to the matching parent pass
                merged = ApplySinglePassOverride(merged, passName, block.Content);
            }
            
            return merged;
        }
        
        /// <summary>
        /// Apply a single pass override: merge tags and render state into the target pass.
        /// </summary>
        static string ApplySinglePassOverride(string merged, string passName, string overrideContent)
        {
            var passBlocks = ShaderBlockUtility.FindAllPassBlocks(merged);
            
            foreach (var block in passBlocks)
            {
                var nameMatch = s_passNameRegex.Match(block.Content);
                if (!nameMatch.Success) continue;
                if (!nameMatch.Groups[1].Value.Equals(passName, StringComparison.OrdinalIgnoreCase))
                    continue;
                
                // Found the target pass. Get the braced region for modification.
                // MergePassTags and SetPassRenderState work on text that includes
                // the outermost braces, so we extract from { to } inclusive.
                int braceStart = merged.IndexOf('{', block.StartIndex);
                string passRegion = merged.Substring(braceStart, block.EndIndex - braceStart);
                string modifiedRegion = passRegion;
                
                // Merge pass tags
                var overrideTags = ParsePassTags(overrideContent);
                if (overrideTags.Count > 0)
                    modifiedRegion = MergePassTags(modifiedRegion, overrideTags);
                
                // Merge render state lines
                var overrideRenderState = ParseRenderStateLines(overrideContent);
                foreach (var kvp in overrideRenderState)
                    modifiedRegion = SetPassRenderState(modifiedRegion, kvp.Key, kvp.Value);
                
                // Replace in merged source
                merged = merged.Substring(0, braceStart)
                       + modifiedRegion
                       + merged.Substring(block.EndIndex);
                
                Debug.Log($"[ShaderInheritance] Applied overrides to pass \"{passName}\"");
                return merged;
            }
            
            Debug.LogWarning($"[ShaderInheritance] Pass override: pass \"{passName}\" not found in parent");
            return merged;
        }
        
        /// <summary>
        /// Parse tag key-value pairs from a pass's Tags block.
        /// </summary>
        static Dictionary<string, string> ParsePassTags(string passContent)
        {
            var tagsMatch = s_passTagsBlockRegex.Match(passContent);
            if (!tagsMatch.Success)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            return ShaderBlockUtility.ParseTagPairs(tagsMatch.Groups[1].Value);
        }
        
        /// <summary>
        /// Parse render state lines (Cull, ZWrite, etc.) from pass content.
        /// Returns keyword to full-line mapping.
        /// </summary>
        static Dictionary<string, string> ParseRenderStateLines(string passContent)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            var matches = s_renderStateRegex.Matches(passContent);
            foreach (Match m in matches)
            {
                string keyword = m.Groups[1].Value;
                result[keyword] = m.Value.Trim();
            }
            
            return result;
        }
        
        /// <summary>
        /// Merge tags into a pass's Tags block (insert-or-replace by key).
        /// </summary>
        static string MergePassTags(string passContent, Dictionary<string, string> tags)
        {
            var tagsMatch = s_passTagsBlockRegex.Match(passContent);
            
            if (tagsMatch.Success)
            {
                string tagsBody = tagsMatch.Groups[1].Value;
                
                foreach (var kvp in tags)
                {
                    var existing = Regex.Match(tagsBody,
                        $@"""(?i:{Regex.Escape(kvp.Key)})""\s*=\s*""[^""]+""");
                    
                    string newTag = $"\"{kvp.Key}\" = \"{kvp.Value}\"";
                    
                    if (existing.Success)
                    {
                        tagsBody = tagsBody.Substring(0, existing.Index)
                            + newTag
                            + tagsBody.Substring(existing.Index + existing.Length);
                    }
                    else
                    {
                        tagsBody = tagsBody.TrimEnd() + $"\n                {newTag}\n            ";
                    }
                }
                
                string replacement = "Tags {" + tagsBody + "}";
                passContent = passContent.Substring(0, tagsMatch.Index)
                    + replacement
                    + passContent.Substring(tagsMatch.Index + tagsMatch.Length);
            }
            else
            {
                // No Tags block, insert one after the opening { and Name line
                var nameEnd = s_passNameRegex.Match(passContent);
                int insertPos = nameEnd.Success
                    ? passContent.IndexOf('\n', nameEnd.Index + nameEnd.Length) + 1
                    : 1; // after the opening {
                
                var sb = new StringBuilder();
                sb.AppendLine("            Tags");
                sb.Append("            {");
                foreach (var kvp in tags)
                {
                    sb.Append($"\n                \"{kvp.Key}\" = \"{kvp.Value}\"");
                }
                sb.AppendLine();
                sb.AppendLine("            }");
                
                passContent = passContent.Insert(insertPos, sb.ToString());
            }
            
            return passContent;
        }
        
        /// <summary>
        /// Set or replace a render state line in pass content.
        /// If the keyword exists, the line is replaced. Otherwise it's inserted
        /// before the HLSLPROGRAM block.
        /// </summary>
        static string SetPassRenderState(string passContent, string keyword, string fullLine)
        {
            var existing = Regex.Match(passContent,
                @"^\s*" + Regex.Escape(keyword) + @"\s+.+$",
                RegexOptions.Multiline);
            
            if (existing.Success)
            {
                return passContent.Remove(existing.Index, existing.Length)
                                 .Insert(existing.Index, "    " + fullLine);
            }
            
            // Insert before HLSLPROGRAM
            int hlslPos = passContent.IndexOf("HLSLPROGRAM");
            if (hlslPos >= 0)
            {
                int lineStart = passContent.LastIndexOf('\n', hlslPos);
                if (lineStart < 0) lineStart = 0;
                return passContent.Insert(lineStart + 1, "    " + fullLine + "\n");
            }
            
            return passContent;
        }
        
        //=============================================================================
        // HLSLINCLUDE Appending
        //=============================================================================
        
        /// <summary>
        /// Append the child's HLSLINCLUDE content to the parent's HLSLINCLUDE block.
        /// CBUFFER_APPEND blocks are stripped (property declarations are auto-generated
        /// from the Properties block by InjectPropertyDeclarations).
        /// Standalone property declarations (for IDE intellisense) are also stripped
        /// to avoid duplication with auto-generated CBUFFER entries.
        /// InheritHook pragmas are stripped (parsed separately for hook resolution).
        /// </summary>
        static string AppendChildHlslInclude(string merged, string childSource)
        {
            // Find child's HLSLINCLUDE content
            var childHlslMatch = s_hlslIncludeBlockRegex.Match(childSource);
            if (!childHlslMatch.Success) return merged;
            
            string childHlslContent = childHlslMatch.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(childHlslContent)) return merged;
            
            // Strip CBUFFER_APPEND blocks (declarations are auto-generated from Properties)
            childHlslContent = s_cbufferAppendRegex.Replace(childHlslContent, "");
            
            // Strip InheritHook pragmas (parsed separately in ResolveInheritHooks)
            childHlslContent = s_inheritHookPragmaRegex.Replace(childHlslContent, "");
            
            // Strip standalone property declarations (intellisense stubs)
            // These are auto-generated in the parent CBUFFER, so duplicates must be removed
            var childPropertyNames = CollectChildPropertyNames(childSource);
            if (childPropertyNames.Count > 0)
                childHlslContent = StripPropertyDeclarations(childHlslContent, childPropertyNames);
            
            // Clean up blank lines left by stripping
            childHlslContent = ShaderSourceUtility.CollapseBlankLines(childHlslContent);
            childHlslContent = childHlslContent.Trim();
            
            // Append remaining HLSLINCLUDE content before parent's ENDHLSL
            if (!string.IsNullOrWhiteSpace(childHlslContent))
            {
                // Find the parent's HLSLINCLUDE ENDHLSL (first occurrence)
                var parentHlslMatch = s_hlslIncludeBlockRegex.Match(merged);
                if (parentHlslMatch.Success)
                {
                    int endHlslPos = merged.IndexOf("ENDHLSL", parentHlslMatch.Index);
                    if (endHlslPos >= 0)
                    {
                        string insertion = "\n\n        // Child shader code\n        "
                            + childHlslContent.Replace("\n", "\n        ")
                            + "\n\n        ";
                        merged = merged.Insert(endHlslPos, insertion);
                    }
                }
                else
                {
                    // Parent has no HLSLINCLUDE. Create one after SubShader Tags.
                    int insertPos = FindSubShaderContentStart(merged);
                    if (insertPos >= 0)
                    {
                        string block = "\n        HLSLINCLUDE\n        "
                            + childHlslContent.Replace("\n", "\n        ")
                            + "\n        ENDHLSL\n";
                        merged = merged.Insert(insertPos, block);
                    }
                }
            }
            
            return merged;
        }
        
        /// <summary>
        /// Collect all property names from the child's Properties block.
        /// </summary>
        static HashSet<string> CollectChildPropertyNames(string childSource)
        {
            var names = new HashSet<string>();
            var propsMatch = s_propertiesOpenRegex.Match(childSource);
            if (!propsMatch.Success) return names;
            
            int brace = childSource.IndexOf('{', propsMatch.Index);
            string content = ShaderSourceUtility.ExtractBraceContent(childSource, brace, out _);
            if (string.IsNullOrEmpty(content)) return names;
            
            foreach (Match m in s_propertyDeclRegex.Matches(content))
                names.Add(m.Groups[1].Value);
            
            return names;
        }
        
        /// <summary>
        /// Strip HLSL variable declarations that match child property names.
        /// These are intellisense stubs that would duplicate auto-generated CBUFFER entries.
        /// Handles scalar/vector types, TEXTURE2D/3D/CUBE, SAMPLER, and _ST variants.
        /// </summary>
        static string StripPropertyDeclarations(string hlslContent, HashSet<string> propertyNames)
        {
            var lines = hlslContent.Split('\n');
            var result = new StringBuilder();
            bool first = true;
            
            foreach (var line in lines)
            {
                if (IsPropertyDeclaration(line.Trim(), propertyNames))
                    continue;
                
                if (!first) result.Append('\n');
                result.Append(line);
                first = false;
            }
            
            return result.ToString();
        }
        
        /// <summary>
        /// Check if a trimmed line is a standalone property declaration.
        /// </summary>
        static bool IsPropertyDeclaration(string trimmedLine, HashSet<string> propertyNames)
        {
            // Match: type _VarName; (float, float4, half, int, uint, etc.)
            var scalarMatch = Regex.Match(trimmedLine,
                @"^(?:float[234]?|half[234]?|int|uint)\s+(_\w+)\s*;$");
            if (scalarMatch.Success)
            {
                string varName = scalarMatch.Groups[1].Value;
                if (propertyNames.Contains(varName))
                    return true;
                // float4 _TexName_ST;
                if (varName.EndsWith("_ST"))
                {
                    string baseName = varName.Substring(0, varName.Length - 3);
                    if (propertyNames.Contains(baseName))
                        return true;
                }
            }
            
            // Match: TEXTURE2D(_Name); TEXTURE3D(_Name); TEXTURECUBE(_Name);
            var texMatch = Regex.Match(trimmedLine,
                @"^TEXTURE(?:2D|3D|CUBE)\s*\(\s*(_\w+)\s*\)\s*;$");
            if (texMatch.Success && propertyNames.Contains(texMatch.Groups[1].Value))
                return true;
            
            // Match: SAMPLER(sampler_Name); where _Name is the property
            var samplerMatch = Regex.Match(trimmedLine,
                @"^SAMPLER\s*\(\s*sampler(_\w+)\s*\)\s*;$");
            if (samplerMatch.Success && propertyNames.Contains(samplerMatch.Groups[1].Value))
                return true;
            
            return false;
        }
        
        /// <summary>
        /// Find a suitable position after the SubShader Tags block for inserting content.
        /// Returns position after the Tags closing }, or after SubShader { if no Tags.
        /// </summary>
        static int FindSubShaderContentStart(string merged)
        {
            var tagsMatch = s_subShaderTagsBlockRegex.Match(merged);
            if (tagsMatch.Success)
                return tagsMatch.Index + tagsMatch.Length;
            
            var subShaderMatch = s_subShaderOpenRegex.Match(merged);
            if (subShaderMatch.Success)
                return subShaderMatch.Index + subShaderMatch.Length;
            
            return -1;
        }
        
        //=============================================================================
        // InheritHook Resolution
        //=============================================================================
        
        /// <summary>
        /// Resolve InheritHook markers in the merged source using the child's pragma bindings.
        ///
        /// Parent declares hook points: {{InheritHook:ModifyColor(inout float4 col, float3 nrm)}}
        /// Child binds functions: #pragma InheritHook ModifyColor MyColorFunc
        ///
        /// Result: marker is replaced with MyColorFunc(col, nrm);
        /// Unbound hooks are replaced with empty string (parent compiles standalone).
        /// </summary>
        static string ResolveInheritHooks(string merged, string childSource)
        {
            // Parse child's InheritHook pragma bindings: hookName → funcName
            var hookBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pragmaMatches = s_inheritHookPragmaRegex.Matches(childSource);
            foreach (Match m in pragmaMatches)
            {
                hookBindings[m.Groups[1].Value] = m.Groups[2].Value;
            }
            
            // Replace all InheritHook markers in merged source
            merged = s_inheritHookMarkerRegex.Replace(merged, match =>
            {
                string hookName = match.Groups[1].Value;
                string paramList = match.Groups[2].Value;
                
                if (!hookBindings.TryGetValue(hookName, out string funcName))
                    return ""; // no binding, strip marker
                
                // Extract argument names from the parameter list
                // "inout float4 finalColor, float3 normalWS" → "finalColor, normalWS"
                string args = ExtractArgumentNames(paramList);
                
                return $"{funcName}({args});";
            });
            
            return merged;
        }
        
        /// <summary>
        /// Extract argument names from a parameter list string.
        /// Strips type qualifiers and modifiers, keeping just the variable names.
        /// "inout float4 finalColor, float3 normalWS, float2 uv" → "finalColor, normalWS, uv"
        /// </summary>
        static string ExtractArgumentNames(string paramList)
        {
            if (string.IsNullOrWhiteSpace(paramList)) return "";
            
            var names = new List<string>();
            var parameters = paramList.Split(',');
            
            foreach (var param in parameters)
            {
                string trimmed = param.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                
                // The argument name is the last word in the parameter
                string[] parts = trimmed.Split(new[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                    names.Add(parts[parts.Length - 1]);
            }
            
            return string.Join(", ", names);
        }
        
        //=============================================================================
        // Marker Appending
        //=============================================================================
        
        /// <summary>
        /// Find [InjectPass:X] and [InjectBasePasses] markers in the child source
        /// and append any that aren't already present (uncommented) in the parent.
        /// Markers are inserted just before the closing } of SubShader.
        /// </summary>
        static string AppendChildMarkers(string merged, string childSource)
        {
            var childMarkers = s_markerRegex.Matches(childSource);
            if (childMarkers.Count == 0) return merged;
            
            var newMarkers = new List<string>();
            
            foreach (Match m in childMarkers)
            {
                if (!HasUncommentedOccurrence(merged, m.Value))
                {
                    newMarkers.Add(m.Value);
                }
            }
            
            if (newMarkers.Count == 0) return merged;
            
            // Find insertion point: just before the closing } of SubShader
            int insertIdx = FindSubShaderClosePosition(merged);
            if (insertIdx < 0)
            {
                Debug.LogWarning("[ShaderInheritance] Could not find SubShader closing brace for marker insertion.");
                return merged;
            }
            
            var sb = new StringBuilder();
            foreach (var marker in newMarkers)
            {
                sb.AppendLine($"        {marker}");
            }
            
            merged = merged.Substring(0, insertIdx)
                + sb.ToString()
                + merged.Substring(insertIdx);
            
            return merged;
        }
        
        /// <summary>
        /// Check if a string appears at least once outside of a comment.
        /// </summary>
        static bool HasUncommentedOccurrence(string source, string text)
        {
            int idx = 0;
            while ((idx = source.IndexOf(text, idx, StringComparison.Ordinal)) >= 0)
            {
                if (!ShaderProcessor.IsInComment(source, idx))
                    return true;
                idx += text.Length;
            }
            return false;
        }
        
        /// <summary>
        /// Find the position of SubShader's closing brace.
        /// Returns the index of the '}' character, or -1 if not found.
        /// </summary>
        static int FindSubShaderClosePosition(string source)
        {
            var match = s_subShaderOpenRegex.Match(source);
            if (!match.Success) return -1;
            
            int end = ShaderSourceUtility.FindMatchingBrace(source, match.Index);
            if (end <= match.Index) return -1;
            
            // FindMatchingBrace returns one past the }, we want the } position itself
            return end - 1;
        }
        
        //=============================================================================
        // Include Path Rewriting
        //=============================================================================
        
        /// <summary>
        /// Rewrite relative #include paths (starting with ../) so they resolve
        /// correctly regardless of where the child shader lives.
        /// Paths starting with "Packages/" are left unchanged since they're
        /// already package-relative and work from any location.
        /// Relative paths are resolved against the parent's directory and
        /// converted to project-relative paths (e.g., "Assets/...").
        /// </summary>
        static string RewriteRelativeIncludes(string source, string parentPath)
        {
            string parentDir = Path.GetDirectoryName(parentPath);
            if (string.IsNullOrEmpty(parentDir)) return source;
            
            return s_relativeIncludeRegex.Replace(source, match =>
            {
                string prefix = match.Groups[1].Value;   // #include "
                string relPath = match.Groups[2].Value;  // ../../ShaderLibrary/Foo.hlsl
                string suffix = match.Groups[3].Value;   // "
                
                // Resolve relative to parent's directory
                string absolutePath = Path.GetFullPath(Path.Combine(parentDir, relPath));
                
                // Convert back to a project-relative path (Assets/... or Packages/...)
                // Unity uses forward slashes
                string projectRoot = Path.GetFullPath(Application.dataPath + "/..");
                string projectRelative = absolutePath;
                
                if (absolutePath.StartsWith(projectRoot))
                {
                    projectRelative = absolutePath.Substring(projectRoot.Length + 1);
                }
                
                projectRelative = projectRelative.Replace('\\', '/');
                
                return prefix + projectRelative + suffix;
            });
        }
        
        //=============================================================================
        // Parent Resolution
        //=============================================================================
        
        /// <summary>
        /// Find the asset path of a shader by its <c>Shader "Name"</c> declaration.
        /// First tries <see cref="Shader.Find"/> for already-compiled shaders.
        /// Falls back to scanning .shader files by their declared name, which handles
        /// cases where the parent hasn't been compiled yet (fresh import, batch reimport,
        /// or parent is also a ShaderGen shader with import ordering issues).
        /// Returns null if the shader is not found.
        /// </summary>
        static string FindShaderAssetPath(string shaderName)
        {
            // Fast path: already-compiled shader
            var shader = Shader.Find(shaderName);
            if (shader != null)
            {
                string path = AssetDatabase.GetAssetPath(shader);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".shader"))
                    return path;
            }
            
            // Fallback: scan .shader files by declared Shader "Name".
            // Use the last segment of the name as a filename hint to narrow the search.
            // e.g., "VFX/VFXMaster" → search for assets matching "VFXMaster"
            string nameHint = shaderName;
            int lastSlash = shaderName.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < shaderName.Length - 1)
                nameHint = shaderName.Substring(lastSlash + 1);
            
            string pattern = $"Shader\\s*\"{Regex.Escape(shaderName)}\"";
            
            // Search with the name hint first (fast, targeted)
            string result = SearchShaderFiles(AssetDatabase.FindAssets(nameHint), pattern);
            if (result != null) return result;
            
            // Broader search if the hint didn't match (shader file might be named differently)
            result = SearchShaderFiles(AssetDatabase.FindAssets("t:Shader"), pattern);
            return result;
        }
        
        /// <summary>
        /// Scan a set of asset GUIDs for .shader files whose Shader "Name" declaration
        /// matches the given regex pattern. Only reads the first 10 lines of each file.
        /// </summary>
        static string SearchShaderFiles(string[] guids, string pattern)
        {
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".shader")) continue;
                
                try
                {
                    // Read just the first few lines - Shader "Name" is always near the top
                    using (var reader = new StreamReader(path))
                    {
                        for (int i = 0; i < 10 && !reader.EndOfStream; i++)
                        {
                            string line = reader.ReadLine();
                            if (line != null && Regex.IsMatch(line, pattern))
                                return path;
                        }
                    }
                }
                catch
                {
                    // Skip files we can't read
                }
            }
            return null;
        }
    }
}
