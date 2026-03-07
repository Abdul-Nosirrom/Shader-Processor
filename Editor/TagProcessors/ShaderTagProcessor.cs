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
        
        /// <summary>Stage 1: Inject into Properties block.</summary>
        void InjectProperties(ShaderContext ctx);
        
        /// <summary>Stage 2: Inject into CBUFFER.</summary>
        void InjectCBuffer(ShaderContext ctx);
        
        /// <summary>Stage 3: Modify the main Forward pass (e.g., add tessellation).</summary>
        void ModifyMainPass(ShaderContext ctx);
        
        /// <summary>Stage 4: Queue passes for injection.</summary>
        void InjectPasses(ShaderContext ctx);
        
        /// <summary>Stage 5: Provide template replacements for generated passes.</summary>
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
        
        public virtual void InjectProperties(ShaderContext ctx) { }
        public virtual void InjectCBuffer(ShaderContext ctx) { }
        public virtual void ModifyMainPass(ShaderContext ctx) { }
        public virtual void InjectPasses(ShaderContext ctx) { }
        
        public virtual Dictionary<string, string> GetPassReplacements(ShaderContext ctx, string passName)
        {
            return null;
        }
        
        //=============================================================================
        // Helper Methods for Derived Classes
        //=============================================================================
        
        /// <summary>
        /// Inject properties at the end of the Properties block.
        /// </summary>
        protected void InjectPropertiesContent(ShaderContext ctx, string properties)
        {
            var match = System.Text.RegularExpressions.Regex.Match(ctx.ProcessedSource,
                @"(Properties\s*\{)(.*?)(\}\s*SubShader)",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            
            if (!match.Success) return;
            
            string content = match.Groups[2].Value;
            string newContent = content.TrimEnd() + "\n" + properties + "\n    ";
            
            ctx.ProcessedSource = ctx.ProcessedSource.Replace(
                match.Value,
                match.Groups[1].Value + newContent + match.Groups[3].Value
            );
        }
        
        /// <summary>
        /// Inject content at the end of the CBUFFER.
        /// </summary>
        protected void InjectCBufferContent(ShaderContext ctx, string cbufferContent)
        {
            var match = System.Text.RegularExpressions.Regex.Match(ctx.ProcessedSource,
                @"(CBUFFER_START\s*\(\s*UnityPerMaterial\s*\))(.*?)(CBUFFER_END)",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            
            if (!match.Success) return;
            
            string content = match.Groups[2].Value;
            string newContent = content.TrimEnd() + "\n" + cbufferContent + "\n    ";
            
            ctx.ProcessedSource = ctx.ProcessedSource.Replace(
                match.Value,
                match.Groups[1].Value + newContent + match.Groups[3].Value
            );
        }
        
        /// <summary>
        /// Queue a pass for injection at the end of SubShader.
        /// </summary>
        protected void QueuePass(ShaderContext ctx, string passCode)
        {
            ctx.QueuedPasses.Add(new QueuedPass
            {
                PassCode = passCode,
                SourceProcessor = TagName
            });
        }
        
        /// <summary>
        /// Check if a property already exists in the Properties block.
        /// </summary>
        protected bool PropertyExists(ShaderContext ctx, string propertyName)
        {
            return ctx.PropertiesBlock?.Contains(propertyName) == true;
        }
        
        /// <summary>
        /// Check if a CBUFFER entry already exists.
        /// </summary>
        protected bool CBufferEntryExists(ShaderContext ctx, string entryName)
        {
            return ctx.CBufferContent?.Contains(entryName) == true;
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
        /// Process all tags found in the shader.
        /// Runs each stage in order across all processors.
        /// </summary>
        public static void ProcessAllTags(ShaderContext ctx)
        {
            Initialize();
            
            // Collect enabled processors
            var enabledProcessors = new List<IShaderTagProcessor>();
            
            foreach (var processor in s_SortedProcessors)
            {
                if (ctx.SubShaderTags.TryGetValue(processor.TagName, out string tagValue))
                {
                    if (IsTagEnabled(tagValue))
                    {
                        enabledProcessors.Add(processor);
                        ctx.EnableFeature(processor.TagName);
                    }
                }
            }
            
            if (enabledProcessors.Count == 0) return;
            
            // Stage 1: Inject Properties
            foreach (var processor in enabledProcessors)
            {
                try
                {
                    processor.InjectProperties(ctx);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ShaderProcessor] {processor.TagName}.InjectProperties failed: {e.Message}");
                }
            }
            
            // Stage 2: Inject CBUFFER
            foreach (var processor in enabledProcessors)
            {
                try
                {
                    processor.InjectCBuffer(ctx);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ShaderProcessor] {processor.TagName}.InjectCBuffer failed: {e.Message}");
                }
            }
            
            // Stage 3: Modify Main Pass
            foreach (var processor in enabledProcessors)
            {
                try
                {
                    processor.ModifyMainPass(ctx);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ShaderProcessor] {processor.TagName}.ModifyMainPass failed: {e.Message}");
                }
            }
            
            // Stage 4: Inject Passes (queued for later injection)
            foreach (var processor in enabledProcessors)
            {
                try
                {
                    processor.InjectPasses(ctx);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ShaderProcessor] {processor.TagName}.InjectPasses failed: {e.Message}");
                }
            }
        }
        
        /// <summary>
        /// Get all processors that are enabled for this shader.
        /// </summary>
        public static IEnumerable<IShaderTagProcessor> GetEnabledProcessors(ShaderContext ctx)
        {
            Initialize();
    
            foreach (var processor in s_SortedProcessors)
            {
                if (ctx.HasFeature(processor.TagName))
                    yield return processor;
            }
        }
        
        static bool IsTagEnabled(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            value = value.Trim().ToLowerInvariant();
            return value == "on" || value == "true" || value == "enabled" || value == "1" || value == "yes";
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
