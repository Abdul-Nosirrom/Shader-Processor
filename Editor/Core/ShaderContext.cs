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
        // Structs
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
        
        /// <summary>Reference to the Forward pass.</summary>
        public PassInfo ForwardPass;
        
        /// <summary>Vertex function name in Forward pass.</summary>
        public string ForwardVertexFunctionName;
        
        /// <summary>Fragment function name in Forward pass.</summary>
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
        
        /// <summary>Parsed hook pragmas and their function bodies.</summary>
        public HookDefinitions Hooks = new HookDefinitions();
        
        //=============================================================================
        // Tag Processor State
        //=============================================================================
        
        /// <summary>Passes queued for injection by tag processors.</summary>
        public List<QueuedPass> QueuedPasses = new List<QueuedPass>();
        
        /// <summary>Feature flags set by tag processors.</summary>
        public HashSet<string> EnabledFeatures = new HashSet<string>();
        
        //=============================================================================
        // Helpers
        //=============================================================================
        
        public bool HasFeature(string feature) => EnabledFeatures.Contains(feature);
        public void EnableFeature(string feature) => EnabledFeatures.Add(feature);
        
        public bool TessellationEnabled => HasFeature("Tessellation");
        public bool OutlinesEnabled => HasFeature("Outlines");
    }
    
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
    }
    
    /// <summary>
    /// Hook function definitions parsed from pragmas.
    /// </summary>
    public class HookDefinitions
    {
        // Function names (null if not defined)
        public string VertexDisplacementName;
        public string InterpolatorTransferName;
        public string AlphaClipName;
        
        // Function bodies (including signature)
        public string VertexDisplacementBody;
        public string InterpolatorTransferBody;
        public string AlphaClipBody;
        
        // Helper functions called by hooks (extracted from Forward pass)
        public List<string> HelperFunctions = new List<string>();
        
        public bool HasVertexDisplacement => !string.IsNullOrEmpty(VertexDisplacementName);
        public bool HasInterpolatorTransfer => !string.IsNullOrEmpty(InterpolatorTransferName);
        public bool HasAlphaClip => !string.IsNullOrEmpty(AlphaClipName);
    }
    
    /// <summary>
    /// A pass queued for injection by a tag processor.
    /// </summary>
    public class QueuedPass
    {
        public string PassCode;
        public string SourceProcessor;  // Which tag processor queued this
    }
}
