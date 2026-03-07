using System.Collections.Generic;
using System.Text;
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
                
        public override void InjectProperties(ShaderContext ctx)
        {
            if (PropertyExists(ctx, "_Tessellation")) return;
            InjectPropertiesContent(ctx, TessellationProperties);
        }
        
        public override void InjectCBuffer(ShaderContext ctx)
        {
            if (CBufferEntryExists(ctx, "_TessellationFactor")) return;
            InjectCBufferContent(ctx, TessellationCBuffer);
        }
        
        public override void ModifyMainPass(ShaderContext ctx)
        {
            string vertexFunc = ctx.ForwardVertexFunctionName ?? "Vert";
            
            string template = TemplateEngine.LoadTemplate("Tessellation");
            if (string.IsNullOrEmpty(template)) return;
            
            string attrName = ctx.Attributes?.Name ?? "Attributes";
            string interpName = ctx.Interpolators?.Name ?? "Interpolators";
            
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
            
            string tessCode = TemplateEngine.Process(template, tessReplacements);
            
            // Find the Forward pass HLSLPROGRAM block
            var passMatch = System.Text.RegularExpressions.Regex.Match(
                ctx.ProcessedSource,
                $@"(Pass\s*\{{\s*Name\s+""(?:Forward|UniversalForward)"".*?HLSLPROGRAM)(.*?)(ENDHLSL)",
                System.Text.RegularExpressions.RegexOptions.Singleline
            );
            
            if (!passMatch.Success) return;
            
            string hlslContent = passMatch.Groups[2].Value;
            
            // Replace vertex pragma
            string newHlslContent = System.Text.RegularExpressions.Regex.Replace(
                hlslContent,
                $@"#pragma\s+vertex\s+{vertexFunc}\b",
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
            
            ctx.ProcessedSource = ctx.ProcessedSource.Replace(
                passMatch.Value,
                passMatch.Groups[1].Value + newHlslContent + passMatch.Groups[3].Value
            );
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