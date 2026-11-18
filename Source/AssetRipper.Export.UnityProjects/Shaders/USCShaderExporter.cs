using AssetRipper.Assets;
using AssetRipper.Assets.Generics;
using AssetRipper.Export.Modules.Shaders.Exporters;
using AssetRipper.Export.Modules.Shaders.Exporters.DirectX;
using AssetRipper.Export.Modules.Shaders.Exporters.USCDirectX;
using AssetRipper.Export.Modules.Shaders.IO;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.Converter;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.DirectX;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL;
using AssetRipper.Export.UnityProjects.Configuration;
using AssetRipper.Import.Logging;
using AssetRipper.IO.Files;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.GpuProgramType;
using AssetRipper.SourceGenerated.Extensions.Enums.Shader.SerializedShader;
using AssetRipper.SourceGenerated.Subclasses.SerializedPass;
using AssetRipper.SourceGenerated.Subclasses.SerializedPlayerSubProgram;
using AssetRipper.SourceGenerated.Subclasses.SerializedProgram;
using AssetRipper.SourceGenerated.Subclasses.SerializedShader;
using AssetRipper.SourceGenerated.Subclasses.SerializedSubProgram;
using AssetRipper.SourceGenerated.Subclasses.SerializedSubShader;
using System.Diagnostics;

namespace AssetRipper.Export.UnityProjects.Shaders
{
	public sealed class USCShaderExporter : ShaderExporterBase
	{
		public static bool IsDX11ExportMode(ShaderExportMode mode) => mode == ShaderExportMode.Disassembly;

		public override bool Export(IExportContainer container, IUnityObjectBase asset, string path)
		{
			using Stream fileStream = File.Create(path);
			try
			{
				ExportBinary((IShader)asset, fileStream, ShaderExporterInstantiator);
				return true;
			}
			catch (Exception ex)
			{
				Logger.Error(ex);
				fileStream.Position = 0;
				DummyShaderTextExporter.ExportShader((IShader)asset, new InvariantStreamWriter(fileStream));
				return false;
			}
		}

		private static ShaderTextExporter ShaderExporterInstantiator(GPUPlatform graphicApi)
		{
			switch (graphicApi)
			{
				case GPUPlatform.d3d11_9x:
				case GPUPlatform.d3d11:
				case GPUPlatform.d3d9:
					return new USCShaderDXExporter(graphicApi);

				case GPUPlatform.vulkan:
					return new ShaderVulkanExporter();

				case GPUPlatform.openGL:
				case GPUPlatform.gles:
				case GPUPlatform.gles3:
				case GPUPlatform.glcore:
					return new ShaderGLESExporter();

				case GPUPlatform.metal:
					return new ShaderMetalExporter();

				case GPUPlatform.unknown:
					return new ShaderTextExporter();

				default:
					return new ShaderUnknownExporter(graphicApi);
			}
		}

		private static void ExportBinary(IShader shader, Stream stream, Func<GPUPlatform, ShaderTextExporter> exporterInstantiator)
		{
			if (shader.Has_ParsedForm())
			{
				using ShaderWriter writer = new ShaderWriter(stream, shader, exporterInstantiator);
				writer.WriteQuotesAroundProgram = false; // this can be removed after ESSWC is finished
														 //((SerializedShader)shader.ParsedForm).Export(writer);
				ExportSerializedShaderDecomp(shader.ParsedForm, writer);
			}
			else if (shader.Has_CompressedBlob())
			{
				using ShaderWriter writer = new ShaderWriter(stream, shader, exporterInstantiator);
				writer.WriteQuotesAroundProgram = false;
				string header = shader.Script.String;
				if (writer.Blobs.Length == 0)
				{
					writer.Write(header);
				}
				else
				{
					writer.Blobs[0].Export(writer, header);
				}
			}
			else
			{
				using BinaryWriter writer = new BinaryWriter(stream);
				writer.Write(shader.Script.String);
			}
		}

		/////////////////////////////////////////////////////
		// we need to lay this out in a specific way that
		// isn't compatible with the way SerializedExtensions
		// does it

		private static void ExportSerializedShaderDecomp(ISerializedShader _this, ShaderWriter writer)
		{
			writer.Write($"Shader \"{_this.Name.String}\" {{\n");

			_this.PropInfo.Export(writer);

			for (int i = 0; i < _this.SubShaders.Count; i++)
			{
				ExportSubShaderDecomp(_this.SubShaders[i], writer);
			}

			if (_this.FallbackName.String.Length != 0)
			{
				writer.WriteIndent(1);
				writer.Write($"Fallback \"{_this.FallbackName.String}\"\n");
			}

			if (_this.CustomEditorName.String.Length != 0)
			{
				writer.WriteIndent(1);
				writer.Write($"CustomEditor \"{_this.CustomEditorName.String}\"\n");
			}

			writer.Write('}');
		}

