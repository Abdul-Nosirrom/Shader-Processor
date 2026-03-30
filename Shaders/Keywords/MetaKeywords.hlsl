#pragma once

// =============================================================================
// META_KEYWORDS.HLSL
// Keywords for the Meta pass (lightmap baking).
// Use: #include_with_pragmas "Library/Keywords/MetaKeywords.hlsl"
// =============================================================================

#pragma target 4.5

// -----------------------------------------------------------------------------
// Editor Visualization
// -----------------------------------------------------------------------------
// Enables lightmap UV visualization and other editor debug modes
#pragma shader_feature EDITOR_VISUALIZATION

// Note: Meta pass typically doesn't need instancing or LOD keywords
// as it only runs during lightmap baking in the editor
