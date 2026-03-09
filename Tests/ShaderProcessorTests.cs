using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace FS.Shaders.Editor.Tests
{
    /// <summary>
    /// EditMode tests for the ShaderGen processing pipeline.
    /// Shows up in Window > General > Test Runner under EditMode tab.
    /// 
    /// These tests call ShaderProcessor.Process() on test shader sources and assert
    /// properties of the output string. No GPU or rendering needed.
    /// </summary>
    public class ShaderProcessorTests
    {
        //=============================================================================
        // Helper
        //=============================================================================
        
        ShaderProcessor _processor;
        
        [SetUp]
        public void Setup()
        {
            _processor = new ShaderProcessor();
        }
        
        /// <summary>
        /// Load a test shader from the Tests folder and process it.
        /// </summary>
        string ProcessTestShader(string testFileName)
        {
            // Find the Tests directory relative to the package
            string testsDir = FindTestsDirectory();
            Assert.IsNotNull(testsDir, "Could not find Tests directory");
            
            string path = Path.Combine(testsDir, testFileName);
            Assert.IsTrue(File.Exists(path), $"Test shader not found: {path}");
            
            string source = File.ReadAllText(path);
            return _processor.Process(source, path);
        }
        
        static string FindTestsDirectory()
        {
            // Walk up from known script locations to find Tests/
            var guids = UnityEditor.AssetDatabase.FindAssets("t:Script ShaderProcessor");
            foreach (var guid in guids)
            {
                string scriptPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                // Go up from Editor/Core/ShaderProcessor.cs to package root
                string dir = Path.GetDirectoryName(scriptPath);
                for (int i = 0; i < 4; i++)
                {
                    string testsPath = Path.Combine(dir, "Tests");
                    if (Directory.Exists(testsPath))
                        return testsPath;
                    dir = Path.GetDirectoryName(dir);
                    if (string.IsNullOrEmpty(dir)) break;
                }
            }
            return null;
        }
        
        /// <summary>Count how many times a substring appears in source.</summary>
        static int CountOccurrences(string source, string substring)
        {
            int count = 0;
            int idx = 0;
            while ((idx = source.IndexOf(substring, idx)) >= 0)
            {
                count++;
                idx += substring.Length;
            }
            return count;
        }
        
        /// <summary>Count Pass blocks in shader source.</summary>
        static int CountPasses(string source)
        {
            return Regex.Matches(source, ShaderParser.PassPattern).Count;
        }
        
        //=============================================================================
        // Test 01: Structs in HLSLINCLUDE
        //=============================================================================
        
        [Test]
        public void Test01_GeneratedPassesExist()
        {
            string output = ProcessTestShader("Test01_StructsInHLSLINCLUDE.shader");
            
            // Forward + 5 base passes = 6 minimum (tess may add more structure but same pass count)
            Assert.IsTrue(output.Contains("Name \"ShadowCaster\""), "Missing ShadowCaster pass");
            Assert.IsTrue(output.Contains("Name \"DepthOnly\""), "Missing DepthOnly pass");
            Assert.IsTrue(output.Contains("Name \"DepthNormals\""), "Missing DepthNormals pass");
            Assert.IsTrue(output.Contains("Name \"MotionVectors\""), "Missing MotionVectors pass");
            Assert.IsTrue(output.Contains("Name \"Meta\""), "Missing Meta pass");
        }
        
        [Test]
        public void Test01_GeneratedPassesHaveCorrectStructNames()
        {
            string output = ProcessTestShader("Test01_StructsInHLSLINCLUDE.shader");
            
            Assert.IsTrue(output.Contains("struct ShadowCasterAttributes"), "Missing ShadowCasterAttributes");
            Assert.IsTrue(output.Contains("struct DepthOnlyAttributes"), "Missing DepthOnlyAttributes");
            Assert.IsTrue(output.Contains("struct DepthNormalsAttributes"), "Missing DepthNormalsAttributes");
            Assert.IsTrue(output.Contains("struct MotionVectorsAttributes"), "Missing MotionVectorsAttributes");
        }
        
        [Test]
        public void Test01_CBufferNotDuplicatedWhenInHLSLINCLUDE()
        {
            string output = ProcessTestShader("Test01_StructsInHLSLINCLUDE.shader");
            
            // CBUFFER is in HLSLINCLUDE, so generated passes should have empty CBUFFER markers.
            // The HLSLINCLUDE CBUFFER_START should appear once (the original).
            // Generated passes should NOT have their own CBUFFER_START(UnityPerMaterial).
            // Count total CBUFFER_STARTs: should be 1 (the HLSLINCLUDE one)
            // Note: tessellation will be in the authored pass CBUFFER since tess props get injected there
            int cbufferCount = CountOccurrences(output, "CBUFFER_START(UnityPerMaterial)");
            Assert.AreEqual(1, cbufferCount,
                $"Expected 1 CBUFFER_START (HLSLINCLUDE only), found {cbufferCount}");
        }
        
        [Test]
        public void Test01_TessFactorOverridePrefixed()
        {
            string output = ProcessTestShader("Test01_StructsInHLSLINCLUDE.shader");
            
            // Forward pass should use ForwardTessellationFactorOverride (or unprefixed in authored code)
            // Generated passes should use prefixed names
            Assert.IsTrue(output.Contains("DepthOnlyTessellationFactorOverride"),
                "Missing prefixed tess factor override in DepthOnly");
            Assert.IsTrue(output.Contains("ShadowCasterTessellationFactorOverride"),
                "Missing prefixed tess factor override in ShadowCaster");
        }
        
        //=============================================================================
        // Test 04: Comments Around Pass Keyword
        //=============================================================================
        
        [Test]
        public void Test04_AllCommentedPassesDetected()
        {
            string output = ProcessTestShader("Test04_PassComments.shader");
            
            // Should detect all three authored passes despite comments
            Assert.IsTrue(output.Contains("Name \"CommentedPass1\""), "Missing CommentedPass1");
            Assert.IsTrue(output.Contains("Name \"CommentedPass2\""), "Missing CommentedPass2");
            Assert.IsTrue(output.Contains("Name \"CommentedPass3\""), "Missing CommentedPass3");
        }
        
        [Test]
        public void Test04_GeneratedPassesPresent()
        {
            string output = ProcessTestShader("Test04_PassComments.shader");
            
            Assert.IsTrue(output.Contains("Name \"ShadowCaster\""), "Missing ShadowCaster");
            Assert.IsTrue(output.Contains("Name \"DepthOnly\""), "Missing DepthOnly");
            Assert.IsTrue(output.Contains("Name \"DepthNormals\""), "Missing DepthNormals");
        }
        
        //=============================================================================
        // Test 07: Hooks with HLSLINCLUDE Structs
        //=============================================================================
        
        [Test]
        public void Test07_HookFunctionsPrefixedInGeneratedPasses()
        {
            string output = ProcessTestShader("Test07_HooksWithHLSLINCLUDE.shader");
            
            // vertexDisplacement hook
            Assert.IsTrue(output.Contains("DepthOnlyWaveDisplace"), "Missing prefixed WaveDisplace in DepthOnly");
            Assert.IsTrue(output.Contains("ShadowCasterWaveDisplace"), "Missing prefixed WaveDisplace in ShadowCaster");
            
            // interpolatorTransfer hook
            Assert.IsTrue(output.Contains("DepthOnlyTransferColor"), "Missing prefixed TransferColor in DepthOnly");
            
            // alphaClip hook
            Assert.IsTrue(output.Contains("DepthOnlyClipIt"), "Missing prefixed ClipIt in DepthOnly");
            Assert.IsTrue(output.Contains("ShadowCasterClipIt"), "Missing prefixed ClipIt in ShadowCaster");
        }
        
        [Test]
        public void Test07_HookStructsRewritten()
        {
            string output = ProcessTestShader("Test07_HooksWithHLSLINCLUDE.shader");
            
            // The hook functions in generated passes should use pass-specific struct names
            Assert.IsTrue(output.Contains("inout DepthOnlyAttributes"), 
                "WaveDisplace not rewritten to DepthOnlyAttributes");
            Assert.IsTrue(output.Contains("inout ShadowCasterAttributes"),
                "WaveDisplace not rewritten to ShadowCasterAttributes");
        }
        
        [Test]
        public void Test07_HookDefinesPresent()
        {
            string output = ProcessTestShader("Test07_HooksWithHLSLINCLUDE.shader");
            
            // Generated passes should have the hook defines
            Assert.IsTrue(output.Contains("#define FS_VERTEX_DISPLACEMENT"), "Missing FS_VERTEX_DISPLACEMENT define");
            Assert.IsTrue(output.Contains("#define FS_ALPHA_CLIP"), "Missing FS_ALPHA_CLIP define");
            Assert.IsTrue(output.Contains("#define FS_INTERPOLATOR_TRANSFER"), "Missing FS_INTERPOLATOR_TRANSFER define");
        }
        
        [Test]
        public void Test07_NoCBufferInGeneratedPasses()
        {
            string output = ProcessTestShader("Test07_HooksWithHLSLINCLUDE.shader");
            
            // CBUFFER in HLSLINCLUDE, generated passes should not duplicate it
            int cbufferCount = CountOccurrences(output, "CBUFFER_START(UnityPerMaterial)");
            Assert.AreEqual(1, cbufferCount,
                $"Expected 1 CBUFFER_START (HLSLINCLUDE only), found {cbufferCount}");
        }
        
        //=============================================================================
        // Test 09: Everything Combined
        //=============================================================================
        
        [Test]
        public void Test09_CustomStructNamesPreserved()
        {
            string output = ProcessTestShader("Test09_EverythingCombined.shader");
            
            // Custom names VIn/VOut should appear in HLSLINCLUDE (original)
            Assert.IsTrue(output.Contains("struct VIn"), "Missing original VIn struct");
            Assert.IsTrue(output.Contains("struct VOut"), "Missing original VOut struct");
            
            // Debug pass has its own struct names
            Assert.IsTrue(output.Contains("struct DbgAttr"), "Missing DbgAttr struct");
            Assert.IsTrue(output.Contains("struct DbgInterp"), "Missing DbgInterp struct");
        }
        
        [Test]
        public void Test09_GeneratedPassesUseReferenceStructNames()
        {
            string output = ProcessTestShader("Test09_EverythingCombined.shader");
            
            // Generated passes should be based on the reference pass (Forward) structs
            // which are VIn/VOut from HLSLINCLUDE. Generated names: DepthOnlyAttributes etc.
            Assert.IsTrue(output.Contains("struct DepthOnlyAttributes"), "Missing DepthOnlyAttributes");
            Assert.IsTrue(output.Contains("struct ShadowCasterInterpolators"), "Missing ShadowCasterInterpolators");
        }
        
        [Test]
        public void Test09_HooksPrefixedCorrectly()
        {
            string output = ProcessTestShader("Test09_EverythingCombined.shader");
            
            Assert.IsTrue(output.Contains("DepthOnlyHeightDisplace"), "Missing prefixed HeightDisplace in DepthOnly");
            Assert.IsTrue(output.Contains("DepthOnlyPassColor"), "Missing prefixed PassColor in DepthOnly");
            Assert.IsTrue(output.Contains("DepthOnlyDoClip"), "Missing prefixed DoClip in DepthOnly");
            Assert.IsTrue(output.Contains("ShadowCasterHeightDisplace"), "Missing prefixed HeightDisplace in ShadowCaster");
            Assert.IsTrue(output.Contains("OutlineHeightDisplace"), "Missing prefixed HeightDisplace in Outline");
        }
        
        [Test]
        public void Test09_OutlinePassGenerated()
        {
            string output = ProcessTestShader("Test09_EverythingCombined.shader");
            
            Assert.IsTrue(output.Contains("Name \"Outline\""), "Missing Outline pass");
            Assert.IsTrue(output.Contains("Cull Front"), "Outline pass missing Cull Front");
            Assert.IsTrue(output.Contains("_ENABLE_OUTLINES"), "Outline pass missing _ENABLE_OUTLINES keyword");
        }
        
        [Test]
        public void Test09_OutlinePropertiesInjected()
        {
            string output = ProcessTestShader("Test09_EverythingCombined.shader");
            
            Assert.IsTrue(output.Contains("_OutlineWidth"), "Missing _OutlineWidth property");
            Assert.IsTrue(output.Contains("_OutlineColor"), "Missing _OutlineColor property");
        }
        
        [Test]
        public void Test09_TessellationInAllPasses()
        {
            string output = ProcessTestShader("Test09_EverythingCombined.shader");
            
            // Tessellation is SubShader level, so all passes should have it.
            // Count TessVertex occurrences (each pass defines it)
            int tessVertexCount = CountOccurrences(output, "TessControlPoint TessVertex(");
            
            // Forward, Debug, Outline, DepthOnly, MotionVectors, DepthNormals, ShadowCaster = 7 passes
            // Each has 2 TessVertex definitions (active + passthrough) = 14
            Assert.IsTrue(tessVertexCount >= 7,
                $"Expected TessVertex in all passes, found {tessVertexCount} definitions");
        }
        
        //=============================================================================
        // Test 10: Individual Pass Injection
        //=============================================================================
        
        [Test]
        public void Test10_OnlyRequestedPassesGenerated()
        {
            string output = ProcessTestShader("Test10_IndividualPassInjection.shader");
            
            // Should have ShadowCaster and DepthOnly
            Assert.IsTrue(output.Contains("Name \"ShadowCaster\""), "Missing ShadowCaster");
            Assert.IsTrue(output.Contains("Name \"DepthOnly\""), "Missing DepthOnly");
            
            // Should NOT have DepthNormals, MotionVectors, or Meta
            Assert.IsFalse(output.Contains("Name \"DepthNormals\""), "DepthNormals should not be present");
            Assert.IsFalse(output.Contains("Name \"MotionVectors\""), "MotionVectors should not be present");
            Assert.IsFalse(output.Contains("Name \"Meta\""), "Meta should not be present");
        }
        
        [Test]
        public void Test10_MarkersRemoved()
        {
            string output = ProcessTestShader("Test10_IndividualPassInjection.shader");
            
            Assert.IsFalse(output.Contains("[InjectPass:ShadowCaster]"), "Marker not replaced");
            Assert.IsFalse(output.Contains("[InjectPass:DepthOnly]"), "Marker not replaced");
        }
        
        //=============================================================================
        // Test 12: CBUFFER in Pass (No HLSLINCLUDE)
        //=============================================================================
        
        [Test]
        public void Test12_CBufferDuplicatedInGeneratedPasses()
        {
            string output = ProcessTestShader("Test12_CBufferInPass.shader");
            
            // No HLSLINCLUDE, so each generated pass needs its own CBUFFER.
            // Forward + generated passes should each have CBUFFER_START.
            int cbufferCount = CountOccurrences(output, "CBUFFER_START(UnityPerMaterial)");
            Assert.IsTrue(cbufferCount > 1,
                $"Expected CBUFFER duplicated in generated passes, found {cbufferCount}");
        }
        
        [Test]
        public void Test12_TexturesDuplicatedInGeneratedPasses()
        {
            string output = ProcessTestShader("Test12_CBufferInPass.shader");
            
            // Textures should also be duplicated when not in HLSLINCLUDE
            int textureCount = CountOccurrences(output, "TEXTURE2D(_BaseMap)");
            Assert.IsTrue(textureCount > 1,
                $"Expected textures duplicated in generated passes, found {textureCount}");
        }
        
        //=============================================================================
        // Regression: InjectBasePasses marker fully consumed
        //=============================================================================
        
        [Test]
        public void Test01_InjectBasePassesMarkerRemoved()
        {
            string output = ProcessTestShader("Test01_StructsInHLSLINCLUDE.shader");
            Assert.IsFalse(output.Contains("[InjectBasePasses]"), "[InjectBasePasses] marker was not replaced");
        }
        
        //=============================================================================
        // Regression: No unreplaced template markers in output
        //=============================================================================
        
        [Test]
        public void Test01_NoUnreplacedTemplateMarkers()
        {
            string output = ProcessTestShader("Test01_StructsInHLSLINCLUDE.shader");
            var matches = Regex.Matches(output, @"\{\{[A-Z_]+\}\}");
            Assert.AreEqual(0, matches.Count,
                $"Found unreplaced template markers: {(matches.Count > 0 ? matches[0].Value : "")}");
        }
        
        [Test]
        public void Test07_NoUnreplacedTemplateMarkers()
        {
            string output = ProcessTestShader("Test07_HooksWithHLSLINCLUDE.shader");
            var matches = Regex.Matches(output, @"\{\{[A-Z_]+\}\}");
            Assert.AreEqual(0, matches.Count,
                $"Found unreplaced template markers: {(matches.Count > 0 ? matches[0].Value : "")}");
        }
        
        [Test]
        public void Test09_NoUnreplacedTemplateMarkers()
        {
            string output = ProcessTestShader("Test09_EverythingCombined.shader");
            var matches = Regex.Matches(output, @"\{\{[A-Z_]+\}\}");
            Assert.AreEqual(0, matches.Count,
                $"Found unreplaced template markers: {(matches.Count > 0 ? matches[0].Value : "")}");
        }
        
        //=============================================================================
        // Regression: DepthNormals has normalWS field
        //=============================================================================
        
        [Test]
        public void Test07_DepthNormalsHasNormalWSField()
        {
            string output = ProcessTestShader("Test07_HooksWithHLSLINCLUDE.shader");
            
            // DepthNormals needs normalWS for its fragment shader.
            // The struct should include it even if the interpolators struct has it under a different name.
            Assert.IsTrue(output.Contains("DepthNormalsInterpolators") && output.Contains("normalWS"),
                "DepthNormals pass missing normalWS in interpolators");
        }
        
        //=============================================================================
        // Regression: Forward pass not modified by generated passes
        //=============================================================================
        
        [Test]
        public void Test07_ForwardPassStillHasOriginalVertexFunc()
        {
            string output = ProcessTestShader("Test07_HooksWithHLSLINCLUDE.shader");
            
            // The Forward pass should still have the original Vert function
            Assert.IsTrue(output.Contains("#pragma vertex Vert"), "Forward pass lost its vertex pragma");
            Assert.IsTrue(output.Contains("Interpolators Vert(Attributes input)"),
                "Forward pass lost its original Vert function");
        }
        
        //=============================================================================
        // Regression: Motion vectors has correct struct overrides
        //=============================================================================
        
        [Test]
        public void Test07_MotionVectorsHasExtraFields()
        {
            string output = ProcessTestShader("Test07_HooksWithHLSLINCLUDE.shader");
            
            Assert.IsTrue(output.Contains("positionOld"), "MotionVectors missing positionOld field");
            Assert.IsTrue(output.Contains("curPositionCS"), "MotionVectors missing curPositionCS field");
            Assert.IsTrue(output.Contains("prevPositionCS"), "MotionVectors missing prevPositionCS field");
        }
        
        //=============================================================================
        // Regression: Meta pass has uv1/uv2 for lightmap
        //=============================================================================
        
        [Test]
        public void Test07_MetaPassHasLightmapUVs()
        {
            string output = ProcessTestShader("Test07_HooksWithHLSLINCLUDE.shader");
            
            // Meta pass needs uv1 and uv2 for lightmap baking
            Assert.IsTrue(output.Contains("MetaAttributes") && output.Contains("uv1"),
                "Meta pass missing uv1 in attributes");
            Assert.IsTrue(output.Contains("MetaAttributes") && output.Contains("uv2"),
                "Meta pass missing uv2 in attributes");
        }
        
        //=============================================================================
        // Test 03: Multi-Pass Different Struct Names
        //=============================================================================
        
        [Test]
        public void Test03_EachPassKeepsOwnStructNames()
        {
            string output = ProcessTestShader("Test03_MultiPassDifferentStructs.shader");
            
            // Pass 1 uses Attributes/Interpolators
            Assert.IsTrue(output.Contains("struct Attributes"), "Missing Attributes struct from Pass 1");
            Assert.IsTrue(output.Contains("struct Interpolators"), "Missing Interpolators struct from Pass 1");
            
            // Pass 2 uses SimpleIn/SimpleOut
            Assert.IsTrue(output.Contains("struct SimpleIn") || output.Contains("SimpleIn"),
                "Missing SimpleIn struct from Pass 2");
        }
        
        [Test]
        public void Test03_TessellationUsesPerPassStructs()
        {
            string output = ProcessTestShader("Test03_MultiPassDifferentStructs.shader");
            
            // Tessellation at SubShader level, both passes get it.
            // Each pass's tessellation should use that pass's struct.
            // Pass 2 has fewer fields, its TessControlPoint should be smaller.
            Assert.IsTrue(output.Contains("TessControlPoint TessVertex(Attributes"),
                "Pass 1 tessellation should use Attributes");
        }
        
        //=============================================================================
        // Test 06: Per-Pass Tessellation
        //=============================================================================
        
        [Test]
        public void Test06_TessellatedPassHasTessellation()
        {
            string output = ProcessTestShader("Test06_PerPassTessellation.shader");
            
            // The "Tessellated" pass (pass-level tag) should have tessellation
            Assert.IsTrue(output.Contains("#pragma hull Hull"), "Missing hull pragma");
            Assert.IsTrue(output.Contains("#pragma domain Domain"), "Missing domain pragma");
            Assert.IsTrue(output.Contains("TessControlPoint"), "Missing TessControlPoint struct");
        }
        
        [Test]
        public void Test06_NoTessPassLacksTessellation()
        {
            string output = ProcessTestShader("Test06_PerPassTessellation.shader");
            
            // The "NoTess" pass should NOT have tessellation injected.
            // Find the NoTess pass content and check it doesn't have hull/domain.
            int noTessIdx = output.IndexOf("Name \"NoTess\"");
            Assert.IsTrue(noTessIdx >= 0, "Missing NoTess pass");
            
            // Find the ENDHLSL after NoTess
            int endHlsl = output.IndexOf("ENDHLSL", noTessIdx);
            string noTessContent = output.Substring(noTessIdx, endHlsl - noTessIdx);
            
            Assert.IsFalse(noTessContent.Contains("TessControlPoint"),
                "NoTess pass should not have tessellation");
            Assert.IsFalse(noTessContent.Contains("#pragma hull"),
                "NoTess pass should not have hull pragma");
        }
        
        [Test]
        public void Test06_GeneratedPassesGetTessellation()
        {
            string output = ProcessTestShader("Test06_PerPassTessellation.shader");
            
            // Generated passes should get tessellation since the feature is enabled
            int depthOnlyIdx = output.IndexOf("Name \"DepthOnly\"");
            Assert.IsTrue(depthOnlyIdx >= 0, "Missing DepthOnly pass");
            
            int endHlsl = output.IndexOf("ENDHLSL", depthOnlyIdx);
            string depthOnlyContent = output.Substring(depthOnlyIdx, endHlsl - depthOnlyIdx);
            
            Assert.IsTrue(depthOnlyContent.Contains("TessControlPoint"),
                "Generated DepthOnly should have tessellation");
        }
        
        //=============================================================================
        // Test 11: ShaderGen at SubShader Level
        //=============================================================================
        
        [Test]
        public void Test11_SubShaderLevelShaderGenGeneratesPasses()
        {
            string output = ProcessTestShader("Test11_ShaderGenSubShaderLevel.shader");
            
            Assert.IsTrue(output.Contains("Name \"ShadowCaster\""), "Missing ShadowCaster");
            Assert.IsTrue(output.Contains("Name \"DepthOnly\""), "Missing DepthOnly");
            Assert.IsTrue(output.Contains("Name \"DepthNormals\""), "Missing DepthNormals");
            Assert.IsTrue(output.Contains("Name \"MotionVectors\""), "Missing MotionVectors");
            Assert.IsTrue(output.Contains("Name \"Meta\""), "Missing Meta");
        }
        
        [Test]
        public void Test11_ForwardPassPreserved()
        {
            string output = ProcessTestShader("Test11_ShaderGenSubShaderLevel.shader");
            
            // Original Forward pass should still be there untouched
            Assert.IsTrue(output.Contains("Name \"Forward\""), "Missing Forward pass");
            Assert.IsTrue(output.Contains("#pragma vertex Vert"), "Forward pass vertex pragma missing");
        }
        
        //=============================================================================
        // Test 12: Additional regression checks
        //=============================================================================
        
        [Test]
        public void Test12_NoUnreplacedTemplateMarkers()
        {
            string output = ProcessTestShader("Test12_CBufferInPass.shader");
            var matches = Regex.Matches(output, @"\{\{[A-Z_]+\}\}");
            Assert.AreEqual(0, matches.Count,
                $"Found unreplaced template markers: {(matches.Count > 0 ? matches[0].Value : "")}");
        }
        
        [Test]
        public void Test12_GeneratedPassesHaveAllBasePasses()
        {
            string output = ProcessTestShader("Test12_CBufferInPass.shader");
            
            Assert.IsTrue(output.Contains("Name \"ShadowCaster\""), "Missing ShadowCaster");
            Assert.IsTrue(output.Contains("Name \"DepthOnly\""), "Missing DepthOnly");
            Assert.IsTrue(output.Contains("Name \"DepthNormals\""), "Missing DepthNormals");
            Assert.IsTrue(output.Contains("Name \"MotionVectors\""), "Missing MotionVectors");
            Assert.IsTrue(output.Contains("Name \"Meta\""), "Missing Meta");
        }
        
        //=============================================================================
        // Test 14: Hooks in HLSLINCLUDE (function name collision fix)
        //=============================================================================
        
        [Test]
        public void Test14_HooksFunctionsPrefixed()
        {
            string output = ProcessTestShader("Test14_HooksInHLSLINCLUDE.shader");
            
            // HeightDisplace body is in HLSLINCLUDE, should be prefixed in generated passes
            Assert.IsTrue(output.Contains("DepthOnlyHeightDisplace"),
                "Missing prefixed HeightDisplace in DepthOnly");
            Assert.IsTrue(output.Contains("ShadowCasterHeightDisplace"),
                "Missing prefixed HeightDisplace in ShadowCaster");
            
            // AlphaClipFunction body is in HLSLINCLUDE too
            Assert.IsTrue(output.Contains("DepthOnlyAlphaClipFunction"),
                "Missing prefixed AlphaClipFunction in DepthOnly");
            Assert.IsTrue(output.Contains("ShadowCasterAlphaClipFunction"),
                "Missing prefixed AlphaClipFunction in ShadowCaster");
        }
        
        [Test]
        public void Test14_ControlCaseHookFromPassAlsoPrefixed()
        {
            string output = ProcessTestShader("Test14_HooksInHLSLINCLUDE.shader");
            
            // TransferWorldPos has body in the pass (not HLSLINCLUDE).
            // Should still be prefixed in generated passes.
            Assert.IsTrue(output.Contains("DepthOnlyTransferWorldPos"),
                "Missing prefixed TransferWorldPos in DepthOnly");
            Assert.IsTrue(output.Contains("ShadowCasterTransferWorldPos"),
                "Missing prefixed TransferWorldPos in ShadowCaster");
        }
        
        [Test]
        public void Test14_HookStructsRewrittenToPassSpecific()
        {
            string output = ProcessTestShader("Test14_HooksInHLSLINCLUDE.shader");
            
            // HeightDisplace originally uses "inout Attributes input"
            // In DepthOnly it should be "inout DepthOnlyAttributes input"
            Assert.IsTrue(output.Contains("inout DepthOnlyAttributes"),
                "HeightDisplace not rewritten to DepthOnlyAttributes");
            Assert.IsTrue(output.Contains("inout ShadowCasterAttributes"),
                "HeightDisplace not rewritten to ShadowCasterAttributes");
        }
        
        [Test]
        public void Test14_AllThreeHookDefinesPresent()
        {
            string output = ProcessTestShader("Test14_HooksInHLSLINCLUDE.shader");
            
            Assert.IsTrue(output.Contains("#define FS_VERTEX_DISPLACEMENT"),
                "Missing FS_VERTEX_DISPLACEMENT define");
            Assert.IsTrue(output.Contains("#define FS_ALPHA_CLIP"),
                "Missing FS_ALPHA_CLIP define");
            Assert.IsTrue(output.Contains("#define FS_INTERPOLATOR_TRANSFER"),
                "Missing FS_INTERPOLATOR_TRANSFER define");
        }
        
        [Test]
        public void Test14_HookCallsUsePrefixedNames()
        {
            string output = ProcessTestShader("Test14_HooksInHLSLINCLUDE.shader");
            
            // In the DepthOnly vertex function, the displacement call should use the prefixed name
            int depthOnlyIdx = output.IndexOf("Name \"DepthOnly\"");
            Assert.IsTrue(depthOnlyIdx >= 0, "Missing DepthOnly pass");
            
            int endHlsl = output.IndexOf("ENDHLSL", depthOnlyIdx);
            string depthOnlyContent = output.Substring(depthOnlyIdx, endHlsl - depthOnlyIdx);
            
            Assert.IsTrue(depthOnlyContent.Contains("DepthOnlyHeightDisplace(input)"),
                "DepthOnly vertex should call DepthOnlyHeightDisplace");
            Assert.IsTrue(depthOnlyContent.Contains("DepthOnlyAlphaClipFunction(input)"),
                "DepthOnly fragment should call DepthOnlyAlphaClipFunction");
            Assert.IsTrue(depthOnlyContent.Contains("DepthOnlyTransferWorldPos(input"),
                "DepthOnly vertex should call DepthOnlyTransferWorldPos");
        }
        
        [Test]
        public void Test14_NoUnreplacedTemplateMarkers()
        {
            string output = ProcessTestShader("Test14_HooksInHLSLINCLUDE.shader");
            var matches = Regex.Matches(output, @"\{\{[A-Z_]+\}\}");
            Assert.AreEqual(0, matches.Count,
                $"Found unreplaced template markers: {(matches.Count > 0 ? matches[0].Value : "")}");
        }
        
        [Test]
        public void Test14_ForwardPassUnchanged()
        {
            string output = ProcessTestShader("Test14_HooksInHLSLINCLUDE.shader");
            
            // Forward pass should keep original unprefixed calls
            // (it calls HeightDisplace directly, which resolves to HLSLINCLUDE version)
            Assert.IsTrue(output.Contains("#pragma vertex Vert"),
                "Forward pass lost its vertex pragma");
            Assert.IsTrue(output.Contains("Interpolators Vert(Attributes input)"),
                "Forward pass lost its original Vert function");
        }
        
        //=============================================================================
        // Test 15: Outline Only (no tessellation)
        //=============================================================================
        
        [Test]
        public void Test15_OutlinePassGenerated()
        {
            string output = ProcessTestShader("Test15_OutlineOnly.shader");
            
            Assert.IsTrue(output.Contains("Name \"Outline\""), "Missing Outline pass");
            Assert.IsTrue(output.Contains("Cull Front"), "Outline pass missing Cull Front");
        }
        
        [Test]
        public void Test15_OutlinePropertiesInjected()
        {
            string output = ProcessTestShader("Test15_OutlineOnly.shader");
            
            // This is the early-return regression: outline properties must be injected
            // even when no tag processors are active
            Assert.IsTrue(output.Contains("_OutlineWidth"), "Missing _OutlineWidth property");
            Assert.IsTrue(output.Contains("_OutlineColor"), "Missing _OutlineColor property");
        }
        
        [Test]
        public void Test15_OutlineCBufferInjected()
        {
            string output = ProcessTestShader("Test15_OutlineOnly.shader");
            
            // Outline CBUFFER entries should be in the authored pass's CBUFFER
            Assert.IsTrue(output.Contains("float _OutlineWidth"),
                "Missing _OutlineWidth in CBUFFER");
            Assert.IsTrue(output.Contains("float4 _OutlineColor"),
                "Missing _OutlineColor in CBUFFER");
        }
        
        [Test]
        public void Test15_NoTessellation()
        {
            string output = ProcessTestShader("Test15_OutlineOnly.shader");
            
            // No tessellation tag, so no tessellation anywhere
            Assert.IsFalse(output.Contains("TessControlPoint"),
                "Should not have tessellation");
            Assert.IsFalse(output.Contains("#pragma hull"),
                "Should not have hull pragma");
        }
        
        [Test]
        public void Test15_BasePassesStillGenerated()
        {
            string output = ProcessTestShader("Test15_OutlineOnly.shader");
            
            Assert.IsTrue(output.Contains("Name \"ShadowCaster\""), "Missing ShadowCaster");
            Assert.IsTrue(output.Contains("Name \"DepthOnly\""), "Missing DepthOnly");
            Assert.IsTrue(output.Contains("Name \"DepthNormals\""), "Missing DepthNormals");
            Assert.IsTrue(output.Contains("Name \"MotionVectors\""), "Missing MotionVectors");
            Assert.IsTrue(output.Contains("Name \"Meta\""), "Missing Meta");
        }
        
        //=============================================================================
        // Regression: HLSLINCLUDE keyword in comments doesn't trigger false detection
        //=============================================================================
        
        [Test]
        public void Test12_HlslIncludeInCommentIgnored()
        {
            string output = ProcessTestShader("Test12_CBufferInPass.shader");
            
            // Test12 has "No HLSLINCLUDE block at all" in its comment header.
            // The parser should NOT treat that as an actual HLSLINCLUDE block.
            // If it did, CBufferInHlslInclude would be true and generated passes
            // would have empty CBUFFERs.
            int cbufferCount = CountOccurrences(output, "CBUFFER_START(UnityPerMaterial)");
            Assert.IsTrue(cbufferCount > 1,
                $"HLSLINCLUDE in comment triggered false detection, found {cbufferCount} CBUFFERs");
        }
    }
}

