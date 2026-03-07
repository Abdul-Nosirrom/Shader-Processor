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
                OriginalSource = source,
                ProcessedSource = source,
                ShaderPath = shaderPath,
                ShaderDirectory = Path.GetDirectoryName(shaderPath)
            };
            
            // Stage 1: Initial parse
            ShaderParser.Parse(ctx);
            
            // Stage 2: Run tag processors (Properties, CBUFFER, ModifyMainPass)
            ShaderTagProcessorRegistry.ProcessAllTags(ctx);
            
            // Stage 3: Re-parse after tag processor modifications
            ctx.OriginalSource = ctx.ProcessedSource;
            ShaderParser.Parse(ctx);
            
            // Stage 4: Process pass markers and generate passes
            ProcessPassMarkers(ctx);
            
            // Stage 5: Inject tag processor passes
            InjectQueuedPasses(ctx);
            
            // Stage 6: Validation
            ValidateProcessedShader(ctx);
            
            return ctx.ProcessedSource;
        }
        
        //=============================================================================
        // Pass Marker Processing
        //=============================================================================
        
        void ProcessPassMarkers(ShaderContext ctx)
        {
            // Replace [InjectBasePasses] with all base passes
            ProcessSingleMarker(ctx, "[InjectBasePasses]", () => PassGenerator.GenerateAllBasePasses(ctx));
            
            // Individual pass markers (if user wants specific passes)
            ProcessSingleMarker(ctx, "[InjectPass:ShadowCaster]", () => PassGenerator.GenerateShadowCasterPass(ctx));
            ProcessSingleMarker(ctx, "[InjectPass:DepthOnly]", () => PassGenerator.GenerateDepthOnlyPass(ctx));
            ProcessSingleMarker(ctx, "[InjectPass:DepthNormals]", () => PassGenerator.GenerateDepthNormalsPass(ctx));
            ProcessSingleMarker(ctx, "[InjectPass:MotionVectors]", () => PassGenerator.GenerateMotionVectorsPass(ctx));
            ProcessSingleMarker(ctx, "[InjectPass:Meta]", () => PassGenerator.GenerateMetaPass(ctx));
        }
        
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
        
        bool IsInComment(string source, int position)
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
        // Queued Pass Injection
        //=============================================================================
        
        void InjectQueuedPasses(ShaderContext ctx)
        {
            if (ctx.QueuedPasses.Count == 0) return;
            
            int injectionIndex = ShaderParser.GetPassInjectionIndex(ctx.ProcessedSource);
            if (injectionIndex < 0)
            {
                Debug.LogWarning("[ShaderProcessor] Could not find pass injection point. Queued passes not injected.");
                return;
            }
            
            // Build all queued passes
            var sb = new System.Text.StringBuilder();
            foreach (var queuedPass in ctx.QueuedPasses)
            {
                sb.AppendLine();
                sb.AppendLine(queuedPass.PassCode);
            }
            
            // Insert at injection point
            ctx.ProcessedSource = ctx.ProcessedSource.Insert(injectionIndex, sb.ToString());
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
