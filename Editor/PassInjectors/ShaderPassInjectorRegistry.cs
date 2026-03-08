using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Registry for shader pass injectors.
    /// Discovers injectors via TypeCache, provides lookup by name,
    /// and a shared Generate method for pass code generation.
    /// </summary>
    public static class ShaderPassInjectorRegistry
    {
        static List<ShaderPassInjector> s_Injectors;
        static Dictionary<string, ShaderPassInjector> s_ByName;
        static bool s_Initialized;
        
        //=============================================================================
        // Initialization
        //=============================================================================
        
        public static void Initialize()
        {
            if (s_Initialized) return;
            
            s_Injectors = new List<ShaderPassInjector>();
            s_ByName = new Dictionary<string, ShaderPassInjector>(StringComparer.OrdinalIgnoreCase);
            
            var types = TypeCache.GetTypesWithAttribute<ShaderPassAttribute>();
            
            foreach (var type in types)
            {
                if (!typeof(ShaderPassInjector).IsAssignableFrom(type))
                    continue;
                if (type.IsAbstract)
                    continue;
                
                try
                {
                    var injector = (ShaderPassInjector)Activator.CreateInstance(type);
                    s_Injectors.Add(injector);
                    s_ByName[injector.PassName] = injector;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ShaderPassInjectorRegistry] Failed to create injector {type.Name}: {e.Message}");
                }
            }
            
            s_Initialized = true;
            Debug.Log($"[ShaderPassInjectorRegistry] Registered {s_Injectors.Count} pass injectors");
        }
        
        //=============================================================================
        // Access
        //=============================================================================
        
        /// <summary>All registered pass injectors.</summary>
        public static IReadOnlyList<ShaderPassInjector> All
        {
            get
            {
                Initialize();
                return s_Injectors;
            }
        }
        
        /// <summary>All pass injectors where IsBasePass is true.</summary>
        public static IEnumerable<ShaderPassInjector> BasePasses
        {
            get
            {
                Initialize();
                return s_Injectors.Where(p => p.IsBasePass);
            }
        }
        
        /// <summary>Look up a pass injector by PassName. Returns null if not found.</summary>
        public static ShaderPassInjector GetByName(string passName)
        {
            Initialize();
            return s_ByName.TryGetValue(passName, out var injector) ? injector : null;
        }
        
        //=============================================================================
        // Generation
        //=============================================================================
        
        /// <summary>
        /// Generate a pass by name. Loads the template, applies struct overrides
        /// and additional replacements from the injector, then runs ProcessPassTemplate.
        /// Returns the generated pass code, or an error comment if the injector or template isn't found.
        /// </summary>
        public static string Generate(ShaderContext ctx, string passName)
        {
            var injector = GetByName(passName);
            if (injector == null)
                return $"// ERROR: No pass injector registered for '{passName}'";
            
            return Generate(ctx, injector);
        }
        
        /// <summary>
        /// Generate a pass from a specific injector instance.
        /// </summary>
        public static string Generate(ShaderContext ctx, ShaderPassInjector injector)
        {
            string template = TemplateEngine.LoadTemplate(injector.TemplateName);
            if (string.IsNullOrEmpty(template))
                return $"// ERROR: Template '{injector.TemplateName}' not found for pass '{injector.PassName}'";
            
            // Collect all overrides from the injector
            var structOverrides = injector.GetStructOverrides(ctx);
            var additionalReplacements = injector.GetAdditionalReplacements(ctx);
            
            // Merge: struct overrides first, then additional replacements on top
            Dictionary<string, string> merged = null;
            if (structOverrides != null || additionalReplacements != null)
            {
                merged = new Dictionary<string, string>();
                
                if (structOverrides != null)
                {
                    foreach (var kvp in structOverrides)
                        merged[kvp.Key] = kvp.Value;
                }
                
                if (additionalReplacements != null)
                {
                    foreach (var kvp in additionalReplacements)
                        merged[kvp.Key] = kvp.Value;
                }
            }
            
            return TemplateEngine.ProcessPassTemplate(template, ctx, injector.PassName, merged);
        }
        
        /// <summary>
        /// Generate all base passes. Used by [InjectBasePasses].
        /// </summary>
        public static string GenerateAllBasePasses(ShaderContext ctx)
        {
            var sb = new StringBuilder();
            bool first = true;
            
            foreach (var injector in BasePasses)
            {
                if (!first) sb.AppendLine();
                sb.AppendLine(Generate(ctx, injector));
                first = false;
            }
            
            return sb.ToString();
        }
        
        //=============================================================================
        // Material Data Collection
        //=============================================================================
        
        /// <summary>
        /// Collect properties and CBUFFER entries from all active pass injectors.
        /// "Active" means the shader source contains [InjectBasePasses] (activating all
        /// base passes) or [InjectPass:Name] (activating a specific pass).
        /// 
        /// Call this before ProcessAllTags so pass injector material data gets injected
        /// alongside tag processor data in the same flow.
        /// </summary>
        public static void CollectMaterialEntries(ShaderContext ctx)
        {
            Initialize();
            
            string source = ctx.ProcessedSource;
            
            // Determine which injectors are active based on markers in the source
            var activeInjectors = new List<ShaderPassInjector>();
            
            // [InjectBasePasses] activates all base passes
            int basePassIdx = source.IndexOf("[InjectBasePasses]", StringComparison.Ordinal);
            if (basePassIdx >= 0 && !ShaderProcessor.IsInComment(source, basePassIdx))
            {
                foreach (var injector in BasePasses)
                    activeInjectors.Add(injector);
            }
            
            // [InjectPass:X] activates specific passes
            foreach (var injector in s_Injectors)
            {
                if (activeInjectors.Contains(injector))
                    continue;
    
                string marker = $"[InjectPass:{injector.PassName}]";
                int idx = source.IndexOf(marker, StringComparison.Ordinal);
                if (idx >= 0 && !ShaderProcessor.IsInComment(source, idx))
                    activeInjectors.Add(injector);
            }
            
            // Collect properties and CBUFFER entries from active injectors
            foreach (var injector in activeInjectors)
            {
                try
                {
                    string props = injector.GetPropertiesEntries(ctx);
                    if (!string.IsNullOrEmpty(props))
                        ctx.ProcessorPropertiesEntries += "\n" + props;
                    
                    string cbuffer = injector.GetCBufferEntries(ctx);
                    if (!string.IsNullOrEmpty(cbuffer))
                        ctx.ProcessorCBufferEntries += "\n" + cbuffer;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ShaderPassInjectorRegistry] {injector.PassName} material collection failed: {e.Message}");
                }
            }
        }
        
        //=============================================================================
        // Reload
        //=============================================================================
        
        public static void Reinitialize()
        {
            s_Initialized = false;
            s_Injectors = null;
            s_ByName = null;
            Initialize();
        }
    }
}
