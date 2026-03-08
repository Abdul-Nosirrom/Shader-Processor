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
            
            // Stage 1: Full parse of the user's original shader source.
            // Establishes struct names/fields, function names, CBUFFER content, textures,
            // hooks, pass list, and forward pass reference. All detection runs against
            // clean, unmodified code — this is the single source of truth for identity.
            ShaderParser.Parse(ctx);
            
            // Stage 2: Run tag processors (property/CBUFFER injection, ModifyPass, pass queuing).
            // Internally this:
            //   1. Injects processor properties and CBUFFER entries into ctx.ProcessedSource
            //   2. Calls ReparseAllPasses — creates fresh PassInfo with expanded CBUFFER content
            //   3. Runs ModifyPass per-pass (e.g., tessellation injects code into ProcessedSource)
            //
            // After this returns, ctx.ForwardPass.HlslProgram is the snapshot from step 2 —
            // it has the expanded CBUFFER but NO ModifyPass modifications (tess code, etc.).
            // ModifyPass only writes to ctx.ProcessedSource, not to PassInfo.HlslProgram.
            // This means ExtractForwardPassContent naturally sees clean content.
            //
            // No second Parse() is needed. Struct names, function names, and struct field
            // definitions from Stage 1 remain correct and untouched. The expanded CBUFFER
            // for generated passes is handled by GenerateCBuffer, which combines
            // ctx.CBufferContent (from Stage 1) with ctx.ProcessorCBufferEntries (from
            // tag processors) — no re-read of source required.
            ShaderTagProcessorRegistry.ProcessAllTags(ctx);
            
            // Stage 3: Process pass markers and generate passes
            ProcessPassMarkers(ctx);
            
            // Stage 4: Inject tag processor passes (outlines, etc.)
            InjectQueuedPasses(ctx);
            
            // Stage 5: Validation
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
