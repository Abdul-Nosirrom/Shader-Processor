// =============================================================================
// TEST 19: Inheritance Child (Minimal)
// 
// Tests: Basic inheritance with just tag merge and pass injection.
// No property overrides, no hooks, no pass overrides. Just the simplest
// possible child to verify the core inheritance path works.
// =============================================================================
Shader "Tests/19_InheritChild_Minimal"
{
    SubShader
    {
        Tags
        {
            "Inherit" = "Tests/19_InheritParent"
            "Tessellation" = "On"
        }
    }
}
