using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Parses shader source and populates ShaderContext with extracted data.
    /// </summary>
    public static class ShaderParser
    {
        //=============================================================================
        // Constants
        //=============================================================================
        
        /// <summary>
        /// Regex pattern matching a Pass declaration, handling optional inline comments.
        /// </summary>
        public const string PassPattern = @"Pass\s*(?://[^\n]*)?\s*(?:/\*.*?\*/)?\s*\{";
        
        /// <summary>
        /// Pattern that matches whitespace, line comments, and block comments.
        /// Used between Properties and SubShader where comments may appear.
        /// </summary>
        public const string CommentOrWhitespace = @"(\s|//[^\n]*\n|/\*.*?\*/)*";
        
        //=============================================================================
        // Main Entry Point
        //=============================================================================
        
        /// <summary>
        /// Parse the shader source and populate the context.
        /// </summary>
        public static void Parse(ShaderContext ctx)
        {
            ParseSubShaderTags(ctx);
            ParsePropertiesBlock(ctx);
            ParseHlslInclude(ctx);
            ParseAllPasses(ctx);
            FindForwardPass(ctx);
            
            // Determine where things are defined
            DetectBlockLocations(ctx);
            
            // Parse from the appropriate location
            ParseCBuffer(ctx);
            ParseTextures(ctx);
            ParseStructs(ctx);
            ParseHookPragmas(ctx);
            
            // Parse per-pass struct info (names + definitions from each pass's own vertex signature)
            ParsePerPassStructs(ctx);
        }
        
        /// <summary>
        /// Re-parse all passes from the current ProcessedSource.
        /// Call this after source modifications (e.g., CBuffer injection) to get fresh
        /// PassInfo objects with correct HlslProgram content and indices.
        /// </summary>
        public static void ReparseAllPasses(ShaderContext ctx)
        {
            ctx.Passes.Clear();
            ParseAllPasses(ctx);
            FindForwardPass(ctx);
            ParsePerPassStructs(ctx);
        }
        
        //=============================================================================
        // Tag Parsing
        //=============================================================================
        
        static void ParseSubShaderTags(ShaderContext ctx)
        {
            var match = Regex.Match(ctx.ProcessedSource,
                @"SubShader\s*\{[^{]*Tags\s*\{([^}]+)\}",
                RegexOptions.Singleline);
            
            if (!match.Success) return;
            
            string tagsContent = match.Groups[1].Value;
            var tagMatches = Regex.Matches(tagsContent, @"""(\w+)""\s*=\s*""([^""]+)""");
            
            foreach (Match tagMatch in tagMatches)
            {
                ctx.SubShaderTags[tagMatch.Groups[1].Value] = tagMatch.Groups[2].Value;
            }
        }
        
        //=============================================================================
        // Block Parsing
        //=============================================================================
        
        static void ParsePropertiesBlock(ShaderContext ctx)
        {
            // Better handling of comments
            var match = Regex.Match(ctx.ProcessedSource,
                $@"Properties\s*\{{(.*?)\}}{CommentOrWhitespace}SubShader",
                RegexOptions.Singleline);
            
            if (match.Success)
            {
                ctx.PropertiesBlock = match.Groups[1].Value.Trim();
            }
        }
        
        static void ParseHlslInclude(ShaderContext ctx)
        {
            var match = Regex.Match(ctx.ProcessedSource,
                @"^\s*HLSLINCLUDE\s*(.*?)\s*ENDHLSL",
                RegexOptions.Singleline | RegexOptions.Multiline);
            
            ctx.HlslIncludeBlock = match.Success ? match.Groups[1].Value : "";
        }
        
        //=============================================================================
        // Location Detection
        //=============================================================================
        
        static void DetectBlockLocations(ShaderContext ctx)
        {
            // Check if CBUFFER is in HLSLINCLUDE
            ctx.CBufferInHlslInclude = !string.IsNullOrEmpty(ctx.HlslIncludeBlock) &&
                Regex.IsMatch(ctx.HlslIncludeBlock, @"CBUFFER_START\s*\(\s*UnityPerMaterial\s*\)");
            
            // Check if textures are in HLSLINCLUDE
            ctx.TexturesInHlslInclude = !string.IsNullOrEmpty(ctx.HlslIncludeBlock) &&
                Regex.IsMatch(ctx.HlslIncludeBlock, @"TEXTURE2D\s*\(");
            
            // Check if structs are in HLSLINCLUDE
            ctx.StructsInHlslInclude = !string.IsNullOrEmpty(ctx.HlslIncludeBlock) &&
                Regex.IsMatch(ctx.HlslIncludeBlock, @"struct\s+\w+\s*\{");
        }
        
        //=============================================================================
        // CBUFFER Parsing
        //=============================================================================
        
        static void ParseCBuffer(ShaderContext ctx)
        {
            // Try HLSLINCLUDE first
            string source = ctx.CBufferInHlslInclude ? ctx.HlslIncludeBlock : ctx.ForwardPass?.HlslProgram;
            if (string.IsNullOrEmpty(source)) return;
            
            var match = Regex.Match(source,
                @"CBUFFER_START\s*\(\s*UnityPerMaterial\s*\)(.*?)CBUFFER_END",
                RegexOptions.Singleline);
            
            if (match.Success)
            {
                ctx.CBufferContent = match.Groups[1].Value.Trim();
            }
        }
        
        //=============================================================================
        // Texture Parsing
        //=============================================================================
        
        static void ParseTextures(ShaderContext ctx)
        {
            string source = ctx.TexturesInHlslInclude ? ctx.HlslIncludeBlock : ctx.ForwardPass?.HlslProgram;
            if (string.IsNullOrEmpty(source)) return;
            
            // Match TEXTURE2D + SAMPLER pairs
            var matches = Regex.Matches(source,
                @"(TEXTURE2D|TEXTURE3D|TEXTURECUBE)\s*\(\s*(\w+)\s*\)\s*;\s*SAMPLER\s*\(\s*(\w+)\s*\)\s*;");
            
            var added = new HashSet<string>();
            foreach (Match match in matches)
            {
                string name = match.Groups[2].Value;
                if (added.Add(name))
                {
                    ctx.Textures.Add(new TextureDeclaration
                    {
                        Type = match.Groups[1].Value,
                        Name = name,
                        SamplerName = match.Groups[3].Value,
                        RawDeclaration = match.Value
                    });
                }
            }
        }
        
        //=============================================================================
        // Pass Parsing
        //=============================================================================
        
        static void ParseAllPasses(ShaderContext ctx)
        {
            ctx.Passes.Clear();
            var matches = Regex.Matches(ctx.ProcessedSource, PassPattern);
            
            foreach (Match match in matches)
            {
                int startIndex = match.Index;
                int endIndex = FindMatchingBrace(ctx.ProcessedSource, startIndex);
                
                if (endIndex > startIndex)
                {
                    string passSource = ctx.ProcessedSource.Substring(startIndex, endIndex - startIndex);
                    var passInfo = ParseSinglePass(passSource, startIndex, endIndex);
                    if (passInfo != null)
                    {
                        ctx.Passes.Add(passInfo);
                    }
                }
            }
        }
        
        static int FindMatchingBrace(string source, int startIndex)
        {
            int braceCount = 0;
            bool foundStart = false;
            
            for (int i = startIndex; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    braceCount++;
                    foundStart = true;
                }
                else if (source[i] == '}')
                {
                    braceCount--;
                    if (foundStart && braceCount == 0)
                        return i + 1;
                }
            }
            return startIndex;
        }
        
        static PassInfo ParseSinglePass(string passSource, int startIndex, int endIndex)
        {
            var pass = new PassInfo
            {
                FullSource = passSource,
                StartIndex = startIndex,
                EndIndex = endIndex
            };
            
            // Parse Name
            var nameMatch = Regex.Match(passSource, @"Name\s*""([^""]+)""");
            if (nameMatch.Success)
                pass.Name = nameMatch.Groups[1].Value;
            
            // Parse all tags in the pass Tags block
            var tagsMatch = Regex.Match(passSource, @"Tags\s*\{([^}]*)\}", RegexOptions.Singleline);
            if (tagsMatch.Success)
            {
                string tagsContent = tagsMatch.Groups[1].Value;
                var tagMatches = Regex.Matches(tagsContent, @"""(\w+)""\s*=\s*""([^""]+)""");
                
                foreach (Match tagMatch in tagMatches)
                {
                    string tagName = tagMatch.Groups[1].Value;
                    string tagValue = tagMatch.Groups[2].Value;
                    pass.Tags[tagName] = tagValue;
                    
                    // Also set LightMode for quick access
                    if (tagName.Equals("LightMode", StringComparison.OrdinalIgnoreCase))
                        pass.LightMode = tagValue;
                }
            }
            
            // Parse HLSLPROGRAM content
            var hlslMatch = Regex.Match(passSource, @"HLSLPROGRAM\s*(.*?)\s*ENDHLSL", RegexOptions.Singleline);
            if (hlslMatch.Success)
            {
                pass.HlslProgram = hlslMatch.Groups[1].Value;
                
                // Extract vertex/fragment function names
                var vertexMatch = Regex.Match(pass.HlslProgram, @"#pragma\s+vertex\s+(\w+)");
                if (vertexMatch.Success)
                    pass.VertexFunctionName = vertexMatch.Groups[1].Value;
                
                var fragmentMatch = Regex.Match(pass.HlslProgram, @"#pragma\s+fragment\s+(\w+)");
                if (fragmentMatch.Success)
                    pass.FragmentFunctionName = fragmentMatch.Groups[1].Value;
            }
            
            return pass;
        }
        
        static void FindForwardPass(ShaderContext ctx)
        {
            // First check if ShaderGen is in SubShader tags
            if (ctx.SubShaderTags.TryGetValue("ShaderGen", out string value) &&
                value.Equals("True", StringComparison.OrdinalIgnoreCase))
            {
                ctx.ShaderGenInSubShader = true;
            }
            
            // Priority 1: Pass with "ShaderGen" = "True" tag
            foreach (var pass in ctx.Passes)
            {
                if (pass.IsTagEnabled("ShaderGen"))
                {
                    ctx.ReferencePass = pass;
                    ctx.ForwardVertexFunctionName = pass.VertexFunctionName;
                    ctx.ForwardFragmentFunctionName = pass.FragmentFunctionName;
                    ctx.ShaderGenInPass = true;
                    ctx.ShaderGenPassName = pass.Name ?? pass.LightMode ?? "unnamed";
                    Debug.Log($"[ShaderGen] Using tagged pass: {ctx.ShaderGenPassName}");
                    return;
                }
            }

            // Only continue if ShaderGen was enabled at SubShader level
            if (!ctx.ShaderGenInSubShader)
            {
                return; // No ShaderGen tag found anywhere, skip processing
            }

            // Priority 2: UniversalForward (fallback when ShaderGen is in SubShader scope)
            foreach (var pass in ctx.Passes)
            {
                if (pass.LightMode == "UniversalForward")
                {
                    ctx.ReferencePass = pass;
                    ctx.ForwardVertexFunctionName = pass.VertexFunctionName;
                    ctx.ForwardFragmentFunctionName = pass.FragmentFunctionName;
                    Debug.Log($"[ShaderGen] Using UniversalForward pass (SubShader fallback)");
                    return;
                }
            }

            // Priority 3: First pass (last resort)
            if (ctx.Passes.Count > 0)
            {
                ctx.ReferencePass = ctx.Passes[0];
                ctx.ForwardVertexFunctionName = ctx.ReferencePass.VertexFunctionName;
                ctx.ForwardFragmentFunctionName = ctx.ReferencePass.FragmentFunctionName;
                Debug.Log($"[ShaderGen] Using first pass as fallback");
            }
        }
        
        //=============================================================================
        // Struct Parsing
        //=============================================================================
        
        static void ParseStructs(ShaderContext ctx)
        {
            // Determine struct names from Forward pass vertex function signature
            DetectStructNames(ctx);
            
            // Parse from appropriate location
            string source = ctx.StructsInHlslInclude ? ctx.HlslIncludeBlock : ctx.ForwardPass?.HlslProgram;
            if (string.IsNullOrEmpty(source)) return;
            
            ctx.Attributes = ParseStruct(ctx.AttributesStructName, source);
            ctx.Interpolators = ParseStruct(ctx.InterpolatorsStructName, source);
            
            // If not found in primary location, try the other
            if (ctx.Attributes == null && ctx.ForwardPass != null)
                ctx.Attributes = ParseStruct(ctx.AttributesStructName, ctx.ForwardPass.HlslProgram);
            if (ctx.Interpolators == null && ctx.ForwardPass != null)
                ctx.Interpolators = ParseStruct(ctx.InterpolatorsStructName, ctx.ForwardPass.HlslProgram);
        }
        
        static void DetectStructNames(ShaderContext ctx)
        {
            if (ctx.ForwardPass == null || string.IsNullOrEmpty(ctx.ForwardVertexFunctionName))
                return;
    
            // Search both HLSLINCLUDE and Forward pass for the function signature
            string searchSource = "";
            if (!string.IsNullOrEmpty(ctx.HlslIncludeBlock))
                searchSource += ctx.HlslIncludeBlock + "\n";
            if (!string.IsNullOrEmpty(ctx.ForwardPass.HlslProgram))
                searchSource += ctx.ForwardPass.HlslProgram;
    
            var names = DetectStructNamesFromSource(searchSource, ctx.ForwardVertexFunctionName);
            if (names != null)
            {
                ctx.AttributesStructName = names.Value.attrName;
                ctx.InterpolatorsStructName = names.Value.interpName;
            }
        }
        
        /// <summary>
        /// Detect attribute and interpolator struct names from a vertex function signature.
        /// Parses "ReturnType FuncName(ParamType param)" to extract ReturnType as interpolator
        /// name and ParamType as attribute name.
        /// Returns null if the function signature is not found.
        /// </summary>
        public static (string attrName, string interpName)? DetectStructNamesFromSource(
            string source, string vertexFunctionName)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(vertexFunctionName))
                return null;
            
            var match = Regex.Match(source,
                $@"(\w+)\s+{Regex.Escape(vertexFunctionName)}\s*\(\s*(\w+)\s+\w+",
                RegexOptions.Singleline);
            
            if (!match.Success) return null;
            
            return (attrName: match.Groups[2].Value, interpName: match.Groups[1].Value);
        }
        
        /// <summary>
        /// Parse per-pass struct info. Each pass gets its own struct names and definitions
        /// detected from its vertex function signature. Falls back to context-level data
        /// for passes that share structs with the reference pass.
        /// </summary>
        static void ParsePerPassStructs(ShaderContext ctx)
        {
            foreach (var pass in ctx.Passes)
            {
                // Detect struct names from this pass's vertex function.
                // Search HLSLINCLUDE + pass HLSL combined (vertex func may be in either).
                string signatureSource = (ctx.HlslIncludeBlock ?? "") + "\n" + (pass.HlslProgram ?? "");
                var names = DetectStructNamesFromSource(signatureSource, pass.VertexFunctionName);
                
                if (names != null)
                {
                    pass.AttributesStructName = names.Value.attrName;
                    pass.InterpolatorsStructName = names.Value.interpName;
                }
                else
                {
                    // Fall back to context-level names (from reference pass)
                    pass.AttributesStructName = ctx.AttributesStructName;
                    pass.InterpolatorsStructName = ctx.InterpolatorsStructName;
                }
                
                // Parse structs from HLSLINCLUDE + this pass's HLSL
                string searchSource = (ctx.HlslIncludeBlock ?? "") + "\n" + (pass.HlslProgram ?? "");
                
                pass.Attributes = ParseStruct(pass.AttributesStructName, searchSource);
                pass.Interpolators = ParseStruct(pass.InterpolatorsStructName, searchSource);
                
                // Fall back to context-level structs (e.g. structs only in HLSLINCLUDE
                // and this pass uses the same names as the reference pass)
                if (pass.Attributes == null) pass.Attributes = ctx.Attributes;
                if (pass.Interpolators == null) pass.Interpolators = ctx.Interpolators;
            }
        }
        
        /// <summary>
        /// Parse a named struct from source code. Returns null if not found.
        /// </summary>
        public static StructDefinition ParseStruct(string structName, string source)
        {
            if (string.IsNullOrEmpty(structName) || string.IsNullOrEmpty(source))
                return null;
            
            var match = Regex.Match(source, $@"struct\s+{Regex.Escape(structName)}\s*\{{");
            if (!match.Success) return null;
            
            int startIndex = match.Index + match.Length;
            int braceCount = 1;
            int endIndex = startIndex;
            
            for (int i = startIndex; i < source.Length && braceCount > 0; i++)
            {
                if (source[i] == '{') braceCount++;
                else if (source[i] == '}') braceCount--;
                endIndex = i;
            }
            
            string body = source.Substring(startIndex, endIndex - startIndex).Trim();
            
            return new StructDefinition
            {
                Name = structName,
                RawBody = body,
                Fields = ParseStructFields(body)
            };
        }
        
        static List<StructField> ParseStructFields(string body)
        {
            var fields = new List<StructField>();
            var lines = body.Split('\n');
    
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                    continue;
        
                // Try to parse as a typed field: type name : SEMANTIC;
                var match = Regex.Match(line, @"(\w+)\s+(\w+)\s*:\s*(\w+)\s*;?");
                if (match.Success)
                {
                    fields.Add(new StructField
                    {
                        Type = match.Groups[1].Value,
                        Name = match.Groups[2].Value,
                        Semantic = match.Groups[3].Value,
                        IsMacro = false,
                        RawLine = line
                    });
                }
                else
                {
                    // Anything that isn't a typed field is treated as a macro
                    // (UNITY_VERTEX_INPUT_INSTANCE_ID, DECLARE_LIGHTMAP_OR_SH(...), etc.)
                    fields.Add(new StructField
                    {
                        IsMacro = true,
                        RawLine = line
                    });
                }
            }
    
            return fields;
        }
        
        //=============================================================================
        // Hook Pragma Parsing
        //=============================================================================
        
        static void ParseHookPragmas(ShaderContext ctx)
        {
            if (ctx.ForwardPass == null) return;
            
            // Search both HLSLINCLUDE and pass for pragma & function bodies.
            string bodySearchSource = (ctx.HlslIncludeBlock ?? "") + "\n" + ctx.ForwardPass.HlslProgram;
            
            // Scan for all registered hook pragmas
            foreach (var hook in ShaderHookRegistry.All)
            {
                string funcName = ParsePragmaValue(bodySearchSource, hook.PragmaName);
                if (!string.IsNullOrEmpty(funcName))
                {
                    string funcBody = ExtractFunction(bodySearchSource, funcName);
                    ctx.Hooks.Register(hook.PragmaName, funcName, funcBody);
                }
            }
        }
        
        static string ParsePragmaValue(string source, string pragmaName)
        {
            var match = Regex.Match(source, $@"#pragma\s+{pragmaName}\s+(\w+)");
            return match.Success ? match.Groups[1].Value : null;
        }
        
        static string ExtractFunction(string source, string funcName)
        {
            if (string.IsNullOrEmpty(funcName)) return null;
            
            // Match function signature and body
            // Pattern: returnType funcName(params) { body }
            var match = Regex.Match(source,
                $@"(\w+\s+{Regex.Escape(funcName)}\s*\([^)]*\)\s*\{{)",
                RegexOptions.Singleline);
            
            if (!match.Success) return null;
            
            int startIndex = match.Index;
            int braceStart = match.Index + match.Length - 1;
            int braceCount = 1;
            int endIndex = braceStart + 1;
            
            for (int i = braceStart + 1; i < source.Length && braceCount > 0; i++)
            {
                if (source[i] == '{') braceCount++;
                else if (source[i] == '}') braceCount--;
                endIndex = i + 1;
            }
            
            return source.Substring(startIndex, endIndex - startIndex);
        }
    }
}
