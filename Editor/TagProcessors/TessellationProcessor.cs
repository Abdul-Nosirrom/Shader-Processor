using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FS.Shaders.Editor
{
    [ShaderTagProcessor("Tessellation", priority: 5)]
    public class TessellationProcessor : ShaderTagProcessorBase
    {
        public override string TagName => "Tessellation";
        public override int Priority => 5;
        
        const string TessellationProperties = @"
            // Tessellation (Auto-injected by ShaderGen)
            [Tessellation] _Tessellation(""Tessellation"", Float) = 0
            [HideInInspector] _TessellationFactor(""Factor"", Range(1, 64)) = 16
            [HideInInspector] _TessellationMinDist(""Min Dist"", Float) = 5
            [HideInInspector] _TessellationMaxDist(""Max Dist"", Float) = 25
            [HideInInspector] _TessellationEdgeLength(""Edge Length"", Float) = 16
            [HideInInspector] _PhongStrength(""Phong"", Range(0, 1)) = 0.5";
        
        const string TessellationCBuffer = @"
            // Tessellation (Auto-injected by ShaderGen)
            float _TessellationFactor;
            float _TessellationMinDist;
            float _TessellationMaxDist;
            float _TessellationEdgeLength;
            float _PhongStrength;";
        
        //=============================================================================
        // Declarative API - System handles injection
        //=============================================================================
        
        public override string GetPropertiesEntries(ShaderContext ctx)
        {
            if (PropertyExists(ctx, "_Tessellation")) return null;
            return TessellationProperties;
        }
        
        public override string GetCBufferEntries(ShaderContext ctx)
        {
            if (CBufferEntryExists(ctx, "_TessellationFactor")) return null;
            return TessellationCBuffer;
        }
        
        public override void ModifyPass(ShaderContext ctx, PassInfo pass)
        {
            string vertexFunc = pass.VertexFunctionName ?? "Vert";
            
            string template = TemplateEngine.LoadTemplate("Tessellation");
            if (string.IsNullOrEmpty(template)) return;
            
            // Use this pass's struct info, not the global reference pass
            string attrName = pass.AttributesStructName ?? ctx.AttributesStructName;
            string interpName = pass.InterpolatorsStructName ?? ctx.InterpolatorsStructName;
            StructDefinition attrStruct = pass.Attributes ?? ctx.Attributes;
            
            // Generate dynamic field handling from this pass's struct
            var fieldGeneration = GenerateFieldCode(attrStruct, attrName);
            
            var tessReplacements = new Dictionary<string, string>
            {
                ["ATTRIBUTES_NAME"] = attrName,
                ["INTERPOLATORS_NAME"] = interpName,
                ["VERTEX_FUNCTION"] = vertexFunc,
                ["TESS_CONTROL_POINT_FIELDS"] = fieldGeneration.ControlPointFields,
                ["TESS_VERTEX_BODY"] = fieldGeneration.VertexBody,
                ["DOMAIN_INTERPOLATION"] = fieldGeneration.DomainInterpolation,
                ["DOMAIN_INTERPOLATION_PASSTHROUGH"] = fieldGeneration.DomainInterpolationPassthrough,
            };
            
            TemplateEngine.AddHookReplacements(tessReplacements, ctx, attrName, interpName);

            string tessCode = TemplateEngine.Process(template, tessReplacements);
            
            // Locate this pass's HLSL content in the current source.
            // Uses parsed HlslProgram as anchor (fresh from ReparseAllPasses).
            if (string.IsNullOrEmpty(pass.HlslProgram))
            {
                Debug.LogWarning($"[TessellationProcessor] Pass '{pass.Name}' has no HLSL program, skipping.");
                return;
            }
            
            int hlslContentIdx = ctx.ProcessedSource.IndexOf(pass.HlslProgram);
            if (hlslContentIdx < 0)
            {
                Debug.LogWarning($"[TessellationProcessor] Could not locate HLSL content for pass '{pass.Name}', skipping.");
                return;
            }
            
            string hlslContent = pass.HlslProgram;
            
            // Replace vertex pragma
            string newHlslContent = Regex.Replace(
                hlslContent,
                $@"#pragma\s+vertex\s+{Regex.Escape(vertexFunc)}\b",
                "#pragma vertex TessVertex"
            );
            
            // Find the end of the fragment function to inject tessellation code after it
            int lastBraceIndex = newHlslContent.LastIndexOf('}');
            if (lastBraceIndex > 0)
            {
                newHlslContent = newHlslContent.Substring(0, lastBraceIndex + 1) 
                    + "\n\n" + tessCode + "\n"
                    + newHlslContent.Substring(lastBraceIndex + 1);
            }
            
            // Index-based replacement instead of string.Replace (avoids hitting duplicates)
            ctx.ProcessedSource = ctx.ProcessedSource.Substring(0, hlslContentIdx)
                + newHlslContent
                + ctx.ProcessedSource.Substring(hlslContentIdx + hlslContent.Length);
            
            Debug.Log($"[TessellationProcessor] Injected tessellation into pass '{pass.Name}'");
        }
        
        public override Dictionary<string, string> GetPassReplacements(ShaderContext ctx, string passName)
        {
            string template = TemplateEngine.LoadTemplate("Tessellation");
            if (string.IsNullOrEmpty(template))
            {
                Debug.LogError("[ShaderGen] Tessellation template not found");
                return null;
            }
            
            string attrName = passName + "Attributes";
            string interpName = passName + "Interpolators";
            string vertexFunc = passName + "Vertex";
            
            // Generate dynamic field handling
            var fieldGeneration = GenerateFieldCode(ctx.Attributes, attrName);
            
            var tessReplacements = new Dictionary<string, string>
            {
                ["ATTRIBUTES_NAME"] = attrName,
                ["INTERPOLATORS_NAME"] = interpName,
                ["VERTEX_FUNCTION"] = vertexFunc,
                ["TESS_CONTROL_POINT_FIELDS"] = fieldGeneration.ControlPointFields,
                ["TESS_VERTEX_BODY"] = fieldGeneration.VertexBody,
                ["DOMAIN_INTERPOLATION"] = fieldGeneration.DomainInterpolation,
                ["DOMAIN_INTERPOLATION_PASSTHROUGH"] = fieldGeneration.DomainInterpolationPassthrough,
            };

            TemplateEngine.AddHookReplacements(tessReplacements, ctx, attrName, interpName);
            
            string tessCode = TemplateEngine.Process(template, tessReplacements);
            
            return new Dictionary<string, string>
            {
                ["TESSELLATION_PRAGMAS"] = "",
                ["TESSELLATION_CODE"] = tessCode,
                ["VERTEX_PRAGMA"] = "#pragma vertex TessVertex",
            };
        }
        
        //=============================================================================
        // Field Generation
        //=============================================================================
        
        struct FieldGenerationResult
        {
            public string ControlPointFields;
            public string VertexBody;
            public string DomainInterpolation;
            public string DomainInterpolationPassthrough;
        }
        
        FieldGenerationResult GenerateFieldCode(StructDefinition attributes, string attrName)
        {
            var controlPointFields = new StringBuilder();
            var vertexBody = new StringBuilder();
            var domainInterp = new StringBuilder();
            var domainInterpPassthrough = new StringBuilder();
            
            if (attributes?.Fields == null)
            {
                // Fallback to minimal defaults
                return GenerateFallbackFields();
            }
            
            foreach (var field in attributes.Fields)
            {
                Debug.Log($"[TessGen] Field: Name={field.Name}, Semantic={field.Semantic}, IsMacro={field.IsMacro}, RawLine={field.RawLine}");
                
                // Skip macros like UNITY_VERTEX_INPUT_INSTANCE_ID
                if (field.IsMacro) continue;
                
                // Generate TessControlPoint field
                string semantic = GetTessControlPointSemantic(field.Semantic);
                controlPointFields.AppendLine($"    {field.Type} {field.Name} : {semantic};");
                
                // Generate TessVertex copy
                vertexBody.AppendLine($"    o.{field.Name} = input.{field.Name};");
                
                // Generate Domain interpolation (full tessellation)
                string interp = GetInterpolationCode(field);
                domainInterp.AppendLine($"    input.{field.Name} = {interp};");
                
                // Generate Domain interpolation (passthrough)
                string interpPassthrough = GetInterpolationCodePassthrough(field);
                domainInterpPassthrough.AppendLine($"    input.{field.Name} = {interpPassthrough};");
            }
            
            return new FieldGenerationResult
            {
                ControlPointFields = controlPointFields.ToString(),
                VertexBody = vertexBody.ToString(),
                DomainInterpolation = domainInterp.ToString(),
                DomainInterpolationPassthrough = domainInterpPassthrough.ToString(),
            };
        }
        
        FieldGenerationResult GenerateFallbackFields()
        {
            return new FieldGenerationResult
            {
                ControlPointFields = @"    float4 positionOS : INTERNALTESSPOS;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;",
                VertexBody = @"    o.positionOS = input.positionOS;
    o.normalOS = input.normalOS;
    o.tangentOS = input.tangentOS;
    o.uv = input.uv;",
                DomainInterpolation = @"    input.positionOS = float4(BARY_INTERP(positionOS.xyz), 1);
    input.normalOS = normalize(BARY_INTERP(normalOS));
    input.tangentOS = float4(normalize(BARY_INTERP(tangentOS.xyz)), patch[0].tangentOS.w);
    input.uv = BARY_INTERP(uv);",
                DomainInterpolationPassthrough = @"    input.positionOS = float4(BARY_INTERP(positionOS.xyz), 1);
    input.normalOS = normalize(BARY_INTERP(normalOS));
    input.tangentOS = float4(normalize(BARY_INTERP(tangentOS.xyz)), patch[0].tangentOS.w);
    input.uv = BARY_INTERP(uv);",
            };
        }
        
        string GetTessControlPointSemantic(string semantic)
        {
            // POSITION becomes INTERNALTESSPOS for tessellation
            if (semantic == "POSITION")
                return "INTERNALTESSPOS";
            
            return semantic;
        }
        
        string GetInterpolationCode(StructField field)
        {
            string name = field.Name;
            string semantic = field.Semantic?.ToUpperInvariant() ?? "";
            string type = field.Type?.ToLowerInvariant() ?? "";
            
            // Position - interpolate xyz, keep w=1
            if (semantic == "POSITION")
            {
                return $"float4(BARY_INTERP({name}.xyz), 1)";
            }
            
            // Normal - interpolate and normalize
            if (semantic == "NORMAL")
            {
                return $"normalize(BARY_INTERP({name}))";
            }
            
            // Tangent - interpolate xyz and normalize, preserve w from first vertex
            if (semantic == "TANGENT")
            {
                return $"float4(normalize(BARY_INTERP({name}.xyz)), patch[0].{name}.w)";
            }
            
            // Everything else - simple barycentric interpolation
            return $"BARY_INTERP({name})";
        }
        
        string GetInterpolationCodePassthrough(StructField field)
        {
            string name = field.Name;
            string semantic = field.Semantic?.ToUpperInvariant() ?? "";
            
            // Position - interpolate xyz, keep w=1
            if (semantic == "POSITION")
            {
                return $"float4(patch[0].{name}.xyz * bary.x + patch[1].{name}.xyz * bary.y + patch[2].{name}.xyz * bary.z, 1)";
            }
            
            // Normal - interpolate and normalize
            if (semantic == "NORMAL")
            {
                return $"normalize(patch[0].{name} * bary.x + patch[1].{name} * bary.y + patch[2].{name} * bary.z)";
            }
            
            // Tangent - interpolate xyz and normalize, preserve w
            if (semantic == "TANGENT")
            {
                return $"float4(normalize(patch[0].{name}.xyz * bary.x + patch[1].{name}.xyz * bary.y + patch[2].{name}.xyz * bary.z), patch[0].{name}.w)";
            }
            
            // Everything else - simple interpolation
            return $"patch[0].{name} * bary.x + patch[1].{name} * bary.y + patch[2].{name} * bary.z";
        }
    }
}