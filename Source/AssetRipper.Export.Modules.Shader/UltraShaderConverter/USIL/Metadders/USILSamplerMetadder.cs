using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;
using AssetRipper.Import.Logging;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Metadders
{
	public class USILSamplerMetadder : IUSILOptimizer
	{
		public bool Run(UShaderProgram shader, ShaderSubProgram shaderData)
		{
			List<USILInstruction> instructions = shader.instructions;
			foreach (USILInstruction instruction in instructions)
			{
				foreach (USILOperand operand in instruction.srcOperands)
				{
					if (operand.operandType == USILOperandType.SamplerRegister)
					{
						TextureParameter? texParam = shaderData.TextureParameters.FirstOrDefault(
							p => p.SamplerIndex == operand.registerIndex
						);

						if (texParam == null)
						{
							operand.operandType = USILOperandType.Sampler2D;
							Logger.Warning($"Could not find texture parameter for sampler {operand}");
							continue;
						}

						int dimension = texParam.Dim;
						switch (dimension)
						{
							case 2:
								operand.operandType = USILOperandType.Sampler2D;
								break;
							case 3:
								operand.operandType = USILOperandType.Sampler3D;
								break;
							case 4:
								operand.operandType = USILOperandType.SamplerCube;
								break;
							case 5:
								operand.operandType = USILOperandType.Sampler2DArray;
								break;
							case 6:
								operand.operandType = USILOperandType.SamplerCubeArray;
								break;
						}

						if (texParam != null)
						{
							operand.metadataName = texParam.Name;
							operand.metadataNameAssigned = true;
						}
					}
					else if (operand.operandType == USILOperandType.ResourceRegister)
					{
						if (instruction.instructionType is USILInstructionType.LoadStructuredUintToFloat
							or USILInstructionType.LoadStructuredWords3 or USILInstructionType.ClosedUnsignedRegion
							or USILInstructionType.DynamicUnsignedLoadPair)
						{
							var matches = shaderData.BufferParameters.Where(p => p.Index == operand.registerIndex).ToArray();
							if (matches.Length != 1 || string.IsNullOrEmpty(matches[0].Name)) throw new InvalidDataException("Missing or ambiguous structured buffer binding.");
							operand.metadataName = matches[0].Name; operand.metadataNameAssigned = true; continue;
						}
						TextureParameter? texParam = shaderData.TextureParameters.FirstOrDefault(
							p => p.Index == operand.registerIndex
						);

						if (texParam == null)
						{
							Logger.Warning($"Could not find texture parameter for resource {operand}");
							continue;
						}

						if (texParam != null)
						{
							operand.metadataName = texParam.Name;
							operand.metadataNameAssigned = true;
						}
					}
				}
			}
			return true; // any changes made?
		}
	}
}