		private static void ExportSubShaderDecomp(ISerializedSubShader _this, ShaderWriter writer)
		{
			writer.WriteIndent(1);
			writer.Write("SubShader {\n");
			if (_this.LOD != 0)
			{
				writer.WriteIndent(2);
				writer.Write($"LOD {_this.LOD}\n");
			}
			_this.Tags.Export(writer, 2);
			for (int i = 0; i < _this.Passes.Count; i++)
			{
				ExportPassDecomp(_this.Passes[i], writer);
			}
			writer.WriteIndent(1);
			writer.Write("}\n");
		}

        private static bool PreserveVariantsFor(IShader shader) => true;
        private static HashSet<string> Keys(ShaderSubProgram p) => p.LocalKeywords.Concat(p.GlobalKeywords).ToHashSet();
        private sealed record ProgramPair(ShaderSubProgram? Vertex, ShaderSubProgram? Fragment, string[] Keywords);
        private static List<ProgramPair> PairPrograms(List<ShaderSubProgram>? vertices, List<ShaderSubProgram>? fragments, bool preserve) {
            if (!preserve) return new(){new(vertices?.First(), fragments?.First(), Array.Empty<string>())};
            var vs=(vertices ?? new()).GroupBy(p=>string.Join(" ",Keys(p).OrderBy(k=>k))).Select(g=>g.First()).ToList();
            var fs=(fragments ?? new()).GroupBy(p=>string.Join(" ",Keys(p).OrderBy(k=>k))).Select(g=>g.First()).ToList();
            var vk=vs.SelectMany(Keys).ToHashSet();var fk=fs.SelectMany(Keys).ToHashSet();
            var shared=vk.Intersect(fk).ToHashSet();
            var result=new List<ProgramPair>();
            foreach(var v in vs) foreach(var f in fs) {
                var a=Keys(v);var b=Keys(f);
                if(!a.Intersect(shared).ToHashSet().SetEquals(b.Intersect(shared))) continue;
                result.Add(new(v,f,a.Union(b).OrderBy(k=>k).ToArray()));
            }
            if(result.Count==0) throw new InvalidOperationException("No compatible original vertex/fragment variants");
            return result.GroupBy(p=>string.Join(" ",p.Keywords)).Select(g=>g.First()).OrderByDescending(p=>p.Keywords.Length).ToList();
        }

