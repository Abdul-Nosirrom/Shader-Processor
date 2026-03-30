using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Custom importer for FreeSkies shaders.
    /// Activated when shader contains "ShaderGen" = "True" tag.
    /// </summary>
    [ScriptedImporter(1, new string[0], new[] { "shader" }, AllowCaching = true)]
    public class FSShaderImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string source = File.ReadAllText(ctx.assetPath);
            
            // Clear template cache so modified templates are picked up on reimport
            TemplateEngine.ClearCache();
            
            // Resolve inheritance - if this shader declares "Inherit" = "Parent",
            // the parent source is loaded, name is replaced, and tags are merged.
            // The rest of the pipeline sees a complete shader source.
            var inheritResult = ShaderInheritance.Resolve(source, ctx.assetPath);
            source = inheritResult.Source;
            
            // Register dependency on parent so this shader reimports when the parent changes
            if (inheritResult.ParentPath != null)
            {
                ctx.DependsOnSourceAsset(inheritResult.ParentPath);
            }
            
            // Process the shader
            var processor = new ShaderProcessor();
            string processed = processor.Process(source, ctx.assetPath);
            
            // Create shader asset
            Shader shader = ShaderUtil.CreateShaderAsset(ctx, processed, true);
         
            if (shader == null)
            {
                ctx.LogImportError("Failed to create shader from processed source");
                return;
            }
            
            if (ShaderUtil.ShaderHasError(shader))
            {
                ctx.LogImportWarning("Shader has compilation errors - check console for details");
            }
            
            ctx.AddObjectToAsset("MainShader", shader);
            ctx.SetMainObject(shader);
        }
    }
    
    /// <summary>
    /// Detects FreeSkies shaders and sets the importer override.
    /// </summary>
    public class ShaderPreprocessor : AssetPostprocessor
    {
        void OnPreprocessAsset()
        {
            if (!assetPath.EndsWith(".shader"))
                return;
            
            try
            {
                string source = File.ReadAllText(assetPath);
                
                if (IsShaderGenNeeded(source))
                {
                    AssetDatabase.SetImporterOverride<FSShaderImporter>(assetPath);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ShaderProcessor] Error checking shader: {e.Message}");
            }
        }
        
        static bool IsShaderGenNeeded(string source)
        {
            // Check for ShaderGen tag in either Pass or SubShader scope
            if (Regex.IsMatch(source,
                @"[""']ShaderGen[""']\s*=\s*[""']True[""']",
                RegexOptions.IgnoreCase))
                return true;
            
            // Check for Inherit tag (child shader inheriting from a parent)
            if (ShaderInheritance.UsesInheritance(source))
                return true;
            
            return false;
        }
    }
    
    /// <summary>
    /// Custom editor for the FreeSkies shader importer.
    /// </summary>
    [CustomEditor(typeof(FSShaderImporter))]
    public class FSShaderImporterEditor : ScriptedImporterEditor
    {
        bool m_ShowShaderGen = true;
        bool m_ShowTags = true;
        bool m_ShowStructs = true;
        bool m_ShowHooks = true;
        bool m_ShowPasses = true;
        bool m_ShowTagProcessors = true;
        ShaderContext m_ParsedContext;
        ShaderInheritance.ResolveResult m_InheritResult;
        
        public override void OnEnable()
        {
            base.OnEnable();
            ParseShaderForDisplay();
        }
        
        void ParseShaderForDisplay()
        {
            var importer = target as FSShaderImporter;
            if (importer == null) return;
            
            try
            {
                string source = File.ReadAllText(importer.assetPath);
                
                // Resolve inheritance so the inspector shows the merged state
                m_InheritResult = ShaderInheritance.Resolve(source, importer.assetPath);
                source = m_InheritResult.Source;
                
                m_ParsedContext = new ShaderContext
                {
                    ProcessedSource = source,
                    ShaderPath = importer.assetPath,
                    ShaderDirectory = Path.GetDirectoryName(importer.assetPath)
                };
                ShaderParser.Parse(m_ParsedContext);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ShaderGen] Failed to parse shader: {e.Message}");
            }
        }
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            EditorGUILayout.LabelField("ShaderGen Processor", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            
            // Inheritance info (shown when using Inherit tag)
            bool bInherited = m_InheritResult.ParentName != null;
            if (bInherited)
            {
                EditorGUILayout.HelpBox(
                    $"Inherits from \"{m_InheritResult.ParentName}\"",
                    MessageType.Info);
                EditorGUILayout.LabelField("Parent Path", m_InheritResult.ParentPath ?? "Not found");
                EditorGUILayout.Space();
            }
            
            // ShaderGen Info section
            m_ShowShaderGen = EditorGUILayout.Foldout(m_ShowShaderGen, "ShaderGen Status", true);
            if (m_ShowShaderGen && m_ParsedContext != null)
            {
                EditorGUI.indentLevel++;
                DrawShaderGenStatus();
                EditorGUI.indentLevel--;
            }
            
            // Tags section
            m_ShowTags = EditorGUILayout.Foldout(m_ShowTags, "SubShader Tags", true);
            if (m_ShowTags && m_ParsedContext != null)
            {
                EditorGUI.indentLevel++;
                
                if (m_ParsedContext.SubShaderTags.Count == 0)
                {
                    EditorGUILayout.HelpBox("No tags detected.", MessageType.Info);
                }
                else
                {
                    foreach (var tag in m_ParsedContext.SubShaderTags)
                    {
                        EditorGUILayout.LabelField(tag.Key, tag.Value);
                    }
                }
                
                EditorGUI.indentLevel--;
            }
            
            // Structs section
            m_ShowStructs = EditorGUILayout.Foldout(m_ShowStructs, "Parsed Structures", true);
            if (m_ShowStructs && m_ParsedContext != null)
            {
                EditorGUI.indentLevel++;
                
                DrawStructInfo("Attributes", m_ParsedContext.Attributes, m_ParsedContext.AttributesStructName);
                DrawStructInfo("Interpolators", m_ParsedContext.Interpolators, m_ParsedContext.InterpolatorsStructName);
                
                // Location info
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Locations:", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("  CBUFFER", m_ParsedContext.CBufferInHlslInclude ? "HLSLINCLUDE" : "Forward Pass");
                EditorGUILayout.LabelField("  Textures", m_ParsedContext.TexturesInHlslInclude ? "HLSLINCLUDE" : "Forward Pass");
                EditorGUILayout.LabelField("  Structs", m_ParsedContext.StructsInHlslInclude ? "HLSLINCLUDE" : "Forward Pass");
                
                EditorGUI.indentLevel--;
            }
            
            // Hooks section
            m_ShowHooks = EditorGUILayout.Foldout(m_ShowHooks, "Detected Hooks", true);
            if (m_ShowHooks && m_ParsedContext != null)
            {
                EditorGUI.indentLevel++;
                
                foreach (var hook in ShaderHookRegistry.All)
                {
                    DrawHookInfo(hook.PragmaName, m_ParsedContext.Hooks.GetFunctionName(hook.PragmaName));
                }
                
                EditorGUI.indentLevel--;
            }
            
            // Active passes section
            m_ShowPasses = EditorGUILayout.Foldout(m_ShowPasses, "Active Passes", true);
            if (m_ShowPasses && m_ParsedContext != null)
            {
                EditorGUI.indentLevel++;
                
                string source = m_ParsedContext.ProcessedSource;
                bool hasBasePasses = source.Contains("[InjectBasePasses]");
                bool anyActive = false;
                
                foreach (var injector in ShaderPassInjectorRegistry.All)
                {
                    bool active = (hasBasePasses && injector.IsBasePass)
                               || source.Contains($"[InjectPass:{injector.PassName}]");
                    
                    if (active)
                    {
                        string label = injector.IsBasePass ? $"{injector.PassName} (base)" : injector.PassName;
                        EditorGUILayout.LabelField(label, $"Template: {injector.TemplateName}.hlsl");
                        anyActive = true;
                    }
                }
                
                if (!anyActive)
                    EditorGUILayout.LabelField("None", EditorStyles.miniLabel);
                
                EditorGUI.indentLevel--;
            }
            
            // Active tag processors section
            m_ShowTagProcessors = EditorGUILayout.Foldout(m_ShowTagProcessors, "Active Tag Processors", true);
            if (m_ShowTagProcessors && m_ParsedContext != null)
            {
                EditorGUI.indentLevel++;
                
                bool anyActive = false;
                foreach (var proc in ShaderTagProcessorRegistry.GetAllProcessors())
                {
                    bool enabled = false;
                    
                    // Check SubShader tags
                    if (m_ParsedContext.SubShaderTags.TryGetValue(proc.TagName, out string val))
                        enabled = ShaderTagUtility.IsTagEnabled(val);
                    
                    // Check pass-level tags
                    if (!enabled)
                    {
                        foreach (var pass in m_ParsedContext.Passes)
                        {
                            if (pass.IsTagEnabled(proc.TagName))
                            {
                                enabled = true;
                                break;
                            }
                        }
                    }
                    
                    if (enabled)
                    {
                        EditorGUILayout.LabelField(proc.TagName, $"Priority: {proc.Priority}");
                        anyActive = true;
                    }
                }
                
                if (!anyActive)
                    EditorGUILayout.LabelField("None", EditorStyles.miniLabel);
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.Space();
            
            // Debug buttons
            EditorGUILayout.LabelField("Debug", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Show Processed Shader"))
            {
                ShowProcessedShader();
            }
            
            if (GUILayout.Button("Refresh"))
            {
                ParseShaderForDisplay();
                Repaint();
            }
            
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space();
            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }
        
        void DrawShaderGenStatus()
        {
            // Source pass info
            if (m_ParsedContext.ReferencePass != null)
            {
                string passName = m_ParsedContext.ReferencePass.Name ?? m_ParsedContext.ReferencePass.LightMode ?? "Unnamed";
                
                // Determine how the pass was selected
                string selectionReason;
                MessageType messageType = MessageType.Info;
                
                if (m_ParsedContext.ShaderGenInPass)
                {
                    selectionReason = "Pass has \"ShaderGen\" = \"True\" tag";
                    messageType = MessageType.None;
                }
                else if (m_ParsedContext.ShaderGenInSubShader)
                {
                    if (m_ParsedContext.ReferencePass.LightMode == "UniversalForward")
                    {
                        selectionReason = "Fallback: UniversalForward LightMode";
                    }
                    else
                    {
                        selectionReason = "Fallback: First pass in SubShader";
                        messageType = MessageType.Warning;
                    }
                }
                else
                {
                    selectionReason = "Unknown";
                    messageType = MessageType.Warning;
                }
                
                EditorGUILayout.LabelField("Source Pass", passName);
                EditorGUILayout.LabelField("Selection", selectionReason);
                
                if (messageType == MessageType.Warning)
                {
                    EditorGUILayout.HelpBox(
                        "Consider adding \"ShaderGen\" = \"True\" to your source pass tags for explicit control.",
                        MessageType.Info);
                }
                
                // Show vertex/fragment functions
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Entry Points:", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("  Vertex", m_ParsedContext.ReferenceVertexFunctionName ?? "Not found");
                EditorGUILayout.LabelField("  Fragment", m_ParsedContext.ReferenceFragmentFunctionName ?? "Not found");
            }
            else
            {
                if (m_InheritResult.ParentName != null)
                {
                    EditorGUILayout.LabelField("Source Pass", $"Inherited from \"{m_InheritResult.ParentName}\"");
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "No source pass found. Add \"ShaderGen\" = \"True\" to a pass or SubShader tags.",
                        MessageType.Error);
                }
            }
            
            // Features enabled
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Enabled Features:", EditorStyles.miniLabel);
            
            if (m_ParsedContext.EnabledFeatures.Count == 0)
            {
                EditorGUILayout.LabelField("  None", EditorStyles.miniLabel);
            }
            else
            {
                foreach (var feature in m_ParsedContext.EnabledFeatures)
                {
                    EditorGUILayout.LabelField($"  • {feature}");
                }
            }
        }
        
        void DrawStructInfo(string label, StructDefinition structDef, string actualName)
        {
            if (structDef != null)
            {
                string displayName = actualName != label ? $"{actualName} ({label})" : label;
                EditorGUILayout.LabelField(displayName, $"{structDef.Fields.Count} fields");
            }
            else
            {
                EditorGUILayout.LabelField(label, "Not found", EditorStyles.miniLabel);
            }
        }
        
        void DrawHookInfo(string pragma, string funcName)
        {
            string status = string.IsNullOrEmpty(funcName) ? "Not defined" : funcName;
            EditorGUILayout.LabelField(pragma, status);
        }
        
        
        void ShowProcessedShader()
        {
            var importer = target as FSShaderImporter;
            if (importer == null) return;
            
            try
            {
                string source = File.ReadAllText(importer.assetPath);
                
                // Resolve inheritance so the preview shows the merged result
                var inheritResult = ShaderInheritance.Resolve(source, importer.assetPath);
                source = inheritResult.Source;
                
                var processor = new ShaderProcessor();
                string processed = processor.Process(source, importer.assetPath);
                ProcessedShaderWindow.Show(processed, importer.assetPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ShaderGen] Failed to process shader: {e.Message}\n{e.StackTrace}");
            }
        }
    }
    
    /// <summary>
    /// Window for viewing the processed shader source.
    /// </summary>
    public class ProcessedShaderWindow : EditorWindow
    {
        string m_Source;
        string m_ShaderPath;
        Vector2 m_Scroll;
        GUIStyle m_TextStyle;
        bool m_WordWrap;
        string m_SearchText = "";
        int m_LineCount;
        
        public static void Show(string source, string shaderPath)
        {
            var window = GetWindow<ProcessedShaderWindow>();
            window.titleContent = new GUIContent("Processed: " + Path.GetFileName(shaderPath));
            window.m_Source = source;
            window.m_ShaderPath = shaderPath;
            window.m_LineCount = source.Split('\n').Length;
            window.minSize = new Vector2(700, 500);
            window.Show();
        }
        
        void OnGUI()
        {
            InitStyles();
            
            // Toolbar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            
            if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                EditorGUIUtility.systemCopyBuffer = m_Source;
                ShowNotification(new GUIContent("Copied to clipboard!"));
            }
            
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                SaveToFile();
            }
            
            GUILayout.FlexibleSpace();
            
            m_SearchText = EditorGUILayout.TextField(m_SearchText, EditorStyles.toolbarSearchField, GUILayout.Width(200));
            
            m_WordWrap = GUILayout.Toggle(m_WordWrap, "Wrap", EditorStyles.toolbarButton, GUILayout.Width(50));
            
            EditorGUILayout.EndHorizontal();
            
            // Stats bar
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Lines: {m_LineCount}", GUILayout.Width(100));
            EditorGUILayout.LabelField($"Chars: {m_Source.Length:N0}", GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();
            
            // Content
            m_TextStyle.wordWrap = m_WordWrap;
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            EditorGUILayout.TextArea(m_Source, m_TextStyle, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            EditorGUILayout.EndScrollView();
        }
        
        void InitStyles()
        {
            if (m_TextStyle == null)
            {
                m_TextStyle = new GUIStyle(EditorStyles.textArea)
                {
                    fontSize = 12,
                    wordWrap = false,
                    richText = false
                };
            }
        }
        
        void SaveToFile()
        {
            string defaultName = Path.GetFileNameWithoutExtension(m_ShaderPath) + "_processed.shader";
            string path = EditorUtility.SaveFilePanel("Save Processed Shader", "", defaultName, "shader");
            
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, m_Source);
                Debug.Log($"[ShaderProcessor] Saved processed shader to: {path}");
            }
        }
    }
}
