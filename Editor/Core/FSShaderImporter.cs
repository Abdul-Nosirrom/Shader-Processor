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
        [NonSerialized]
        public string LastProcessedSource;
        
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string source = File.ReadAllText(ctx.assetPath);
            
            // Process the shader
            var processor = new ShaderProcessor();
            string processed = processor.Process(source, ctx.assetPath);
            
            LastProcessedSource = processed;
            
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
            return Regex.IsMatch(source,
                @"[""']ShaderGen[""']\s*=\s*[""']True[""']",
                RegexOptions.IgnoreCase);
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
        ShaderContext m_ParsedContext;
        
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
                m_ParsedContext = new ShaderContext
                {
                    OriginalSource = source,
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
                
                if (m_ParsedContext.Hooks.HelperFunctions.Count > 0)
                {
                    EditorGUILayout.LabelField($"Helper Functions: {m_ParsedContext.Hooks.HelperFunctions.Count}");
                }
                
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
            if (m_ParsedContext.ForwardPass != null)
            {
                string passName = m_ParsedContext.ForwardPass.Name ?? m_ParsedContext.ForwardPass.LightMode ?? "Unnamed";
                
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
                    if (m_ParsedContext.ForwardPass.LightMode == "UniversalForward")
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
                EditorGUILayout.LabelField("  Vertex", m_ParsedContext.ForwardVertexFunctionName ?? "Not found");
                EditorGUILayout.LabelField("  Fragment", m_ParsedContext.ForwardFragmentFunctionName ?? "Not found");
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No source pass found. Add \"ShaderGen\" = \"True\" to a pass or SubShader tags.",
                    MessageType.Error);
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
        
        public static void Show(string source, string shaderPath)
        {
            var window = GetWindow<ProcessedShaderWindow>();
            window.titleContent = new GUIContent("Processed: " + Path.GetFileName(shaderPath));
            window.m_Source = source;
            window.m_ShaderPath = shaderPath;
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
            EditorGUILayout.LabelField($"Lines: {m_Source.Split('\n').Length}", GUILayout.Width(100));
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
