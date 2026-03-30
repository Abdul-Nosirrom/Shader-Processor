using System;
using System.Collections.Generic;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Attribute to mark a class as a shader pass injector.
    /// Used by ShaderPassInjectorRegistry for TypeCache discovery.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ShaderPassAttribute : Attribute { }
    
    /// <summary>
    /// Defines how to generate a shader pass from a template.
    /// 
    /// Pass injectors are activated in two ways:
    ///   - [InjectBasePasses] expands all injectors where IsBasePass == true
    ///   - [InjectPass:Name] expands a specific injector by PassName
    ///
    /// Pass injectors can declare material data (properties, CBUFFER entries) that
    /// get injected into the shader source alongside tag processor additions.
    /// </summary>
    public abstract class ShaderPassInjector
    {
        /// <summary>
        /// Pass name used in markers and as the generated pass's Name tag.
        /// Example: "ShadowCaster" → activated by [InjectPass:ShadowCaster]
        /// </summary>
        public abstract string PassName { get; }
        
        /// <summary>
        /// Template file name (without extension). Maps to Templates/{TemplateName}.hlsl.
        /// </summary>
        public abstract string TemplateName { get; }
        
        /// <summary>
        /// If true, this pass is included when [InjectBasePasses] is used.
        /// Feature passes like Outline should return false.
        /// </summary>
        public virtual bool IsBasePass => false;
        
        /// <summary>
        /// Property declarations to inject into the Properties block.
        /// Return null if no properties needed.
        /// Called once during pipeline initialization.
        /// </summary>
        public virtual string GetPropertiesEntries(ShaderContext ctx) => null;
        
        /// <summary>
        /// CBUFFER variable declarations to inject.
        /// Return null if no CBUFFER entries needed.
        /// Called once during pipeline initialization.
        /// </summary>
        public virtual string GetCBufferEntries(ShaderContext ctx) => null;
        
        /// <summary>
        /// Override struct generation for passes that need special fields.
        /// Return a dictionary with "ATTRIBUTES_STRUCT" and/or "INTERPOLATORS_STRUCT" keys.
        /// Return null to use default struct generation from ProcessPassTemplate.
        /// </summary>
        public virtual Dictionary<string, string> GetStructOverrides(ShaderContext ctx) => null;
        
        /// <summary>
        /// Additional template replacements beyond what ProcessPassTemplate provides.
        /// Merged after struct overrides, so these take highest priority.
        /// Return null if no additional replacements needed.
        /// </summary>
        public virtual Dictionary<string, string> GetAdditionalReplacements(ShaderContext ctx) => null;
        
        /// <summary>
        /// The return statement(s) to replace each 'return ...;' in the forward fragment
        /// body when injected into this pass. Used by the InjectForwardBody tag processor.
        ///
        /// Supports two reference types (resolved before injection):
        ///   {{SV_POSITION}}, {{NORMAL_WS}}     - struct field names by semantic
        ///   {{output:albedo}}, {{output:normal}} - variable names from #pragma fragmentOutput
        ///
        /// Return null to skip fragment injection for this pass entirely.
        /// </summary>
        /// <param name="ctx">The shader context.</param>
        /// <param name="isFallback">
        /// True when one or more {{output:X}} references can't be resolved from pragmas.
        /// Provide a safe expression that doesn't depend on pragma variables.
        /// </param>
        public virtual string GetFragmentReturnExpression(ShaderContext ctx, bool isFallback) => null;
    }
}
