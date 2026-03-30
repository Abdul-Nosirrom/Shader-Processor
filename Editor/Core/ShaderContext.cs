using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Contains all parsed shader data and tracks modifications during processing.
    /// Passed through the entire pipeline as the single source of truth.
    /// </summary>
    public class ShaderContext
    {
        //=============================================================================
        // Source
        //=============================================================================
        
        /// <summary>Source being modified by processors.</summary>
        public string ProcessedSource;
        
        /// <summary>Full path to the shader file.</summary>
        public string ShaderPath;
        
        /// <summary>Directory containing the shader file.</summary>
        public string ShaderDirectory;
        
        //=============================================================================
        // Parsed Blocks
        //=============================================================================
        
        /// <summary>Content of HLSLINCLUDE block (without HLSLINCLUDE/ENDHLSL tags).</summary>
        public string HlslIncludeBlock;
        
        /// <summary>Content of Properties block (without Properties { }).</summary>
        public string PropertiesBlock;
        
        /// <summary>Content inside CBUFFER_START/CBUFFER_END.</summary>
        public string CBufferContent;
        
        /// <summary>All SubShader tags.</summary>
        public Dictionary<string, string> SubShaderTags = new Dictionary<string, string>();
        
        //=============================================================================
        // Location Flags - Where things are defined
        //=============================================================================
        
        /// <summary>True if CBUFFER is in HLSLINCLUDE (shared), false if in Forward pass.</summary>
        public bool CBufferInHlslInclude;
        
        /// <summary>True if textures are in HLSLINCLUDE (shared).</summary>
        public bool TexturesInHlslInclude;
        
        /// <summary>True if structs are in HLSLINCLUDE (shared).</summary>
        public bool StructsInHlslInclude;
        
        //=============================================================================
        // Structs (from reference pass - used for generated passes)
        //=============================================================================
        
        /// <summary>Name of the attributes struct (e.g., "Attributes", "VertexInput").</summary>
        public string AttributesStructName = "Attributes";
        
        /// <summary>Name of the interpolators struct (e.g., "Interpolators", "Varyings").</summary>
        public string InterpolatorsStructName = "Interpolators";
        
        /// <summary>Parsed Attributes struct.</summary>
        public StructDefinition Attributes;
        
        /// <summary>Parsed Interpolators struct.</summary>
        public StructDefinition Interpolators;
        
        //=============================================================================
        // Textures
        //=============================================================================
        
        /// <summary>All texture declarations found.</summary>
        public List<TextureDeclaration> Textures = new List<TextureDeclaration>();
        
        //=============================================================================
        // Passes
        //=============================================================================
        
        /// <summary>All parsed passes.</summary>
        public List<PassInfo> Passes = new List<PassInfo>();
        
        /// <summary>
        /// The reference pass used for content copying (structs, hooks, etc.).
        /// This is the pass with "ShaderGen" = "True", or fallback to UniversalForward/first pass.
        /// </summary>
        public PassInfo ReferencePass;
        
        /// <summary>Vertex function name in reference pass.</summary>
        public string ReferenceVertexFunctionName;
        
        /// <summary>Fragment function name in reference pass.</summary>
        public string ReferenceFragmentFunctionName;
        
        /// <summary>
        /// Extra parameters from the reference fragment function signature,
        /// beyond the interpolator struct. e.g., ", bool isFrontFace : SV_IsFrontFace".
        /// These are system-value semantics provided by the rasterizer and are
        /// propagated to all generated pass fragment signatures.
        /// </summary>
        public string ExtraFragmentParams;
        
        // Where was ShaderGen tag found?
        /// <summary> Whether ShaderGen tag was found in the sub-shader block </summary>
        public bool ShaderGenInSubShader { get; set; }
        /// <summary> Whether ShaderGen tag was found in the pass block </summary>
        public bool ShaderGenInPass { get; set; }
        /// <summary> Name of pass where ShaderGen tag was found </summary>
        public string ShaderGenPassName { get; set; }
        
        //=============================================================================
        // Hooks (parsed from pragmas)
        //=============================================================================
        
        /// <summary>Active hooks parsed from reference pass pragmas.</summary>
        public HookState Hooks = new HookState();
        
        //=============================================================================
        // Tag Processor State
        //=============================================================================
        
        /// <summary>
        /// Feature flags set by tag processors, keyed by feature name.
        /// Values are the mode: "Full" (apply to authored + generated passes)
        /// or "Pass" (apply only to authored passes that declare it).
        /// </summary>
        public Dictionary<string, string> EnabledFeatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        //=============================================================================
        // Processor Additions (collected from pass injectors and tag processors)
        //=============================================================================
        
        /// <summary>Property entries declared by pass injectors and tag processors.</summary>
        public string ProcessorPropertiesEntries = "";
        
        /// <summary>CBUFFER entries declared by pass injectors and tag processors.</summary>
        public string ProcessorCBufferEntries = "";
        
        //=============================================================================
        // Helpers
        //=============================================================================
        
        public bool HasFeature(string feature) => EnabledFeatures.ContainsKey(feature);
        
        /// <summary>
        /// Enable a feature with a mode. "Full" = authored + generated passes.
        /// "Pass" = authored passes only (generated passes skip GetPassReplacements).
        /// </summary>
        public void EnableFeature(string feature, string mode = "Full") => EnabledFeatures[feature] = mode;
        
        /// <summary>Get the mode for an enabled feature. Returns null if not enabled.</summary>
        public string GetFeatureMode(string feature) => EnabledFeatures.TryGetValue(feature, out var mode) ? mode : null;
        
        /// <summary>
        /// Check if a property already exists (in original Properties block or processor additions).
        /// </summary>
        public bool PropertyExists(string propertyName) => PropertiesBlock?.Contains(propertyName) == true || ProcessorPropertiesEntries?.Contains(propertyName) == true;
        
        /// <summary>
        /// Check if a CBUFFER entry already exists (in original CBUFFER or processor additions).
        /// </summary>
        public bool CBufferEntryExists(string entryName) =>  CBufferContent?.Contains(entryName) == true || ProcessorCBufferEntries?.Contains(entryName) == true;
        
        public bool TessellationEnabled => HasFeature("Tessellation");
    }
    
    //=============================================================================
    // Hook State (parsed from pragmas, keyed by pragma name)
    //=============================================================================
    
    /// <summary>
    /// Stores which hooks are active for this shader and their parsed function data.
    /// Keyed by pragma name (e.g., "vertexDisplacement").
    /// </summary>
    public class HookState
    {
        readonly Dictionary<string, HookInstance> m_active = new Dictionary<string, HookInstance>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>Register an active hook with its parsed function name and body.</summary>
        public void Register(string pragmaName, string functionName, string functionBody)
        {
            m_active[pragmaName] = new HookInstance
            {
                FunctionName = functionName,
                FunctionBody = functionBody
            };
        }
        
        /// <summary>Check if a hook is active by pragma name.</summary>
        public bool IsActive(string pragmaName) => m_active.ContainsKey(pragmaName);
        
        /// <summary>Get the user's function name for a hook. Returns null if not active.</summary>
        public string GetFunctionName(string pragmaName)
        {
            return m_active.TryGetValue(pragmaName, out var h) ? h.FunctionName : null;
        }
        
        /// <summary>All active hooks for iteration.</summary>
        public IEnumerable<KeyValuePair<string, HookInstance>> Active => m_active;
    }
    
    /// <summary>
    /// A single active hook instance - the parsed function name and body.
    /// </summary>
    public class HookInstance
    {
        /// <summary>User's function name (e.g., "HeightDisplace").</summary>
        public string FunctionName;
        
        /// <summary>Full function body including signature (e.g., "void HeightDisplace(inout Attributes input) { ... }").</summary>
        public string FunctionBody;
    }
    
    //=============================================================================
    // Struct, Texture, Pass, and other data classes
    //=============================================================================
    
    /// <summary>
    /// Parsed struct definition with field information.
    /// </summary>
    public class StructDefinition
    {
        public string Name;
        public string RawBody;
        public List<StructField> Fields = new List<StructField>();
        
        /// <summary>Get field by semantic (e.g., "POSITION", "NORMAL").</summary>
        public StructField GetField(string semantic)
        {
            foreach (var field in Fields)
            {
                if (field.Semantic == semantic)
                    return field;
            }
            return null;
        }
        
        /// <summary>Check if struct has a field with given semantic.</summary>
        public bool HasField(string semantic) => GetField(semantic) != null;
    }
    
    /// <summary>
    /// A single field in a struct.
    /// </summary>
    public class StructField
    {
        public string Type;       // float4, float3, float2, etc.
        public string Name;       // positionOS, normalOS, uv, etc.
        public string Semantic;   // POSITION, NORMAL, TEXCOORD0, etc.
        public bool IsMacro;      // True for UNITY_VERTEX_INPUT_INSTANCE_ID etc.
        public string RawLine;    // Original line from source
        
        /// <summary>
        /// The preprocessor guard wrapping this field (e.g., "#ifdef _PARTICLE_SYSTEM"),
        /// or null if the field is unconditional. Stored as the raw directive line so it
        /// can be replayed verbatim in generated code.
        /// </summary>
        public string PreprocessorGuard;
    }
    
    /// <summary>
    /// A texture declaration.
    /// </summary>
    public class TextureDeclaration
    {
        public string Name;           // _BaseMap
        public string Type;           // TEXTURE2D, TEXTURE3D, etc.
        public string SamplerName;    // sampler_BaseMap
        public string RawDeclaration; // Full declaration line(s)
    }
    
    /// <summary>
    /// Information about a shader pass.
    /// </summary>
    public class PassInfo
    {
        public string Name;
        public string LightMode;
        public string FullSource;
        public string HlslProgram;    // Content between HLSLPROGRAM/ENDHLSL
        public int StartIndex;
        public int EndIndex;
        public string VertexFunctionName;
        public string FragmentFunctionName;
        
        //=============================================================================
        // Per-Pass Struct Info (detected from this pass's vertex function signature)
        //=============================================================================
        
        /// <summary>Attributes struct name for this pass (e.g., "Attributes", "attr").</summary>
        public string AttributesStructName;
        
        /// <summary>Interpolators struct name for this pass (e.g., "Interpolators", "interp").</summary>
        public string InterpolatorsStructName;
        
        /// <summary>Parsed Attributes struct for this pass.</summary>
        public StructDefinition Attributes;
        
        /// <summary>Parsed Interpolators struct for this pass.</summary>
        public StructDefinition Interpolators;
        
        //=============================================================================
        // Tags
        //=============================================================================
        
        /// <summary>All tags declared in this pass's Tags block.</summary>
        public Dictionary<string, string> Tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>Check if this pass has a specific tag with a specific value.</summary>
        public bool HasTag(string tagName, string tagValue)
        {
            return Tags.TryGetValue(tagName, out var val) && 
                   val.Equals(tagValue, StringComparison.OrdinalIgnoreCase);
        }
        
        /// <summary>Check if a tag is enabled (On/True/Enabled/1/Yes/Full/Pass).</summary>
        public bool IsTagEnabled(string tagName)
        {
            if (!Tags.TryGetValue(tagName, out var val)) return false;
            return ShaderTagUtility.IsTagEnabled(val);
        }
        
        /// <summary>
        /// Get the tag mode for a tag processor feature.
        /// Returns "Full" for on/true/enabled/1/yes/full, "Pass" for pass, null if not set or off.
        /// </summary>
        public string GetTagMode(string tagName)
        {
            if (!Tags.TryGetValue(tagName, out var val)) return null;
            return ShaderTagUtility.ParseTagMode(val);
        }
    }
    
    //=============================================================================
    // Shared Tag Parsing Utility
    //=============================================================================
    
    /// <summary>
    /// Single source of truth for parsing shader tag values into modes.
    /// Used by PassInfo, ShaderTagProcessorRegistry, and editor tooling.
    /// </summary>
    public static class ShaderTagUtility
    {
        /// <summary>
        /// Check if a tag value represents an enabled state.
        /// Recognizes: on, true, enabled, 1, yes, full, pass (case-insensitive).
        /// </summary>
        public static bool IsTagEnabled(string value)
        {
            return ParseTagMode(value) != null;
        }
        
        /// <summary>
        /// Parse a tag value into a mode string.
        /// Returns "Full" for on/true/enabled/1/yes/full, "Pass" for pass, null for anything else.
        /// </summary>
        public static string ParseTagMode(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            value = value.Trim().ToLowerInvariant();
            if (value == "pass") return "Pass";
            if (value == "on" || value == "true" || value == "enabled" || value == "1" || value == "yes" || value == "full")
                return "Full";
            return null;
        }
    }
    
    //=============================================================================
    // Shared Source Scanning Utility
    //=============================================================================
    
    /// <summary>
    /// Comment-aware source code scanning utilities.
    /// Used by ShaderParser, HookProcessor, and anywhere that needs to find
    /// matching braces or skip over comments in shader source.
    /// </summary>
    public static class ShaderSourceUtility
    {
        /// <summary>
        /// Find the index of the closing brace that matches the first opening brace
        /// at or after <paramref name="startIndex"/>. Skips braces inside line comments
        /// (//) and block comments (/* */).
        /// Returns the index one past the closing brace, or <paramref name="startIndex"/>
        /// if no matching brace is found.
        /// </summary>
        public static int FindMatchingBrace(string source, int startIndex)
        {
            int braceCount = 0;
            bool foundStart = false;
            bool inLineComment = false;
            bool inBlockComment = false;
            
            for (int i = startIndex; i < source.Length; i++)
            {
                char c = source[i];
                char next = (i + 1 < source.Length) ? source[i + 1] : '\0';
                
                // Track comment state
                if (!inLineComment && !inBlockComment && c == '/' && next == '/')
                {
                    inLineComment = true;
                    i++; // skip second /
                    continue;
                }
                if (inLineComment && c == '\n')
                {
                    inLineComment = false;
                    continue;
                }
                if (!inLineComment && !inBlockComment && c == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++; // skip *
                    continue;
                }
                if (inBlockComment && c == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++; // skip /
                    continue;
                }
                
                // Skip content inside comments
                if (inLineComment || inBlockComment)
                    continue;
                
                if (c == '{')
                {
                    braceCount++;
                    foundStart = true;
                }
                else if (c == '}')
                {
                    braceCount--;
                    if (foundStart && braceCount == 0)
                        return i + 1;
                }
            }
            return startIndex;
        }
        
        /// <summary>
        /// Extract the body of a brace-delimited block starting at the opening brace
        /// position. Comment-aware - ignores braces inside comments.
        /// Returns the content between the braces (exclusive), or null if not found.
        /// <paramref name="braceStartIndex"/> should point at the '{' character.
        /// Also outputs <paramref name="endIndex"/> pointing one past the closing '}'.
        /// </summary>
        public static string ExtractBraceContent(string source, int braceStartIndex, out int endIndex)
        {
            endIndex = braceStartIndex;
            if (braceStartIndex >= source.Length || source[braceStartIndex] != '{')
                return null;
            
            int matchEnd = FindMatchingBrace(source, braceStartIndex);
            if (matchEnd <= braceStartIndex)
                return null;
            
            endIndex = matchEnd;
            // Content is between the { and } (exclusive of both)
            int contentStart = braceStartIndex + 1;
            int contentEnd = matchEnd - 1; // the } character position
            return source.Substring(contentStart, contentEnd - contentStart).Trim();
        }
        
        /// <summary>
        /// Collapse runs of multiple blank lines down to a single blank line.
        /// Handles both truly empty lines and whitespace-only lines.
        /// Used after stripping operations that leave behind excess whitespace.
        /// </summary>
        public static string CollapseBlankLines(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            return Regex.Replace(source, @"\n\s*\n\s*\n", "\n\n");
        }
    }
}