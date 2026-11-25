using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Custom importer for FreeSkies shaders.
    /// Activated when shader contains an enabled "ShaderGen" tag or an "Inherit" tag.
    /// </summary>
    // Version history: bump whenever processing output changes, otherwise Unity's
    // import cache can keep serving results produced by the old pipeline.
    //   2: review fixes (hook prefixing, return brace-wrapping, CBUFFER anchoring,
    //      struct modifier preservation, inheritance full passes)
    //   3: DepthNormals writes signed normals like URP; hoisted #defines keep their
    //      #if guards and multi-line bodies
    //   4: generated passes copy the forward pass's Cull, ShadowCaster shadow clamping,
    //      MotionVectors shader_feature, inheritance CBUFFER/include fixes
    //   5: structs, functions and hooks are also found in files a pass #includes
    //   6: ShadowCaster/DepthOnly/DepthNormals apply LOD crossfade; Meta fills the
    //      Scene view editor-visualisation data
    //   7: generated passes emit SHADERGEN_* pass defines and include their own
    //      keyword files; forward keyword files gate themselves out of them;
    //      function and fragment-signature lookups skip matches inside comments
    //   8: fragmentOutput pragmas are blanked in the authored passes
    //   9: texture and sampler declarations stay in the copied forward code as
    //      written instead of being collected into a {{TEXTURES}} slot
    [ScriptedImporter(9,new string[0], new[] { "shader" }, AllowCaching = true)]
    public class FSShaderImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            string source = File.ReadAllText(ctx.assetPath);

            // Clear template cache so modified templates are picked up on reimport
            TemplateEngine.ClearCache();

            // Register every template as a dependency. Template content shapes the
            // generated passes but isn't part of the source hash, so without this the
            // import cache serves stale results after a template edit. Registering all
            // of them (rather than tracking which ones this shader used) slightly
            // over-triggers reimports, which is cheap and always correct.
            foreach (string templatePath in TemplateEngine.GetTemplatePaths())
            {
                ctx.DependsOnSourceAsset(templatePath);
            }

            // Resolve inheritance - if this shader declares "Inherit" = "Parent",
            // the parent source is loaded, name is replaced, and tags are merged.
            // The rest of the pipeline sees a complete shader source.
            var inheritResult = ShaderInheritance.Resolve(source, ctx.assetPath);
            source = inheritResult.Source;

            // Register dependency on parent so this shader reimports when the parent changes
            if (inheritResult.ParentPath != null)
            {
                ctx.DependsOnSourceAsset(inheritResult.ParentPath);
            }
            
            // Process the shader
            var processor = new ShaderProcessor();
            string processed = processor.Process(source, ctx.assetPath);
            
            // Create shader asset
            Shader shader = ShaderUtil.CreateShaderAsset(ctx, processed, true);
         
            if (shader == null)
            {
                ctx.LogImportError("Failed to create shader from processed source");
                return;
            }
            
            if (ShaderUtil.ShaderHasError(shader))
            {
                ctx.LogImportWarning("Shader has compilation errors - check console for details");
            }
            
            ctx.AddObjectToAsset("MainShader", shader);
            ctx.SetMainObject(shader);
        }
    }
    
    /// <summary>
    /// Detects FreeSkies shaders and sets the importer override.
    /// </summary>
    public class ShaderPreprocessor : AssetPostprocessor
    {
        void OnPreprocessAsset()
        {
            if (!assetPath.EndsWith(".shader"))
                return;

            try
            {
                string source = File.ReadAllText(assetPath);

                if (IsShaderGenNeeded(source))
                {
                    AssetDatabase.SetImporterOverride<FSShaderImporter>(assetPath);
                }
                else if (AssetDatabase.GetImporterOverride(assetPath) == typeof(FSShaderImporter))
                {
                    // Tag was removed - return the shader to Unity's default importer.
                    // The override is stored in the meta file, so without this it
                    // sticks forever and the shader keeps going through the pipeline.
                    AssetDatabase.ClearImporterOverride(assetPath);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ShaderProcessor] Error checking shader: {e.Message}");
            }
        }

        static bool IsShaderGenNeeded(string source)
        {
            // Check for ShaderGen tag in either Pass or SubShader scope. Accepts the
            // same value set as ShaderTagUtility.IsTagEnabled - if the activation regex
            // only accepted "True", a tag value the pipeline would honor ("On", "1")
            // would never activate the importer in the first place.
            var matches = Regex.Matches(source,
                @"[""']ShaderGen[""']\s*=\s*[""']([^""']+)[""']",
                RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                if (ShaderBlockUtility.IsInComment(source, match.Index))
                    continue;

                if (ShaderTagUtility.IsTagEnabled(match.Groups[1].Value))
                    return true;
            }

            // Check for Inherit tag (child shader inheriting from a parent)
            if (ShaderInheritance.UsesInheritance(source))
                return true;

            return false;
        }
    }
}
