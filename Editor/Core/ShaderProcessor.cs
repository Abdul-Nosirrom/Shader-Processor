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
            // Skip if no reference pass was found - generated passes would be invalid
            // without struct, hook, and content data from a reference pass.
            if (ctx.ReferencePass != null)
            {
                ProcessPassMarkers(ctx);
            }
            else
            {
                Debug.LogWarning($"[ShaderProcessor] No reference pass found in {ctx.ShaderPath}. Pass generation skipped.");
            }
            
            // Stage 7: Strip remaining InheritHook markers
            // Parent shaders declare {{InheritHook:Name(...)}} points that child shaders
            // fill via inheritance. When the parent compiles standalone (no child), these
            // markers need to be removed so they don't break HLSL compilation.
            StripInheritHookMarkers(ctx);
            
            // Stage 8: Validation
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
        
        /// <summary>
        /// Check if a position in source code is inside a comment.
        /// Delegates to <see cref="ShaderBlockUtility.IsInComment"/> which
        /// handles both line comments (//) and block comments (/* */),
        /// including // inside closed block comments on the same line.
        /// </summary>
        public static bool IsInComment(string source, int position)
        {
            return ShaderBlockUtility.IsInComment(source, position);
        }
        
        //=============================================================================
        // InheritHook Cleanup
        //=============================================================================
        
        static readonly Regex s_inheritHookMarkerRegex = new Regex(
            @"\{\{InheritHook:\w+\([^)]*\)\}\}", RegexOptions.Compiled);
        
        /// <summary>
        /// Strip any remaining InheritHook markers from the processed source.
        /// These markers are placed by parent shaders as extension points for children.
        /// When the parent is compiled standalone, they must be removed.
        /// When a child inherits, the markers are already resolved by
        /// <see cref="ShaderInheritance.ResolveInheritHooks"/> before this runs.
        /// </summary>
        void StripInheritHookMarkers(ShaderContext ctx)
        {
            ctx.ProcessedSource = s_inheritHookMarkerRegex.Replace(ctx.ProcessedSource, "");
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