		private static void ExportPassDecomp(ISerializedPass _this, ShaderWriter writer)
		{
			writer.WriteIndent(2);
			writer.Write($"{(SerializedPassType)_this.Type} ");

			if ((SerializedPassType)_this.Type == SerializedPassType.UsePass)
			{
				writer.Write($"\"{_this.UseName}\"\n");
			}
			else
			{
				writer.Write("{\n");

				if ((SerializedPassType)_this.Type == SerializedPassType.GrabPass)
				{
					if (_this.TextureName.Data.Length > 0)
					{
						writer.WriteIndent(3);
						writer.Write($"\"{_this.TextureName}\"\n");
					}
				}
				else if ((SerializedPassType)_this.Type == SerializedPassType.Pass)
				{
					_this.State.Export(writer);

					bool hasVertex = (_this.ProgramMask & ShaderType.Vertex.ToProgramMask()) != 0;
					bool hasFragment = (_this.ProgramMask & ShaderType.Fragment.ToProgramMask()) != 0;

					List<ShaderSubProgram>? vertexSubPrograms = null;
					List<ShaderSubProgram>? fragmentSubPrograms = null;

					if (hasVertex)
					{
						vertexSubPrograms = GetSubPrograms(writer.Shader, writer.Blobs, _this.ProgVertex, writer.Version, writer.Platform, ShaderType.Vertex, _this);
						if (vertexSubPrograms.Count == 0)
						{
							writer.WriteIndent(3);
							writer.Write("// No subprograms found\n");
							writer.WriteIndent(2);
							writer.Write("}\n");
							return;
						}
					}
					if (hasFragment)
					{
						fragmentSubPrograms = GetSubPrograms(writer.Shader, writer.Blobs, _this.ProgFragment, writer.Version, writer.Platform, ShaderType.Fragment, _this);
						if (fragmentSubPrograms.Count == 0)
						{
							writer.WriteIndent(3);
							writer.Write("// No subprograms found\n");
							writer.WriteIndent(2);
							writer.Write("}\n");
							return;
						}
					}

					bool preserveVariants = PreserveVariantsFor(writer.Shader);
                    var pairs = PairPrograms(vertexSubPrograms, fragmentSubPrograms, preserveVariants);
                    var allKeywords = pairs.SelectMany(p => p.Keywords).Distinct().OrderBy(k=>k).ToArray();
                    writer.WriteLine("CGPROGRAM");
                    writer.WriteLine("#pragma target 4.5");
                    if (preserveVariants) foreach(var keyword in allKeywords) writer.WriteLine($"#pragma shader_feature __ {keyword}");
                    int pairIndex = 0;
                    foreach (var pair in pairs) {
                    if(preserveVariants) {
                        string condition=allKeywords.Length==0 ? "1" : string.Join(" && ",allKeywords.Select(k=>pair.Keywords.Contains(k) ? $"defined({k})" : $"!defined({k})"));
                        writer.WriteLine($"{(pairIndex++==0 ? "#if" : "#elif")} {condition}");
                    }
                    ShaderSubProgram? vertexSubProgram = null;
					ShaderSubProgram? fragmentSubProgram = null;
					USCShaderConverter? vertexConverter = null;
					USCShaderConverter? fragmentConverter = null;

					if (hasVertex)
					{
						vertexSubProgram = pair.Vertex!;

						byte[] trimmedProgramData = TrimShaderBytes(vertexSubProgram, writer.Version, writer.Platform);

						vertexConverter = new USCShaderConverter();
						vertexConverter.LoadDirectXCompiledShader(new MemoryStream(trimmedProgramData));
					}
					if (hasFragment)
					{
						fragmentSubProgram = pair.Fragment!;

						byte[] trimmedProgramData = TrimShaderBytes(fragmentSubProgram, writer.Version, writer.Platform);

						fragmentConverter = new USCShaderConverter();
						fragmentConverter.LoadDirectXCompiledShader(new MemoryStream(trimmedProgramData));
					}

					writer.WriteIndent(3);

					if (hasVertex)
					{
						writer.WriteIndent(3);
						writer.WriteLine("#pragma vertex vert");
					}
					if (hasFragment)
					{
						writer.WriteIndent(3);
						writer.WriteLine("#pragma fragment frag");
					}
					if (hasVertex || hasFragment)
					{
						writer.WriteIndent(3);
						writer.WriteLine("");
					}

					writer.WriteIndent(3);
					writer.WriteLine("#include \"UnityCG.cginc\"");

					if (hasVertex)
					{
						// v2f struct (vert output/frag input)
						writer.WriteIndent(3);
						writer.WriteLine($"struct {USILConstants.VERT_TO_FRAG_STRUCT_NAME}");
						writer.WriteIndent(3);
						writer.WriteLine("{");

						DirectXCompiledShader? dxShader = vertexConverter!.DxShader;
						Debug.Assert(dxShader != null);
						foreach (OSGN.Output output in dxShader.Osgn.outputs)
						{
							string format = DXShaderNamingUtils.GetOSGNFormatName(output);
							string type = output.name + output.index;
							string name = DXShaderNamingUtils.GetOSGNOutputName(output);

							writer.WriteIndent(4);
							writer.WriteLine($"{format} {name} : {type};");
						}

						writer.WriteIndent(3);
						writer.WriteLine("};");
					}
					if (hasFragment)
					{
						// fout struct (frag output)
						writer.WriteIndent(3);
						writer.WriteLine($"struct {USILConstants.FRAG_OUTPUT_STRUCT_NAME}");
						writer.WriteIndent(3);
						writer.WriteLine("{");

						DirectXCompiledShader? dxShader = fragmentConverter!.DxShader;
						Debug.Assert(dxShader != null);
						foreach (OSGN.Output output in dxShader.Osgn.outputs)
						{
							string format = DXShaderNamingUtils.GetOSGNFormatName(output);
							string type = output.name + output.index;
							string name = DXShaderNamingUtils.GetOSGNOutputName(output);

							writer.WriteIndent(4);
							writer.WriteLine($"{format} {name} : {type};");
						}

						writer.WriteIndent(3);
						writer.WriteLine("};");
					}

					HashSet<string?> declaredBufs = new();

					if (hasVertex)
					{
						writer.WriteIndent(3);
						writer.WriteLine("// $Globals ConstantBuffers for Vertex Shader");

						ExportPassConstantBufferDefinitions(vertexSubProgram!, writer, declaredBufs, "$Globals", 3);
					}

					if (hasFragment)
					{
						writer.WriteIndent(3);
						writer.WriteLine("// $Globals ConstantBuffers for Fragment Shader");

						ExportPassConstantBufferDefinitions(fragmentSubProgram!, writer, declaredBufs, "$Globals", 3);
					}

					if (hasVertex)
					{
						writer.WriteIndent(3);
						writer.WriteLine("// Custom ConstantBuffers for Vertex Shader");

						foreach (ConstantBuffer cbuffer in vertexSubProgram!.ConstantBuffers)
						{
							if (UnityShaderConstants.BUILTIN_CBUFFER_NAMES.Contains(cbuffer.Name))
							{
								continue;
							}

							ExportPassConstantBufferDefinitions(vertexSubProgram, writer, declaredBufs, cbuffer, 3);
						}
					}

					if (hasFragment)
					{
						writer.WriteIndent(3);
						writer.WriteLine("// Custom ConstantBuffers for Fragment Shader");

						foreach (ConstantBuffer cbuffer in fragmentSubProgram!.ConstantBuffers)
						{
							if (UnityShaderConstants.BUILTIN_CBUFFER_NAMES.Contains(cbuffer.Name))
							{
								continue;
							}

							ExportPassConstantBufferDefinitions(fragmentSubProgram, writer, declaredBufs, cbuffer, 3);
						}
					}

					if (hasVertex)
					{
						writer.WriteIndent(3);
						writer.WriteLine("// Texture params for Vertex Shader");

						ExportPassTextureParamDefinitions(vertexSubProgram!, vertexConverter!.DxShader!, writer, declaredBufs, 3);
					}

					if (hasFragment)
					{
						writer.WriteIndent(3);
						writer.WriteLine("// Texture params for Fragment Shader");

						ExportPassTextureParamDefinitions(fragmentSubProgram!, fragmentConverter!.DxShader!, writer, declaredBufs, 3);
					}

					writer.WriteIndent(3);
					writer.WriteLine("");

					if (hasVertex)
					{
						string keywordsList = string.Join(' ', vertexSubProgram!.LocalKeywords.Concat(vertexSubProgram.GlobalKeywords));

						writer.WriteIndent(3);
						writer.WriteLine($"// Keywords: {keywordsList}");

						writer.WriteIndent(3);
						string args = $"appdata_full {USILConstants.VERT_INPUT_NAME}";
						if (vertexConverter!.DxShader!.Isgn.inputs.Any(input => input.IsVertexId))
						{
							args += $", uint {USILConstants.VERTEX_ID_INPUT_NAME} : SV_VertexID";
						}
						writer.WriteLine($"{USILConstants.VERT_TO_FRAG_STRUCT_NAME} vert({args})");
						writer.WriteIndent(3);
						writer.WriteLine("{");

						vertexConverter!.ConvertShaderToUShaderProgram();
						vertexConverter.ApplyMetadataToProgram(vertexSubProgram, writer.Version);
						string progamText = vertexConverter.CovnertUShaderProgramToHLSL(4);
						writer.Write(progamText);

						writer.WriteIndent(3);
						writer.WriteLine("}");
					}

					if (hasFragment)
					{
						string keywordsList = string.Join(' ', fragmentSubProgram!.LocalKeywords.Concat(fragmentSubProgram.GlobalKeywords));

						writer.WriteIndent(3);
						writer.WriteLine($"// Keywords: {keywordsList}");

						// needs to move somewhere else...
						DirectXCompiledShader? dxShader = fragmentConverter!.DxShader;
						Debug.Assert(dxShader != null);
						bool hasFrontFace = dxShader.Isgn.inputs.Any(i => i.name == "SV_IsFrontFace");

						writer.WriteIndent(3);

						string args = $"{USILConstants.VERT_TO_FRAG_STRUCT_NAME} {USILConstants.FRAG_INPUT_NAME}";
						if (hasFrontFace)
						{
							// not part of v2f
							args += $", float facing: VFACE";
						}
						writer.WriteLine($"{USILConstants.FRAG_OUTPUT_STRUCT_NAME} frag({args})");
						writer.WriteIndent(3);
						writer.WriteLine("{");

						fragmentConverter.ConvertShaderToUShaderProgram();
						fragmentConverter.ApplyMetadataToProgram(fragmentSubProgram, writer.Version);
						string progamText = fragmentConverter.CovnertUShaderProgramToHLSL(4);
						writer.Write(progamText);

						writer.WriteIndent(3);
						writer.WriteLine("}");
					}

					writer.WriteIndent(3);
					}
                    if(preserveVariants) writer.WriteLine("#else\n#error Original shader has no recovered variant for this keyword combination\n#endif");
                    writer.WriteLine("ENDCG");
				}
				else
				{
					throw new NotSupportedException($"Unsupported pass type {_this.Type}");
				}

				writer.WriteIndent(2);
				writer.Write("}\n");
			}
		}

		private static void ExportPassConstantBufferDefinitions(
			ShaderSubProgram _this, ShaderWriter writer, HashSet<string?> declaredBufs,
			string cbufferName, int depth)
		{
			ConstantBuffer? cbuffer = _this.ConstantBuffers.FirstOrDefault(cb => cb.Name == cbufferName);

			ExportPassConstantBufferDefinitions(_this, writer, declaredBufs, cbuffer, depth);
		}

		private static void ExportPassConstantBufferDefinitions(
			ShaderSubProgram _this, ShaderWriter writer, HashSet<string?> declaredBufs,
			ConstantBuffer? cbuffer, int depth)
		{
			if (cbuffer != null)
			{
				bool nonGlobalCbuffer = cbuffer.Name != "$Globals";

				if (nonGlobalCbuffer)
				{
					writer.WriteIndent(depth);
					writer.WriteLine($"CBUFFER_START({cbuffer.Name})");
					depth++;
				}

				NumericShaderParameter[] allParams = cbuffer.AllNumericParams;
				foreach (NumericShaderParameter param in allParams)
				{
					string typeName = DXShaderNamingUtils.GetConstantBufferParamTypeName(param);
					string? name = param.Name;

					// skip things like unity_MatrixVP if they show up in $Globals
					if (UnityShaderConstants.INCLUDED_UNITY_PROP_NAMES.Contains(name))
					{
						continue;
					}

					if (!declaredBufs.Contains(name))
					{
						if (param.ArraySize > 0)
						{
							writer.WriteIndent(depth);
							writer.WriteLine($"{typeName} {name}[{param.ArraySize}];");
						}
						else
						{
							writer.WriteIndent(depth);
							writer.WriteLine($"{typeName} {name};");
						}
						declaredBufs.Add(name);
					}
				}

				if (nonGlobalCbuffer)
				{
					depth--;
					writer.WriteIndent(depth);
					writer.WriteLine("CBUFFER_END");
				}
			}
		}

		private static string GetTextureDeclaration(TextureParameter param, IEnumerable<SHDRInstruction> instructions)
        {
            if (param.MultiSampled) throw new NotSupportedException("Multisampled texture declaration: " + param.Name);
            var modes = instructions.Where(i => i.opcode == Opcode.dcl_sampler &&
                i.declData != null && i.declData.samplerIndex == param.SamplerIndex)
                .Select(i => i.declData!.samplerMode).Distinct().ToArray();
            if (modes.Length > 1) throw new InvalidDataException("Conflicting original sampler modes: " + param.Name);
            bool comparison = modes.Length == 1 && modes[0] == SamplerMode.Comparison;
            string kind = param.Dim switch {
                2 => "Texture2D", 3 => "Texture3D", 4 => "TextureCube",
                5 => "Texture2DArray", 6 => "TextureCubeArray",
                _ => throw new NotSupportedException("Unknown original texture dimension: " + param.Dim)
            };
            if (comparison && param.Dim == 3) throw new NotSupportedException("Comparison sampler on 3D texture");
            string element = comparison ? "float" : "float4";
            string sampler = comparison ? "SamplerComparisonState" : "SamplerState";
            return $"{kind}<{element}> {param.Name}; {sampler} sampler{param.Name};";
        }

		private static void ExportPassTextureParamDefinitions(ShaderSubProgram program, DirectXCompiledShader dxShader,
            ShaderWriter writer, HashSet<string?> declaredBufs, int depth)
        {
            var unsignedBuffers = new Dictionary<int, int>();
            var vertexBuffers = new HashSet<int>();
            var instructions = dxShader.Shdr.shaderInstructions;
            for (int i = 0; i < instructions.Count; i++)
            {
                if (DirectXProgramToUSIL.TryBuildClosedUnsignedRegion(dxShader.Shdr, i, out _, out int regionBinding, out int regionEnd))
                {
                    unsignedBuffers[regionBinding] = 1;
                    i = regionEnd;
                    continue;
                }
                if (DirectXProgramToUSIL.IsDynamicUnsignedLoadPair(dxShader.Shdr, i, out int pairBinding))
                {
                    unsignedBuffers[pairBinding] = 1;
                    i += 3;
                    continue;
                }
                if (i + 2 < instructions.Count &&
                    DirectXProgramToUSIL.IsSupportedStructuredUintLoad(dxShader.Shdr, instructions[i], instructions[i + 1], instructions[i + 2], out int binding, out int width))
                {
                    unsignedBuffers[binding] = width;
                }
                if (DirectXProgramToUSIL.IsSupportedStructuredVertexLoad(dxShader, instructions[i], out int vertexBinding))
                {
                    vertexBuffers.Add(vertexBinding);
                }
            }
            foreach (BufferBinding buffer in program.BufferParameters)
            {
                bool vertexBuffer = vertexBuffers.Contains(buffer.Index);
                if (!unsignedBuffers.TryGetValue(buffer.Index, out int width) && !vertexBuffer) continue;
                if (!System.Text.RegularExpressions.Regex.IsMatch(buffer.Name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                    throw new InvalidDataException("Invalid structured buffer binding name.");
                if (!declaredBufs.Add(buffer.Name)) continue;
                if (vertexBuffer)
                {
                    ExportStructuredWords3Buffer(buffer.Name, writer, depth);
                    continue;
                }
                string elementType = width == 1 ? "uint" : "uint4";
                string rawType = width == 1 ? "uint" : "uint3";
                string resultType = width == 1 ? "float" : "float3";
                string components = width == 1 ? "" : ".xyz";
                writer.WriteIndent(depth); writer.WriteLine($"StructuredBuffer<{elementType}> {buffer.Name};");
                writer.WriteLine("#if defined(SHADER_API_METAL)");
                writer.WriteIndent(depth); writer.WriteLine($"int _NativeH3VRBufferCount{buffer.Name};");
                writer.WriteLine("#endif");
                writer.WriteIndent(depth); writer.WriteLine($"{rawType} NativeStructuredUintRaw{buffer.Name}(uint index) {{");
                writer.WriteLine("#if defined(SHADER_API_METAL)");
                writer.WriteIndent(depth + 1); writer.WriteLine($"uint count = (uint)max(_NativeH3VRBufferCount{buffer.Name}, 0);");
                writer.WriteLine("#elif defined(SHADER_API_D3D11)");
                writer.WriteIndent(depth + 1); writer.WriteLine($"uint count, stride; {buffer.Name}.GetDimensions(count, stride);");
                writer.WriteLine("#else"); writer.WriteLine("#error Structured buffer bounds are unsupported on this shader backend"); writer.WriteLine("#endif");
                // Unity 2021's Metal translator may speculate a load past an
                // early return. Clamp the actual load, then select zero. The host
                // must bind at least a one-element fallback when count is zero.
                writer.WriteIndent(depth + 1); writer.WriteLine("uint safeIndex = min(index, max(count, 1u) - 1u);");
                writer.WriteIndent(depth + 1); writer.WriteLine($"{rawType} value = {buffer.Name}[safeIndex]{components};");
                writer.WriteIndent(depth + 1); writer.WriteLine($"return index < count ? value : ({rawType})0;");
                writer.WriteIndent(depth); writer.WriteLine("}");
                writer.WriteIndent(depth); writer.WriteLine($"{resultType} NativeStructuredUintLoad{buffer.Name}(float v) {{");
                writer.WriteIndent(depth + 1); writer.WriteLine("uint index; if (isnan(v) || v <= 0.0) index = 0u; else if (v >= 4294967296.0) index = 0xffffffffu; else index = (uint)v;");
                writer.WriteIndent(depth + 1); writer.WriteLine($"return ({resultType})NativeStructuredUintRaw{buffer.Name}(index);");
                writer.WriteIndent(depth); writer.WriteLine("}");
            }
            foreach (TextureParameter param in program.TextureParameters)
            {
                string name = param.Name;
                if (declaredBufs.Contains(name) ||
                    (UnityShaderConstants.BUILTIN_TEXTURE_NAMES.Contains(name) && name != "_ShadowMapTexture")) continue;
                // UnityCG supplies this one only inside its deprecated collector
                // helpers; ordinary recovered lighting passes still need it.
                if (name == "_ShadowMapTexture") {
                    writer.WriteIndent(depth);
                    writer.WriteLine("#if !defined(SHADOW_COLLECTOR_PASS) || defined(SHADOWMAPSAMPLER_DEFINED)");
                }
                writer.WriteIndent(depth);
                writer.WriteLine(GetTextureDeclaration(param, dxShader.Shdr.shaderInstructions));
                if (name == "_ShadowMapTexture") {
                    writer.WriteIndent(depth); writer.WriteLine("#endif");
                }
                declaredBufs.Add(name);
            }
        }

        private static void ExportStructuredWords3Buffer(string name, ShaderWriter writer, int depth)
        {
            string element = $"NativeStructuredWords3{name}";
            string count = $"_NativeH3VRBufferCount{name}";
            // Metal aligns a uint3 member to 16 bytes. Three scalar words keep the DXBC stride.
            writer.WriteIndent(depth); writer.WriteLine($"struct {element} {{ uint x; uint y; uint z; }};");
            writer.WriteIndent(depth); writer.WriteLine($"StructuredBuffer<{element}> {name};");
            writer.WriteLine("#if defined(SHADER_API_METAL)");
            writer.WriteIndent(depth); writer.WriteLine($"int {count};");
            writer.WriteLine("#endif");
            writer.WriteIndent(depth); writer.WriteLine($"float3 NativeStructuredWords3Load{name}(uint index) {{");
            writer.WriteLine("#if defined(SHADER_API_METAL)");
            writer.WriteIndent(depth + 1); writer.WriteLine($"uint count = (uint)max({count}, 0);");
            writer.WriteLine("#elif defined(SHADER_API_D3D11)");
            writer.WriteIndent(depth + 1); writer.WriteLine($"uint count, stride; {name}.GetDimensions(count, stride);");
            writer.WriteLine("#else"); writer.WriteLine("#error Structured buffer bounds are unsupported on this shader backend"); writer.WriteLine("#endif");
            // Clamp the issued load even when HLSLcc evaluates it before the bounds predicate.
            writer.WriteIndent(depth + 1); writer.WriteLine("uint safeIndex = min(index, max(count, 1u) - 1u);");
            writer.WriteIndent(depth + 1); writer.WriteLine($"{element} words = {name}[safeIndex];");
            writer.WriteIndent(depth + 1); writer.WriteLine("return index < count ? asfloat(uint3(words.x, words.y, words.z)) : float3(0.0, 0.0, 0.0);");
            writer.WriteIndent(depth); writer.WriteLine("}");
        }

		private static void ExportSerializedProgramDecomp(ISerializedProgram _this, ShaderWriter writer, ShaderType type)
		{
			if (_this.Has_PlayerSubPrograms())
			{
				if (_this.GetPlayerSubPrograms().Count == 0)
				{
					return;
				}
			}
			else
			{
				if (_this.SubPrograms.Count == 0)
				{
					return;
				}
			}

			writer.WriteIndent(3);
			writer.Write($"Program \"{type.ToProgramTypeString()}\" {{\n");
			if (_this.Has_PlayerSubPrograms())
			{
				IReadOnlyList<ISerializedPlayerSubProgram> subPrograms = _this.GetPlayerSubPrograms();
				for (int i = 0; i < subPrograms.Count; i++)
				{
					subPrograms[i].Export(_this.GetParameterBlobIndices()[i], writer, type);
				}
			}
			else
			{
				int tierCount = _this.GetTierCount();
				for (int i = 0; i < _this.SubPrograms.Count; i++)
				{
					_this.SubPrograms[i].Export(writer, type, tierCount > 1);
				}
			}
			writer.WriteIndent(3);
			writer.Write("}\n");
		}

		private static List<ShaderSubProgram> GetSubPrograms(IShader shader, ShaderSubProgramBlob[] blobs, ISerializedProgram program, UnityVersion version, BuildTarget platform, ShaderType shaderType, ISerializedPass pass)
		{
			List<ShaderSubProgram> matchingPrograms = new();
			ShaderSubProgram? fallbackProgram = null;

			int subProgramCount = program.Has_PlayerSubPrograms() ? program.GetPlayerSubPrograms().Count : program.SubPrograms.Count;
			for (int i = 0; i < subProgramCount; i++)
			{
				ShaderGpuProgramType programType;
				if (program.Has_PlayerSubPrograms())
				{
					programType = program.GetPlayerSubPrograms()[i].GetProgramType(version);
				}
				else
				{
					programType = program.SubPrograms[i].GetProgramType(version);
				}
				GPUPlatform graphicApi = programType.ToGPUPlatform(platform);

				if (graphicApi != GPUPlatform.d3d11)
				{
					continue;
				}

				bool matched = false;

				switch (programType)
				{
					case ShaderGpuProgramType.DX11VertexSM40:
					case ShaderGpuProgramType.DX11VertexSM50:
						if (shaderType == ShaderType.Vertex)
						{
							matched = true;
						}

						break;
					case ShaderGpuProgramType.DX11PixelSM40:
					case ShaderGpuProgramType.DX11PixelSM50:
						if (shaderType == ShaderType.Fragment)
						{
							matched = true;
						}

						break;
				}

				ShaderSubProgram? matchedProgram = null;
				if (matched && shader.Has_Platforms())
				{
					int platformIndex = shader.Platforms.IndexOf((uint)graphicApi);
					if (program.Has_PlayerSubPrograms())
					{
						matchedProgram = blobs[platformIndex].GetSubProgram(program.GetPlayerSubPrograms()[i].BlobIndex, program.GetParameterBlobIndices()[i]);
					}
					else
					{
						matchedProgram = blobs[platformIndex].GetSubProgram(program.SubPrograms[i].BlobIndex);
					}
				}

				if (!PreserveVariantsFor(shader)) {
				// skip instanced shaders
				Utf8String INSTANCING_ON = "INSTANCING_ON"u8;
				if (pass.NameIndices.ContainsKey(INSTANCING_ON))
				{
					if (program.Has_PlayerSubPrograms())
					{
						ISerializedPlayerSubProgram playerSubProgram = program.GetPlayerSubPrograms()[i];
						for (int j = 0; j < playerSubProgram.KeywordIndices.Count; j++)
						{
							if (pass.NameIndices[INSTANCING_ON] == playerSubProgram.KeywordIndices[j])
							{
								matched = false;
							}
						}
					}
					else
					{
						ISerializedSubProgram subProgram = program.SubPrograms[i];
						if (subProgram.GlobalKeywordIndices != null)
						{
							for (int j = 0; j < subProgram.GlobalKeywordIndices.Count; j++)
							{
								if (pass.NameIndices[INSTANCING_ON] == subProgram.GlobalKeywordIndices[j])
								{
									matched = false;
								}
							}
						}
						if (subProgram.LocalKeywordIndices != null)
						{
							for (int j = 0; j < subProgram.LocalKeywordIndices.Count; j++)
							{
								if (pass.NameIndices[INSTANCING_ON] == subProgram.LocalKeywordIndices[j])
								{
									matched = false;
								}
							}
						}
					}
				}

				Utf8String DIRECTIONAL = (Utf8String)"DIRECTIONAL";
				bool hasDirectional = false;
				bool matchesDirectional = false;
				if (pass.NameIndices.ContainsKey(DIRECTIONAL))
				{
					hasDirectional = true;
					if (program.Has_PlayerSubPrograms())
					{
						ISerializedPlayerSubProgram playerSubProgram = program.GetPlayerSubPrograms()[i];
						for (int j = 0; j < playerSubProgram.KeywordIndices.Count; j++)
						{
							if (pass.NameIndices[DIRECTIONAL] == playerSubProgram.KeywordIndices[j])
							{
								matchesDirectional = true;
							}
						}
					}
					else
					{
						ISerializedSubProgram subProgram = program.SubPrograms[i];
						if (subProgram.GlobalKeywordIndices != null)
						{
							for (int j = 0; j < subProgram.GlobalKeywordIndices.Count; j++)
							{
								if (pass.NameIndices[DIRECTIONAL] == subProgram.GlobalKeywordIndices[j])
								{
									matchesDirectional = true;
								}
							}
						}
						if (subProgram.LocalKeywordIndices != null)
						{
							for (int j = 0; j < subProgram.LocalKeywordIndices.Count; j++)
							{
								if (pass.NameIndices[DIRECTIONAL] == subProgram.LocalKeywordIndices[j])
								{
									matchesDirectional = true;
								}
							}
						}
					}
				}

				Utf8String POINT = (Utf8String)"POINT";
				bool hasPoint = false;
				bool matchesPoint = false;
				if (pass.NameIndices.ContainsKey(POINT))
				{
					hasPoint = true;
					if (program.Has_PlayerSubPrograms())
					{
						ISerializedPlayerSubProgram playerSubProgram = program.GetPlayerSubPrograms()[i];
						for (int j = 0; j < playerSubProgram.KeywordIndices.Count; j++)
						{
							if (pass.NameIndices[POINT] == playerSubProgram.KeywordIndices[j])
							{
								matchesPoint = true;
							}
						}
					}
					else
					{
						ISerializedSubProgram subProgram = program.SubPrograms[i];
						if (subProgram.GlobalKeywordIndices != null)
						{
							for (int j = 0; j < subProgram.GlobalKeywordIndices.Count; j++)
							{
								if (pass.NameIndices[POINT] == subProgram.GlobalKeywordIndices[j])
								{
									matchesPoint = true;
								}
							}
						}
						if (subProgram.LocalKeywordIndices != null)
						{
							for (int j = 0; j < subProgram.LocalKeywordIndices.Count; j++)
							{
								if (pass.NameIndices[POINT] == subProgram.LocalKeywordIndices[j])
								{
									matchesPoint = true;
								}
							}
						}
					}
				}

				if ((hasDirectional || hasPoint) && !matchesDirectional && !matchesPoint)
				{
					matched = false;
				}

                }

				if (matchedProgram != null)
				{
					if (matched)
					{
						matchingPrograms.Add(matchedProgram);
					}
					else if (fallbackProgram == null)
					{
						// we don't want a case where no programs match, so pick at least one
						fallbackProgram = matchedProgram;
					}
				}
			}

			if (matchingPrograms.Count == 0 && fallbackProgram != null)
			{
				matchingPrograms.Add(fallbackProgram);
			}

			return matchingPrograms;
		}

		// this is necessary because sometimes vertex programs can have more
		// variations than frag programs and vice versa. we need to collect
		// all possible combos for both. if one has more combos than the other,
		// they will always be at the end (I think).
		private static List<string[]> GetAllVariantCombos(List<ShaderSubProgram> subPrograms, UnityVersion version, BuildTarget platform, List<GPUPlatform> progPlats)
		{
			List<string[]> combos = new List<string[]>();
			HashSet<string> comboHashes = new HashSet<string>();
			foreach (ShaderSubProgram subProgram in subPrograms)
			{
				ShaderGpuProgramType programType = subProgram.GetProgramType(version);
				GPUPlatform graphicApi = programType.ToGPUPlatform(platform);

				if (!progPlats.Contains(graphicApi))
				{
					continue;
				}

				List<string> keywords = new List<string>();

				if (subProgram.GlobalKeywords.Length > 0)
				{
					keywords.AddRange(subProgram.GlobalKeywords);
				}

				if (subProgram.LocalKeywords.Length > 0)
				{
					keywords.AddRange(subProgram.LocalKeywords);
				}

				// don't have to worry about order I don't think
				// although it would probably be safer to sort
				string keywordsStr = string.Join(',', keywords);

				if (!comboHashes.Contains(keywordsStr))
				{
					comboHashes.Add(keywordsStr);
					combos.Add(keywords.ToArray());
				}
			}

			return combos;
		}

		// DUPLICATE CODE!!!!
		private static byte[] TrimShaderBytes(ShaderSubProgram subProgram, UnityVersion version, BuildTarget platform)
		{
			ShaderGpuProgramType programType = subProgram.GetProgramType(version);
			GPUPlatform graphicApi = programType.ToGPUPlatform(platform);

			int dataOffset = 0;
			if (DXDataHeader.HasHeader(graphicApi))
			{
				dataOffset = DXDataHeader.GetDataOffset(version, graphicApi, subProgram.ProgramData[0]);
			}

			return GetRelevantData(subProgram.ProgramData, dataOffset);
		}

		private static byte[] GetRelevantData(byte[] bytes, int offset)
		{
			if (bytes == null)
			{
				throw new ArgumentNullException(nameof(bytes));
			}

			if (offset < 0 || offset > bytes.Length)
			{
				throw new ArgumentOutOfRangeException(nameof(offset));
			}

			int size = bytes.Length - offset;
			byte[] result = new byte[size];
			for (int i = 0; i < size; i++)
			{
				result[i] = bytes[i + offset];
			}
			return result;
		}
	}
}
