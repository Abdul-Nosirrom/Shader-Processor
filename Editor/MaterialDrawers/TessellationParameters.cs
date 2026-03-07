using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Material property drawer that renders all tessellation settings from a single property.
    /// 
    /// Usage in shader:
    ///     [Tessellation] _Tessellation("Tessellation", Float) = 0
    /// 
    /// This drawer will:
    /// - Show enable toggle
    /// - Show mode dropdown (Uniform, Edge Length, Distance, Edge+Distance)
    /// - Show relevant parameters based on mode
    /// - Show optional features (Phong, Frustum Cull, Backface Cull)
    /// - Set appropriate keywords on the material
    /// 
    /// Required shader properties (auto-created if missing):
    /// - _TessellationFactor (float, 1-64)
    /// - _TessellationMinDist (float)
    /// - _TessellationMaxDist (float)
    /// - _TessellationEdgeLength (float)
    /// - _PhongStrength (float, 0-1)
    /// </summary>
    public class TessellationDrawer : MaterialPropertyDrawer
    {
        // Layout constants
        private const float k_headerHeight = 24f;
        private const float k_propertyHeight = 20f;
        private const float k_spacing = 2f;
        private const float k_indent = 15f;
        
        // Tessellation modes
        private enum TessellationMode
        {
            Uniform = 0,
            EdgeLength = 1,
            Distance = 2,
            EdgeDistance = 3
        }
        
        // Keywords (these match what Tessellation.hlsl expects)
        private const string KEYWORD_TESS_ENABLED = "_TESSELLATION";
        private const string KEYWORD_TESS_MODE_UNIFORM = "TESS_MODE_UNIFORM";
        private const string KEYWORD_TESS_MODE_EDGE_LENGTH = "TESS_MODE_EDGE_LENGTH";
        private const string KEYWORD_TESS_MODE_DISTANCE = "TESS_MODE_DISTANCE";
        private const string KEYWORD_TESS_MODE_EDGE_DISTANCE = "TESS_MODE_EDGE_DISTANCE";
        private const string KEYWORD_TESS_PHONG = "TESS_PHONG";
        private const string KEYWORD_TESS_FRUSTUM_CULL = "TESS_FRUSTUM_CULL";
        private const string KEYWORD_TESS_BACKFACE_CULL = "TESS_BACKFACE_CULL";
        
        // Property names
        private const string PROP_TESS_FACTOR = "_TessellationFactor";
        private const string PROP_TESS_MIN_DIST = "_TessellationMinDist";
        private const string PROP_TESS_MAX_DIST = "_TessellationMaxDist";
        private const string PROP_TESS_EDGE_LENGTH = "_TessellationEdgeLength";
        private const string PROP_PHONG_STRENGTH = "_PhongStrength";
        
        // Tooltips
        private static readonly GUIContent s_labelEnabled = new GUIContent("Enable Tessellation", "Enable tessellation for this material");
        private static readonly GUIContent s_labelMode = new GUIContent("Mode", "Tessellation subdivision mode");
        private static readonly GUIContent s_labelFactor = new GUIContent("Factor", "Base/max tessellation factor (1-64)");
        private static readonly GUIContent s_labelMinDist = new GUIContent("Min Distance", "Distance where tessellation is maximum");
        private static readonly GUIContent s_labelMaxDist = new GUIContent("Max Distance", "Distance where tessellation is minimum");
        private static readonly GUIContent s_labelEdgeLength = new GUIContent("Edge Length", "Target edge length in pixels");
        private static readonly GUIContent s_labelPhong = new GUIContent("Phong Smoothing", "Smooth silhouettes using Phong tessellation");
        private static readonly GUIContent s_labelPhongStrength = new GUIContent("Phong Strength", "Phong smoothing intensity (0-1)");
        private static readonly GUIContent s_labelFrustumCull = new GUIContent("Frustum Culling", "Cull patches outside view frustum");
        private static readonly GUIContent s_labelBackfaceCull = new GUIContent("Backface Culling", "Cull back-facing patches (for closed meshes)");
        
        private static readonly GUIContent[] s_modeNames =
        {
            new GUIContent("Uniform", "Constant Tessellation Factor Everywhere"),
            new GUIContent("Edge Length", "Screen-Space Adaptive Edge Length"),
            new GUIContent("Distance", "Camera Distance Based"),
            new GUIContent("Edge + Distance", "Hybrid: Edge Length + Distance Falloff")
        };
        
        public override void OnGUI(Rect position, MaterialProperty prop, string label, MaterialEditor editor)
        {
            // Get material(s)
            Material mat = editor.target as Material;
            if (mat == null) return;
            
            // Read current state from keywords
            bool isEnabled = mat.IsKeywordEnabled(KEYWORD_TESS_ENABLED);
            TessellationMode mode = GetCurrentMode(mat);
            bool phongEnabled = mat.IsKeywordEnabled(KEYWORD_TESS_PHONG);
            bool frustumCull = mat.IsKeywordEnabled(KEYWORD_TESS_FRUSTUM_CULL);
            bool backfaceCull = mat.IsKeywordEnabled(KEYWORD_TESS_BACKFACE_CULL);
            
            // Begin property for undo support
            EditorGUI.BeginChangeCheck();
            
            // Use GUILayout inside the allocated rect
            //position.y += 200;
            //GUILayout.BeginArea(new Rect(k_indent * 2, position.height + k_propertyHeight * 2f, position.width, position.height));
            
            // Header
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Tessellation", EditorStyles.boldLabel);
            
            // Enable toggle
            isEnabled = EditorGUILayout.Toggle(s_labelEnabled, isEnabled);
            
            if (isEnabled)
            {
                EditorGUI.indentLevel++;
                
                // Mode dropdown
                EditorGUIUtility.labelWidth /= 2f;
                mode = (TessellationMode)EditorGUILayout.Popup(s_labelMode, (int)mode, s_modeNames);
                EditorGUIUtility.labelWidth *= 2f;
                
                // Factor (always shown)
                DrawLayoutSlider(editor, mat, PROP_TESS_FACTOR, s_labelFactor, 1f, 64f);
                
                // Mode-specific parameters
                if (mode == TessellationMode.Distance || mode == TessellationMode.EdgeDistance)
                {
                    EditorGUIUtility.labelWidth /= 2f;
                    if (!mat.HasProperty(PROP_TESS_MIN_DIST) || !mat.HasProperty(PROP_TESS_MAX_DIST))
                    {
                        EditorGUILayout.LabelField("Min/Max Tess Distance", $"Missing: {PROP_TESS_MIN_DIST} or {PROP_TESS_MAX_DIST}");
                    }
                    else
                    {
                        EditorGUI.BeginChangeCheck();
                        var minDist = mat.GetFloat(PROP_TESS_MIN_DIST);
                        var maxDist = mat.GetFloat(PROP_TESS_MAX_DIST);
                        var res = SirenixEditorFields.MinMaxSlider(
                            new GUIContent("Min/Max Tess Distance", "Blend distances of tessellation factor"),
                            new Vector2(minDist, maxDist), new Vector2(0, 128), true);
                        if (EditorGUI.EndChangeCheck())
                        {
                            mat.SetFloat(PROP_TESS_MIN_DIST, res.x);
                            mat.SetFloat(PROP_TESS_MAX_DIST, res.y);
                        }
                    }
                    EditorGUIUtility.labelWidth *= 2f;
                }
                
                if (mode == TessellationMode.EdgeLength || mode == TessellationMode.EdgeDistance)
                {
                    DrawLayoutSlider(editor, mat, PROP_TESS_EDGE_LENGTH, s_labelEdgeLength, 4f, 100f);
                }
                
                EditorGUILayout.Space(4);
                
                // Optional features
                phongEnabled = EditorGUILayout.Toggle(s_labelPhong, phongEnabled);
                
                if (phongEnabled)
                {
                    EditorGUI.indentLevel++;
                    EditorGUIUtility.labelWidth /= 2f;
                    DrawLayoutSlider(editor, mat, PROP_PHONG_STRENGTH, s_labelPhongStrength, 0f, 1f);
                    EditorGUIUtility.labelWidth *= 2f;
                    EditorGUI.indentLevel--;
                }
                
                frustumCull = EditorGUILayout.Toggle(s_labelFrustumCull, frustumCull);
                backfaceCull = EditorGUILayout.Toggle(s_labelBackfaceCull, backfaceCull);
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndVertical();
            //GUILayout.EndArea();
            
            // Apply changes
            if (EditorGUI.EndChangeCheck())
            {
                foreach (var target in prop.targets)
                {
                    Material m = target as Material;
                    if (m == null) continue;
                    
                    // Set enable keyword
                    SetKeyword(m, KEYWORD_TESS_ENABLED, isEnabled);
                    
                    if (isEnabled)
                    {
                        // Set mode keywords (mutually exclusive)
                        SetKeyword(m, KEYWORD_TESS_MODE_UNIFORM, mode == TessellationMode.Uniform);
                        SetKeyword(m, KEYWORD_TESS_MODE_EDGE_LENGTH, mode == TessellationMode.EdgeLength);
                        SetKeyword(m, KEYWORD_TESS_MODE_DISTANCE, mode == TessellationMode.Distance);
                        SetKeyword(m, KEYWORD_TESS_MODE_EDGE_DISTANCE, mode == TessellationMode.EdgeDistance);
                        
                        // Set optional feature keywords
                        SetKeyword(m, KEYWORD_TESS_PHONG, phongEnabled);
                        SetKeyword(m, KEYWORD_TESS_FRUSTUM_CULL, frustumCull);
                        SetKeyword(m, KEYWORD_TESS_BACKFACE_CULL, backfaceCull);
                    }
                    else
                    {
                        // Disable all tessellation keywords
                        SetKeyword(m, KEYWORD_TESS_MODE_UNIFORM, false);
                        SetKeyword(m, KEYWORD_TESS_MODE_EDGE_LENGTH, false);
                        SetKeyword(m, KEYWORD_TESS_MODE_DISTANCE, false);
                        SetKeyword(m, KEYWORD_TESS_MODE_EDGE_DISTANCE, false);
                        SetKeyword(m, KEYWORD_TESS_PHONG, false);
                        SetKeyword(m, KEYWORD_TESS_FRUSTUM_CULL, false);
                        SetKeyword(m, KEYWORD_TESS_BACKFACE_CULL, false);
                    }
                    
                    EditorUtility.SetDirty(m);
                }
            }
        }
        
        private TessellationMode GetCurrentMode(Material mat)
        {
            if (mat.IsKeywordEnabled(KEYWORD_TESS_MODE_UNIFORM)) return TessellationMode.Uniform;
            if (mat.IsKeywordEnabled(KEYWORD_TESS_MODE_EDGE_LENGTH)) return TessellationMode.EdgeLength;
            if (mat.IsKeywordEnabled(KEYWORD_TESS_MODE_DISTANCE)) return TessellationMode.Distance;
            if (mat.IsKeywordEnabled(KEYWORD_TESS_MODE_EDGE_DISTANCE)) return TessellationMode.EdgeDistance;
            return TessellationMode.Distance; // Default
        }
        
        private void SetKeyword(Material mat, string keyword, bool enabled)
        {
            if (enabled)
                mat.EnableKeyword(keyword);
            else
                mat.DisableKeyword(keyword);
        }
        
        private void SetMaterialFloat(MaterialEditor editor, string propName, float value)
        {
            foreach (var target in editor.targets)
            {
                Material m = target as Material;
                if (m != null && m.HasProperty(propName))
                {
                    m.SetFloat(propName, value);
                }
            }
        }
        
        private void DrawLayoutSlider(MaterialEditor editor, Material mat, string propName, GUIContent label, float min, float max)
        {
            EditorGUIUtility.labelWidth /= 2f;
            if (!mat.HasProperty(propName))
            {
                EditorGUILayout.LabelField(label.text, $"Missing: {propName}");
                EditorGUIUtility.labelWidth *= 2f;
                return;
            }
            
            EditorGUI.BeginChangeCheck();
            float value = mat.GetFloat(propName);
            
            value = EditorGUILayout.Slider(label, value, min, max);
            
            if (EditorGUI.EndChangeCheck())
            {
                SetMaterialFloat(editor, propName, value);
            }
            EditorGUIUtility.labelWidth *= 2f;
        }
        
        private void DrawLayoutFloat(MaterialEditor editor, Material mat, string propName, GUIContent label)
        {
            if (!mat.HasProperty(propName))
            {
                EditorGUILayout.LabelField(label.text, $"Missing: {propName}");
                return;
            }
            
            EditorGUI.BeginChangeCheck();
            float value = mat.GetFloat(propName);
            value = EditorGUILayout.FloatField(label, value);
            
            if (EditorGUI.EndChangeCheck())
            {
                SetMaterialFloat(editor, propName, value);
            }
        }
    }
}