using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Editor window that displays documentation for all registered
    /// hooks, tag processors, and passes. All data is discovered
    /// dynamically via TypeCache, so it's always up to date.
    /// </summary>
    public class ShaderProcessorDocsWindow : EditorWindow
    {
        int _selectedTab;
        Vector2 _scrollPos;
        
        static readonly string[] TabNames = { "Hooks", "Tag Processors", "Passes" };
        
        // Cached data (refreshed on enable)
        List<ShaderHookDefinition> _hooks;
        List<IShaderTagProcessor> _tagProcessors;
        
        // Styles (lazy-initialized)
        GUIStyle _headerStyle;
        GUIStyle _subHeaderStyle;
        GUIStyle _monoStyle;
        GUIStyle _descriptionStyle;
        GUIStyle _cardStyle;
        bool _stylesInitialized;
        
        //=============================================================================
        // Window Lifecycle
        //=============================================================================
        
        [MenuItem("Tools/ShaderProcessor/Docs")]
        public static void ShowWindow()
        {
            var window = GetWindow<ShaderProcessorDocsWindow>("ShaderGen Docs");
            window.minSize = new Vector2(450, 350);
        }
        
        void OnEnable()
        {
            RefreshData();
        }
        
        void RefreshData()
        {
            ShaderHookRegistry.Initialize();
            ShaderTagProcessorRegistry.Initialize();
            
            _hooks = ShaderHookRegistry.All.ToList();
            _tagProcessors = ShaderTagProcessorRegistry.GetAllProcessors().ToList();
        }
        
        //=============================================================================
        // Styles
        //=============================================================================
        
        void InitStyles()
        {
            if (_stylesInitialized) return;
            
            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                margin = new RectOffset(4, 4, 8, 4)
            };
            
            _subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                margin = new RectOffset(4, 4, 4, 2)
            };
            
            _monoStyle = new GUIStyle(EditorStyles.label)
            {
                font = Font.CreateDynamicFontFromOSFont("Consolas", 11),
                fontSize = 11,
                richText = true
            };
            
            _descriptionStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                fontSize = 11,
                margin = new RectOffset(8, 8, 2, 2)
            };
            
            _cardStyle = new GUIStyle("HelpBox")
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(4, 4, 4, 4)
            };
            
            _stylesInitialized = true;
        }
        
        //=============================================================================
        // Drawing
        //=============================================================================
        
        void OnGUI()
        {
            InitStyles();
            
            // Toolbar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _selectedTab = GUILayout.Toolbar(_selectedTab, TabNames, EditorStyles.toolbarButton);
            
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                _stylesInitialized = false;
                ShaderHookRegistry.Reinitialize();
                ShaderTagProcessorRegistry.Reinitialize();
                RefreshData();
            }
            EditorGUILayout.EndHorizontal();
            
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            
            switch (_selectedTab)
            {
                case 0: DrawHooksTab(); break;
                case 1: DrawTagProcessorsTab(); break;
                case 2: DrawPassesTab(); break;
            }
            
            EditorGUILayout.EndScrollView();
        }
        
        //=============================================================================
        // Hooks Tab
        //=============================================================================
        
        void DrawHooksTab()
        {
            EditorGUILayout.LabelField("Shader Hooks", _headerStyle);
            EditorGUILayout.LabelField(
                "Hooks let shader authors inject custom functions at specific pipeline stages. " +
                "Declare a hook with #pragma <pragmaName> <FunctionName> in your forward pass.",
                _descriptionStyle);
            EditorGUILayout.Space(4);
            
            if (_hooks == null || _hooks.Count == 0)
            {
                EditorGUILayout.HelpBox("No hooks registered.", MessageType.Info);
                return;
            }
            
            foreach (var hook in _hooks)
            {
                EditorGUILayout.BeginVertical(_cardStyle);
                
                EditorGUILayout.LabelField(hook.PragmaName, _subHeaderStyle);
                
                DrawField("Pragma", $"#pragma {hook.PragmaName} <FunctionName>");
                DrawField("Define", $"#define {hook.Define}");
                DrawField("Call Pattern", hook.CallPattern);
                DrawField("Template Marker", $"{{{{{hook.TemplateMarker}}}}}");
                DrawField("Type", hook.GetType().Name);
                
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginVertical(_cardStyle);
            EditorGUILayout.LabelField("Adding a New Hook", _subHeaderStyle);
            EditorGUILayout.LabelField(
                "1. Create a class inheriting ShaderHookDefinition with [ShaderHook] attribute\n" +
                "2. Override PragmaName, Define, CallPattern, and TemplateMarker\n" +
                "3. Place {{MARKER}} in your template at the desired call site\n" +
                "4. The hook is automatically discovered — no pipeline code changes needed",
                _descriptionStyle);
            EditorGUILayout.EndVertical();
        }
        
        //=============================================================================
        // Tag Processors Tab
        //=============================================================================
        
        void DrawTagProcessorsTab()
        {
            EditorGUILayout.LabelField("Tag Processors", _headerStyle);
            EditorGUILayout.LabelField(
                "Tag processors activate when their tag is present in SubShader or Pass tags. " +
                "They can inject properties, CBUFFER entries, modify passes, and queue new passes.",
                _descriptionStyle);
            EditorGUILayout.Space(4);
            
            if (_tagProcessors == null || _tagProcessors.Count == 0)
            {
                EditorGUILayout.HelpBox("No tag processors registered.", MessageType.Info);
                return;
            }
            
            foreach (var proc in _tagProcessors)
            {
                EditorGUILayout.BeginVertical(_cardStyle);
                
                EditorGUILayout.LabelField(proc.TagName, _subHeaderStyle);
                
                DrawField("Tag", $"\"{proc.TagName}\" = \"On\"");
                DrawField("Priority", proc.Priority.ToString());
                DrawField("Type", proc.GetType().Name);
                
                // Show capabilities
                var caps = new List<string>();
                // We can't call the methods without a context, but we can check if they're overridden
                var type = proc.GetType();
                if (IsMethodOverridden(type, "GetPropertiesEntries")) caps.Add("Properties");
                if (IsMethodOverridden(type, "GetCBufferEntries")) caps.Add("CBUFFER");
                if (IsMethodOverridden(type, "ModifyPass")) caps.Add("ModifyPass");
                if (IsMethodOverridden(type, "InjectPasses")) caps.Add("InjectPasses");
                if (IsMethodOverridden(type, "GetPassReplacements")) caps.Add("PassReplacements");
                
                if (caps.Count > 0)
                    DrawField("Capabilities", string.Join(", ", caps));
                
                EditorGUILayout.EndVertical();
            }
            
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginVertical(_cardStyle);
            EditorGUILayout.LabelField("Adding a New Tag Processor", _subHeaderStyle);
            EditorGUILayout.LabelField(
                "1. Create a class inheriting ShaderTagProcessorBase\n" +
                "2. Add [ShaderTagProcessor(\"TagName\", priority: N)] attribute\n" +
                "3. Override desired stages: GetPropertiesEntries, GetCBufferEntries,\n" +
                "   ModifyPass, InjectPasses, GetPassReplacements\n" +
                "4. Enable in shader: Tags { \"TagName\" = \"On\" }",
                _descriptionStyle);
            EditorGUILayout.EndVertical();
        }
        
        //=============================================================================
        // Passes Tab
        //=============================================================================
        
        void DrawPassesTab()
        {
            EditorGUILayout.LabelField("Generated Passes", _headerStyle);
            EditorGUILayout.LabelField(
                "Passes are generated from templates and injected via [InjectBasePasses] or " +
                "[InjectPass:PassName] markers in your shader.",
                _descriptionStyle);
            EditorGUILayout.Space(4);
            
            // Base passes (currently hardcoded in PassGenerator)
            var basePasses = new[]
            {
                ("ShadowCaster", "Renders shadow map depth. Supports vertex displacement and alpha clip hooks."),
                ("DepthOnly", "Writes depth for the depth prepass. Supports all standard hooks."),
                ("DepthNormals", "Writes depth and world-space normals. Adds normalWS if missing from structs."),
                ("MotionVectors", "Outputs per-pixel motion for temporal effects. Adds previous frame position fields."),
                ("Meta", "Used for lightmap baking and GI. Adds uv1/uv2 for lightmap coordinates."),
            };
            
            EditorGUILayout.LabelField("Base Passes", _subHeaderStyle);
            EditorGUILayout.LabelField(
                "Injected via [InjectBasePasses] or individually via [InjectPass:Name].",
                _descriptionStyle);
            EditorGUILayout.Space(2);
            
            foreach (var (name, desc) in basePasses)
            {
                EditorGUILayout.BeginVertical(_cardStyle);
                EditorGUILayout.LabelField(name, _subHeaderStyle);
                EditorGUILayout.LabelField(desc, _descriptionStyle);
                DrawField("Marker", $"[InjectPass:{name}]");
                DrawField("Template", $"Templates/{name}.hlsl");
                EditorGUILayout.EndVertical();
            }
            
            // Tag processor passes
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Tag Processor Passes", _subHeaderStyle);
            EditorGUILayout.LabelField(
                "Additional passes injected by tag processors when their tag is enabled.",
                _descriptionStyle);
            EditorGUILayout.Space(2);
            
            if (_tagProcessors != null)
            {
                bool anyPassInjector = false;
                foreach (var proc in _tagProcessors)
                {
                    if (IsMethodOverridden(proc.GetType(), "InjectPasses"))
                    {
                        anyPassInjector = true;
                        EditorGUILayout.BeginVertical(_cardStyle);
                        EditorGUILayout.LabelField(proc.TagName, _subHeaderStyle);
                        DrawField("Source", proc.GetType().Name);
                        DrawField("Activated by", $"Tags {{ \"{proc.TagName}\" = \"On\" }}");
                        EditorGUILayout.EndVertical();
                    }
                }
                
                if (!anyPassInjector)
                    EditorGUILayout.HelpBox("No tag processors currently inject passes.", MessageType.Info);
            }
        }
        
        //=============================================================================
        // Helpers
        //=============================================================================
        
        void DrawField(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel, GUILayout.Width(110));
            EditorGUILayout.SelectableLabel(value, _monoStyle, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }
        
        static bool IsMethodOverridden(System.Type type, string methodName)
        {
            var method = type.GetMethod(methodName, 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return method != null && method.DeclaringType != typeof(ShaderTagProcessorBase);
        }
    }
}
