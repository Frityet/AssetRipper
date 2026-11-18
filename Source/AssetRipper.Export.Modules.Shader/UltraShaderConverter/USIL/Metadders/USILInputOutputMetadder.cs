using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.DirectX;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL.Metadders
{
	public class USILInputOutputMetadder : IUSILOptimizer
	{
		public bool Run(UShaderProgram shader, ShaderSubProgram shaderData)
		{
			// DXBC packs unrelated semantics into different lanes of one output register.
            // Evaluate the original instruction once, then distribute every written lane.
            List<USILInstruction> instructions = new();
            int packedIndex = 0;
            foreach (var inst in shader.instructions) {
                var dest = inst.destOperand;
                var outputs = dest?.operandType == USILOperandType.OutputRegister
                    ? shader.outputs.Where(o => o.register == dest.registerIndex && dest.mask.Any(m => (o.mask & (1 << m)) != 0)).ToList()
                    : new List<USILInputOutput>();
                if (outputs.Count <= 1) { instructions.Add(inst); continue; }
                string name = "packedOutput" + packedIndex++;
                shader.locals.Add(new USILLocal("float4", name, USILLocalType.Vector4));
                var temporary = new USILOperand(dest!) { operandType = USILOperandType.TempRegister, metadataName = name, metadataNameAssigned = true };
                inst.destOperand = temporary;
                instructions.Add(inst);
                foreach (var output in outputs) {
                    int[] lanes = dest!.mask.Where(m => (output.mask & (1 << m)) != 0).ToArray();
                    instructions.Add(new USILInstruction {
                        instructionType = USILInstructionType.Move,
                        destOperand = new USILOperand(dest) { mask = lanes },
                        srcOperands = new() { new USILOperand(temporary) { mask = lanes } }
                    });
                }
            }
            shader.instructions = instructions;
			foreach (USILInstruction instruction in instructions)
			{
				if (instruction.destOperand != null)
				{
					UseMetadata(instruction.destOperand, shader);
				}
				foreach (USILOperand operand in instruction.srcOperands)
				{
					UseMetadata(operand, shader);
				}
			}
			return true; // any changes made?
		}

		private static void UseMetadata(USILOperand operand, UShaderProgram shader)
		{
			if (operand.operandType == USILOperandType.InputRegister)
			{
				var matches = operand.mask.Select(m => shader.inputs.First(i => i.register == operand.registerIndex && (i.mask & (1 << m)) != 0)).Distinct().ToList();
                if (matches.Count > 1) {
                    var components = operand.mask.Select(m => new USILOperand(operand) { mask = new[] { m }, negative = false, absoluteValue = false }).ToArray();
                    foreach (var component in components) UseMetadata(component, shader);
                    operand.operandType = USILOperandType.Multiple;
                    operand.children = components;
                    operand.mask = Array.Empty<int>();
                    operand.displayMask = false;
                    return;
                }
                int searchMask = operand.mask.Length != 0 ? 1 << operand.mask[0] : 0;
				USILInputOutput input = shader.inputs.First(
					i => i.register == operand.registerIndex && (searchMask & i.mask) == searchMask
				);

				// correct mask
				operand.mask = MatchMaskToInputOutput(operand.mask, input.mask, true);

				if (shader.shaderFunctionType == UShaderFunctionType.Vertex && input.isVertexId)
				{
					operand.metadataName = USILConstants.VERTEX_ID_INPUT_NAME;
					operand.displayMask = operand.mask.Length > 1;
				}
				else if (shader.shaderFunctionType == UShaderFunctionType.Fragment && input.type == "SV_IsFrontFace")
				{
					operand.metadataName = input.name;
				}
				else
				{
					operand.metadataName = shader.shaderFunctionType switch
					{
						UShaderFunctionType.Vertex => $"{USILConstants.VERT_INPUT_NAME}.{input.name}",
						UShaderFunctionType.Fragment => $"{USILConstants.FRAG_INPUT_NAME}.{input.name}",
						_ => $"unk_input.{input.name}",
					};
				}

				operand.metadataNameAssigned = true;
			}
			else if (operand.operandType == USILOperandType.OutputRegister)
			{
				int searchMask = 0;
				for (int i = 0; i < operand.mask.Length; i++)
				{
					searchMask |= 1 << operand.mask[i];
				}

				List<USILInputOutput> outputs = shader.outputs.Where(
					o => o.register == operand.registerIndex && (searchMask & o.mask) != 0
				).ToList();

				foreach (USILInputOutput output in outputs)
				{
					// correct mask
					int[] matchedMask = MatchMaskToInputOutput(operand.mask, output.mask, true);
					int[] realMatchedMask = MatchMaskToInputOutput(operand.mask, output.mask, false);
					operand.mask = matchedMask;

					operand.metadataName = shader.shaderFunctionType switch
					{
						UShaderFunctionType.Vertex => $"{USILConstants.VERT_OUTPUT_LOCAL_NAME}.{output.name}",
						UShaderFunctionType.Fragment => $"{USILConstants.FRAG_OUTPUT_LOCAL_NAME}.{output.name}",
						_ => $"unk_output.{output.name}",
					};
					operand.metadataNameAssigned = true;
				}
			}
			else if (DXShaderNamingUtils.HasSpecialInputOutputName(operand.operandType))
			{
				string name = DXShaderNamingUtils.GetSpecialInputOutputName(operand.operandType);

				operand.metadataName = shader.shaderFunctionType switch
				{
					UShaderFunctionType.Vertex => $"{USILConstants.VERT_OUTPUT_LOCAL_NAME}.{name}",
					UShaderFunctionType.Fragment => $"{USILConstants.FRAG_OUTPUT_LOCAL_NAME}.{name}",
					_ => $"unk_special.{name}",
				};
				operand.metadataNameAssigned = true;
			}
		}

		private static int[] MatchMaskToInputOutput(int[] mask, int maskTest, bool moveSwizzles)
		{
			// Move swizzles (for example, .zw -> .xy) based on first letter
			int moveCount = 0;
			int i;
			for (i = 0; i < 4; i++)
			{
				if ((maskTest & 1) == 1)
				{
					break;
				}

				moveCount++;
				maskTest >>= 1;
			}

			// Count remaining 1 bits
			int bitCount = 0;
			for (; i < 4; i++)
			{
				if ((maskTest & 1) == 0)
				{
					break;
				}

				bitCount++;
				maskTest >>= 1;
			}

			List<int> result = new List<int>();
			for (int j = 0; j < mask.Length; j++)
			{
				if (mask[j] >= moveCount && mask[j] < bitCount + moveCount)
				{
					if (moveSwizzles)
					{
						result.Add(mask[j] - moveCount);
					}
					else
					{
						result.Add(mask[j]);
					}
				}
			}
			return result.ToArray();
		}
	}
}
