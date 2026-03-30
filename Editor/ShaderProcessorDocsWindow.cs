using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Editor window that displays documentation for all registered
    /// hooks, tag processors, and pass injectors. All data is discovered
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
        List<ShaderPassInjector> _passInjectors;
        
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
            ShaderPassInjectorRegistry.Initialize();
            
            _hooks = ShaderHookRegistry.All.ToList();
            _tagProcessors = ShaderTagProcessorRegistry.GetAllProcessors().ToList();
            _passInjectors = ShaderPassInjectorRegistry.All.ToList();
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
                fontSize = 11,
                richText = true,
                wordWrap = false
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
                ShaderPassInjectorRegistry.Reinitialize();
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
                "4. The hook is automatically discovered - no pipeline code changes needed",
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
                "They modify existing passes, inject properties/CBUFFER entries, and provide " +
                "template replacements for generated passes.",
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
                
                // Show capabilities based on which methods are overridden
                var caps = new List<string>();
                var type = proc.GetType();
                if (IsMethodOverridden(type, "GetPropertiesEntries")) caps.Add("Properties");
                if (IsMethodOverridden(type, "GetCBufferEntries")) caps.Add("CBUFFER");
                if (IsMethodOverridden(type, "ModifyPass")) caps.Add("ModifyPass");
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
                "   ModifyPass, GetPassReplacements\n" +
                "4. Enable in shader: Tags { \"TagName\" = \"On\" }",
                _descriptionStyle);
            EditorGUILayout.EndVertical();
        }
        
        //=============================================================================
        // Passes Tab
        //=============================================================================
        
        void DrawPassesTab()
        {
            EditorGUILayout.LabelField("Pass Injectors", _headerStyle);
            EditorGUILayout.LabelField(
                "Pass injectors define how to generate shader passes from templates. " +
                "Activated by [InjectBasePasses] (all base passes) or [InjectPass:Name] (specific pass).",
                _descriptionStyle);
            EditorGUILayout.Space(4);
            
            if (_passInjectors == null || _passInjectors.Count == 0)
            {
                EditorGUILayout.HelpBox("No pass injectors registered.", MessageType.Info);
                return;
            }
            
            // Base passes
            var basePasses = _passInjectors.Where(p => p.IsBasePass).ToList();
            if (basePasses.Count > 0)
            {
                EditorGUILayout.LabelField("Base Passes", _subHeaderStyle);
                EditorGUILayout.LabelField(
                    "Included in [InjectBasePasses]. Can also be used individually via [InjectPass:Name].",
                    _descriptionStyle);
                EditorGUILayout.Space(2);
                
                foreach (var pass in basePasses)
                {
                    DrawPassCard(pass);
                }
            }
            
            // Feature passes
            var featurePasses = _passInjectors.Where(p => !p.IsBasePass).ToList();
            if (featurePasses.Count > 0)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Feature Passes", _subHeaderStyle);
                EditorGUILayout.LabelField(
                    "Not included in [InjectBasePasses]. Activate individually via [InjectPass:Name].",
                    _descriptionStyle);
                EditorGUILayout.Space(2);
                
                foreach (var pass in featurePasses)
                {
                    DrawPassCard(pass);
                }
            }
            
            // How to add
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginVertical(_cardStyle);
            EditorGUILayout.LabelField("Adding a New Pass", _subHeaderStyle);
            EditorGUILayout.LabelField(
                "1. Create a template file: Templates/MyPass.hlsl\n" +
                "2. Create a class inheriting ShaderPassInjector with [ShaderPass] attribute\n" +
                "3. Override PassName and TemplateName (required)\n" +
                "4. Optionally override: IsBasePass, GetPropertiesEntries, GetCBufferEntries,\n" +
                "   GetStructOverrides, GetAdditionalReplacements\n" +
                "5. Use [InjectPass:MyPass] in your shader",
                _descriptionStyle);
            EditorGUILayout.EndVertical();
        }
        
        void DrawPassCard(ShaderPassInjector pass)
        {
            EditorGUILayout.BeginVertical(_cardStyle);
            
            EditorGUILayout.LabelField(pass.PassName, _subHeaderStyle);
            
            DrawField("Marker", $"[InjectPass:{pass.PassName}]");
            DrawField("Template", $"Templates/{pass.TemplateName}.hlsl");
            DrawField("Base Pass", pass.IsBasePass ? "Yes" : "No");
            DrawField("Type", pass.GetType().Name);
            
            // Show capabilities
            var caps = new List<string>();
            var type = pass.GetType();
            if (IsPassMethodOverridden(type, "GetPropertiesEntries")) caps.Add("Properties");
            if (IsPassMethodOverridden(type, "GetCBufferEntries")) caps.Add("CBUFFER");
            if (IsPassMethodOverridden(type, "GetStructOverrides")) caps.Add("StructOverrides");
            if (IsPassMethodOverridden(type, "GetAdditionalReplacements")) caps.Add("AdditionalReplacements");
            
            if (caps.Count > 0)
                DrawField("Overrides", string.Join(", ", caps));
            
            EditorGUILayout.EndVertical();
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
        
        static bool IsPassMethodOverridden(System.Type type, string methodName)
        {
            var method = type.GetMethod(methodName, 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            return method != null && method.DeclaringType != typeof(ShaderPassInjector);
        }
    }
}
