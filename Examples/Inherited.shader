// =============================================================================
// FreeSkies Example: Inherited Shader
// 
// Demonstrates shader inheritance. This shader inherits everything from
// the Basic shader, adds Tessellation, overrides the Forward pass LightMode,
// and excludes the Meta pass.
//
// Features shown:
//   - SubShader tag merge (Tessellation)
//   - Pass exclusion (Meta)
//   - Pass override (Forward LightMode)
//   - Property override (_AlphaCutoff default)
//
// For InheritHook examples, see the test shaders (Test19).
// =============================================================================

Shader "FreeSkies/Examples/BasicTessellated"
{
    Properties
    {
        // Override parent's default cutoff
        _AlphaCutoff("Alpha Cutoff", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "Inherit" = "FreeSkies/Examples/Basic"
            "Tessellation" = "On"
            "ExcludePasses" = "Meta"
        }

        // Override Forward pass LightMode without replacing the pass code
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
        }
    }
}
