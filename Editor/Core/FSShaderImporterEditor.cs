using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.Rendering;
using UnityEngine;

namespace FS.Shaders.Editor
{
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

            DrawCompilerMessages();

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
                ResetCompilerMessages();
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
        
        
        //=============================================================================
        // Compiler Messages
        //
        // Unity's Shader inspector lists compiler messages with processed-shader line
        // numbers. This section draws the same list (Unity's own drawer, called by
        // reflection) with each message pointed back at where it came from: your file
        // and line, or the generated pass it sits in. Generated rows point at a copy of
        // the processed shader in Temp/, so double-clicking still lands on the line.
        //=============================================================================

        // Mirrors UnityEditor.ShaderInspector.ShaderErrorListUI(Object, ShaderMessage[],
        // ref Vector2), the internal drawer behind the Shader inspector's error list.
        // If Unity renames it, find the new drawer in ShaderInspector and update this.
        static readonly MethodInfo s_errorListMethod = typeof(UnityEditor.Editor).Assembly
            .GetType("UnityEditor.ShaderInspector")
            ?.GetMethod("ShaderErrorListUI", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(UnityEngine.Object), typeof(ShaderMessage[]), typeof(Vector2).MakeByRefType() },
                null);

        // ShaderMessage only has getters; these are its compiler-generated backing
        // fields, set on a boxed copy to re-point a message at another file and line
        static readonly FieldInfo s_messageTextField = GetMessageField("message");
        static readonly FieldInfo s_messageFileField = GetMessageField("file");
        static readonly FieldInfo s_messageLineField = GetMessageField("line");

        static bool s_hasWarnedAboutReflection;

        bool m_ShowMessages = true;
        Shader m_ImportedShader;
        ShaderMessage[] m_RawMessages;
        ShaderMessage[] m_MappedMessages;
        ShaderSourceMap m_SourceMap;
        string m_ProcessedCopyPath;
        string m_MessageSummary;
        Vector2 m_MessageScroll;

        void DrawCompilerMessages()
        {
            var importer = target as FSShaderImporter;
            if (importer == null) return;

            if (m_ImportedShader == null)
                m_ImportedShader = AssetDatabase.LoadAssetAtPath<Shader>(importer.assetPath);
            if (m_ImportedShader == null) return;

            // Read on Layout only, like Unity's inspector, so Layout and Repaint see
            // the same rows
            if (Event.current.type == EventType.Layout)
                UpdateCompilerMessages(importer.assetPath);

            m_ShowMessages = EditorGUILayout.Foldout(m_ShowMessages, "Compiler Messages", true);
            if (!m_ShowMessages || m_MappedMessages == null)
                return;

            if (m_MappedMessages.Length == 0)
            {
                EditorGUILayout.LabelField("No errors or warnings in the variants compiled so far.",
                    EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField(m_MessageSummary, EditorStyles.wordWrappedLabel);
                DrawMessageList();
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                "Only variants Unity has compiled are listed. Compile All Passes compiles the default variant of every pass.",
                EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Compile All Passes", GUILayout.Width(140)))
            {
                CompileAllPasses(m_ImportedShader);
                // Re-read on the next Layout
                m_RawMessages = null;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        void UpdateCompilerMessages(string assetPath)
        {
            ShaderMessage[] messages = ShaderUtil.GetShaderMessageCount(m_ImportedShader) > 0
                ? ShaderUtil.GetShaderMessages(m_ImportedShader)
                : new ShaderMessage[0];

            if (m_RawMessages != null && SameMessages(messages, m_RawMessages))
                return;

            // New messages usually mean a reimport or a recompile, so the source may have
            // changed too: map them against a fresh processed text
            m_RawMessages = messages;
            m_SourceMap = null;
            MapCompilerMessages(assetPath);
        }

        void MapCompilerMessages(string assetPath)
        {
            m_MappedMessages = new ShaderMessage[m_RawMessages.Length];
            if (m_RawMessages.Length == 0)
                return;

            // Building the map re-runs inheritance and processing, so it's only done
            // when there's something to map
            if (m_SourceMap == null)
                BuildSourceMap(assetPath);

            for (int i = 0; i < m_RawMessages.Length; i++)
            {
                ShaderMessage message = m_RawMessages[i];
                m_MappedMessages[i] = message;

                // Messages with no line, or from an included .hlsl file (which already
                // points at the right file and line), stay as they are
                if (message.line <= 0 || !ShaderSourceMap.IsInProcessedShader(message, assetPath))
                    continue;

                if (m_SourceMap == null || !m_SourceMap.TryLocate(message.line, out ShaderLineLocation location))
                    continue;

                if (location.Origin == ShaderLineOrigin.YourCode)
                {
                    m_MappedMessages[i] = Repoint(message, location.FilePath, location.Line, message.message);
                    continue;
                }

                string passLabel = location.Origin == ShaderLineOrigin.Generated
                    ? location.PassName
                    : (location.PassName != null ? location.PassName + ", added" : "added");
                m_MappedMessages[i] = Repoint(message, m_ProcessedCopyPath, message.line,
                    $"[{passLabel}] {message.message}");
            }

            m_MessageSummary = m_SourceMap != null
                ? ShaderSourceMap.Summarize(m_RawMessages, m_SourceMap, assetPath)
                : "Couldn't map these messages back to your code (see the console). Lines are processed-shader lines.";
        }

        /// <summary>
        /// Rebuild the processed text the same way the importer does, map it back to the
        /// source, and write the copy that generated-pass rows point at.
        /// </summary>
        void BuildSourceMap(string assetPath)
        {
            try
            {
                string source = File.ReadAllText(assetPath);
                var inheritResult = ShaderInheritance.Resolve(source, assetPath);
                string processed = new ShaderProcessor().Process(inheritResult.Source, assetPath);

                m_SourceMap = ShaderSourceMap.Build(processed, assetPath, inheritResult);

                string copyDirectory = Path.GetFullPath(Path.Combine("Temp", "ShaderGen"));
                Directory.CreateDirectory(copyDirectory);
                m_ProcessedCopyPath = Path.Combine(copyDirectory,
                    Path.GetFileNameWithoutExtension(assetPath) + " (processed).shader");
                File.WriteAllText(m_ProcessedCopyPath, processed);
            }
            catch (Exception e)
            {
                m_SourceMap = null;
                Debug.LogError($"[ShaderGen] Couldn't map compiler messages for {assetPath}: {e.Message}");
            }
        }

        void DrawMessageList()
        {
            bool canRepoint = s_messageTextField != null && s_messageFileField != null && s_messageLineField != null;
            if (s_errorListMethod != null && canRepoint)
            {
                var args = new object[] { m_ImportedShader, m_MappedMessages, m_MessageScroll };
                try
                {
                    s_errorListMethod.Invoke(null, args);
                }
                catch (TargetInvocationException e) when (e.InnerException != null)
                {
                    // Double-click ends the GUI pass with ExitGUIException, which IMGUI
                    // expects to see as itself. Reflection wraps it, so rethrow the
                    // original or Unity reports it as an error.
                    ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                }
                m_MessageScroll = (Vector2)args[2];
                return;
            }

            if (!s_hasWarnedAboutReflection)
            {
                s_hasWarnedAboutReflection = true;
                string missing = s_errorListMethod == null
                    ? "UnityEditor.ShaderInspector.ShaderErrorListUI"
                    : "ShaderMessage backing fields (<message>, <file>, <line>k__BackingField)";
                Debug.LogWarning($"[ShaderGen] Compiler message list falls back to a plain list: {missing} not found in this Unity version.");
            }

            foreach (ShaderMessage message in m_MappedMessages)
            {
                MessageType type = message.severity == ShaderCompilerMessageSeverity.Error
                    ? MessageType.Error
                    : MessageType.Warning;
                EditorGUILayout.HelpBox($"{Path.GetFileName(message.file)}:{message.line}  {message.message}", type);
            }
        }

        void ResetCompilerMessages()
        {
            m_RawMessages = null;
            m_MappedMessages = null;
            m_SourceMap = null;
        }

        /// <summary>
        /// Compile every pass of the shader now. Same approach as the package's compile
        /// tests: with async compilation on, Unity otherwise only compiles a variant
        /// when something draws with it, so its messages never show up.
        /// </summary>
        static void CompileAllPasses(Shader shader)
        {
            bool wasAsyncAllowed = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                for (int pass = 0; pass < shader.passCount; pass++)
                {
                    ShaderUtil.CompilePass(material, pass, forceSync: true);
                }
            }
            finally
            {
                DestroyImmediate(material);
                ShaderUtil.allowAsyncCompilation = wasAsyncAllowed;
            }
        }

        static ShaderMessage Repoint(ShaderMessage message, string file, int line, string text)
        {
            if (s_messageTextField == null || s_messageFileField == null || s_messageLineField == null)
                return message;

            // Set on a boxed copy: setting a field on the struct itself through
            // reflection would change a temporary copy
            object boxed = message;
            s_messageTextField.SetValue(boxed, text);
            s_messageFileField.SetValue(boxed, file);
            s_messageLineField.SetValue(boxed, line);
            return (ShaderMessage)boxed;
        }

        static FieldInfo GetMessageField(string propertyName)
        {
            return typeof(ShaderMessage).GetField($"<{propertyName}>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        static bool SameMessages(ShaderMessage[] a, ShaderMessage[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!a[i].Equals(b[i]))
                    return false;
            }
            return true;
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
}
