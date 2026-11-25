#pragma once

// =============================================================================
// META_KEYWORDS.HLSL
// Keywords for the Meta pass (lightmap baking).
// Use: #include_with_pragmas "Packages/com.abdulal.shaderprocessor/Shaders/Keywords/MetaKeywords.hlsl"
// The ShaderGen Meta template includes this, so generated passes get it already.
// =============================================================================

#pragma target 4.5

// -----------------------------------------------------------------------------
// Editor Visualization
// -----------------------------------------------------------------------------
// Enables lightmap UV visualization and other editor debug modes
#pragma shader_feature EDITOR_VISUALIZATION

// Note: Meta pass typically doesn't need instancing or LOD keywords
// as it only runs during lightmap baking in the editor
