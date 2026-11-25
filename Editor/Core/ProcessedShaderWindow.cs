using System.IO;
using UnityEditor;
using UnityEngine;

namespace FS.Shaders.Editor
{
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
