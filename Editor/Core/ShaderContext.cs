using System;
using System.Collections.Generic;

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
        
        /// <summary>Original unmodified shader source.</summary>
        public string OriginalSource;
        
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
        
        /// <summary>Alias for ReferencePass (legacy compatibility).</summary>
        public PassInfo ForwardPass
        {
            get => ReferencePass;
            set => ReferencePass = value;
        }
        
        /// <summary>Vertex function name in reference pass.</summary>
        public string ForwardVertexFunctionName;
        
        /// <summary>Fragment function name in reference pass.</summary>
        public string ForwardFragmentFunctionName;
        
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
        
        /// <summary>Feature flags set by tag processors.</summary>
        public HashSet<string> EnabledFeatures = new HashSet<string>();
        
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
        
        public bool HasFeature(string feature) => EnabledFeatures.Contains(feature);
        public void EnableFeature(string feature) => EnabledFeatures.Add(feature);
        
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
        readonly Dictionary<string, HookInstance> _active = new Dictionary<string, HookInstance>(StringComparer.OrdinalIgnoreCase);
        
        /// <summary>Register an active hook with its parsed function name and body.</summary>
        public void Register(string pragmaName, string functionName, string functionBody)
        {
            _active[pragmaName] = new HookInstance
            {
                FunctionName = functionName,
                FunctionBody = functionBody
            };
        }
        
        /// <summary>Check if a hook is active by pragma name.</summary>
        public bool IsActive(string pragmaName) => _active.ContainsKey(pragmaName);
        
        /// <summary>Get the user's function name for a hook. Returns null if not active.</summary>
        public string GetFunctionName(string pragmaName)
        {
            return _active.TryGetValue(pragmaName, out var h) ? h.FunctionName : null;
        }
        
        /// <summary>Get the parsed function body for a hook. Returns null if not active.</summary>
        public string GetFunctionBody(string pragmaName)
        {
            return _active.TryGetValue(pragmaName, out var h) ? h.FunctionBody : null;
        }
        
        /// <summary>All active hooks for iteration.</summary>
        public IEnumerable<KeyValuePair<string, HookInstance>> Active => _active;
        
        /// <summary>Helper functions extracted from forward pass (non-hook, non-vert/frag functions).</summary>
        public List<string> HelperFunctions = new List<string>();
    }
    
    /// <summary>
    /// A single active hook instance — the parsed function name and body.
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
        
        /// <summary>Check if a tag is enabled (On/True/Enabled/1/Yes).</summary>
        public bool IsTagEnabled(string tagName)
        {
            if (!Tags.TryGetValue(tagName, out var val)) return false;
            val = val.Trim().ToLowerInvariant();
            return val == "on" || val == "true" || val == "enabled" || val == "1" || val == "yes";
        }
    }
}
