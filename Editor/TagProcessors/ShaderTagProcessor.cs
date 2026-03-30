using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Interface for shader tag processors.
    /// Processors are activated when their tag is present in SubShader tags.
    /// </summary>
    public interface IShaderTagProcessor
    {
        /// <summary>Tag name to match (e.g., "Tessellation", "Outlines").</summary>
        string TagName { get; }
        
        /// <summary>Processing priority. Lower values run first.</summary>
        int Priority { get; }
        
        /// <summary>
        /// Stage 1: Declare property entries needed by this processor.
        /// Return the property declarations (will be injected into Properties block).
        /// Return null or empty if no properties needed.
        /// </summary>
        string GetPropertiesEntries(ShaderContext ctx);
        
        /// <summary>
        /// Stage 2: Declare CBUFFER entries needed by this processor.
        /// Return the CBUFFER variable declarations (will be injected into CBUFFER).
        /// Return null or empty if no entries needed.
        /// </summary>
        string GetCBufferEntries(ShaderContext ctx);
        
        /// <summary>
        /// Stage 3: Modify a pass where this processor's tag applies.
        /// Called once for each existing pass that has this processor's tag enabled
        /// (either at SubShader level or pass level).
        /// </summary>
        void ModifyPass(ShaderContext ctx, PassInfo pass);
        
        /// <summary>Stage 4: Provide template replacements for generated passes.</summary>
        Dictionary<string, string> GetPassReplacements(ShaderContext ctx, string passName);
    }
    
    /// <summary>
    /// Attribute to mark a class as a shader tag processor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public class ShaderTagProcessorAttribute : Attribute
    {
        public string TagName { get; }
        public int Priority { get; }
        
        public ShaderTagProcessorAttribute(string tagName, int priority = 100)
        {
            TagName = tagName;
            Priority = priority;
        }
    }
    
    /// <summary>
    /// Base class for shader tag processors with default empty implementations.
    /// </summary>
    public abstract class ShaderTagProcessorBase : IShaderTagProcessor
    {
        public abstract string TagName { get; }
        public virtual int Priority => 100;
        
        public virtual string GetPropertiesEntries(ShaderContext ctx) => null;
        public virtual string GetCBufferEntries(ShaderContext ctx) => null;
        public virtual void ModifyPass(ShaderContext ctx, PassInfo pass) { }
        
        public virtual Dictionary<string, string> GetPassReplacements(ShaderContext ctx, string passName)
        {
            return null;
        }
    }
    
    /// <summary>
    /// Registry for shader tag processors.
    /// Discovers processors via TypeCache and runs them in priority order.
    /// </summary>
    public static class ShaderTagProcessorRegistry
    {
        static Dictionary<string, IShaderTagProcessor> s_Processors;
        static List<IShaderTagProcessor> s_SortedProcessors;
        static bool s_Initialized;
        
        //=============================================================================
        // Initialization
        //=============================================================================
        
        public static void Initialize()
        {
            if (s_Initialized) return;
            
            s_Processors = new Dictionary<string, IShaderTagProcessor>(StringComparer.OrdinalIgnoreCase);
            s_SortedProcessors = new List<IShaderTagProcessor>();
            
            // Discover processors via TypeCache
            var processorTypes = TypeCache.GetTypesWithAttribute<ShaderTagProcessorAttribute>();
            
            foreach (var type in processorTypes)
            {
                if (!typeof(IShaderTagProcessor).IsAssignableFrom(type))
                    continue;
                if (type.IsAbstract || type.IsInterface)
                    continue;
                
                try
                {
                    var processor = (IShaderTagProcessor)Activator.CreateInstance(type);
                    RegisterProcessor(processor);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ShaderProcessor] Failed to create processor {type.Name}: {e.Message}");
                }
            }
            
            // Sort by priority
            s_SortedProcessors = s_SortedProcessors.OrderBy(p => p.Priority).ToList();
            s_Initialized = true;
            
            Debug.Log($"[ShaderProcessor] Initialized {s_SortedProcessors.Count} tag processors");
        }
        
        public static void RegisterProcessor(IShaderTagProcessor processor)
        {
            if (processor == null) return;
            
            if (s_Processors == null)
            {
                s_Processors = new Dictionary<string, IShaderTagProcessor>(StringComparer.OrdinalIgnoreCase);
                s_SortedProcessors = new List<IShaderTagProcessor>();
            }
            
            // Remove existing processor with same tag
            if (s_Processors.ContainsKey(processor.TagName))
            {
                s_SortedProcessors.RemoveAll(p => 
                    p.TagName.Equals(processor.TagName, StringComparison.OrdinalIgnoreCase));
            }
            
            s_Processors[processor.TagName] = processor;
            s_SortedProcessors.Add(processor);
        }
        
        public static void Reinitialize()
        {
            s_Initialized = false;
            s_Processors = null;
            s_SortedProcessors = null;
            Initialize();
        }
        
        //=============================================================================
        // Processing
        //=============================================================================
        
        /// <summary>
        /// Discover enabled tag processors and collect their material data.
        /// Appends to ctx.ProcessorPropertiesEntries and ctx.ProcessorCBufferEntries
        /// (alongside any entries already collected from pass injectors).
        /// Does NOT inject into source or modify passes - that happens in the main pipeline.
        /// </summary>
        public static void CollectTagProcessorEntries(ShaderContext ctx)
        {
            Initialize();
            
            // Collect processors enabled at SubShader level
            foreach (var processor in s_SortedProcessors)
            {
                if (ctx.SubShaderTags.TryGetValue(processor.TagName, out string tagValue))
                {
                    string mode = ShaderTagUtility.ParseTagMode(tagValue);
                    if (mode != null)
                    {
                        ctx.EnableFeature(processor.TagName, mode);
                    }
                }
            }
            
            // Also check pass-level tags to enable features
            // SubShader-level takes precedence (don't overwrite if already set)
            foreach (var pass in ctx.Passes)
            {
                foreach (var processor in s_SortedProcessors)
                {
                    if (ctx.HasFeature(processor.TagName))
                        continue; // Already enabled at SubShader level
                    
                    string mode = pass.GetTagMode(processor.TagName);
                    if (mode != null)
                    {
                        ctx.EnableFeature(processor.TagName, mode);
                    }
                }
            }
            
            // Collect properties and CBUFFER entries from enabled processors
            var enabledProcessors = s_SortedProcessors
                .Where(p => ctx.HasFeature(p.TagName))
                .OrderBy(p => p.Priority)
                .ToList();
            
            foreach (var processor in enabledProcessors)
            {
                try
                {
                    string props = processor.GetPropertiesEntries(ctx);
                    if (!string.IsNullOrEmpty(props))
                        ctx.ProcessorPropertiesEntries += "\n" + props;
                    
                    string cbuffer = processor.GetCBufferEntries(ctx);
                    if (!string.IsNullOrEmpty(cbuffer))
                        ctx.ProcessorCBufferEntries += "\n" + cbuffer;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ShaderProcessor] {processor.TagName} material collection failed: {e.Message}");
                }
            }
        }
        
        /// <summary>
        /// Inject accumulated processor properties into the Properties block in source.
        /// Call this after both CollectMaterialEntries and CollectTagProcessorEntries
        /// have populated ctx.ProcessorPropertiesEntries.
        /// </summary>
        public static void InjectProcessorProperties(ShaderContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.ProcessorPropertiesEntries)) return;
            
            var match = System.Text.RegularExpressions.Regex.Match(ctx.ProcessedSource,
                $@"(Properties\s*\{{)(.*?)(\}}{ShaderParser.CommentOrWhitespace}SubShader)",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            
            if (!match.Success)
            {
                Debug.LogWarning("[ShaderProcessor] Could not find Properties block for injection");
                return;
            }
            
            string content = match.Groups[2].Value;
            string newContent = content.TrimEnd() + "\n" + ctx.ProcessorPropertiesEntries + "\n    ";
            string replacement = match.Groups[1].Value + newContent + match.Groups[3].Value;
            
            // Index-based replacement to avoid hitting duplicate matches elsewhere in the source
            ctx.ProcessedSource = ctx.ProcessedSource.Substring(0, match.Index)
                + replacement
                + ctx.ProcessedSource.Substring(match.Index + match.Length);
        }
        
        /// <summary>
        /// Inject accumulated processor CBUFFER entries into the CBUFFER in source.
        /// Call this after both CollectMaterialEntries and CollectTagProcessorEntries
        /// have populated ctx.ProcessorCBufferEntries.
        /// </summary>
        public static void InjectProcessorCBuffer(ShaderContext ctx)
        {
            if (string.IsNullOrEmpty(ctx.ProcessorCBufferEntries)) return;
            
            string result = ShaderBlockUtility.InsertBeforeCBufferEnd(
                ctx.ProcessedSource,
                ctx.ProcessorCBufferEntries.TrimEnd() + "\n            ");
            
            if (result == null)
            {
                Debug.LogWarning("[ShaderProcessor] Could not find CBUFFER for injection");
                return;
            }
            
            ctx.ProcessedSource = result;
        }
        
        /// <summary>
        /// Run ModifyPass on all passes where tag processors apply.
        /// Call this AFTER injection and reparse so passes have fresh HlslProgram content.
        /// </summary>
        public static void ModifyTaggedPasses(ShaderContext ctx)
        {
            Initialize();
            
            // Rebuild SubShader-level processor list from enabled features + SubShader tags
            var subShaderEnabledProcessors = new List<IShaderTagProcessor>();
            foreach (var processor in s_SortedProcessors)
            {
                if (ctx.SubShaderTags.TryGetValue(processor.TagName, out string tagValue))
                {
                    string mode = ShaderTagUtility.ParseTagMode(tagValue);
                    if (mode != null)
                        subShaderEnabledProcessors.Add(processor);
                }
            }
            
            foreach (var pass in ctx.Passes)
            {
                var processorsForPass = GetProcessorsForPass(ctx, pass, subShaderEnabledProcessors);
                
                foreach (var processor in processorsForPass)
                {
                    try
                    {
                        processor.ModifyPass(ctx, pass);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[ShaderProcessor] {processor.TagName}.ModifyPass failed on pass '{pass.Name}': {e.Message}");
                    }
                }
            }
        }
        
        /// <summary>
        /// Determine which processors apply to a specific pass.
        /// A processor applies if:
        /// - Its tag is enabled at SubShader level (applies to all passes), OR
        /// - Its tag is enabled at pass level (applies to this pass only)
        /// </summary>
        static List<IShaderTagProcessor> GetProcessorsForPass(
            ShaderContext ctx, 
            PassInfo pass, 
            List<IShaderTagProcessor> subShaderEnabledProcessors)
        {
            var result = new List<IShaderTagProcessor>();
            var addedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // Add processors enabled at SubShader level (they apply to all passes)
            foreach (var processor in subShaderEnabledProcessors)
            {
                result.Add(processor);
                addedTags.Add(processor.TagName);
            }
            
            // Add processors enabled at pass level (if not already added from SubShader)
            foreach (var processor in s_SortedProcessors)
            {
                if (addedTags.Contains(processor.TagName))
                    continue; // Already added from SubShader, skip to avoid duplicates
                
                if (pass.IsTagEnabled(processor.TagName))
                {
                    result.Add(processor);
                    addedTags.Add(processor.TagName);
                }
            }
            
            // Sort by priority
            return result.OrderBy(p => p.Priority).ToList();
        }
        
        /// <summary>
        /// Get all registered processors (regardless of which shader they apply to).
        /// Used by documentation and editor tooling.
        /// </summary>
        public static IReadOnlyList<IShaderTagProcessor> GetAllProcessors()
        {
            Initialize();
            return s_SortedProcessors;
        }
        
        /// <summary>
        /// Get processors that should contribute to generated passes.
        /// Filters out "Pass" mode processors since generated passes should only
        /// receive replacements from "Full" mode features.
        /// </summary>
        public static IEnumerable<IShaderTagProcessor> GetEnabledProcessors(ShaderContext ctx)
        {
            Initialize();
    
            foreach (var processor in s_SortedProcessors)
            {
                string mode = ctx.GetFeatureMode(processor.TagName);
                if (mode == "Full")
                    yield return processor;
            }
        }
        
        //=============================================================================
        // Menu Items
        //=============================================================================
        
        [MenuItem("Tools/ShaderProcessor/Reload Tag Processors")]
        public static void ReloadProcessors()
        {
            Reinitialize();
        }
    }
}
