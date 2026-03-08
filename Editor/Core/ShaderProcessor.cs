using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FS.Shaders.Editor
{
    /// <summary>
    /// Main shader processor that orchestrates parsing, tag processing, and pass generation.
    /// </summary>
    public class ShaderProcessor
    {
        //=============================================================================
        // Processing Pipeline
        //=============================================================================
        
        /// <summary>
        /// Process a shader source file through the full pipeline.
        /// </summary>
        public string Process(string source, string shaderPath)
        {
            // Initialize context
            var ctx = new ShaderContext
            {
                ProcessedSource = source,
                ShaderPath = shaderPath,
                ShaderDirectory = Path.GetDirectoryName(shaderPath)
            };
            
            // Stage 1: Full parse of the user's original shader source.
            // Establishes struct names/fields, function names, CBUFFER content, textures,
            // hooks, pass list, and forward pass reference.
            ShaderParser.Parse(ctx);
            
            // Stage 2: Collect material data from active pass injectors.
            // Scans for [InjectBasePasses] and [InjectPass:X] markers to determine which
            // pass injectors are active, then collects their properties and CBUFFER entries
            // into ProcessorPropertiesEntries/ProcessorCBufferEntries. This runs BEFORE
            // ProcessAllTags so pass injector material data gets injected into source
            // alongside tag processor data in the same flow.
            ShaderPassInjectorRegistry.CollectMaterialEntries(ctx);
            
            // Stage 3: Run tag processors (property/CBUFFER injection, ModifyPass).
            // Internally this:
            //   1. Collects tag processor properties/CBUFFER (appended to same fields)
            //   2. Injects ALL accumulated entries (pass injector + tag processor) into source
            //   3. Calls ReparseAllPasses for fresh PassInfo with expanded CBUFFER
            //   4. Runs ModifyPass per-pass (e.g., tessellation injects code into source)
            //
            // After this returns, ctx.ForwardPass.HlslProgram is post-CBUFFER but pre-ModifyPass.
            ShaderTagProcessorRegistry.ProcessAllTags(ctx);
            
            // Stage 4: Process pass markers and generate passes using the registry.
            ProcessPassMarkers(ctx);
            
            // Stage 5: Validation
            ValidateProcessedShader(ctx);
            
            return ctx.ProcessedSource;
        }
        
        //=============================================================================
        // Pass Marker Processing
        //=============================================================================
        
        /// <summary>
        /// Find and replace pass injection markers with generated pass code.
        /// Handles [InjectBasePasses] (all base passes) and [InjectPass:X] (specific pass).
        /// </summary>
        void ProcessPassMarkers(ShaderContext ctx)
        {
            // [InjectBasePasses] → all passes where IsBasePass == true
            ProcessSingleMarker(ctx, "[InjectBasePasses]",
                () => ShaderPassInjectorRegistry.GenerateAllBasePasses(ctx));
            
            // [InjectPass:X] → specific pass by name, discovered from registry
            foreach (var injector in ShaderPassInjectorRegistry.All)
            {
                string marker = $"[InjectPass:{injector.PassName}]";
                ProcessSingleMarker(ctx, marker,
                    () => ShaderPassInjectorRegistry.Generate(ctx, injector));
            }
        }
        
        /// <summary>
        /// Replace the first non-commented occurrence of a marker with generated code.
        /// </summary>
        void ProcessSingleMarker(ShaderContext ctx, string marker, System.Func<string> generator)
        {
            string source = ctx.ProcessedSource;
            var matches = Regex.Matches(source, Regex.Escape(marker));
            
            foreach (Match match in matches)
            {
                // Skip if in a comment
                if (IsInComment(source, match.Index))
                    continue;
                
                // Generate replacement and insert
                string replacement = generator();
                ctx.ProcessedSource = source.Substring(0, match.Index)
                    + replacement
                    + source.Substring(match.Index + match.Length);
                
                // Only replace first non-comment occurrence
                return;
            }
        }
        
        public static bool IsInComment(string source, int position)
        {
            int lineStart = source.LastIndexOf('\n', position);
            if (lineStart < 0) lineStart = 0;
            
            string lineBeforePosition = source.Substring(lineStart, position - lineStart);
            
            // Check for // comment
            if (lineBeforePosition.Contains("//"))
                return true;
            
            // Check for /* */ block comment (simplified check)
            int blockCommentStart = source.LastIndexOf("/*", position);
            if (blockCommentStart >= 0)
            {
                int blockCommentEnd = source.IndexOf("*/", blockCommentStart);
                if (blockCommentEnd < 0 || blockCommentEnd > position)
                    return true;
            }
            
            return false;
        }
        
        //=============================================================================
        // Validation
        //=============================================================================
        
        void ValidateProcessedShader(ShaderContext ctx)
        {
            if (ctx.Attributes == null)
            {
                Debug.LogWarning($"[ShaderProcessor] Could not find {ctx.AttributesStructName} struct in {ctx.ShaderPath}");
            }
            
            if (ctx.Interpolators == null)
            {
                Debug.LogWarning($"[ShaderProcessor] Could not find {ctx.InterpolatorsStructName} struct in {ctx.ShaderPath}");
            }
            
            if (ctx.Attributes != null && !ctx.Attributes.HasField("POSITION"))
            {
                Debug.LogWarning($"[ShaderProcessor] {ctx.AttributesStructName} is missing POSITION semantic");
            }
            
            if (ctx.Interpolators != null && !ctx.Interpolators.HasField("SV_POSITION"))
            {
                Debug.LogWarning($"[ShaderProcessor] {ctx.InterpolatorsStructName} is missing SV_POSITION semantic");
            }
        }
    }
}
