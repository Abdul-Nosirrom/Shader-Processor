using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Registry for shader hook definitions.
    /// Discovers hooks via TypeCache and provides ordered iteration.
    /// </summary>
    public static class ShaderHookRegistry
    {
        static List<ShaderHookDefinition> s_Hooks;
        static Dictionary<string, ShaderHookDefinition> s_ByPragmaName;
        static bool s_Initialized;
        
        //=============================================================================
        // Initialization
        //=============================================================================
        
        /// <summary>
        /// Discover and register all hook definitions via TypeCache.
        /// Safe to call multiple times - subsequent calls are no-ops.
        /// </summary>
        public static void Initialize()
        {
            if (s_Initialized) return;
            
            s_Hooks = new List<ShaderHookDefinition>();
            s_ByPragmaName = new Dictionary<string, ShaderHookDefinition>(StringComparer.OrdinalIgnoreCase);
            
            var hookTypes = TypeCache.GetTypesWithAttribute<ShaderHookAttribute>();
            
            foreach (var type in hookTypes)
            {
                if (!typeof(ShaderHookDefinition).IsAssignableFrom(type))
                    continue;
                if (type.IsAbstract)
                    continue;
                
                try
                {
                    var hook = (ShaderHookDefinition)Activator.CreateInstance(type);
                    s_Hooks.Add(hook);
                    s_ByPragmaName[hook.PragmaName] = hook;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ShaderHookRegistry] Failed to create hook {type.Name}: {e.Message}");
                }
            }
            
            s_Hooks = s_Hooks.OrderBy(h => h.PragmaName).ToList();
            s_Initialized = true;
            
            Debug.Log($"[ShaderHookRegistry] Registered {s_Hooks.Count} hooks");
        }
        
        //=============================================================================
        // Access
        //=============================================================================
        
        /// <summary>All registered hooks, sorted by Order.</summary>
        public static IReadOnlyList<ShaderHookDefinition> All
        {
            get
            {
                Initialize();
                return s_Hooks;
            }
        }
        
        /// <summary>Look up a hook definition by pragma name. Returns null if not found.</summary>
        public static ShaderHookDefinition GetByPragmaName(string pragmaName)
        {
            Initialize();
            return s_ByPragmaName.TryGetValue(pragmaName, out var hook) ? hook : null;
        }
        
        //=============================================================================
        // Reload
        //=============================================================================
        
        /// <summary>
        /// Force re-discovery of all hook definitions. Clears existing state first.
        /// </summary>
        public static void Reinitialize()
        {
            s_Initialized = false;
            s_Hooks = null;
            s_ByPragmaName = null;
            Initialize();
        }
    }
}
