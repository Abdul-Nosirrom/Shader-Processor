// =============================================================================
// TEST 25: Include used by Test25_Sub/Test25_InheritChild_RelInclude.shader
//
// The child includes this as "../Test25_ChildInclude.hlsl", relative to its own
// folder. If that path were resolved against the parent's folder instead, the
// include would not be found and the child would fail to compile.
// =============================================================================
#ifndef TEST25_CHILD_INCLUDE
#define TEST25_CHILD_INCLUDE

half3 Test25Tint(half3 color)
{
    return color * 0.5;
}

#endif
