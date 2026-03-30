using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Loads and processes HLSL template files.
    /// Templates use {{MARKER}} syntax for replacements.
    /// </summary>
    public static class TemplateEngine
    {
        static Dictionary<string, string> s_TemplateCache = new Dictionary<string, string>();
        static string s_TemplatesPath;
        
        //=============================================================================
        // Path Management
        //=============================================================================
        
        public static string TemplatesPath
        {
            get
            {
                if (string.IsNullOrEmpty(s_TemplatesPath))
                    s_TemplatesPath = FindTemplatesPath();
                return s_TemplatesPath;
            }
        }
        
        static string FindTemplatesPath()
        {
            var guids = AssetDatabase.FindAssets("t:Script TemplateEngine");
            
            foreach (var guid in guids)
            {
                string scriptPath = AssetDatabase.GUIDToAssetPath(guid);
                string dir = Path.GetDirectoryName(scriptPath);
                
                string templatesPath = Path.Combine(dir, "Templates");
                if (AssetDatabase.IsValidFolder(templatesPath))
                    return templatesPath;
                
                string parentDir = Path.GetDirectoryName(dir);
                templatesPath = Path.Combine(parentDir, "Templates");
                if (AssetDatabase.IsValidFolder(templatesPath))
                    return templatesPath;
            }
            
            var fallbackPaths = new[]
            {
                "Assets/Editor/ShaderProcessing/Templates",
                "Assets/FreeSkies/Editor/ShaderProcessing/Templates",
                "Assets/Scripts/Editor/ShaderProcessing/Templates"
            };
            
            foreach (var path in fallbackPaths)
            {
                if (AssetDatabase.IsValidFolder(path))
                    return path;
            }
            
            Debug.LogWarning("[ShaderProcessor] Could not find Templates folder. Using default path.");
            return "Assets/Editor/ShaderProcessing/Templates";
        }
        
        //=============================================================================
        // Template Loading
        //=============================================================================
        
        public static string LoadTemplate(string templateName)
        {
            if (s_TemplateCache.TryGetValue(templateName, out string cached))
                return cached;
            
            string path = Path.Combine(TemplatesPath, templateName + ".hlsl");
            
            if (!File.Exists(path))
            {
                Debug.LogError($"[ShaderProcessor] Template not found: {path}");
                return null;
            }
            
            string content = File.ReadAllText(path);
            s_TemplateCache[templateName] = content;
            return content;
        }
        
        public static void ClearCache()
        {
            s_TemplateCache.Clear();
            s_TemplatesPath = null;
        }
        
        [MenuItem("Tools/ShaderProcessor/Reload Templates")]
        public static void ReloadTemplates()
        {
            ClearCache();
            
            if (Directory.Exists(TemplatesPath))
            {
                var files = Directory.GetFiles(TemplatesPath, "*.hlsl");
                Debug.Log($"[ShaderProcessor] Found {files.Length} templates in {TemplatesPath}");
            }
            else
            {
                Debug.LogWarning($"[ShaderProcessor] Templates folder not found: {TemplatesPath}");
            }
        }
        
        //=============================================================================
        // Template Processing
        //=============================================================================
        
        /// <summary>
        /// Process a template with simple key-value replacements.
        /// </summary>
        public static string Process(string template, Dictionary<string, string> replacements)
        {
            if (string.IsNullOrEmpty(template)) return template;
            if (replacements == null || replacements.Count == 0) return template;
            
            string result = template;
            
            foreach (var kvp in replacements)
            {
                result = result.Replace("{{" + kvp.Key + "}}", kvp.Value ?? "");
            }
            
            // Remove any unreplaced markers
            result = Regex.Replace(result, @"\{\{[^}]+\}\}", "");
            
            return result;
        }
        
        /// <summary>
        /// Process a pass template with context-aware replacements.
        /// </summary>
        public static string ProcessPassTemplate(string template, ShaderContext ctx, string passName,
            Dictionary<string, string> additionalReplacements = null)
        {
            var replacements = new Dictionary<string, string>();
            
            // Pass-specific struct names
            string attrName = passName + "Attributes";
            string interpName = passName + "Interpolators";
            replacements["PASS_NAME"] = passName;
            replacements["ATTRIBUTES_NAME"] = attrName;
            replacements["INTERPOLATORS_NAME"] = interpName;
            
            // Semantic-based field name resolution
            AddSemanticReplacements(replacements, ctx);
            
            // Generate structs
            replacements["ATTRIBUTES_STRUCT"] = StructGenerator.GenerateAttributesStruct(ctx, attrName);
            replacements["INTERPOLATORS_STRUCT"] = StructGenerator.GenerateInterpolatorsStruct(ctx, interpName);
            
            // Forward pass content, split into preprocessor and code.
            // Preprocessor (includes, defines, pragmas) goes above CBUFFER so macros
            // used in CBUFFER/structs are defined. Code (helper functions) goes below
            // CBUFFER/textures/structs since it references those declarations.
            var forwardContent = HookProcessor.ExtractForwardPassContentSplit(ctx, attrName, interpName);
            replacements["FORWARD_PREPROCESSOR"] = forwardContent.Preprocessor;
            replacements["FORWARD_CONTENT"] = forwardContent.Content;
            
            // CBUFFER (only if not in HLSLINCLUDE)
            if (!ctx.CBufferInHlslInclude)
            {
                replacements["CBUFFER"] = GenerateCBuffer(ctx);
            }
            else
            {
                replacements["CBUFFER"] = "";
            }
            
            // Textures (only if not in HLSLINCLUDE)
            if (!ctx.TexturesInHlslInclude)
            {
                replacements["TEXTURES"] = GenerateTextures(ctx);
            }
            else
            {
                replacements["TEXTURES"] = "";
            }
            
            // Hook functions with rewritten struct names and prefixed function names
            replacements["HOOK_FUNCTIONS"] = HookProcessor.GenerateHookFunctions(ctx, attrName, interpName, passName);
            
            // Hook feature defines
            replacements["HOOK_DEFINES"] = GenerateHookDefines(ctx);
            
            // Hook calls (with #ifdef guards) - one per registered hook
            foreach (var hook in ShaderHookRegistry.All)
            {
                replacements[hook.TemplateMarker] = GenerateHookCall(ctx, hook, passName);
            }
            
            // Default vertex pragma (tag processors can override)
            replacements["VERTEX_PRAGMA"] = $"#pragma vertex {passName}Vertex";
            
            // Extra fragment parameters (SV_IsFrontFace, etc.) from reference pass
            replacements["EXTRA_FRAG_PARAMS"] = ctx.ExtraFragmentParams ?? "";
            
            // Empty defaults for tag processor markers
            replacements["TESSELLATION_PRAGMAS"] = "";
            replacements["TESSELLATION_CODE"] = "";
            
            // Let tag processors contribute replacements
            foreach (var processor in ShaderTagProcessorRegistry.GetEnabledProcessors(ctx))
            {
                var procReplacements = processor.GetPassReplacements(ctx, passName);
                if (procReplacements != null)
                {
                    foreach (var kvp in procReplacements)
                        replacements[kvp.Key] = kvp.Value;
                }
            }
            
            // Merge additional replacements (highest priority)
            if (additionalReplacements != null)
            {
                foreach (var kvp in additionalReplacements)
                    replacements[kvp.Key] = kvp.Value;
            }
            
            return Process(template, replacements);
        }
        
        /// <summary>
        /// Add hook-related replacements to a dictionary.
        /// Call this from tag processors that process their own templates and need hook support.
        /// Populates HOOK_DEFINES, HOOK_FUNCTIONS, and all registered hook call markers.
        /// Pass passName to prefix function names (avoids collision with HLSLINCLUDE versions).
        /// </summary>
        public static void AddHookReplacements(Dictionary<string, string> replacements,
            ShaderContext ctx, string attrName, string interpName, string passName = "")
        {
            replacements["HOOK_DEFINES"] = GenerateHookDefines(ctx);
            replacements["HOOK_FUNCTIONS"] = HookProcessor.GenerateHookFunctions(ctx, attrName, interpName, passName);
            
            foreach (var hook in ShaderHookRegistry.All)
            {
                replacements[hook.TemplateMarker] = GenerateHookCall(ctx, hook, passName);
            }
        }
        
        //=============================================================================
        // Semantic Replacements
        //=============================================================================
        
        static void AddSemanticReplacements(Dictionary<string, string> replacements, ShaderContext ctx)
        {
            // Attributes semantics - with fallback defaults
            if (ctx.Attributes != null)
            {
                replacements["POSITION"] = ctx.Attributes.GetField("POSITION")?.Name ?? "positionOS";
                replacements["NORMAL"] = ctx.Attributes.GetField("NORMAL")?.Name ?? "normalOS";
                replacements["TANGENT"] = ctx.Attributes.GetField("TANGENT")?.Name ?? "tangentOS";
                replacements["TEXCOORD0"] = ctx.Attributes.GetField("TEXCOORD0")?.Name ?? "uv";
                replacements["TEXCOORD1"] = ctx.Attributes.GetField("TEXCOORD1")?.Name ?? "uv1";
                replacements["TEXCOORD2"] = ctx.Attributes.GetField("TEXCOORD2")?.Name ?? "uv2";
                replacements["COLOR"] = ctx.Attributes.GetField("COLOR")?.Name ?? "color";
            }
            else
            {
                // Fallback defaults when struct not found
                replacements["POSITION"] = "positionOS";
                replacements["NORMAL"] = "normalOS";
                replacements["TANGENT"] = "tangentOS";
                replacements["TEXCOORD0"] = "uv";
                replacements["TEXCOORD1"] = "uv1";
                replacements["TEXCOORD2"] = "uv2";
                replacements["COLOR"] = "color";
            }
            
            // Interpolators - only SV_POSITION has a real semantic
            replacements["SV_POSITION"] = ctx.Interpolators?.GetField("SV_POSITION")?.Name ?? "positionCS";
            
            // Normal needed for DepthNormals pass. We can't reliably pick up the interpolator
            // field by name, so we ask shader authors to mark up interpolator semantics.
            // For convenience, we try multiple semantic conventions (NORMAL, NORMALWS, NORMAL_WS).
            replacements["NORMAL_WS"] = ctx.Interpolators?.GetField("NORMAL")?.Name
                                     ?? ctx.Interpolators?.GetField("NORMALWS")?.Name
                                     ?? ctx.Interpolators?.GetField("NORMAL_WS")?.Name
                                     ?? "normalWS";
        }
        
        //=============================================================================
        // Code Generation
        //=============================================================================
        
        static string GenerateCBuffer(ShaderContext ctx)
        {
            // Combine parsed CBUFFER content with processor additions
            string allContent = (ctx.CBufferContent ?? "") + (ctx.ProcessorCBufferEntries ?? "");
            
            if (string.IsNullOrWhiteSpace(allContent))
                return "";
            
            // Normalize indentation
            var sb = new System.Text.StringBuilder();
            foreach (var line in allContent.Split('\n'))
            {
                string trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    sb.AppendLine("    " + trimmed);
            }
            
            return $@"CBUFFER_START(UnityPerMaterial)
{sb.ToString().TrimEnd()}
CBUFFER_END";
        }
        
        static string GenerateTextures(ShaderContext ctx)
        {
            if (ctx.Textures.Count == 0) return "";
            
            var sb = new System.Text.StringBuilder();
            var added = new HashSet<string>();
            
            foreach (var tex in ctx.Textures)
            {
                if (added.Add(tex.Name))
                    sb.AppendLine(tex.RawDeclaration);
            }
            
            return sb.ToString();
        }
        
        static string GenerateHookDefines(ShaderContext ctx)
        {
            var sb = new System.Text.StringBuilder();
            
            foreach (var hook in ShaderHookRegistry.All)
            {
                if (ctx.Hooks.IsActive(hook.PragmaName))
                    sb.AppendLine($"#define {hook.Define}");
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Generate a guarded hook call for a template marker.
        /// When passName is provided, the function name is prefixed to match the renamed
        /// version in HOOK_FUNCTIONS (e.g., "HeightDisplace" → "DepthOnlyHeightDisplace").
        /// Returns empty string if the hook is not active for this shader.
        /// </summary>
        static string GenerateHookCall(ShaderContext ctx, ShaderHookDefinition hook, string passName = "")
        {
            if (!ctx.Hooks.IsActive(hook.PragmaName)) return "";
            
            string funcName = ctx.Hooks.GetFunctionName(hook.PragmaName);
            
            // Prefix with pass name to match renamed function in HOOK_FUNCTIONS
            if (!string.IsNullOrEmpty(passName))
                funcName = passName + funcName;
            
            string call = hook.CallPattern.Replace("{FuncName}", funcName);
            
            return $@"#ifdef {hook.Define}
        {call}
#endif";
        }
    }
}