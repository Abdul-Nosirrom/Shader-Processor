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
            
            // Stage 1: Parse
            // Extracts structs, CBUFFER, textures, hooks, passes, and forward pass reference.
            ShaderParser.Parse(ctx);
            
            // Stage 2: Collect material data from pass injectors
            // Scans for [InjectBasePasses] and [InjectPass:X] markers, collects properties
            // and CBUFFER entries from active injectors.
            ShaderPassInjectorRegistry.CollectMaterialEntries(ctx);
            
            // Stage 3: Collect material data from tag processors
            // Discovers enabled processors, sets feature flags, collects properties
            // and CBUFFER entries (appended to same fields as pass injector entries).
            ShaderTagProcessorRegistry.CollectTagProcessorEntries(ctx);
            
            // Stage 4: Inject all accumulated material data into source
            // Properties and CBUFFER entries from BOTH pass injectors and tag processors
            // get injected here. This always runs regardless of which sources contributed.
            ShaderTagProcessorRegistry.InjectProcessorProperties(ctx);
            ShaderTagProcessorRegistry.InjectProcessorCBuffer(ctx);
            
            // Properties entries are now in source, safe to clear.
            ctx.ProcessorPropertiesEntries = null;
            
            // NOTE: Do NOT clear ProcessorCBufferEntries. GenerateCBuffer (called during
            // pass generation) combines ctx.CBufferContent (from first parse) with
            // ProcessorCBufferEntries (the delta) to produce complete CBUFFERs for
            // generated passes.
            
            // Stage 5: Reparse and modify passes
            // Reparse gives ModifyPass fresh HlslProgram content after injection.
            // ModifyPass runs tag processors on authored passes (e.g., tessellation).
            ShaderParser.ReparseAllPasses(ctx);
            ShaderTagProcessorRegistry.ModifyTaggedPasses(ctx);
            
            // Stage 6: Generate passes
            ProcessPassMarkers(ctx);
            
            // Stage 7: Validation
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
