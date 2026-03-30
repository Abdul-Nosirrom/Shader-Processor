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
        // Pre-compiled Regex (avoids recompilation per import)
        //=============================================================================
        
        static readonly Regex s_passRegex = new Regex(PassPattern, RegexOptions.Compiled);
        static readonly Regex s_hlslProgramRegex = new Regex(@"HLSLPROGRAM\s*(.*?)\s*ENDHLSL", RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex s_vertexPragmaRegex = new Regex(@"#pragma\s+vertex\s+(\w+)", RegexOptions.Compiled);
        static readonly Regex s_fragmentPragmaRegex = new Regex(@"#pragma\s+fragment\s+(\w+)", RegexOptions.Compiled);
        static readonly Regex s_passNameRegex = new Regex(@"Name\s*""([^""]+)""", RegexOptions.Compiled);
        static readonly Regex s_passTagsRegex = new Regex(@"Tags\s*\{([^}]*)\}", RegexOptions.Compiled | RegexOptions.Singleline);
        static readonly Regex s_structFieldRegex = new Regex(@"(\w+)\s+(\w+)\s*:\s*(\w+)\s*;?", RegexOptions.Compiled);
        static readonly Regex s_texture2DDetectRegex = new Regex(@"TEXTURE2D\s*\(", RegexOptions.Compiled);
        static readonly Regex s_structDetectRegex = new Regex(@"struct\s+\w+\s*\{", RegexOptions.Compiled);
        static readonly Regex s_hlslIncludeRegex = new Regex(@"^\s*HLSLINCLUDE\s*(.*?)\s*ENDHLSL", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.Multiline);
        
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
            ExtractExtraFragmentParams(ctx);
            
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
            ExtractExtraFragmentParams(ctx);
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
            
            var tags = ShaderBlockUtility.ParseTagPairs(match.Groups[1].Value);
            foreach (var kvp in tags)
            {
                ctx.SubShaderTags[kvp.Key] = kvp.Value;
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
            var match = s_hlslIncludeRegex.Match(ctx.ProcessedSource);
            
            ctx.HlslIncludeBlock = match.Success ? match.Groups[1].Value : "";
        }
        
        //=============================================================================
        // Location Detection
        //=============================================================================
        
        static void DetectBlockLocations(ShaderContext ctx)
        {
            // Check if CBUFFER is in HLSLINCLUDE
            ctx.CBufferInHlslInclude = !string.IsNullOrEmpty(ctx.HlslIncludeBlock) &&
                ShaderBlockUtility.HasCBuffer(ctx.HlslIncludeBlock);
            
            // Check if textures are in HLSLINCLUDE
            ctx.TexturesInHlslInclude = !string.IsNullOrEmpty(ctx.HlslIncludeBlock) &&
                s_texture2DDetectRegex.IsMatch(ctx.HlslIncludeBlock);
            
            // Check if structs are in HLSLINCLUDE
            ctx.StructsInHlslInclude = !string.IsNullOrEmpty(ctx.HlslIncludeBlock) &&
                s_structDetectRegex.IsMatch(ctx.HlslIncludeBlock);
        }
        
        //=============================================================================
        // CBUFFER Parsing
        //=============================================================================
        
        static void ParseCBuffer(ShaderContext ctx)
        {
            // Try HLSLINCLUDE first, then reference pass
            string source = ctx.CBufferInHlslInclude ? ctx.HlslIncludeBlock : ctx.ReferencePass?.HlslProgram;
            if (string.IsNullOrEmpty(source)) return;
            
            ctx.CBufferContent = ShaderBlockUtility.GetCBufferContent(source);
        }
        
        //=============================================================================
        // Texture Parsing
        //=============================================================================
        
        static void ParseTextures(ShaderContext ctx)
        {
            string source = ctx.TexturesInHlslInclude ? ctx.HlslIncludeBlock : ctx.ReferencePass?.HlslProgram;
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
            var matches = s_passRegex.Matches(ctx.ProcessedSource);
            
            foreach (Match match in matches)
            {
                int startIndex = match.Index;
                
                // Skip passes inside comments (e.g., commented-out depth pass)
                if (ShaderProcessor.IsInComment(ctx.ProcessedSource, startIndex))
                    continue;
                
                int endIndex = ShaderSourceUtility.FindMatchingBrace(ctx.ProcessedSource, startIndex);
                
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
        
        static PassInfo ParseSinglePass(string passSource, int startIndex, int endIndex)
        {
            var pass = new PassInfo
            {
                FullSource = passSource,
                StartIndex = startIndex,
                EndIndex = endIndex
            };
            
            // Parse Name
            var nameMatch = s_passNameRegex.Match(passSource);
            if (nameMatch.Success)
                pass.Name = nameMatch.Groups[1].Value;
            
            // Parse all tags in the pass Tags block
            var tagsMatch = s_passTagsRegex.Match(passSource);
            if (tagsMatch.Success)
            {
                var tags = ShaderBlockUtility.ParseTagPairs(tagsMatch.Groups[1].Value);
                foreach (var kvp in tags)
                {
                    pass.Tags[kvp.Key] = kvp.Value;
                    
                    if (kvp.Key.Equals("LightMode", StringComparison.OrdinalIgnoreCase))
                        pass.LightMode = kvp.Value;
                }
            }
            
            // Parse HLSLPROGRAM content
            var hlslMatch = s_hlslProgramRegex.Match(passSource);
            if (hlslMatch.Success)
            {
                pass.HlslProgram = hlslMatch.Groups[1].Value;
                
                // Extract vertex/fragment function names
                var vertexMatch = s_vertexPragmaRegex.Match(pass.HlslProgram);
                if (vertexMatch.Success)
                    pass.VertexFunctionName = vertexMatch.Groups[1].Value;
                
                var fragmentMatch = s_fragmentPragmaRegex.Match(pass.HlslProgram);
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
                    ctx.ReferenceVertexFunctionName = pass.VertexFunctionName;
                    ctx.ReferenceFragmentFunctionName = pass.FragmentFunctionName;
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
            // Skip passes with empty HLSL programs (e.g., Preview pass stubs)
            foreach (var pass in ctx.Passes)
            {
                if (pass.LightMode == "UniversalForward" && !string.IsNullOrWhiteSpace(pass.HlslProgram))
                {
                    ctx.ReferencePass = pass;
                    ctx.ReferenceVertexFunctionName = pass.VertexFunctionName;
                    ctx.ReferenceFragmentFunctionName = pass.FragmentFunctionName;
                    Debug.Log($"[ShaderGen] Using UniversalForward pass (SubShader fallback)");
                    return;
                }
            }

            // Priority 3: First pass with actual HLSL content (last resort)
            foreach (var pass in ctx.Passes)
            {
                if (!string.IsNullOrWhiteSpace(pass.HlslProgram))
                {
                    ctx.ReferencePass = pass;
                    ctx.ReferenceVertexFunctionName = pass.VertexFunctionName;
                    ctx.ReferenceFragmentFunctionName = pass.FragmentFunctionName;
                    Debug.Log($"[ShaderGen] Using first pass as fallback: {pass.Name ?? pass.LightMode ?? "unnamed"}");
                    return;
                }
            }
        }
        
        /// <summary>
        /// Extract extra parameters from the reference fragment function signature,
        /// beyond the interpolator struct parameter. System-value semantics like
        /// SV_IsFrontFace, SV_SampleIndex, SV_PrimitiveID are provided by the
        /// rasterizer and safe to propagate to all generated passes.
        /// e.g., "half4 Frag(Interpolators i, bool isFrontFace : SV_IsFrontFace)"
        /// extracts ", bool isFrontFace : SV_IsFrontFace".
        /// </summary>
        static void ExtractExtraFragmentParams(ShaderContext ctx)
        {
            if (ctx.ReferencePass == null || string.IsNullOrEmpty(ctx.ReferenceFragmentFunctionName))
                return;
            
            string source = ctx.ReferencePass.HlslProgram;
            string funcName = ctx.ReferenceFragmentFunctionName;
            
            // Match: returnType funcName(StructType paramName[, extra params...])
            // Check all definitions (there may be stubs behind #if guards) and
            // take the first with extra parameters.
            var matches = Regex.Matches(source,
                $@"\w+\s+{Regex.Escape(funcName)}\s*\(\s*\w+\s+\w+(.*?)\)");
            
            foreach (Match match in matches)
            {
                string extra = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(extra))
                {
                    ctx.ExtraFragmentParams = extra;
                    break;
                }
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
            string source = ctx.StructsInHlslInclude ? ctx.HlslIncludeBlock : ctx.ReferencePass?.HlslProgram;
            if (string.IsNullOrEmpty(source)) return;
            
            ctx.Attributes = ParseStruct(ctx.AttributesStructName, source);
            ctx.Interpolators = ParseStruct(ctx.InterpolatorsStructName, source);
            
            // If not found in primary location, try the other
            if (ctx.Attributes == null && ctx.ReferencePass != null)
                ctx.Attributes = ParseStruct(ctx.AttributesStructName, ctx.ReferencePass.HlslProgram);
            if (ctx.Interpolators == null && ctx.ReferencePass != null)
                ctx.Interpolators = ParseStruct(ctx.InterpolatorsStructName, ctx.ReferencePass.HlslProgram);
        }
        
        static void DetectStructNames(ShaderContext ctx)
        {
            if (ctx.ReferencePass == null || string.IsNullOrEmpty(ctx.ReferenceVertexFunctionName))
                return;
    
            // Search both HLSLINCLUDE and Forward pass for the function signature
            string searchSource = "";
            if (!string.IsNullOrEmpty(ctx.HlslIncludeBlock))
                searchSource += ctx.HlslIncludeBlock + "\n";
            if (!string.IsNullOrEmpty(ctx.ReferencePass.HlslProgram))
                searchSource += ctx.ReferencePass.HlslProgram;
    
            var types = ShaderFunctionUtility.GetSignatureTypes(searchSource, ctx.ReferenceVertexFunctionName);
            if (types != null)
            {
                // Return type is Interpolators, first param type is Attributes
                ctx.InterpolatorsStructName = types.Value.returnType;
                ctx.AttributesStructName = types.Value.firstParamType;
            }
        }
        
        /// <summary>
        /// Detect attribute and interpolator struct names from a vertex function signature.
        /// Delegates to <see cref="ShaderFunctionUtility.GetSignatureTypes"/>.
        /// Returns null if the function signature is not found.
        /// </summary>
        public static (string attrName, string interpName)? DetectStructNamesFromSource(
            string source, string vertexFunctionName)
        {
            var types = ShaderFunctionUtility.GetSignatureTypes(source, vertexFunctionName);
            if (types == null) return null;

            // Return type is Interpolators, first param type is Attributes
            return (attrName: types.Value.firstParamType, interpName: types.Value.returnType);
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
                var types = ShaderFunctionUtility.GetSignatureTypes(signatureSource, pass.VertexFunctionName);
                
                if (types != null)
                {
                    // Return type is Interpolators, first param type is Attributes
                    pass.InterpolatorsStructName = types.Value.returnType;
                    pass.AttributesStructName = types.Value.firstParamType;
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
            
            // The { is the last character of the match
            int braceIndex = match.Index + match.Length - 1;
            string body = ShaderSourceUtility.ExtractBraceContent(source, braceIndex, out _);
            if (body == null) return null;
            
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
            
            // Track preprocessor guard stack. Each entry is the raw directive
            // (e.g., "#ifdef _PARTICLE_SYSTEM"). Fields inside the guard get
            // the top-of-stack assigned as their PreprocessorGuard.
            var guardStack = new List<string>();
    
            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                    continue;
                
                // Track preprocessor directives - don't add them as fields
                if (line.StartsWith("#ifdef") || line.StartsWith("#if ") || line.StartsWith("#if("))
                {
                    guardStack.Add(line);
                    continue;
                }
                if (line.StartsWith("#ifndef"))
                {
                    guardStack.Add(line);
                    continue;
                }
                if (line.StartsWith("#else"))
                {
                    // Negate the current guard: #ifdef X → #ifndef X and vice versa
                    if (guardStack.Count > 0)
                    {
                        string current = guardStack[guardStack.Count - 1];
                        guardStack[guardStack.Count - 1] = NegateGuard(current);
                    }
                    continue;
                }
                if (line.StartsWith("#elif"))
                {
                    // Replace current guard with the #elif condition as an #if
                    if (guardStack.Count > 0)
                    {
                        guardStack[guardStack.Count - 1] = line.Replace("#elif", "#if");
                    }
                    continue;
                }
                if (line.StartsWith("#endif"))
                {
                    if (guardStack.Count > 0)
                        guardStack.RemoveAt(guardStack.Count - 1);
                    continue;
                }
                
                // Current guard is the innermost (top of stack), null if unconditional
                string currentGuard = guardStack.Count > 0 ? guardStack[guardStack.Count - 1] : null;
        
                // Try to parse as a typed field: type name : SEMANTIC;
                var match = s_structFieldRegex.Match(line);
                if (match.Success)
                {
                    fields.Add(new StructField
                    {
                        Type = match.Groups[1].Value,
                        Name = match.Groups[2].Value,
                        Semantic = match.Groups[3].Value,
                        IsMacro = false,
                        RawLine = line,
                        PreprocessorGuard = currentGuard
                    });
                }
                else
                {
                    // Anything that isn't a typed field is treated as a macro
                    // (UNITY_VERTEX_INPUT_INSTANCE_ID, DECLARE_LIGHTMAP_OR_SH(...), etc.)
                    fields.Add(new StructField
                    {
                        IsMacro = true,
                        RawLine = line,
                        PreprocessorGuard = currentGuard
                    });
                }
            }
    
            return fields;
        }
        
        /// <summary>
        /// Negate a preprocessor guard directive for #else handling.
        /// #ifdef X → #ifndef X, #ifndef X → #ifdef X.
        /// For #if expressions, wraps in negation: #if X → #if !(X).
        /// </summary>
        static string NegateGuard(string guard)
        {
            if (guard.StartsWith("#ifdef "))
                return "#ifndef " + guard.Substring("#ifdef ".Length);
            if (guard.StartsWith("#ifndef "))
                return "#ifdef " + guard.Substring("#ifndef ".Length);
            if (guard.StartsWith("#if "))
                return "#if !(" + guard.Substring("#if ".Length) + ")";
            return guard; // Can't negate, return as-is
        }
        
        //=============================================================================
        // Hook Pragma Parsing
        //=============================================================================
        
        static void ParseHookPragmas(ShaderContext ctx)
        {
            if (ctx.ReferencePass == null) return;
            
            // Search both HLSLINCLUDE and pass for pragma & function bodies.
            string bodySearchSource = (ctx.HlslIncludeBlock ?? "") + "\n" + ctx.ReferencePass.HlslProgram;
            
            // Scan for all registered hook pragmas
            foreach (var hook in ShaderHookRegistry.All)
            {
                string funcName = ShaderPragmaUtility.GetValue(bodySearchSource, hook.PragmaName);
                if (string.IsNullOrEmpty(funcName))
                    continue;
                
                var funcInfo = ShaderFunctionUtility.FindFunction(bodySearchSource, funcName);
                if (funcInfo == null)
                {
                    Debug.LogWarning($"[ShaderProcessor] Hook '{hook.PragmaName}' references function " +
                        $"'{funcName}' but the function body could not be found.");
                    continue;
                }
                
                // Validate parameter count if the hook definition specifies one
                if (hook.ExpectedParameterCount >= 0)
                {
                    int actualCount = ShaderFunctionUtility.CountParameters(funcInfo.Value.ParameterList);
                    if (actualCount != hook.ExpectedParameterCount)
                    {
                        string expected = hook.ExpectedSignature ?? $"{hook.ExpectedParameterCount} parameter(s)";
                        Debug.LogWarning($"[ShaderProcessor] Hook '{hook.PragmaName}': function " +
                            $"'{funcName}' has {actualCount} parameter(s) but expected {hook.ExpectedParameterCount}. " +
                            $"Expected signature: {expected}");
                    }
                }
                
                ctx.Hooks.Register(hook.PragmaName, funcName, funcInfo.Value.FullText);
            }
        }
    }
}
