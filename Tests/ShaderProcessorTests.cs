using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

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
        
        /// <summary>
        /// Run inheritance resolution on a child shader. Returns the merged source
        /// before processing. Use this to test the inheritance pre-pass in isolation.
        /// </summary>
        string ResolveTestShader(string childFileName)
        {
            string testsDir = FindTestsDirectory();
            Assert.IsNotNull(testsDir, "Could not find Tests directory");
            
            string childPath = Path.Combine(testsDir, childFileName);
            Assert.IsTrue(File.Exists(childPath), $"Test shader not found: {childPath}");
            
            string childSource = File.ReadAllText(childPath);
            var result = ShaderInheritance.Resolve(childSource, childPath);
            Assert.IsNotNull(result.ParentPath, "Inheritance resolution failed - parent not found");
            
            return result.Source;
        }
        
        /// <summary>
        /// Run inheritance resolution then full processing on a child shader.
        /// This is what the importer does: resolve first, then process.
        /// </summary>
        string ResolveAndProcessTestShader(string childFileName)
        {
            string testsDir = FindTestsDirectory();
            Assert.IsNotNull(testsDir, "Could not find Tests directory");
            
            string childPath = Path.Combine(testsDir, childFileName);
            Assert.IsTrue(File.Exists(childPath), $"Test shader not found: {childPath}");
            
            string childSource = File.ReadAllText(childPath);
            var result = ShaderInheritance.Resolve(childSource, childPath);
            Assert.IsNotNull(result.ParentPath, "Inheritance resolution failed - parent not found");
            
            return _processor.Process(result.Source, childPath);
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

        /// <summary>
        /// Slice a generated pass from its Name line up to its ENDHLSL.
        /// Fails the test if the pass isn't in the output.
        /// </summary>
        static string GetPassContent(string output, string passName)
        {
            int passIdx = output.IndexOf($"Name \"{passName}\"");
            Assert.GreaterOrEqual(passIdx, 0, $"Missing {passName} pass");
            int endHlsl = output.IndexOf("ENDHLSL", passIdx);
            Assert.Greater(endHlsl, passIdx, $"{passName} pass has no ENDHLSL");
            return output.Substring(passIdx, endHlsl - passIdx);
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
        public void Test01_DepthNormalsFallbackWritesSignedNormal()
        {
            // No InjectForwardBody here, so this is the template's own return line.
            // URP writes signed normals into an SNorm target and SampleSceneNormals
            // doesn't decode, so a [0,1] remap would disagree with URP Lit.
            string output = ProcessTestShader("Test01_StructsInHLSLINCLUDE.shader");
            string passContent = GetPassContent(output, "DepthNormals");

            Assert.IsTrue(passContent.Contains("half4(NormalizeNormalPerPixel(input.normalWS), 0.0)"),
                "DepthNormals template should write the signed normal with w = 0, like URP");
            Assert.IsFalse(passContent.Contains("* 0.5 + 0.5"),
                "DepthNormals must not remap normals to [0,1]");
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

            // Generated passes should use prefixed names (their pass templates emit the
            // renamed hook bodies via {{HOOK_FUNCTIONS}})
            Assert.IsTrue(output.Contains("DepthOnlyTessellationFactorOverride"),
                "Missing prefixed tess factor override in DepthOnly");
            Assert.IsTrue(output.Contains("ShadowCasterTessellationFactorOverride"),
                "Missing prefixed tess factor override in ShadowCaster");
        }

        [Test]
        public void Test01_AuthoredPassTessFactorOverrideNotPrefixed()
        {
            string output = ProcessTestShader("Test01_StructsInHLSLINCLUDE.shader");

            // The authored Forward pass keeps its original struct names, so the tess
            // code injected into it must call the user's function under its original
            // name. A pass-name prefixed call would reference a renamed function body
            // that is never emitted (the Tessellation template has no HOOK_FUNCTIONS
            // marker), producing an undefined identifier at compile time.
            int fwdIdx = output.IndexOf("Name \"Forward\"");
            Assert.IsTrue(fwdIdx >= 0, "Missing Forward pass");

            int endHlsl = output.IndexOf("ENDHLSL", fwdIdx);
            string fwdContent = output.Substring(fwdIdx, endHlsl - fwdIdx);

            Assert.IsTrue(fwdContent.Contains("TessellationFactorOverride(_overrideFactor, input);"),
                "Forward pass tess code should call the user's hook function");
            Assert.IsFalse(fwdContent.Contains("ForwardTessellationFactorOverride"),
                "Forward pass tess code must not use a pass-name prefixed hook call");
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
            
            Assert.IsTrue(output.Contains("Name \"Inverse Hull Outlines\""), "Missing Outline pass");
            Assert.IsTrue(output.Contains("Cull Front"), "Outline pass missing Cull Front");
            Assert.IsTrue(output.Contains("_ENABLE_OUTLINES"), "Outline pass missing _ENABLE_OUTLINES keyword");
        }
        
        [Test]
        public void Test09_OutlinePropertiesInjected()
        {
            string output = ProcessTestShader("Test09_EverythingCombined.shader");
            
            // Check the Properties block specifically, not just anywhere in output
            int propsStart = output.IndexOf("Properties");
            Assert.IsTrue(propsStart >= 0, "Missing Properties block");
            
            int subShaderIdx = output.IndexOf("SubShader", propsStart);
            string propsContent = output.Substring(propsStart, subShaderIdx - propsStart);
            
            Assert.IsTrue(propsContent.Contains("_OutlineWidth"),
                "Missing _OutlineWidth in Properties block");
            Assert.IsTrue(propsContent.Contains("_OutlineColor"),
                "Missing _OutlineColor in Properties block");
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
            
            Assert.IsTrue(output.Contains("Name \"Inverse Hull Outlines\""), "Missing Outline pass");
            Assert.IsTrue(output.Contains("Cull Front"), "Outline pass missing Cull Front");
        }
        
        [Test]
        public void Test15_OutlinePropertiesInjected()
        {
            string output = ProcessTestShader("Test15_OutlineOnly.shader");
            
            // Outline properties must be injected into the Properties block,
            // not just present in generated pass template output.
            // Find Properties block content.
            int propsStart = output.IndexOf("Properties");
            Assert.IsTrue(propsStart >= 0, "Missing Properties block");
            
            int subShaderIdx = output.IndexOf("SubShader", propsStart);
            string propsContent = output.Substring(propsStart, subShaderIdx - propsStart);
            
            Assert.IsTrue(propsContent.Contains("_OutlineWidth"),
                "Missing _OutlineWidth in Properties block");
            Assert.IsTrue(propsContent.Contains("_OutlineColor"),
                "Missing _OutlineColor in Properties block");
        }
        
        [Test]
        public void Test15_OutlineCBufferInjected()
        {
            string output = ProcessTestShader("Test15_OutlineOnly.shader");
            
            // Outline CBUFFER entries should be injected into the Forward pass's CBUFFER
            // (not just present in the generated Outline pass template).
            // Find the Forward pass CBUFFER specifically.
            int fwdIdx = output.IndexOf("Name \"Forward\"");
            Assert.IsTrue(fwdIdx >= 0, "Missing Forward pass");
            
            int endHlsl = output.IndexOf("ENDHLSL", fwdIdx);
            string fwdContent = output.Substring(fwdIdx, endHlsl - fwdIdx);
            
            Assert.IsTrue(fwdContent.Contains("float _OutlineWidth"),
                "Missing _OutlineWidth in Forward pass CBUFFER");
            Assert.IsTrue(fwdContent.Contains("float4 _OutlineColor"),
                "Missing _OutlineColor in Forward pass CBUFFER");
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
        
        //=============================================================================
        // Test 06: Full mode (On) still propagates to generated passes
        //=============================================================================
        
        [Test]
        public void Test06_FullModeStillPropagates()
        {
            string output = ProcessTestShader("Test06_PerPassTessellation.shader");
            
            // "Tessellation" = "On" on the Tessellated pass maps to "Full" mode.
            // Generated passes should still get tessellation (backwards compatible).
            int depthOnlyIdx = output.IndexOf("Name \"DepthOnly\"");
            Assert.IsTrue(depthOnlyIdx >= 0, "Missing DepthOnly pass");
            
            int endHlsl = output.IndexOf("ENDHLSL", depthOnlyIdx);
            string depthOnlyContent = output.Substring(depthOnlyIdx, endHlsl - depthOnlyIdx);
            
            Assert.IsTrue(depthOnlyContent.Contains("TessControlPoint"),
                "Full mode (On) should propagate tessellation to generated DepthOnly");
        }
        
        //=============================================================================
        // Test 16: Pass-Only Tag Mode
        //=============================================================================
        
        [Test]
        public void Test16_AuraPassHasTessellation()
        {
            string output = ProcessTestShader("Test16_PassOnlyTagMode.shader");
            
            // The Aura pass declares "Tessellation" = "Pass", so it should get tessellation
            int auraIdx = output.IndexOf("Name \"Aura\"");
            Assert.IsTrue(auraIdx >= 0, "Missing Aura pass");
            
            int endHlsl = output.IndexOf("ENDHLSL", auraIdx);
            string auraContent = output.Substring(auraIdx, endHlsl - auraIdx);
            
            Assert.IsTrue(auraContent.Contains("TessControlPoint"),
                "Aura pass should have tessellation (Pass mode still applies to declaring pass)");
            Assert.IsTrue(auraContent.Contains("#pragma hull"),
                "Aura pass should have hull pragma");
        }
        
        [Test]
        public void Test16_ForwardPassNoTessellation()
        {
            string output = ProcessTestShader("Test16_PassOnlyTagMode.shader");
            
            // Forward pass doesn't declare tessellation, so it shouldn't have it
            int fwdIdx = output.IndexOf("Name \"Forward\"");
            Assert.IsTrue(fwdIdx >= 0, "Missing Forward pass");
            
            int endHlsl = output.IndexOf("ENDHLSL", fwdIdx);
            string fwdContent = output.Substring(fwdIdx, endHlsl - fwdIdx);
            
            Assert.IsFalse(fwdContent.Contains("TessControlPoint"),
                "Forward pass should NOT have tessellation");
            Assert.IsFalse(fwdContent.Contains("#pragma hull"),
                "Forward pass should NOT have hull pragma");
        }
        
        [Test]
        public void Test16_GeneratedPassesNoTessellation()
        {
            string output = ProcessTestShader("Test16_PassOnlyTagMode.shader");
            
            // "Pass" mode means generated passes should NOT get tessellation
            string[] generatedPasses = { "DepthOnly", "ShadowCaster", "DepthNormals", "MotionVectors" };
            
            foreach (string passName in generatedPasses)
            {
                int passIdx = output.IndexOf($"Name \"{passName}\"");
                Assert.IsTrue(passIdx >= 0, $"Missing {passName} pass");
                
                int endHlsl = output.IndexOf("ENDHLSL", passIdx);
                string passContent = output.Substring(passIdx, endHlsl - passIdx);
                
                Assert.IsFalse(passContent.Contains("TessControlPoint"),
                    $"{passName} should NOT have tessellation in Pass mode");
                Assert.IsFalse(passContent.Contains("#pragma hull"),
                    $"{passName} should NOT have hull pragma in Pass mode");
            }
        }
        
        [Test]
        public void Test16_TessPropertiesStillInjected()
        {
            string output = ProcessTestShader("Test16_PassOnlyTagMode.shader");
            
            // Even in "Pass" mode, material properties should be injected into
            // the Properties block because the Aura pass needs them
            int propsStart = output.IndexOf("Properties");
            Assert.IsTrue(propsStart >= 0, "Missing Properties block");
            
            int subShaderIdx = output.IndexOf("SubShader", propsStart);
            string propsContent = output.Substring(propsStart, subShaderIdx - propsStart);
            
            Assert.IsTrue(propsContent.Contains("_TessellationFactor"),
                "Tessellation properties should be in Properties block even in Pass mode");
        }
        
        [Test]
        public void Test16_GeneratedPassesStillExist()
        {
            string output = ProcessTestShader("Test16_PassOnlyTagMode.shader");
            
            // Base passes should still be generated (just without tessellation)
            Assert.IsTrue(output.Contains("Name \"ShadowCaster\""), "Missing ShadowCaster");
            Assert.IsTrue(output.Contains("Name \"DepthOnly\""), "Missing DepthOnly");
            Assert.IsTrue(output.Contains("Name \"DepthNormals\""), "Missing DepthNormals");
            Assert.IsTrue(output.Contains("Name \"MotionVectors\""), "Missing MotionVectors");
            Assert.IsTrue(output.Contains("Name \"Meta\""), "Missing Meta");
        }
        
        [Test]
        public void Test16_NoUnreplacedTemplateMarkers()
        {
            string output = ProcessTestShader("Test16_PassOnlyTagMode.shader");
            var matches = Regex.Matches(output, @"\{\{[A-Z_]+\}\}");
            Assert.AreEqual(0, matches.Count,
                $"Found unreplaced template markers: {(matches.Count > 0 ? matches[0].Value : "")}");
        }
        
        //=============================================================================
        // Test 17: Forward Body Injection (Vertex + Fragment)
        //=============================================================================
        
        [Test]
        public void Test17_AllBasePassesGenerated()
        {
            string output = ProcessTestShader("Test17_ForwardBodyInjection.shader");
            
            Assert.IsTrue(output.Contains("Name \"ShadowCaster\""), "Missing ShadowCaster");
            Assert.IsTrue(output.Contains("Name \"DepthOnly\""), "Missing DepthOnly");
            Assert.IsTrue(output.Contains("Name \"DepthNormals\""), "Missing DepthNormals");
            Assert.IsTrue(output.Contains("Name \"MotionVectors\""), "Missing MotionVectors");
            Assert.IsTrue(output.Contains("Name \"Meta\""), "Missing Meta");
        }
        
        [Test]
        public void Test17_VertexBodyInjectsUVTransfer()
        {
            string output = ProcessTestShader("Test17_ForwardBodyInjection.shader");
            
            int passIdx = output.IndexOf("Name \"DepthOnly\"");
            int endHlsl = output.IndexOf("ENDHLSL", passIdx);
            string passContent = output.Substring(passIdx, endHlsl - passIdx);
            
            // Vertex body should transfer UVs
            Assert.IsTrue(passContent.Contains("TRANSFORM_TEX"),
                "DepthOnly vertex should have UV transfer from injected body");
        }
        
        [Test]
        public void Test17_VertexBodyInjectsNormalTransfer()
        {
            string output = ProcessTestShader("Test17_ForwardBodyInjection.shader");
            
            int passIdx = output.IndexOf("Name \"DepthNormals\"");
            int endHlsl = output.IndexOf("ENDHLSL", passIdx);
            string passContent = output.Substring(passIdx, endHlsl - passIdx);
            
            // Vertex body should transfer normalWS
            Assert.IsTrue(passContent.Contains("TransformObjectToWorldNormal"),
                "DepthNormals vertex should have normal transfer from injected body");
        }
        
        [Test]
        public void Test17_VertexBoilerplateStripped()
        {
            string output = ProcessTestShader("Test17_ForwardBodyInjection.shader");
    
            int passIdx = output.IndexOf("Name \"DepthOnly\"");
            int vertIdx = output.IndexOf("DepthOnlyInterpolators DepthOnlyVertex(", passIdx);
    
            // Find the end of the vertex function (matching closing brace), not the fragment
            int braceStart = output.IndexOf('{', vertIdx);
            int depth = 1;
            int i = braceStart + 1;
            while (i < output.Length && depth > 0)
            {
                if (output[i] == '{') depth++;
                else if (output[i] == '}') depth--;
                i++;
            }
            string vertContent = output.Substring(vertIdx, i - vertIdx);
    
            // Should not have double instance ID setup
            int setupCount = Regex.Matches(vertContent, @"UNITY_SETUP_INSTANCE_ID").Count;
            Assert.AreEqual(1, setupCount,
                "Vertex should have exactly one UNITY_SETUP_INSTANCE_ID (template's, not injected)");
        }
        
        [Test]
        public void Test17_FragmentBodyInjectedWithClip()
        {
            string output = ProcessTestShader("Test17_ForwardBodyInjection.shader");
            
            int passIdx = output.IndexOf("Name \"DepthOnly\"");
            int endHlsl = output.IndexOf("ENDHLSL", passIdx);
            string passContent = output.Substring(passIdx, endHlsl - passIdx);
            
            Assert.IsTrue(passContent.Contains("SAMPLE_TEXTURE2D(_BaseMap"),
                "DepthOnly fragment should have injected _BaseMap sampling");
            Assert.IsTrue(passContent.Contains("clip("),
                "DepthOnly fragment should have clip() from injected body");
        }
        
        [Test]
        public void Test17_FragmentReturnsSwapped()
        {
            string output = ProcessTestShader("Test17_ForwardBodyInjection.shader");
            
            int passIdx = output.IndexOf("Name \"DepthOnly\"");
            int endHlsl = output.IndexOf("ENDHLSL", passIdx);
            string passContent = output.Substring(passIdx, endHlsl - passIdx);
            
            Assert.IsFalse(passContent.Contains("return color;"),
                "Forward's original return should be swapped out");
            Assert.IsTrue(passContent.Contains("return input.positionCS.z;"),
                "DepthOnly should return depth value");
        }
        
        [Test]
        public void Test17_ShadowCasterReturnsZero()
        {
            string output = ProcessTestShader("Test17_ForwardBodyInjection.shader");
            
            int passIdx = output.IndexOf("Name \"ShadowCaster\"");
            int endHlsl = output.IndexOf("ENDHLSL", passIdx);
            string passContent = output.Substring(passIdx, endHlsl - passIdx);
            
            Assert.IsTrue(passContent.Contains("clip("),
                "ShadowCaster should have clip() from injected body");
            Assert.IsTrue(passContent.Contains("return 0;"),
                "ShadowCaster should return 0");
        }
        
        [Test]
        public void Test17_StructNamesRewrittenInBothBodies()
        {
            string output = ProcessTestShader("Test17_ForwardBodyInjection.shader");
            
            int passIdx = output.IndexOf("Name \"DepthOnly\"");
            int endHlsl = output.IndexOf("ENDHLSL", passIdx);
            string passContent = output.Substring(passIdx, endHlsl - passIdx);
            
            // The injected bodies should not reference the original struct names as types
            Assert.IsFalse(Regex.IsMatch(passContent, @"\(Interpolators\)"),
                "Injected body should not cast to original 'Interpolators'");
        }
        
        [Test]
        public void Test17_MetaUsesOutputPragmas()
        {
            string output = ProcessTestShader("Test17_ForwardBodyInjection.shader");
            
            int passIdx = output.IndexOf("Name \"Meta\"");
            Assert.IsTrue(passIdx >= 0, "Missing Meta pass");
            
            int endHlsl = output.IndexOf("ENDHLSL", passIdx);
            string passContent = output.Substring(passIdx, endHlsl - passIdx);
            
            Assert.IsTrue(passContent.Contains("albedo.rgb"),
                "Meta should reference 'albedo' from fragmentOutput pragma");
            Assert.IsTrue(passContent.Contains("_mi.Emission = emission"),
                "Meta should reference 'emission' from fragmentOutput pragma");
        }
        
        [Test]
        public void Test17_DepthNormalsUsesComputedNormal()
        {
            string output = ProcessTestShader("Test17_ForwardBodyInjection.shader");
            
            int passIdx = output.IndexOf("Name \"DepthNormals\"");
            int endHlsl = output.IndexOf("ENDHLSL", passIdx);
            string passContent = output.Substring(passIdx, endHlsl - passIdx);
            
            Assert.IsTrue(passContent.Contains("NormalizeNormalPerPixel(computedNormal)"),
                "DepthNormals should use 'computedNormal' from fragmentOutput:normal pragma");
        }

        [Test]
        public void Test17_DepthNormalsWritesSignedNormal()
        {
            // The fragmentOutput:normal path goes through the injector's return
            // expression, not the template line, so it needs its own check.
            string output = ProcessTestShader("Test17_ForwardBodyInjection.shader");
            string passContent = GetPassContent(output, "DepthNormals");

            Assert.IsTrue(passContent.Contains("half4(NormalizeNormalPerPixel(computedNormal), 0.0)"),
                "DepthNormals should write the signed computed normal with w = 0, like URP");
            Assert.IsFalse(passContent.Contains("* 0.5 + 0.5"),
                "DepthNormals must not remap normals to [0,1]");
        }

        [Test]
        public void Test17_MetaVertexHasUVTransfer()
        {
            string output = ProcessTestShader("Test17_ForwardBodyInjection.shader");
            
            int passIdx = output.IndexOf("Name \"Meta\"");
            int endHlsl = output.IndexOf("ENDHLSL", passIdx);
            string passContent = output.Substring(passIdx, endHlsl - passIdx);
            
            Assert.IsTrue(passContent.Contains("TRANSFORM_TEX"),
                "Meta vertex should have UV transfer from injected body");
        }
        
        [Test]
        public void Test17_NoUnreplacedTemplateMarkers()
        {
            string output = ProcessTestShader("Test17_ForwardBodyInjection.shader");
            var matches = Regex.Matches(output, @"\{\{[A-Z_]+\}\}");
            Assert.AreEqual(0, matches.Count,
                $"Found unreplaced template markers: {(matches.Count > 0 ? matches[0].Value : "")}");
        }
        
        [Test]
        public void Test17_FragmentOutputPragmasStrippedFromContent()
        {
            string output = ProcessTestShader("Test17_ForwardBodyInjection.shader");
            
            // fragmentOutput pragmas should not appear in generated passes
            // (they're stripped from FORWARD_CONTENT)
            int depthOnlyIdx = output.IndexOf("Name \"DepthOnly\"");
            int endHlsl = output.IndexOf("ENDHLSL", depthOnlyIdx);
            string passContent = output.Substring(depthOnlyIdx, endHlsl - depthOnlyIdx);
            
            Assert.IsFalse(passContent.Contains("fragmentOutput:"),
                "fragmentOutput pragmas should be stripped from generated pass content");
        }
        
        //=============================================================================
        // Test 18: Forward Body Injection with Custom Variable Names
        //
        // Verifies that non-standard variable names (v/o/i instead of input/output)
        // are normalized to match template conventions before injection.
        //=============================================================================
        
        
        [Test]
        public void Test18_AllBasePassesGenerated()
        {
            string output = ProcessTestShader("Test18_ForwardBodyInjection_CustomVarNames.shader");
            
            Assert.IsTrue(output.Contains("Name \"ShadowCaster\""), "Missing ShadowCaster");
            Assert.IsTrue(output.Contains("Name \"DepthOnly\""), "Missing DepthOnly");
            Assert.IsTrue(output.Contains("Name \"DepthNormals\""), "Missing DepthNormals");
            Assert.IsTrue(output.Contains("Name \"MotionVectors\""), "Missing MotionVectors");
            Assert.IsTrue(output.Contains("Name \"Meta\""), "Missing Meta");
        }
        
        [Test]
        public void Test18_VertexBodyUsesNormalizedOutputName()
        {
            string output = ProcessTestShader("Test18_ForwardBodyInjection_CustomVarNames.shader");
            
            // Extract DepthOnly vertex function body
            int passIdx = output.IndexOf("Name \"DepthOnly\"");
            int vertIdx = output.IndexOf("DepthOnlyInterpolators DepthOnlyVertex(", passIdx);
            int braceStart = output.IndexOf('{', vertIdx);
            int depth = 1;
            int i = braceStart + 1;
            while (i < output.Length && depth > 0)
            {
                if (output[i] == '{') depth++;
                else if (output[i] == '}') depth--;
                i++;
            }
            string vertContent = output.Substring(braceStart, i - braceStart);
            
            // The injected body should use 'output.' not 'o.'
            Assert.IsTrue(vertContent.Contains("output.normalWS"),
                "Injected vertex body should use normalized 'output' variable name");
            Assert.IsFalse(Regex.IsMatch(vertContent, @"\bo\.normalWS\b"),
                "Injected vertex body should NOT contain original 'o.' variable references");
        }
        
        [Test]
        public void Test18_VertexBodyUsesNormalizedInputName()
        {
            string output = ProcessTestShader("Test18_ForwardBodyInjection_CustomVarNames.shader");
            
            // Extract DepthOnly vertex function body
            int passIdx = output.IndexOf("Name \"DepthOnly\"");
            int vertIdx = output.IndexOf("DepthOnlyInterpolators DepthOnlyVertex(", passIdx);
            int braceStart = output.IndexOf('{', vertIdx);
            int depth = 1;
            int i = braceStart + 1;
            while (i < output.Length && depth > 0)
            {
                if (output[i] == '{') depth++;
                else if (output[i] == '}') depth--;
                i++;
            }
            string vertContent = output.Substring(braceStart, i - braceStart);
            
            // The injected body should use 'input.' not 'v.'
            Assert.IsTrue(vertContent.Contains("input.normalOS"),
                "Injected vertex body should use normalized 'input' variable name");
            Assert.IsFalse(Regex.IsMatch(vertContent, @"\bv\.normalOS\b"),
                "Injected vertex body should NOT contain original 'v.' variable references");
        }
        
        [Test]
        public void Test18_FragmentBodyUsesNormalizedInputName()
        {
            string output = ProcessTestShader("Test18_ForwardBodyInjection_CustomVarNames.shader");
            
            int passIdx = output.IndexOf("Name \"DepthOnly\"");
            int endHlsl = output.IndexOf("ENDHLSL", passIdx);
            string passContent = output.Substring(passIdx, endHlsl - passIdx);
            
            // Fragment body should use 'input.' not 'i.'
            Assert.IsTrue(passContent.Contains("input.uv"),
                "Injected fragment body should use normalized 'input' variable name");
            Assert.IsFalse(Regex.IsMatch(passContent, @"\bi\.uv\b"),
                "Injected fragment body should NOT contain original 'i.' variable references");
        }
        
        [Test]
        public void Test18_FragmentReturnsSwappedCorrectly()
        {
            string output = ProcessTestShader("Test18_ForwardBodyInjection_CustomVarNames.shader");
            
            int passIdx = output.IndexOf("Name \"DepthOnly\"");
            int endHlsl = output.IndexOf("ENDHLSL", passIdx);
            string passContent = output.Substring(passIdx, endHlsl - passIdx);
            
            Assert.IsFalse(passContent.Contains("return color;"),
                "Forward's original return should be swapped out");
            Assert.IsTrue(passContent.Contains("return input.positionCS.z;"),
                "DepthOnly should return depth value using normalized 'input' name");
        }
        
        [Test]
        public void Test18_MetaUsesOutputPragmasWithNormalizedNames()
        {
            string output = ProcessTestShader("Test18_ForwardBodyInjection_CustomVarNames.shader");
            
            int passIdx = output.IndexOf("Name \"Meta\"");
            Assert.IsTrue(passIdx >= 0, "Missing Meta pass");
            
            int endHlsl = output.IndexOf("ENDHLSL", passIdx);
            string passContent = output.Substring(passIdx, endHlsl - passIdx);
            
            // Meta fragment should reference pragmas with normalized input name
            Assert.IsTrue(passContent.Contains("input.uv"),
                "Meta fragment should use normalized 'input' for UV access");
            Assert.IsTrue(passContent.Contains("albedo.rgb"),
                "Meta should reference 'albedo' from fragmentOutput pragma");
        }
        
        [Test]
        public void Test18_DepthNormalsUsesComputedNormal()
        {
            string output = ProcessTestShader("Test18_ForwardBodyInjection_CustomVarNames.shader");
            
            int passIdx = output.IndexOf("Name \"DepthNormals\"");
            int endHlsl = output.IndexOf("ENDHLSL", passIdx);
            string passContent = output.Substring(passIdx, endHlsl - passIdx);
            
            // Should use computed normal from pragma, with normalized input name
            Assert.IsTrue(passContent.Contains("NormalizeNormalPerPixel(computedNormal)"),
                "DepthNormals should use 'computedNormal' from fragmentOutput:normal pragma");
            Assert.IsTrue(passContent.Contains("input.tangentWS"),
                "DepthNormals fragment should use normalized 'input' name");
        }
        
        [Test]
        public void Test18_BoilerplateStrippedWithCustomNames()
        {
            string output = ProcessTestShader("Test18_ForwardBodyInjection_CustomVarNames.shader");
            
            int passIdx = output.IndexOf("Name \"DepthOnly\"");
            int vertIdx = output.IndexOf("DepthOnlyInterpolators DepthOnlyVertex(", passIdx);
            int braceStart = output.IndexOf('{', vertIdx);
            int depth = 1;
            int i = braceStart + 1;
            while (i < output.Length && depth > 0)
            {
                if (output[i] == '{') depth++;
                else if (output[i] == '}') depth--;
                i++;
            }
            string vertContent = output.Substring(vertIdx, i - vertIdx);
            
            // Instance ID setup should appear exactly once (from template, not injected)
            int setupCount = Regex.Matches(vertContent, @"UNITY_SETUP_INSTANCE_ID").Count;
            Assert.AreEqual(1, setupCount,
                "Vertex should have exactly one UNITY_SETUP_INSTANCE_ID (template's, not injected)");
        }
        
        [Test]
        public void Test18_NoUnreplacedTemplateMarkers()
        {
            string output = ProcessTestShader("Test18_ForwardBodyInjection_CustomVarNames.shader");
            var matches = Regex.Matches(output, @"\{\{[A-Z_]+\}\}");
            Assert.AreEqual(0, matches.Count,
                $"Found unreplaced template markers: {(matches.Count > 0 ? matches[0].Value : "")}");
        }
        
        //=============================================================================
        // Test 19: Shader Inheritance
        //=============================================================================
        
        // --- Parent standalone ---
        
        [Test]
        public void Test19_ParentCompilesStandalone()
        {
            // Parent has InheritHook markers that should be stripped
            string output = ProcessTestShader("Test19_InheritParent.shader");
            Assert.IsTrue(output.Contains("Name \"Forward\""), "Missing Forward pass");
            Assert.IsTrue(output.Contains("Name \"ExtraPass\""), "Missing ExtraPass");
        }
        
        [Test]
        public void Test19_ParentInheritHookMarkersStripped()
        {
            string output = ProcessTestShader("Test19_InheritParent.shader");
            Assert.IsFalse(output.Contains("{{InheritHook:"),
                "InheritHook markers should be stripped when compiling parent standalone");
        }
        
        // --- Minimal child (basic inheritance, no new features) ---
        
        [Test]
        public void Test19_MinimalChild_InheritsShaderName()
        {
            string output = ResolveTestShader("Test19_InheritChild_Minimal.shader");
            Assert.IsTrue(output.Contains("Shader \"Tests/19_InheritChild_Minimal\""),
                "Child shader name should replace parent name");
            Assert.IsFalse(output.Contains("Shader \"Tests/19_InheritParent\""),
                "Parent shader name should not remain");
        }
        
        [Test]
        public void Test19_MinimalChild_TagsMerged()
        {
            string output = ResolveTestShader("Test19_InheritChild_Minimal.shader");
            // Child adds Tessellation, parent has RenderType and RenderPipeline
            Assert.IsTrue(output.Contains("\"Tessellation\" = \"On\""),
                "Child tag 'Tessellation' should be merged");
            Assert.IsTrue(output.Contains("\"RenderType\" = \"Opaque\""),
                "Parent tag 'RenderType' should be preserved");
        }
        
        [Test]
        public void Test19_MinimalChild_InheritTagRemoved()
        {
            string output = ResolveTestShader("Test19_InheritChild_Minimal.shader");
            Assert.IsFalse(output.Contains("\"Inherit\""),
                "Inherit tag should not appear in resolved output");
        }
        
        [Test]
        public void Test19_MinimalChild_ParentPassesPresent()
        {
            string output = ResolveTestShader("Test19_InheritChild_Minimal.shader");
            Assert.IsTrue(output.Contains("Name \"Forward\""), "Missing inherited Forward pass");
            Assert.IsTrue(output.Contains("Name \"ExtraPass\""), "Missing inherited ExtraPass");
        }
        
        // --- Combined child (all features) ---
        
        [Test]
        public void Test19_PropertyOverride_DefaultChanged()
        {
            string output = ResolveTestShader("Test19_InheritChild.shader");
            // Child overrides _AlphaCutoff with Range(0.1, 0.9) = 0.3
            Assert.IsTrue(output.Contains("Range(0.1, 0.9)"),
                "Child should override _AlphaCutoff range");
            Assert.IsTrue(output.Contains("= 0.3"),
                "Child should override _AlphaCutoff default value");
        }
        
        [Test]
        public void Test19_PropertyAddition()
        {
            string output = ResolveTestShader("Test19_InheritChild.shader");
            Assert.IsTrue(output.Contains("_RimPower"),
                "Child property _RimPower should be added to Properties block");
        }
        
        [Test]
        public void Test19_PropertyPreserved()
        {
            string output = ResolveTestShader("Test19_InheritChild.shader");
            // Parent properties that child doesn't override should remain unchanged
            Assert.IsTrue(output.Contains("_Smoothness"),
                "Parent property _Smoothness should be preserved");
            Assert.IsTrue(output.Contains("_BaseMap"),
                "Parent property _BaseMap should be preserved");
        }
        
        [Test]
        public void Test19_PassExclusion()
        {
            string output = ResolveTestShader("Test19_InheritChild.shader");
            Assert.IsFalse(output.Contains("Name \"ExtraPass\""),
                "ExtraPass should be excluded");
            Assert.IsTrue(output.Contains("Name \"Forward\""),
                "Forward pass should still be present");
        }
        
        [Test]
        public void Test19_PassOverride_LightMode()
        {
            string output = ResolveTestShader("Test19_InheritChild.shader");
            // Child overrides Forward's LightMode to "VFXForward"
            // Find the Forward pass and check its tags
            int forwardIdx = output.IndexOf("Name \"Forward\"");
            Assert.IsTrue(forwardIdx >= 0, "Forward pass should exist");
            
            // Look at the Tags block near the Forward pass
            string afterForward = output.Substring(forwardIdx, 
                System.Math.Min(500, output.Length - forwardIdx));
            Assert.IsTrue(afterForward.Contains("\"VFXForward\""),
                "Forward pass should have overridden LightMode");
        }
        
        [Test]
        public void Test19_PassOverride_RenderState()
        {
            string output = ResolveTestShader("Test19_InheritChild.shader");
            // Child overrides Forward's Cull to Off
            // The parent has Cull Back at SubShader level, child overrides at pass level
            int forwardIdx = output.IndexOf("Name \"Forward\"");
            Assert.IsTrue(forwardIdx >= 0, "Forward pass should exist");
            
            // Find the pass's Cull directive (should be "Cull Off" from override)
            string passRegion = output.Substring(forwardIdx,
                System.Math.Min(500, output.Length - forwardIdx));
            Assert.IsTrue(passRegion.Contains("Cull Off"),
                "Forward pass should have Cull Off from child override");
        }
        
        [Test]
        public void Test19_NewPropertyAutoDeclaresInCBuffer()
        {
            string output = ResolveTestShader("Test19_InheritChild.shader");
            // _RimPower is a new child property (Range type), should auto-generate
            // a float declaration inside the parent's CBUFFER
            int cbufferEnd = output.IndexOf("CBUFFER_END");
            Assert.IsTrue(cbufferEnd >= 0, "CBUFFER_END should exist");
            
            int rimPowerInCbuffer = output.IndexOf("float _RimPower");
            Assert.IsTrue(rimPowerInCbuffer >= 0, "_RimPower should be auto-declared in CBUFFER");
            Assert.IsTrue(rimPowerInCbuffer < cbufferEnd,
                "_RimPower declaration should appear before CBUFFER_END");
        }
        
        [Test]
        public void Test19_HlslIncludeAppend()
        {
            string output = ResolveTestShader("Test19_InheritChild.shader");
            // Child's hook function should be appended to parent's HLSLINCLUDE
            Assert.IsTrue(output.Contains("void ApplyRim"),
                "Child hook function should be in merged output");
        }
        
        [Test]
        public void Test19_IntellisenseStubStripped()
        {
            string output = ResolveTestShader("Test19_InheritChild.shader");
            // Child may declare property variables in HLSLINCLUDE for IDE intellisense.
            // These must be stripped since they're auto-generated in the CBUFFER.
            // float _RimPower should appear once (in CBUFFER), not twice.
            int count = 0;
            int idx = 0;
            while ((idx = output.IndexOf("float _RimPower", idx)) >= 0)
            {
                count++;
                idx += "float _RimPower".Length;
            }
            Assert.AreEqual(1, count,
                $"float _RimPower should appear once (auto-generated in CBUFFER), found {count}");
        }
        
        [Test]
        public void Test19_InheritHookResolved()
        {
            string output = ResolveTestShader("Test19_InheritChild.shader");
            // The InheritHook marker should be replaced with a function call
            Assert.IsTrue(output.Contains("ApplyRim(col, input.normalWS);"),
                "InheritHook should resolve to function call with extracted args");
            Assert.IsFalse(output.Contains("{{InheritHook:"),
                "InheritHook markers should be fully resolved");
        }
        
        [Test]
        public void Test19_InheritHookPragmaStripped()
        {
            string output = ResolveTestShader("Test19_InheritChild.shader");
            Assert.IsFalse(output.Contains("#pragma InheritHook"),
                "InheritHook pragma should be stripped from appended HLSLINCLUDE");
        }
        
        [Test]
        public void Test19_OverriddenPropertyNotDuplicatedInCBuffer()
        {
            string output = ResolveTestShader("Test19_InheritChild.shader");
            // _AlphaCutoff is an override (exists in parent), should NOT get a second
            // declaration auto-generated. Parent already has float _AlphaCutoff.
            int cbufferStart = output.IndexOf("CBUFFER_START");
            int cbufferEnd = output.IndexOf("CBUFFER_END");
            string cbufferBlock = output.Substring(cbufferStart, cbufferEnd - cbufferStart);
            
            int count = 0;
            int idx = 0;
            while ((idx = cbufferBlock.IndexOf("_AlphaCutoff", idx)) >= 0)
            {
                count++;
                idx += "_AlphaCutoff".Length;
            }
            Assert.AreEqual(1, count,
                $"_AlphaCutoff should appear once in CBUFFER (parent's), not duplicated. Found {count}");
        }
        
        [Test]
        public void Test19_SubShaderTagMerge()
        {
            string output = ResolveTestShader("Test19_InheritChild.shader");
            Assert.IsTrue(output.Contains("\"Tessellation\" = \"On\""),
                "Tessellation tag from child should be merged");
            Assert.IsFalse(output.Contains("\"ExcludePasses\""),
                "ExcludePasses is a directive, should not appear as a tag");
        }

        // --- Full-pass child (Pass block with HLSLPROGRAM gets appended) ---

        [Test]
        public void Test19_FullPassChild_PassAppended()
        {
            string output = ResolveTestShader("Test19_InheritChild_FullPass.shader");

            Assert.IsTrue(output.Contains("Name \"ChildExtra\""),
                "Child full pass should be appended to the merged source");
            Assert.IsTrue(output.Contains("ChildVert"),
                "Child pass HLSL should be carried over intact");
        }

        [Test]
        public void Test19_FullPassChild_ParentPassesRetained()
        {
            string output = ResolveTestShader("Test19_InheritChild_FullPass.shader");

            Assert.IsTrue(output.Contains("Name \"Forward\""),
                "Parent Forward pass should remain");
            Assert.IsTrue(output.Contains("Name \"ExtraPass\""),
                "Parent ExtraPass should remain");
        }

        [Test]
        public void Test19_FullPassChild_PassInsideSubShader()
        {
            string output = ResolveTestShader("Test19_InheritChild_FullPass.shader");

            // The appended pass must land inside the SubShader block, and the pass
            // parser must be able to find it there.
            var blocks = ShaderBlockUtility.FindAllPassBlocks(output);
            bool found = false;
            foreach (var block in blocks)
            {
                if (block.Content.Contains("\"ChildExtra\""))
                {
                    found = true;
                    break;
                }
            }
            Assert.IsTrue(found, "ChildExtra should parse as a proper Pass block in the merged source");
        }
        
        //=============================================================================
        // Test 20: Parsing Utilities
        //=============================================================================
        
        [Test]
        public void Test20_FindAllPassBlocks_SkipsCommentedPasses()
        {
            string source = @"
SubShader
{
    Pass { Name ""Active"" }
    // Pass { Name ""LineCommented"" }
    /* Pass { Name ""BlockCommented"" } */
    Pass { Name ""AlsoActive"" }
}";
            var blocks = ShaderBlockUtility.FindAllPassBlocks(source);
            Assert.AreEqual(2, blocks.Count,
                "Should find 2 uncommented passes");
            Assert.IsTrue(blocks[0].Content.Contains("Active"),
                "First pass should be 'Active'");
            Assert.IsTrue(blocks[1].Content.Contains("AlsoActive"),
                "Second pass should be 'AlsoActive'");
        }
        
        [Test]
        public void Test20_FindAllPassBlocks_HandlesBlockCommentBeforeBrace()
        {
            // This is the edge case the old simple Pass\s*\{ pattern would miss.
            // The comment-aware pattern handles comments between "Pass" and "{".
            string source = @"
SubShader
{
    Pass /* some note */
    {
        Name ""Forward""
        Tags { ""LightMode"" = ""UniversalForward"" }
    }
}";
            var blocks = ShaderBlockUtility.FindAllPassBlocks(source);
            Assert.AreEqual(1, blocks.Count,
                "Should find the pass despite block comment before opening brace");
            Assert.IsTrue(blocks[0].Content.Contains("Forward"),
                "Pass content should contain the Name tag");
        }
        
        [Test]
        public void Test20_FindAllPassBlocks_HandlesLineCommentBeforeBrace()
        {
            string source = @"
SubShader
{
    Pass // this is fine
    {
        Name ""DepthOnly""
    }
}";
            var blocks = ShaderBlockUtility.FindAllPassBlocks(source);
            Assert.AreEqual(1, blocks.Count,
                "Should find the pass despite line comment before opening brace");
            Assert.IsTrue(blocks[0].Content.Contains("DepthOnly"),
                "Pass content should contain the Name tag");
        }
        
        [Test]
        public void Test20_ParseTagPairs_ExtractsKeyValuePairs()
        {
            string tags = @"""RenderPipeline"" = ""UniversalPipeline"" ""ShaderGen"" = ""True"" ""LightMode"" = ""UniversalForward""";
            var result = ShaderBlockUtility.ParseTagPairs(tags);
            
            Assert.AreEqual(3, result.Count, "Should parse 3 tag pairs");
            Assert.AreEqual("UniversalPipeline", result["RenderPipeline"]);
            Assert.AreEqual("True", result["ShaderGen"]);
            Assert.AreEqual("UniversalForward", result["LightMode"]);
        }
        
        [Test]
        public void Test20_ParseTagPairs_IsCaseInsensitive()
        {
            string tags = @"""LightMode"" = ""UniversalForward""";
            var result = ShaderBlockUtility.ParseTagPairs(tags);
            
            Assert.AreEqual("UniversalForward", result["lightmode"],
                "Tag lookup should be case-insensitive");
        }
        
        [Test]
        public void Test20_CollapseBlankLines_CollapsesEmptyLines()
        {
            string input = "line1\n\n\n\nline2\n\n\n\n\nline3";
            string result = ShaderSourceUtility.CollapseBlankLines(input);
            
            Assert.IsFalse(result.Contains("\n\n\n"),
                "Should not have 3+ consecutive newlines after collapse");
            Assert.IsTrue(result.Contains("line1\n\nline2"),
                "Should preserve single blank line between content");
        }
        
        [Test]
        public void Test20_CollapseBlankLines_CollapsesWhitespaceOnlyLines()
        {
            string input = "line1\n   \n   \nline2";
            string result = ShaderSourceUtility.CollapseBlankLines(input);
            
            Assert.AreEqual("line1\n\nline2", result,
                "Whitespace-only blank lines should collapse to a single blank line");
        }
        
        [Test]
        public void Test20_FindFunction_HandlesSemantic()
        {
            string source = @"
half4 frag(Interpolators input) : SV_Target
{
    return half4(1, 0, 0, 1);
}";
            var info = ShaderFunctionUtility.FindFunction(source, "frag");
            Assert.IsNotNull(info, "Should find function with output semantic");
            Assert.AreEqual("SV_Target", info.Value.Semantic);
            Assert.AreEqual("half4", info.Value.ReturnType);
        }
        
        [Test]
        public void Test20_FindFunction_HandlesNestedParens()
        {
            string source = @"
void myFunc(float a, float b = max(0, 1))
{
    // body
}";
            var info = ShaderFunctionUtility.FindFunction(source, "myFunc");
            Assert.IsNotNull(info, "Should find function with nested parens in default value");
            
            var parameters = ShaderFunctionUtility.ParseParameters(info.Value.ParameterList);
            Assert.AreEqual(2, parameters.Count,
                "Should parse 2 parameters (not 3 from the comma inside max())");
        }
        
        [Test]
        public void Test20_FindLongestFunction_PicksRealImplementation()
        {
            string source = @"
#if FEATURE_DISABLED
float4 frag(Interpolators input) : SV_Target { discard; return 0; }
#else
float4 frag(Interpolators input) : SV_Target
{
    // This is the real implementation with more code
    float4 color = float4(1, 0, 0, 1);
    color.rgb *= _Tint.rgb;
    return color;
}
#endif";
            var info = ShaderFunctionUtility.FindLongestFunction(source, "frag");
            Assert.IsNotNull(info, "Should find the function");
            Assert.IsTrue(info.Value.Body.Contains("_Tint"),
                "Should pick the longer (real) implementation, not the stub");
        }

        [Test]
        public void Test20_PropertyExists_DoesNotMatchInsideLongerName()
        {
            var ctx = new ShaderContext
            {
                PropertiesBlock = "_TessellationFactor(\"Factor\", Range(1, 64)) = 16"
            };

            Assert.IsFalse(ctx.PropertyExists("_Tessellation"),
                "_Tessellation must not match inside _TessellationFactor");
            Assert.IsTrue(ctx.PropertyExists("_TessellationFactor"),
                "Exact property name should match");
        }

        [Test]
        public void Test20_CBufferEntryExists_DoesNotMatchInsideLongerName()
        {
            var ctx = new ShaderContext
            {
                CBufferContent = "float _TessellationFactor;"
            };

            Assert.IsFalse(ctx.CBufferEntryExists("_Tessellation"),
                "_Tessellation must not match inside _TessellationFactor");
            Assert.IsTrue(ctx.CBufferEntryExists("_TessellationFactor"),
                "Exact entry name should match");
        }

        [Test]
        public void Test20_PragmaGetValue_SkipsCommentedPragmas()
        {
            string source =
                "// #pragma vertexDisplacement CommentedOut\n" +
                "#pragma vertexDisplacement RealFunc\n";

            Assert.AreEqual("RealFunc", ShaderPragmaUtility.GetValue(source, "vertexDisplacement"),
                "Commented pragma should be skipped in favor of the real one");

            string onlyCommented =
                "// #pragma vertexDisplacement CommentedOut\n" +
                "/* #pragma alphaClip AlsoOut */\n";

            Assert.IsNull(ShaderPragmaUtility.GetValue(onlyCommented, "vertexDisplacement"),
                "Line-commented pragma must not activate a hook");
            Assert.IsNull(ShaderPragmaUtility.GetValue(onlyCommented, "alphaClip"),
                "Block-commented pragma must not activate a hook");
        }

        [Test]
        public void Test20_FragmentOutputs_SkipCommentedPragmas()
        {
            string source =
                "// #pragma fragmentOutput:normal oldNormal\n" +
                "#pragma fragmentOutput:albedo myAlbedo\n";

            var outputs = ShaderPragmaUtility.ParseFragmentOutputs(source);

            Assert.IsFalse(outputs.ContainsKey("normal"),
                "Commented fragmentOutput pragma must be ignored");
            Assert.AreEqual("myAlbedo", outputs["albedo"]);
        }

        [Test]
        public void Test20_InsertBeforeCBufferEnd_TargetsUnityPerMaterialOnly()
        {
            string source =
                "CBUFFER_START(OtherBuffer)\nfloat _Other;\nCBUFFER_END\n" +
                "CBUFFER_START(UnityPerMaterial)\nfloat _Base;\nCBUFFER_END\n";

            string result = ShaderBlockUtility.InsertBeforeCBufferEnd(source, "float _Injected;\n");

            Assert.IsNotNull(result, "UnityPerMaterial block exists, insertion should succeed");

            // The first CBUFFER_END in the result belongs to OtherBuffer. The injected
            // entry must appear after it (inside the UnityPerMaterial block instead).
            int otherEnd = result.IndexOf("CBUFFER_END");
            int injected = result.IndexOf("_Injected");
            Assert.IsTrue(injected > otherEnd,
                "Entries must go into the UnityPerMaterial block, not the first CBUFFER in the file");
        }

        [Test]
        public void Test20_InsertBeforeCBufferEnd_InsertsIntoEveryUnityPerMaterialBlock()
        {
            // Pass-local CBUFFERs must stay identical across passes for the SRP batcher,
            // so processor entries go into every UnityPerMaterial block.
            string source =
                "CBUFFER_START(UnityPerMaterial)\nfloat _A;\nCBUFFER_END\n" +
                "CBUFFER_START(UnityPerMaterial)\nfloat _B;\nCBUFFER_END\n";

            string result = ShaderBlockUtility.InsertBeforeCBufferEnd(source, "float _Injected;\n");

            Assert.AreEqual(2, CountOccurrences(result, "_Injected"),
                "Both UnityPerMaterial blocks should receive the entries");
        }

        [Test]
        public void Test20_IsInComment_SlashStarInsideLineCommentDoesNotOpenBlock()
        {
            // "// see Assets/Shaders/* for examples" contains a /* with no later */.
            // Treating it as a live block opener makes everything below the line read
            // as commented out, which at the importer activation callsite silently
            // unhooks the shader from the pipeline.
            string source =
                "// see Assets/Shaders/* for examples\n" +
                "\"ShaderGen\" = \"True\"\n";

            int tagIdx = source.IndexOf("\"ShaderGen\"");
            Assert.IsFalse(ShaderBlockUtility.IsInComment(source, tagIdx),
                "A /* inside a // line comment must not open a block comment");
        }

        [Test]
        public void Test20_IsInComment_RealBlockCommentsStillDetected()
        {
            string source = "/* open\ninside\n*/ after";

            Assert.IsTrue(ShaderBlockUtility.IsInComment(source, source.IndexOf("inside")),
                "Position inside an open block comment should be detected");
            Assert.IsFalse(ShaderBlockUtility.IsInComment(source, source.IndexOf("after")),
                "Position after a closed block comment should not be flagged");

            string unclosed = "// dead /*\n/* live opener\ntarget";
            Assert.IsTrue(ShaderBlockUtility.IsInComment(unclosed, unclosed.IndexOf("target")),
                "A live unclosed opener after a dead one should still comment out what follows");
        }

        //=============================================================================
        // Test 21: Include Resolution
        //=============================================================================
        
        [Test]
        public void Test21_IncludeResolution_GeneratesAllPasses()
        {
            string output = ProcessTestShader("Test21_IncludeResolution.shader");
            
            Assert.IsTrue(output.Contains("Name \"ShadowCaster\""), "Missing ShadowCaster pass");
            Assert.IsTrue(output.Contains("Name \"DepthOnly\""), "Missing DepthOnly pass");
            Assert.IsTrue(output.Contains("Name \"DepthNormals\""), "Missing DepthNormals pass");
            Assert.IsTrue(output.Contains("Name \"MotionVectors\""), "Missing MotionVectors pass");
            Assert.IsTrue(output.Contains("Name \"Meta\""), "Missing Meta pass");
        }
        
        [Test]
        public void Test21_IncludeResolution_StructNamesDetected()
        {
            // Structs are in Test21_SharedInclude.hlsl, not in the shader file.
            // If include resolution works, the pipeline finds them and generates
            // per-pass struct variants.
            string output = ProcessTestShader("Test21_IncludeResolution.shader");
            
            Assert.IsTrue(output.Contains("struct ShadowCasterAttributes"),
                "Missing ShadowCasterAttributes (struct detection from include failed)");
            Assert.IsTrue(output.Contains("struct DepthOnlyAttributes"),
                "Missing DepthOnlyAttributes (struct detection from include failed)");
        }
        
        [Test]
        public void Test21_IncludeResolution_CBufferNotDuplicated()
        {
            // CBUFFER is in Test21_SharedInclude.hlsl, referenced via #include.
            // The output keeps the #include line (not inlined), so CBUFFER_START
            // doesn't appear in the output text at all. The key test is that the
            // resolver detected CBufferInHlslInclude = true, so generated passes
            // have empty Material Data sections (no CBUFFER duplication).
            string output = ProcessTestShader("Test21_IncludeResolution.shader");
            
            int cbufferCount = CountOccurrences(output, "CBUFFER_START(UnityPerMaterial)");
            Assert.AreEqual(0, cbufferCount,
                "CBUFFER is inside the include file, should not appear in output text. " +
                "Generated passes should have empty Material Data (CBUFFER shared via HLSLINCLUDE include).");
        }
        
        [Test]
        public void Test21_IncludeResolution_TexturesNotDuplicated()
        {
            // Textures are in Test21_SharedInclude.hlsl, referenced via #include.
            // Same as CBUFFER: the resolver detected TexturesInHlslInclude = true,
            // so generated passes don't emit their own texture declarations.
            string output = ProcessTestShader("Test21_IncludeResolution.shader");
            
            int texCount = CountOccurrences(output, "TEXTURE2D(_BaseMap)");
            Assert.AreEqual(0, texCount,
                "Textures are inside the include file, should not appear in output text. " +
                "Generated passes should have empty Material Data (textures shared via HLSLINCLUDE include).");
        }
        
        [Test]
        public void Test21_IncludeResolution_HookFromInclude_GeneratesPasses()
        {
            string output = ProcessTestShader("Test21_IncludeResolution_Hook.shader");
            
            Assert.IsTrue(output.Contains("Name \"ShadowCaster\""), "Missing ShadowCaster pass");
            Assert.IsTrue(output.Contains("Name \"DepthOnly\""), "Missing DepthOnly pass");
        }
        
        [Test]
        public void Test21_IncludeResolution_HookFromInclude_FunctionPrefixed()
        {
            // The displaceVertex function is in Test21_SharedHook.hlsl.
            // If include resolution works, the pipeline finds the function body,
            // registers it as a vertexDisplacement hook, and prefixes it per pass.
            string output = ProcessTestShader("Test21_IncludeResolution_Hook.shader");
            
            Assert.IsTrue(output.Contains("ShadowCasterdisplaceVertex"),
                "Hook function from include should be prefixed in ShadowCaster pass");
            Assert.IsTrue(output.Contains("DepthOnlydisplaceVertex"),
                "Hook function from include should be prefixed in DepthOnly pass");
        }
        
        [Test]
        public void Test21_IncludeResolution_HookFromInclude_StructsRewritten()
        {
            string output = ProcessTestShader("Test21_IncludeResolution_Hook.shader");
            
            // The hook function takes "inout Attributes input". In generated passes,
            // "Attributes" should be rewritten to the pass-specific name.
            Assert.IsTrue(output.Contains("inout ShadowCasterAttributes"),
                "Hook function struct param should be rewritten in ShadowCaster");
            Assert.IsTrue(output.Contains("inout DepthOnlyAttributes"),
                "Hook function struct param should be rewritten in DepthOnly");
        }
        
        [Test]
        public void Test21_IncludeResolver_SkipsUnityPackageIncludes()
        {
            // Unit test: the default filter skips Packages/com.unity.* includes
            string source =
                "#include \"Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl\"\n" +
                "float _MyValue;";
            
            string resolved = ShaderIncludeResolver.Resolve(source, "");
            
            Assert.IsTrue(resolved.Contains(
                "#include \"Packages/com.unity.render-pipelines.universal"),
                "URP includes should be left as-is");
            Assert.IsTrue(resolved.Contains("_MyValue"),
                "Non-include content should be preserved");
        }
        
        [Test]
        public void Test21_IncludeResolver_NullAndEmptySource()
        {
            Assert.IsNull(ShaderIncludeResolver.Resolve(null, ""),
                "Null source should return null");
            Assert.AreEqual("", ShaderIncludeResolver.Resolve("", ""),
                "Empty source should return empty");
        }
        
        [Test]
        public void Test21_BuildSearchSource_PrefersResolvedOverRaw()
        {
            var ctx = new ShaderContext
            {
                HlslIncludeBlock = "float _RawOnly;",
                ResolvedHlslInclude = "float _RawOnly;\nfloat _FromInclude;"
            };
            
            string search = ctx.BuildSearchSource("float _PassVar;");
            
            Assert.IsTrue(search.Contains("_FromInclude"),
                "Should use ResolvedHlslInclude which has the include content");
            Assert.IsTrue(search.Contains("_PassVar"),
                "Should append pass HLSL");
        }
        
        [Test]
        public void Test21_BuildSearchSource_FallsBackToRawWhenNoResolved()
        {
            var ctx = new ShaderContext
            {
                HlslIncludeBlock = "float _RawVar;",
                ResolvedHlslInclude = null
            };
            
            string search = ctx.BuildSearchSource();
            
            Assert.IsTrue(search.Contains("_RawVar"),
                "Should fall back to HlslIncludeBlock when ResolvedHlslInclude is null");
        }

        //=============================================================================
        // Test 22: Conditional and Multi-Line Defines
        //
        // Defines get hoisted above the CBUFFER/structs in generated passes. They
        // have to keep the #ifdef/#else they were written under, and a define
        // spanning several lines has to move as one unit. The compile test covers
        // the end result; these pin down where the lines land.
        //=============================================================================

        /// <summary>
        /// The part of a generated pass above its Attributes struct, where the
        /// hoisted preprocessor lines go. Test22 keeps CBUFFER and textures in
        /// HLSLINCLUDE, so nothing else sits between them.
        /// </summary>
        static string GetHoistedRegion(string passContent, string passName)
        {
            int structIdx = passContent.IndexOf($"struct {passName}Attributes");
            Assert.Greater(structIdx, 0, $"{passName} pass has no {passName}Attributes struct");
            return passContent.Substring(0, structIdx);
        }

        [Test]
        public void Test22_HoistedDefineKeepsGuard()
        {
            string output = ProcessTestShader("Test22_ConditionalDefines.shader");
            string hoisted = GetHoistedRegion(GetPassContent(output, "DepthOnly"), "DepthOnly");

            // Each search starts where the previous one landed. The template has its own
            // conditional blocks above the hoisted lines (the LOD crossfade include), and
            // their #else/#endif must not be mistaken for this block's.
            int ifdefIdx = hoisted.IndexOf("#ifdef FS_TEST_GUARD");
            Assert.GreaterOrEqual(ifdefIdx, 0, "#ifdef FS_TEST_GUARD should be mirrored above the structs");

            int onDefineIdx = hoisted.IndexOf("#define TEST_SCALE(x) ( \\", ifdefIdx);
            int elseIdx = onDefineIdx >= 0 ? hoisted.IndexOf("#else", onDefineIdx) : -1;
            int offDefineIdx = elseIdx >= 0 ? hoisted.IndexOf("test_else_branch_must_not_compile", elseIdx) : -1;
            int endifIdx = offDefineIdx >= 0 ? hoisted.IndexOf("#endif", offDefineIdx) : -1;

            Assert.Greater(onDefineIdx, ifdefIdx, "The 'on' TEST_SCALE should sit inside #ifdef FS_TEST_GUARD");
            Assert.Greater(elseIdx, onDefineIdx, "#else should follow the 'on' TEST_SCALE");
            Assert.Greater(offDefineIdx, elseIdx, "The 'off' TEST_SCALE should sit inside the #else");
            Assert.Greater(endifIdx, offDefineIdx, "#endif should close the block after the 'off' TEST_SCALE");
        }

        [Test]
        public void Test22_MultiLineDefineStaysTogether()
        {
            string output = ProcessTestShader("Test22_ConditionalDefines.shader");
            string passContent = GetPassContent(output, "DepthOnly");
            string hoisted = GetHoistedRegion(passContent, "DepthOnly");

            // "TestGuardHelper(x) * 2.0" only appears on the define's continuation line
            // (the helper's own declaration reads "TestGuardHelper(float x)")
            Assert.AreEqual(1, CountOccurrences(passContent, "TestGuardHelper(x) * 2.0"),
                "The define's continuation line should appear exactly once in the pass");
            Assert.IsTrue(hoisted.Contains("TestGuardHelper(x) * 2.0"),
                "The define's continuation line should be hoisted with its #define");
            Assert.IsTrue(hoisted.Contains("+ 0.0)"),
                "The define's last line should be hoisted with its #define");
            Assert.IsFalse(hoisted.Contains("\\\r"),
                "A '\\' followed by a carriage return may not splice; continuations should end in '\\' + newline");
        }

        [Test]
        public void Test22_GuardedHelperStaysInCode()
        {
            string output = ProcessTestShader("Test22_ConditionalDefines.shader");
            string passContent = GetPassContent(output, "DepthOnly");
            string hoisted = GetHoistedRegion(passContent, "DepthOnly");

            int helperIdx = passContent.IndexOf("float TestGuardHelper(float x)");
            Assert.Greater(helperIdx, hoisted.Length,
                "The guarded helper function is code and should stay below the structs");

            // Its own #ifdef must come along so it's still conditional down there
            int codeIfdefIdx = passContent.LastIndexOf("#ifdef FS_TEST_GUARD", helperIdx);
            Assert.Greater(codeIfdefIdx, hoisted.Length,
                "The helper's #ifdef FS_TEST_GUARD should also be kept in the code section");
        }

        //=============================================================================
        // Test 23: Generated Passes Cull Like the Forward Pass
        //
        // ShadowCaster, DepthOnly, DepthNormals and MotionVectors copy the reference
        // pass's Cull line through {{CULL}}. With no Cull line on the reference pass
        // they get none, and inherit the SubShader's state like the forward pass does.
        //=============================================================================

        static readonly string[] s_cullFollowingPasses = { "ShadowCaster", "DepthOnly", "DepthNormals", "MotionVectors" };

        [Test]
        public void Test23_GeneratedPassesCopyForwardCull()
        {
            string output = ProcessTestShader("Test23_CullFromForward.shader");

            foreach (string passName in s_cullFollowingPasses)
            {
                string passContent = GetPassContent(output, passName);
                Assert.IsTrue(passContent.Contains("Cull [_Cull]"),
                    $"{passName} should copy the forward pass's 'Cull [_Cull]'");
                Assert.IsFalse(Regex.IsMatch(passContent, @"(?m)^\s*Cull Back"),
                    $"{passName} should not hard-code Cull Back");
            }
        }

        [Test]
        public void Test23_MetaKeepsCullOff()
        {
            // Meta's culling is part of the technique (lightmap UV space), not copied
            string output = ProcessTestShader("Test23_CullFromForward.shader");
            Assert.IsTrue(GetPassContent(output, "Meta").Contains("Cull Off"),
                "Meta should keep its own Cull Off");
        }

        [Test]
        public void Test01_NoForwardCullMeansNoGeneratedCull()
        {
            // Test01's forward pass has no Cull line, so the generated passes must not
            // add one: they inherit the SubShader's state, as the forward pass does
            string output = ProcessTestShader("Test01_StructsInHLSLINCLUDE.shader");

            foreach (string passName in s_cullFollowingPasses)
            {
                Assert.IsFalse(Regex.IsMatch(GetPassContent(output, passName), @"(?m)^\s*Cull\b"),
                    $"{passName} should have no Cull line when the forward pass has none");
            }
        }

        //=============================================================================
        // Generated pass details that follow URP 17
        //=============================================================================

        [Test]
        public void Test01_ShadowCasterAppliesShadowClamping()
        {
            // URP's ShadowCasterPass clamps the biased clip position so casters behind
            // the near plane still land in the shadow map (directional lights)
            string output = ProcessTestShader("Test01_StructsInHLSLINCLUDE.shader");
            Assert.IsTrue(GetPassContent(output, "ShadowCaster").Contains("ApplyShadowClamping("),
                "ShadowCaster should apply ApplyShadowClamping like URP's ShadowCasterPass");
        }

        [Test]
        public void Test01_MotionVectorsUsesShaderFeatureForPrecomputedVelocity()
        {
            // Matches URP Lit. multi_compile would build the Alembic variant for every
            // shader whether a material uses it or not. The pragma lives in the keyword
            // file the template includes, so check both halves.
            string output = ProcessTestShader("Test01_StructsInHLSLINCLUDE.shader");
            string passContent = GetPassContent(output, "MotionVectors");

            Assert.IsTrue(passContent.Contains("#include_with_pragmas \"Packages/com.abdulal.shaderprocessor/Shaders/Keywords/MotionVectorsKeywords.hlsl\""),
                "MotionVectors should include its keyword file");

            string keywordsPath = Path.Combine(Path.GetDirectoryName(FindTestsDirectory()),
                "Shaders", "Keywords", "MotionVectorsKeywords.hlsl");
            string keywords = File.ReadAllText(keywordsPath);
            Assert.IsTrue(keywords.Contains("#pragma shader_feature_local_vertex _ADD_PRECOMPUTED_VELOCITY"),
                "MotionVectorsKeywords.hlsl should declare _ADD_PRECOMPUTED_VELOCITY with shader_feature_local_vertex");
            Assert.IsFalse(keywords.Contains("multi_compile _ _ADD_PRECOMPUTED_VELOCITY"),
                "MotionVectorsKeywords.hlsl should not multi_compile _ADD_PRECOMPUTED_VELOCITY");
        }

        //=============================================================================
        // Small fixes: tag case, template marker cleanup
        //=============================================================================

        [Test]
        public void Test11_SubShaderShaderGenTagIsCaseInsensitive()
        {
            // The importer activates on any casing of the tag, so the pipeline must
            // see it too. Test11 has ShaderGen at SubShader level only, which is the
            // lookup that used to be case-sensitive.
            string testsDir = FindTestsDirectory();
            string path = Path.Combine(testsDir, "Test11_ShaderGenSubShaderLevel.shader");
            string source = File.ReadAllText(path);
            Assert.IsTrue(source.Contains("\"ShaderGen\""), "Test11 should declare a ShaderGen tag");

            string output = _processor.Process(source.Replace("\"ShaderGen\"", "\"shadergen\""), path);

            Assert.IsTrue(output.Contains("Name \"DepthNormals\""),
                "A lowercase SubShader-level shadergen tag should still generate passes");
        }

        [Test]
        public void TemplateEngine_MarkerCleanupKeepsBraceInitializers()
        {
            // Unreplaced markers are stripped after substitution, when replacement
            // values (user code) are already in the text. Only the marker shape may go.
            var replacements = new System.Collections.Generic.Dictionary<string, string> { ["X"] = "x" };
            string result = TemplateEngine.Process("a {{X}} {{Y}} {{1,2}}", replacements);

            Assert.AreEqual("a x  {{1,2}}", result,
                "Only unreplaced {{MARKER}} names should be stripped, not brace initializers");
        }

        //=============================================================================
        // Test 24: Child Property Declarations Reach Every Material CBUFFER
        //=============================================================================

        [Test]
        public void Test24_NewPropertiesDeclaredInEveryMaterialCBuffer()
        {
            string merged = ResolveTestShader("Test24_InheritChild_PassCBuffers.shader");

            // One per pass-local UnityPerMaterial block
            Assert.AreEqual(2, CountOccurrences(merged, "float _Test24Scale;"),
                "The new scalar should be declared in both UnityPerMaterial CBUFFERs");
            Assert.AreEqual(2, CountOccurrences(merged, "float4 _Test24Tex_ST;"),
                "The new texture's _ST should be declared in both UnityPerMaterial CBUFFERs");
            Assert.AreEqual(2, CountOccurrences(merged, "TEXTURE2D(_Test24Tex);"),
                "The new texture should be declared after both UnityPerMaterial CBUFFERs");
            Assert.AreEqual(2, CountOccurrences(merged, "SAMPLER(sampler_Test24Tex);"),
                "The new sampler should be declared after both UnityPerMaterial CBUFFERs");
        }

        [Test]
        public void Test24_OtherCBuffersAndCommentsUntouched()
        {
            string merged = ResolveTestShader("Test24_InheritChild_PassCBuffers.shader");

            // The trap CBUFFER in HLSLINCLUDE comes before both material blocks
            int otherStart = merged.IndexOf("CBUFFER_START(Test24Other)");
            Assert.Greater(otherStart, 0, "Test24Other CBUFFER should still be there");
            int otherEnd = merged.IndexOf("CBUFFER_END", otherStart);
            string otherBlock = merged.Substring(otherStart, otherEnd - otherStart);
            Assert.IsFalse(otherBlock.Contains("_Test24Scale"),
                "A CBUFFER that isn't UnityPerMaterial must not receive the child's entries");

            // The header comment mentions CBUFFER_START(UnityPerMaterial) and CBUFFER_END
            int shaderKeyword = merged.IndexOf("Shader \"Tests/24_InheritChild_PassCBuffers\"");
            Assert.Greater(shaderKeyword, 0, "Merged shader should carry the child's name");
            Assert.IsFalse(merged.Substring(0, shaderKeyword).Contains("_Test24"),
                "Nothing should be inserted into the header comment");
        }

        //=============================================================================
        // Test 25: A Child's "../" Include Resolves From the Child's Folder
        //=============================================================================

        const string TEST25_CHILD = "Test25_Sub/Test25_InheritChild_RelInclude.shader";

        [Test]
        public void Test25_ChildRelativeIncludeUsesChildFolder()
        {
            string merged = ResolveTestShader(TEST25_CHILD);

            Assert.IsTrue(merged.Contains("#include \"Packages/com.abdulal.shaderprocessor/Tests/Test25_ChildInclude.hlsl\""),
                "The child's ../ include should resolve against the child's folder (Tests/Test25_Sub)");
            Assert.IsFalse(merged.Contains("../Test25_ChildInclude.hlsl"),
                "No relative include should be left in the merged source");
        }

        [Test]
        public void Test25_ChildWithRelativeIncludeCompiles()
        {
            // Not covered by the enumerated compile tests, which only look in Tests/ itself
            string output = ResolveAndProcessTestShader(TEST25_CHILD);
            AssertCompiles(output, TEST25_CHILD);
        }

        //=============================================================================
        // Test 26: Pass-Level Includes
        //
        // Lookups (structs, vertex/fragment functions, hooks, extra fragment params)
        // see files a pass #includes inside HLSLPROGRAM, not just HLSLINCLUDE ones.
        // Both shaders are also covered by the enumerated compile test: their includes
        // use package paths, which CreateShaderAsset can resolve.
        //=============================================================================

        [Test]
        public void Test26_StructsFromPassIncludeUsedForGeneratedStructs()
        {
            string output = ProcessTestShader("Test26_PassIncludeStructs.shader");
            string depthOnly = GetPassContent(output, "DepthOnly");

            // positionWS only exists in the include's Interpolators, not in the
            // fallback struct the generator falls back to when it finds nothing
            Assert.IsTrue(depthOnly.Contains("struct DepthOnlyInterpolators"),
                "DepthOnly should generate its interpolators struct");
            Assert.IsTrue(depthOnly.Contains("positionWS : TEXCOORD1"),
                "Generated interpolators should copy the fields of the included Interpolators struct");
        }

        [Test]
        public void Test26_MaterialDataFromPassIncludeNotEmittedAgain()
        {
            // The CBUFFER and textures live in the included file. Generated passes get
            // the forward pass's #include lines, so emitting them as well would declare
            // UnityPerMaterial and _BaseMap twice.
            string output = ProcessTestShader("Test26_PassIncludeStructs.shader");

            Assert.AreEqual(0, CountOccurrences(output, "CBUFFER_START(UnityPerMaterial)"),
                "No pass should emit its own UnityPerMaterial CBUFFER");
            Assert.AreEqual(0, CountOccurrences(output, "TEXTURE2D(_BaseMap)"),
                "No pass should emit its own _BaseMap declaration");
        }

        [Test]
        public void Test26_HookFunctionFromPassIncludeIsCalled()
        {
            string output = ProcessTestShader("Test26_PassIncludeFunctions.shader");

            Assert.IsTrue(GetPassContent(output, "DepthOnly").Contains("DepthOnlyTest26Displace("),
                "DepthOnly should call its prefixed copy of the included hook");
            Assert.IsTrue(GetPassContent(output, "ShadowCaster").Contains("ShadowCasterTest26Displace("),
                "ShadowCaster should call its prefixed copy of the included hook");
        }

        [Test]
        public void Test26_ForwardBodiesFromPassIncludeAreInjected()
        {
            string depthOnly = GetPassContent(
                ProcessTestShader("Test26_PassIncludeFunctions.shader"), "DepthOnly");

            Assert.IsTrue(depthOnly.Contains("output.positionWS = TransformObjectToWorld"),
                "The included vertex function's body should be injected into DepthOnlyVertex");
            Assert.IsTrue(depthOnly.Contains("faceShade"),
                "The included fragment function's body should be injected into DepthOnlyFragment");
        }

        [Test]
        public void Test26_ExtraFragmentParamFromPassIncludeIsKept()
        {
            string depthOnly = GetPassContent(
                ProcessTestShader("Test26_PassIncludeFunctions.shader"), "DepthOnly");

            Assert.IsTrue(depthOnly.Contains("input, bool isFrontFace : SV_IsFrontFace)"),
                "DepthOnlyFragment should take the SV_IsFrontFace parameter the injected body reads");
        }

        [Test]
        public void Test26_PassIncludeSkipsFilesHlslIncludeAlreadyInlined()
        {
            const string sharedInclude =
                "#include \"Packages/com.abdulal.shaderprocessor/Tests/Test21_SharedInclude.hlsl\"";
            string source =
                "Shader \"Tests/26_Unit\" { SubShader { Tags { \"ShaderGen\" = \"True\" }\n" +
                "HLSLINCLUDE\n" + sharedInclude + "\nENDHLSL\n" +
                "Pass { Name \"Forward\" Tags { \"LightMode\" = \"UniversalForward\" }\n" +
                "HLSLPROGRAM\n" +
                "#pragma vertex vert\n" +
                "#pragma fragment frag\n" +
                sharedInclude + "\n" +
                "#include \"Packages/com.abdulal.shaderprocessor/Tests/Test26_PassFunctions.hlsl\"\n" +
                "ENDHLSL } } }";

            var ctx = new ShaderContext { ProcessedSource = source, ShaderPath = "", ShaderDirectory = "" };
            ShaderParser.Parse(ctx);

            Assert.AreEqual(1, ctx.Passes.Count, "Expected the one authored pass");
            PassInfo pass = ctx.Passes[0];
            string search = ctx.BuildSearchSource(pass);

            Assert.AreEqual(1, CountOccurrences(search, "struct Attributes"),
                "A file the HLSLINCLUDE already inlined shouldn't be inlined again for the pass");
            Assert.IsTrue(search.Contains("void Test26Displace"),
                "A file only the pass includes should be inlined into its search source");
            Assert.IsFalse(pass.HlslProgram.Contains("void Test26Displace"),
                "The pass's own program text should keep its #include lines as written");
        }

        //=============================================================================
        // Compilation: every test shader must produce compilable output
        //=============================================================================

        /// <summary>
        /// Enumerate test shaders for the standalone compile test, so new test shaders
        /// are covered automatically instead of via a hand-maintained list.
        ///
        /// Exclusions:
        /// - Test21 shaders use relative #include paths that can't resolve via
        ///   ShaderUtil.CreateShaderAsset (no file context). They compile correctly
        ///   when imported from disk via FSShaderImporter.
        /// - InheritChild shaders need the inheritance pre-pass first; they're covered
        ///   by InheritanceChildShaders_CompileWithoutErrors below.
        /// </summary>
        static string[] CompilableTestShaders()
        {
            string testsDir = FindTestsDirectory();
            if (testsDir == null) return new string[0];

            var results = new System.Collections.Generic.List<string>();
            foreach (string path in Directory.GetFiles(testsDir, "Test*.shader"))
            {
                string name = Path.GetFileName(path);
                if (name.StartsWith("Test21")) continue;
                if (name.Contains("InheritChild")) continue;
                results.Add(name);
            }
            results.Sort();
            return results.ToArray();
        }

        /// <summary>Inheritance child shaders, compiled after the inheritance pre-pass.</summary>
        static string[] InheritanceChildShaders()
        {
            string testsDir = FindTestsDirectory();
            if (testsDir == null) return new string[0];

            var results = new System.Collections.Generic.List<string>();
            foreach (string path in Directory.GetFiles(testsDir, "Test*.shader"))
            {
                string name = Path.GetFileName(path);
                if (name.Contains("InheritChild"))
                    results.Add(name);
            }
            results.Sort();
            return results.ToArray();
        }

        [Test]
        public void AllTestShaders_CompileWithoutErrors([ValueSource(nameof(CompilableTestShaders))] string testFile)
        {
            string output = ProcessTestShader(testFile);
            AssertCompiles(output, testFile);
        }

        [Test]
        public void InheritanceChildShaders_CompileWithoutErrors([ValueSource(nameof(InheritanceChildShaders))] string testFile)
        {
            string output = ResolveAndProcessTestShader(testFile);
            AssertCompiles(output, testFile);
        }

        [Test]
        public void CompileTestEnumeration_FindsTestShaders()
        {
            // An empty [ValueSource] produces zero test cases and reads as a green
            // suite, so a broken FindTestsDirectory would silently disable every
            // compile test above. Fail loudly instead.
            Assert.Greater(CompilableTestShaders().Length, 0,
                "Compile-test enumeration found no shaders - FindTestsDirectory is broken " +
                "and the compile tests are passing vacuously");
            Assert.Greater(InheritanceChildShaders().Length, 0,
                "Inheritance compile-test enumeration found no child shaders");
        }

        [Test]
        public void AssertCompiles_FailsOnBrokenHlsl()
        {
            // Negative control: proves the compile harness can actually fail. If THIS
            // test fails, AssertCompiles is not surfacing HLSL errors in this editor
            // version and every compile test above is passing vacuously. This happened
            // once already, when CreateShaderAsset + ShaderHasError stopped reporting
            // errors with async shader compilation on (fixed by the synchronous
            // per-pass compile in AssertCompiles). Next fallback is to import the test
            // shaders through FSShaderImporter.
            //
            // The shader compiler also logs each error to the console, and the test
            // framework fails any test that logs an error it wasn't told to expect.
            // Those logs are the point here, so they're ignored. The count varies
            // (one per message per compile), which rules out LogAssert.Expect. The
            // test framework resets this flag for every test, so it only covers this one.
            LogAssert.ignoreFailingMessages = true;

            string broken = @"Shader ""Tests/CanaryBroken""
{
    SubShader
    {
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""

            struct Attributes { float4 positionOS : POSITION; };
            struct Interpolators { float4 positionCS : SV_POSITION; };

            Interpolators Vert(Attributes input)
            {
                Interpolators output = (Interpolators)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Interpolators input) : SV_Target
            {
                return undefined_symbol_that_cannot_compile;
            }
            ENDHLSL
        }
    }
}";
            Assert.Throws<AssertionException>(() => AssertCompiles(broken, "CanaryBroken"),
                "AssertCompiles accepted deliberately broken HLSL - the compile harness cannot catch shader errors");
        }

        /// <summary>
        /// Create a shader from processed source and assert it has no compile errors.
        ///
        /// CreateShaderAsset alone isn't enough: with the project's asynchronous shader
        /// compilation on, it can return before anything is compiled, ShaderHasError only
        /// reads cached messages, and the canary below passed broken HLSL as clean. So
        /// async compilation is switched off for the duration and every pass is compiled
        /// synchronously through a material before the messages are read.
        ///
        /// The material has no keywords enabled, so this validates ShaderLab plus the
        /// default-keyword variant of each pass. Keyword-gated code (e.g. the
        /// _TESSELLATION block) is only compiled in variants where the keyword is on,
        /// so it isn't covered.
        /// </summary>
        static void AssertCompiles(string processedSource, string testFile)
        {
            bool wasAsyncAllowed = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            Shader shader = null;
            Material material = null;
            try
            {
                shader = ShaderUtil.CreateShaderAsset(processedSource, true);
                Assert.IsNotNull(shader, $"{testFile}: CreateShaderAsset returned null");

                material = new Material(shader);
                for (int pass = 0; pass < shader.passCount; pass++)
                {
                    ShaderUtil.CompilePass(material, pass, forceSync: true);
                }

                bool hasError = ShaderUtil.ShaderHasError(shader);
                string details = "";

                var messages = ShaderUtil.GetShaderMessages(shader);
                foreach (var message in messages)
                {
                    if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                        hasError = true;
                    details += $"\n[{message.severity}] line {message.line}: {message.message}";
                }

                Assert.IsFalse(hasError, $"{testFile} failed to compile after processing:{details}");
            }
            finally
            {
                if (material != null)
                    UnityEngine.Object.DestroyImmediate(material);
                if (shader != null)
                    UnityEngine.Object.DestroyImmediate(shader);

                ShaderUtil.allowAsyncCompilation = wasAsyncAllowed;
            }
        }
    }
}

