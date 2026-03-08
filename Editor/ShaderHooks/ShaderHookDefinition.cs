using System;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Attribute to mark a class as a shader hook definition.
    /// Used by ShaderHookRegistry for TypeCache discovery.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ShaderHookAttribute : Attribute { }
    
    /// <summary>
    /// Defines the schema for a shader hook.
    /// Each hook connects a user pragma declaration to a template call site.
    /// 
    /// User writes:    #pragma vertexDisplacement MyFunc
    /// Template uses:  {{VERTEX_DISPLACEMENT_CALL}}
    /// Generated:      #ifdef FS_VERTEX_DISPLACEMENT
    ///                     MyFunc(input);
    ///                 #endif
    /// </summary>
    public abstract class ShaderHookDefinition
    {
        /// <summary>
        /// The pragma name users write in their shader.
        /// Example: "vertexDisplacement" → user writes #pragma vertexDisplacement MyFunc
        /// </summary>
        public abstract string PragmaName { get; }
        
        /// <summary>
        /// The #define emitted when this hook is active.
        /// Example: "FS_VERTEX_DISPLACEMENT" → emitted as #define FS_VERTEX_DISPLACEMENT
        /// Templates use #ifdef to gate the call site.
        /// </summary>
        public abstract string Define { get; }
        
        /// <summary>
        /// How this hook is called in generated code.
        /// Use {FuncName} as placeholder for the user's function name.
        /// Example: "{FuncName}(input);" or "{FuncName}(input, output);"
        /// </summary>
        public abstract string CallPattern { get; }
        
        /// <summary>
        /// The {{MARKER}} name used in templates for this hook's call site.
        /// Example: "VERTEX_DISPLACEMENT_CALL" → template uses {{VERTEX_DISPLACEMENT_CALL}}
        /// </summary>
        public abstract string TemplateMarker { get; }
    }
    
    //=============================================================================
    // Built-in Hook Definitions
    //=============================================================================
    
    /// <summary>
    /// Vertex displacement hook. Called in vertex shaders to modify vertex position.
    /// Signature: void FuncName(inout Attributes input)
    /// </summary>
    [ShaderHook]
    public class VertexDisplacementHook : ShaderHookDefinition
    {
        public override string PragmaName => "vertexDisplacement";
        public override string Define => "FS_VERTEX_DISPLACEMENT";
        public override string CallPattern => "{FuncName}(input);";
        public override string TemplateMarker => "VERTEX_DISPLACEMENT_CALL";
    }
    
    /// <summary>
    /// Interpolator transfer hook. Called after standard vertex outputs to pass extra data.
    /// Signature: void FuncName(Attributes input, inout Interpolators output)
    /// </summary>
    [ShaderHook]
    public class InterpolatorTransferHook : ShaderHookDefinition
    {
        public override string PragmaName => "interpolatorTransfer";
        public override string Define => "FS_INTERPOLATOR_TRANSFER";
        public override string CallPattern => "{FuncName}(input, output);";
        public override string TemplateMarker => "INTERPOLATOR_TRANSFER_CALL";
    }
    
    /// <summary>
    /// Alpha clip hook. Called in fragment shaders to discard transparent pixels.
    /// Signature: void FuncName(Interpolators input)
    /// </summary>
    [ShaderHook]
    public class AlphaClipHook : ShaderHookDefinition
    {
        public override string PragmaName => "alphaClip";
        public override string Define => "FS_ALPHA_CLIP";
        public override string CallPattern => "{FuncName}(input);";
        public override string TemplateMarker => "ALPHA_CLIP_CALL";
    }

    /// <summary>
    /// Alpha clip hook. Called in fragment shaders to discard transparent pixels.
    /// Signature: void FuncName(Interpolators input)
    /// </summary>
    [ShaderHook]
    public class TessellationFactorOverride : ShaderHookDefinition
    {
        public override string PragmaName => "tessFactorOverride";
        public override string Define => "FS_TESSELLATION_FACTOR_OVERRIDE";
        public override string CallPattern => "{FuncName}(_overrideFactor, input);";
        public override string TemplateMarker => "TESSELLATION_FACTOR_OVERRIDE_CALL";
    }
}
