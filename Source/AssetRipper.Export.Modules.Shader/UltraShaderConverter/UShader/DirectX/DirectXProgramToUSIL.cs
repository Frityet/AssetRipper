using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL;
using System.Diagnostics;
using System.Text;

namespace AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.DirectX
{
	public class DirectXProgramToUSIL
	{
		private readonly DirectXCompiledShader _dxShader;

		public UShaderProgram shader;

		private List<USILLocal> Locals => shader.locals;
		private List<USILInstruction> Instructions => shader.instructions;
		private List<USILInputOutput> Inputs => shader.inputs;
		private List<USILInputOutput> Outputs => shader.outputs;

		private delegate void InstHandler(SHDRInstruction inst);
		private readonly Dictionary<Opcode, InstHandler> _instructionHandlers;

		private readonly Dictionary<int, ResourceDimension> _resourceToDimension;

		public DirectXProgramToUSIL(DirectXCompiledShader dxShader)
		{
			_dxShader = dxShader;

			shader = new UShaderProgram();

			_instructionHandlers = new()
			{
				{ Opcode.mov, new InstHandler(HandleMov) },
				{ Opcode.movc, new InstHandler(HandleMovc) },
				{ Opcode.add, new InstHandler(HandleAdd) },
				{ Opcode.iadd, new InstHandler(HandleAdd) },
				{ Opcode.mul, new InstHandler(HandleMul) },
				{ Opcode.imul, new InstHandler(HandleIMul) },
				{ Opcode.umul, new InstHandler(HandleIMul) },
				{ Opcode.div, new InstHandler(HandleDiv) },
				{ Opcode.udiv, new InstHandler(HandleDiv) },
				{ Opcode.mad, new InstHandler(HandleMad) },
				{ Opcode.imad, new InstHandler(HandleMad) },
				{ Opcode.and, new InstHandler(HandleAnd) },
				{ Opcode.or, new InstHandler(HandleOr) },
                { Opcode.xor, new InstHandler(HandleXor) },
                { Opcode.ineg, new InstHandler(HandleIntegerNegate) },
				{ Opcode.not, new InstHandler(HandleNot) },
				{ Opcode.ftoi, new InstHandler(HandleFtoi) },
				{ Opcode.ftou, new InstHandler(HandleFtoi) },
				{ Opcode.itof, new InstHandler(HandleItof) },
				{ Opcode.utof, new InstHandler(HandleItof) },
				{ Opcode.min, new InstHandler(HandleMin) },
				{ Opcode.imin, new InstHandler(HandleMin) },
				{ Opcode.umin, new InstHandler(HandleMin) },
				{ Opcode.max, new InstHandler(HandleMax) },
				{ Opcode.imax, new InstHandler(HandleMax) },
				{ Opcode.umax, new InstHandler(HandleMax) },
				{ Opcode.sqrt, new InstHandler(HandleSqrt) },
				{ Opcode.rsq, new InstHandler(HandleRsq) },
				{ Opcode.log, new InstHandler(HandleLog) },
				{ Opcode.exp, new InstHandler(HandleExp) },
				{ Opcode.rcp, new InstHandler(HandleRcp) },
				{ Opcode.frc, new InstHandler(HandleFrc) },
				{ Opcode.round_ni, new InstHandler(HandleRoundNi) },
				{ Opcode.round_pi, new InstHandler(HandleRoundPi) },
				{ Opcode.round_ne, new InstHandler(HandleRoundNe) },
				{ Opcode.round_z, new InstHandler(HandleRoundZ) },
				{ Opcode.sincos, new InstHandler(HandleSincos) },
				{ Opcode.ishl, new InstHandler(HandleIShl) },
				{ Opcode.ishr, new InstHandler(HandleIShr) },
				{ Opcode.ushr, new InstHandler(HandleIShr) },
				{ Opcode.dp2, new InstHandler(HandleDp2) },
				{ Opcode.dp3, new InstHandler(HandleDp3) },
				{ Opcode.dp4, new InstHandler(HandleDp4) },
				{ Opcode.sample, new InstHandler(HandleSample) },
				{ Opcode.sample_c, new InstHandler(HandleSampleC) },
				{ Opcode.sample_c_lz, new InstHandler(HandleSampleC) },
				{ Opcode.sample_l, new InstHandler(HandleSampleL) },
				{ Opcode.sample_b, new InstHandler(HandleSampleL) },
				{ Opcode.sample_d, new InstHandler(HandleSampleD) },
				{ Opcode.gather4, new InstHandler(HandleGather) },
				{ Opcode.ld, new InstHandler(HandleLd) },
				{ Opcode.ldms, new InstHandler(RejectMultisampledLoad) },
				{ Opcode.ld_structured, new InstHandler(HandleLdStructured) },
				{ Opcode.discard, new InstHandler(HandleDiscard) },
				{ Opcode.resinfo, new InstHandler(HandleResInfo) },
				{ Opcode.sampleinfo, new InstHandler(HandleSampleInfo) },
				{ Opcode.deriv_rtx, new InstHandler(HandleDerivRt) },
				{ Opcode.deriv_rty, new InstHandler(HandleDerivRt) },
				{ Opcode.deriv_rtx_coarse, new InstHandler(HandleDerivRt) },
				{ Opcode.deriv_rty_coarse, new InstHandler(HandleDerivRt) },
				{ Opcode.deriv_rtx_fine, new InstHandler(HandleDerivRt) },
				{ Opcode.deriv_rty_fine, new InstHandler(HandleDerivRt) },
				{ Opcode.@if, new InstHandler(HandleIf) },
				{ Opcode.@else, new InstHandler(HandleElse) },
				{ Opcode.endif, new InstHandler(HandleEndIf) },
				{ Opcode.loop, new InstHandler(HandleLoop) },
				{ Opcode.endloop, new InstHandler(HandleEndLoop) },
				{ Opcode.@break, new InstHandler(HandleBreak) },
				{ Opcode.breakc, new InstHandler(HandleBreakc) },
				{ Opcode.@continue, new InstHandler(HandleContinue) },
				{ Opcode.continuec, new InstHandler(HandleContinuec) },
				{ Opcode.eq, new InstHandler(HandleEq) },
				{ Opcode.ieq, new InstHandler(HandleEq) },
				{ Opcode.ne, new InstHandler(HandleNeq) },
				{ Opcode.ine, new InstHandler(HandleNeq) },
				{ Opcode.lt, new InstHandler(HandleLt) },
				{ Opcode.ilt, new InstHandler(HandleLt) },
				{ Opcode.ult, new InstHandler(HandleLt) },
				{ Opcode.ge, new InstHandler(HandleGe) },
				{ Opcode.ige, new InstHandler(HandleGe) },
				{ Opcode.uge, new InstHandler(HandleGe) },
				{ Opcode.ret, new InstHandler(HandleRet) },
                ////dec
                { Opcode.dcl_temps, new InstHandler(HandleTemps) },
				{ Opcode.dcl_resource, new InstHandler(HandleResource) },
				{ Opcode.customdata, new InstHandler(HandleCustomData) }
			};

			_resourceToDimension = new Dictionary<int, ResourceDimension>();
		}

		public void Convert()
		{
			ConvertInstructions();
			ConvertInputs();
			ConvertOutputs();
		}

		private void ConvertInstructions()
		{
			SHDR shdr = _dxShader.Shdr;

			AddOutputLocal();

			List<SHDRInstruction> instructions = shdr.shaderInstructions;
			for (int i = 0; i < instructions.Count; i++)
			{
				SHDRInstruction inst = instructions[i];
				if (TryBuildClosedUnsignedRegion(shdr, i, out var region, out int binding, out int end))
                {
                    var buffer = new USILOperand { operandType = USILOperandType.ResourceRegister, registerIndex = binding, mask = new[] { 0 } };
                    Instructions.Add(new USILInstruction { instructionType = USILInstructionType.ClosedUnsignedRegion, srcOperands = new() { buffer }, unsignedRegion = region });
                    // The matcher admits only output moves that neither read nor write
                    // region state. Their original order is preserved when hoisted.
                    for (int j = i; j <= end; j++) if (instructions[j].opcode == Opcode.mov) HandleMov(instructions[j]);
                    i = end;
                    continue;
                }
				if (i + 3 < instructions.Count && IsDynamicUnsignedLoadPair(shdr, i, out int pairBinding))
                {
                    var value = new USILOperand(); var next = new USILOperand(); var input = new USILOperand();
                    FillUSILOperand(instructions[i].operands[0], value, instructions[i].operands[0].swizzle, false);
                    FillUSILOperand(instructions[i + 1].operands[0], next, instructions[i + 1].operands[0].swizzle, false);
                    FillUSILOperand(instructions[i].operands[1], input, instructions[i].operands[1].swizzle, false);
                    var buffer = new USILOperand { operandType = USILOperandType.ResourceRegister, registerIndex = pairBinding, mask = new[] { 0 } };
                    Instructions.Add(new USILInstruction { instructionType = USILInstructionType.DynamicUnsignedLoadPair, destOperand = value, srcOperands = new() { input, buffer, next } });
                    i += 3; continue;
                }
				if (i + 2 < instructions.Count && TryFuseStructuredUintLoad(inst, instructions[i + 1], instructions[i + 2]))
				{
					i += 2;
					continue;
				}
				if (_instructionHandlers.ContainsKey(inst.opcode))
				{
					_instructionHandlers[inst.opcode](inst);
				}
				else if (!SHDRInstruction.IsDeclaration(inst.opcode))
				{
					Instructions.Add(new USILInstruction
					{
						instructionType = USILInstructionType.Comment,
						destOperand = new USILOperand
						{
							comment = $"unsupported_{inst.opcode}",
							operandType = USILOperandType.Comment
						},
						srcOperands = new()
					});
				}
			}
		}

		private void ConvertInputs()
		{
			ISGN isgn = _dxShader.Isgn;

			ISGN.Input[] dxInputs = isgn.inputs;
			for (int i = 0; i < dxInputs.Length; i++)
			{
				ISGN.Input dxInput = dxInputs[i];

				string inputType = dxInput.name;
				string inputName = DXShaderNamingUtils.GetISGNInputName(dxInput);
				int register = dxInput.register;
				int mask = dxInput.mask;

				USILInputOutput usilInput = new()
				{
					type = inputType,
					name = inputName,
					register = register,
					mask = mask,
					isOutput = false,
					isVertexId = dxInput.IsVertexId,
				};

				Inputs.Add(usilInput);
			}
		}

		private void ConvertOutputs()
		{
			OSGN osgn = _dxShader.Osgn;

			OSGN.Output[] dxOutputs = osgn.outputs;
			for (int i = 0; i < dxOutputs.Length; i++)
			{
				OSGN.Output dxOutput = dxOutputs[i];

				string outputType = dxOutput.name;
				string outputName = DXShaderNamingUtils.GetOSGNOutputName(dxOutput);
				int register = dxOutput.register;
				int mask = dxOutput.mask;

				USILInputOutput usilOutput = new()
				{
					type = outputType,
					name = outputName,
					register = register,
					mask = mask,
					isOutput = true
				};

				Outputs.Add(usilOutput);
			}
		}

		public override string ToString()
		{
			StringBuilder sb = new();

			int depth = 4;
			foreach (USILInstruction instruction in Instructions)
			{
				if (instruction.instructionType is USILInstructionType.EndIf or
					USILInstructionType.EndLoop or
					USILInstructionType.Else)
				{
					depth--;
				}

				sb.Append(new string(' ', depth * 4));
				sb.AppendLine(instruction.ToString());

				if (instruction.instructionType is USILInstructionType.IfFalse or
					USILInstructionType.IfTrue or
					USILInstructionType.Else or
					USILInstructionType.Loop)
				{
					depth++;
				}
			}

			return sb.ToString();
		}

		///////////////////////
		// internal stuff

		private void FillUSILOperand(SHDRInstructionOperand dxOperand, USILOperand usilOperand, int[] mask, bool immIsInt)
		{
			usilOperand.mask = mask;

			usilOperand.negative = (dxOperand.extendedData & 0x40) >> 6 == 1;
			usilOperand.absoluteValue = (dxOperand.extendedData & 0x80) >> 7 == 1;

			switch (dxOperand.operand)
			{
				case Operand.Null:
					{
						usilOperand.operandType = USILOperandType.Null;
						break;
					}
				case Operand.ConstantBuffer:
					{
						// figure out cb names in a later pass
						if (dxOperand.subOperands[1] != null)
						{
							// todo combine cb and icb
							int cbSlotIdx = dxOperand.arraySizes[0];
							int cbOff = dxOperand.registerNumber;
							SHDRInstructionOperand cbOperand = dxOperand.subOperands[1];

							usilOperand.operandType = USILOperandType.ConstantBuffer;
							usilOperand.registerIndex = cbSlotIdx;
							usilOperand.arrayRelative = new USILOperand();
							FillUSILOperand(cbOperand, usilOperand.arrayRelative, cbOperand.swizzle, false);
							usilOperand.arrayIndex = cbOff;
						}
						else
						{
							int cbSlotIdx = dxOperand.arraySizes[0];
							int cbArrIdx = dxOperand.arraySizes[1];

							usilOperand.operandType = USILOperandType.ConstantBuffer;
							usilOperand.registerIndex = cbSlotIdx;
							usilOperand.arrayIndex = cbArrIdx;
						}
						break;
					}
				case Operand.IndexableTemp:
					{
						int cbSlotIdx = dxOperand.arraySizes[0];
						int cbArrIdx = dxOperand.arraySizes[1];

						usilOperand.operandType = USILOperandType.ConstantBuffer;
						usilOperand.registerIndex = cbSlotIdx;
						usilOperand.arrayIndex = cbArrIdx;
						break;
					}
				case Operand.Input:
					{
						int inRegIdx = dxOperand.arraySizes[0];

						usilOperand.operandType = USILOperandType.InputRegister;
						usilOperand.registerIndex = inRegIdx;
						break;
					}
				case Operand.Output:
					{
						int outRegIdx = dxOperand.arraySizes[0];

						usilOperand.operandType = USILOperandType.OutputRegister;
						usilOperand.registerIndex = outRegIdx;
						break;
					}
				case Operand.Temp:
					{
						int tmpRegIdx;
						if (dxOperand.arraySizes.Length > 0)
						{
							tmpRegIdx = dxOperand.arraySizes[0];
						}
						else
						{
							tmpRegIdx = 0;
						}

						usilOperand.operandType = USILOperandType.TempRegister;
						usilOperand.registerIndex = tmpRegIdx;
						break;
					}
				case Operand.Resource:
					{
						int rscRegIdx = dxOperand.arraySizes[0];

						usilOperand.operandType = USILOperandType.ResourceRegister;
						usilOperand.registerIndex = rscRegIdx;
						break;
					}
				case Operand.Sampler:
					{
						int smpRegIdx = dxOperand.arraySizes[0];

						usilOperand.operandType = USILOperandType.SamplerRegister;
						usilOperand.registerIndex = smpRegIdx;
						break;
					}

				case Operand.InputCoverageMask:
					usilOperand.operandType = USILOperandType.InputCoverageMask;
					break;
				case Operand.InputThreadGroupID:
					usilOperand.operandType = USILOperandType.InputThreadGroupID;
					break;
				case Operand.InputThreadID:
					usilOperand.operandType = USILOperandType.InputThreadID;
					break;
				case Operand.InputThreadIDInGroup:
					usilOperand.operandType = USILOperandType.InputThreadIDInGroup;
					break;
				case Operand.InputThreadIDInGroupFlattened:
					usilOperand.operandType = USILOperandType.InputThreadIDInGroupFlattened;
					break;
				case Operand.InputPrimitiveID:
					usilOperand.operandType = USILOperandType.InputPrimitiveID;
					break;
				case Operand.InputForkInstanceID:
					usilOperand.operandType = USILOperandType.InputForkInstanceID;
					break;
				case Operand.InputGSInstanceID:
					usilOperand.operandType = USILOperandType.InputGSInstanceID;
					break;
				case Operand.InputDomainPoint:
					usilOperand.operandType = USILOperandType.InputDomainPoint;
					break;
				case Operand.OutputControlPointID:
					usilOperand.operandType = USILOperandType.OutputControlPointID;
					break;
				case Operand.OutputDepth:
					usilOperand.operandType = USILOperandType.OutputDepth;
					break;
				case Operand.OutputCoverageMask:
					usilOperand.operandType = USILOperandType.OutputCoverageMask;
					break;
				case Operand.OutputDepthGreaterEqual:
					usilOperand.operandType = USILOperandType.OutputDepthGreaterEqual;
					break;
				case Operand.OutputDepthLessEqual:
					usilOperand.operandType = USILOperandType.OutputDepthLessEqual;
					break;
				case Operand.StencilRef:
					usilOperand.operandType = USILOperandType.StencilRef;
					break;

				case Operand.Immediate32:
					{
						usilOperand.immIsInt = immIsInt;
						usilOperand.operandType = immIsInt ? USILOperandType.ImmediateInt : USILOperandType.ImmediateFloat;

						if (dxOperand.immValues.Length == 1)
						{
							if (immIsInt)
							{
								usilOperand.immValueInt = new int[1]
								{
								ConvertFloatToInt((float)dxOperand.immValues[0])
								};
							}
							else
							{
								usilOperand.immValueFloat = new float[1]
								{
								(float)dxOperand.immValues[0]
								};
							}
						}
						else if (dxOperand.immValues.Length == 4)
						{
							if (immIsInt)
							{
								usilOperand.immValueInt = new int[mask.Length];
								for (int i = 0; i < mask.Length; i++)
								{
									usilOperand.immValueInt[i] = ConvertFloatToInt((float)dxOperand.immValues[mask[i]]);
								}
							}
							else
							{
								usilOperand.immValueFloat = new float[mask.Length];
								for (int i = 0; i < mask.Length; i++)
								{
									usilOperand.immValueFloat[i] = (float)dxOperand.immValues[mask[i]];
								}
							}
						}
						else
						{
							throw new Exception($"{dxOperand.immValues.Length} imm values not possible");
						}
						break;
					}
				case Operand.ImmediateConstantBuffer:
					{
						int icbSlotIdx = dxOperand.arraySizes[0];
						int icbOff = dxOperand.registerNumber;
						SHDRInstructionOperand icbOperand = dxOperand.subOperands[0];

						usilOperand.operandType = USILOperandType.ImmediateConstantBuffer;
						usilOperand.registerIndex = icbSlotIdx;
						usilOperand.arrayRelative = new USILOperand();
						FillUSILOperand(icbOperand, usilOperand.arrayRelative, icbOperand.swizzle, false);
						usilOperand.arrayIndex = icbOff;
						break;
					}
				default:
					{
						throw new NotSupportedException($"operand type {dxOperand.operand} not supported");
					}
			}
		}

		// there is a better way to check this, but this works too
		private static int[] MapMask(int[] destMask, int[] srcMask)
        {
            if (srcMask == null || srcMask.Length == 0) return destMask;
            if (destMask == null || destMask.Length == 0) return srcMask;
            // A scalar source selector is broadcast; a four-lane source
            // swizzle is indexed by each destination lane, including .w.
            if (srcMask.Length == 1) return Enumerable.Repeat(srcMask[0], destMask.Length).ToArray();
            return destMask.Select(lane => srcMask[lane]).ToArray();
        }

        private void HandleMov(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0];
			SHDRInstructionOperand src0 = inst.operands[1];

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Move,
				destOperand = usilDest,
				srcOperands = new() { usilSrc0 },
				saturate = inst.saturated
			};

			Instructions.Add(usilInst);
		}

		private void HandleMovc(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation. If src0, then dest = src1 else dest = src2
			SHDRInstructionOperand src0 = inst.operands[1]; // The components on which to test the condition.
			SHDRInstructionOperand src1 = inst.operands[2]; // The components to move. 
			SHDRInstructionOperand src2 = inst.operands[3]; // The components to move. 

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();
			USILOperand usilSrc2 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), false);
			FillUSILOperand(src2, usilSrc2, MapMask(dest.swizzle, src2.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.MoveConditional,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1,
					usilSrc2,
				},
				saturate = inst.saturated,
			};

			Instructions.Add(usilInst);
		}

		private void HandleAdd(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The (vector/number) to add to src1.
			SHDRInstructionOperand src1 = inst.operands[2]; // The (vector/number) to add to src0.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			bool isInt = inst.opcode == Opcode.iadd;

			FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Add,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1
				},
				saturate = inst.saturated,
				isIntVariant = isInt
			};

			Instructions.Add(usilInst);
		}

		private void HandleMul(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The result of the operation. dest = src0 * src1
			SHDRInstructionOperand src0 = inst.operands[1]; // The multiplicand.
			SHDRInstructionOperand src1 = inst.operands[2]; // The multiplier.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			bool isInt = inst.opcode is Opcode.imul or Opcode.umul;

			FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Multiply,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1
				},
				saturate = inst.saturated,
				isIntVariant = isInt,
				isIntUnsigned = inst.opcode == Opcode.umul
			};

			Instructions.Add(usilInst);
		}

		private void HandleIMul(SHDRInstruction inst)
		{
			SHDRInstructionOperand destHI = inst.operands[0]; // The address of the high 32 bits of the result.
			SHDRInstructionOperand destLO = inst.operands[1]; // The address of the high 32 bits of the result.
			SHDRInstructionOperand src0 = inst.operands[2]; // The value to multiply with src1.
			SHDRInstructionOperand src1 = inst.operands[3]; // The value to multiply with src0.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			// todo, hi isn't handled
			FillUSILOperand(destLO, usilDest, destLO.swizzle, true);
			FillUSILOperand(src0, usilSrc0, MapMask(destLO.swizzle, src0.swizzle), true);
			FillUSILOperand(src1, usilSrc1, MapMask(destLO.swizzle, src1.swizzle), true);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Multiply,
				destOperand = usilDest,
				srcOperands = new() { usilSrc0, usilSrc1 },
				saturate = inst.saturated,
				isIntVariant = true,
				isIntUnsigned = inst.opcode == Opcode.umul
			};

			Instructions.Add(usilInst);
		}

		private void HandleDiv(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The dividend.
			SHDRInstructionOperand src1 = inst.operands[2]; // The divisor.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			bool isInt = inst.opcode == Opcode.udiv;

			FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Divide,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1
				},
				saturate = inst.saturated,
				isIntVariant = isInt,
				isIntUnsigned = inst.opcode == Opcode.udiv,
			};


			Instructions.Add(usilInst);
		}

		private void HandleMad(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The result of the operation. dest = src0 * src1 + src2
			SHDRInstructionOperand src0 = inst.operands[1]; // The multiplicand.
			SHDRInstructionOperand src1 = inst.operands[2]; // The multiplier.
			SHDRInstructionOperand src2 = inst.operands[3]; // The addend.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();
			USILOperand usilSrc2 = new();

			bool isInt = inst.opcode is Opcode.imad or Opcode.umad;

			FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);
			FillUSILOperand(src2, usilSrc2, MapMask(dest.swizzle, src2.swizzle), isInt);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.MultiplyAdd,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1,
					usilSrc2
				},
				saturate = inst.saturated,
				isIntVariant = isInt,
				isIntUnsigned = inst.opcode == Opcode.umad
			};

			Instructions.Add(usilInst);
		}

		private void HandleAnd(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The 32-bit value to AND with src1.
			SHDRInstructionOperand src1 = inst.operands[2]; // The 32-bit value to AND with src0.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.And,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1,
				},
			};

			Instructions.Add(usilInst);
		}

		private void HandleOr(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The components to OR with src1.
			SHDRInstructionOperand src1 = inst.operands[2]; // The components to OR with src0.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Or,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1,
				},
			};

			Instructions.Add(usilInst);
		}

		private void HandleXor(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The components to OR with src1.
			SHDRInstructionOperand src1 = inst.operands[2]; // The components to OR with src0.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Xor,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1,
				},
			};

			Instructions.Add(usilInst);
		}

        private void HandleIntegerNegate(SHDRInstruction inst) {
            var dest=new USILOperand();var src=new USILOperand();
            FillUSILOperand(inst.operands[0],dest,inst.operands[0].swizzle,true);
            FillUSILOperand(inst.operands[1],src,MapMask(inst.operands[0].swizzle,inst.operands[1].swizzle),true);
            src.negative=!src.negative;
            Instructions.Add(new USILInstruction{instructionType=USILInstructionType.Move,destOperand=dest,srcOperands=new(){src}});
        }
		private void HandleNot(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The original components.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Not,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0
				},
			};

			Instructions.Add(usilInst);
		}

		private void HandleFtoi(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation. dest = round_z(src0)
			SHDRInstructionOperand src0 = inst.operands[1]; // The component to convert.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = inst.opcode switch
				{
					Opcode.ftoi => USILInstructionType.FloatToInt,
					Opcode.ftou => USILInstructionType.FloatToUInt,
					_ => USILInstructionType.FloatToUInt,//Throw exception?
				},
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0
				},
			};

			Instructions.Add(usilInst);
		}

		private void HandleItof(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // Contains the result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // Contains the value to convert.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = inst.opcode switch
				{
					Opcode.itof => USILInstructionType.IntToFloat,
					Opcode.utof => USILInstructionType.UIntToFloat,
					_ => USILInstructionType.UIntToFloat,//Throw exception?
				},
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0
				},
			};

			Instructions.Add(usilInst);
		}

		private void HandleMin(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The result of the operation. dest = src0 < src1 ? src0 : src1
			SHDRInstructionOperand src0 = inst.operands[1]; // The components to compare to src1.
			SHDRInstructionOperand src1 = inst.operands[2]; // The components to compare to src0.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			bool isInt = inst.opcode is Opcode.imin or Opcode.umin;

			FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Minimum,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1
				},
				isIntVariant = isInt,
				isIntUnsigned = inst.opcode == Opcode.umin
			};

			Instructions.Add(usilInst);
		}

		private void HandleMax(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The result of the operation. dest = src0 >= src1 ? src0 : src1
			SHDRInstructionOperand src0 = inst.operands[1]; // The components to compare to src1.
			SHDRInstructionOperand src1 = inst.operands[2]; // The components to compare to src0.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			bool isInt = inst.opcode is Opcode.imax or Opcode.umax;

			FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Maximum,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1
				},
				isIntVariant = isInt,
				isIntUnsigned = inst.opcode == Opcode.umax
			};

			Instructions.Add(usilInst);
		}

		private void HandleSqrt(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The result of the operation. dest = sqrt(src0)
			SHDRInstructionOperand src0 = inst.operands[1]; // The components for which to take the square root.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.SquareRoot,
				destOperand = usilDest,
				srcOperands = new() { usilSrc0 }
			};

			Instructions.Add(usilInst);
		}

		private void HandleRsq(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // Contains the results of the operation. dest = 1.0f / sqrt(src0)
			SHDRInstructionOperand src0 = inst.operands[1]; // The components for the operation.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.SquareRootReciprocal,
				destOperand = usilDest,
				srcOperands = new() { usilSrc0 }
			};

			Instructions.Add(usilInst);
		}

		private void HandleLog(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation. dest = log2(src0)
			SHDRInstructionOperand src0 = inst.operands[1]; // The value for the operation.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Logarithm2,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0
				}
			};

			Instructions.Add(usilInst);
		}

		private void HandleExp(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The result of the operation. dest = 2src0
			SHDRInstructionOperand src0 = inst.operands[1]; // The exponent.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Exponential,
				destOperand = usilDest,
				srcOperands = new() { usilSrc0 }
			};

			Instructions.Add(usilInst);
		}

		private void HandleRcp(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the results dest = 1.0f / src0.
			SHDRInstructionOperand src0 = inst.operands[1]; // The number to take the reciprocal of.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Reciprocal,
				destOperand = usilDest,
				srcOperands = new() { usilSrc0 }
			};

			Instructions.Add(usilInst);
		}

		private void HandleFrc(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation. dest = src0 - round_ni(src0)
			SHDRInstructionOperand src0 = inst.operands[1]; // The component in the operation.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Fractional,
				destOperand = usilDest,
				srcOperands = new() { usilSrc0 }
			};

			Instructions.Add(usilInst);
		}

		private void HandleRoundNi(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the results of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The components in the operation.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Floor,
				destOperand = usilDest,
				srcOperands = new() { usilSrc0 }
			};

			Instructions.Add(usilInst);
		}

		private void HandleRoundPi(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the results of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The components in the operation.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Ceiling,
				destOperand = usilDest,
				srcOperands = new() { usilSrc0 }
			};

			Instructions.Add(usilInst);
		}

		private void HandleRoundNe(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the results of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The components in the operation.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Round,
				destOperand = usilDest,
				srcOperands = new() { usilSrc0 }
			};

			Instructions.Add(usilInst);
		}

		private void HandleRoundZ(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the results of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The components in the operation.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Truncate,
				destOperand = usilDest,
				srcOperands = new() { usilSrc0 }
			};

			Instructions.Add(usilInst);
		}

		private void HandleSincos(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest0 = inst.operands[0]; // The address of sin(src0), computed per component.
			SHDRInstructionOperand dest1 = inst.operands[1]; // The address of cos(src0), computed per component.
			SHDRInstructionOperand src = inst.operands[2]; // The components for which to compute sin and cos.

			// there is a sincos function in hlsl but we'll just skip that and use individual instructions

			if (dest0.operand != Operand.Null)
			{
				USILOperand usilSinDest = new();
				USILOperand usilSinSrc = new();

				FillUSILOperand(dest0, usilSinDest, dest0.swizzle, false);
				FillUSILOperand(src, usilSinSrc, MapMask(dest0.swizzle, src.swizzle), false);

				USILInstruction usilSinInst = new()
				{
					instructionType = USILInstructionType.Sine,
					destOperand = usilSinDest,
					srcOperands = new() { usilSinSrc }
				};

				Instructions.Add(usilSinInst);
			}

			if (dest1.operand != Operand.Null)
			{
				USILOperand usilCosDest = new();
				USILOperand usilCosSrc = new();

				FillUSILOperand(dest1, usilCosDest, dest1.swizzle, false);
				FillUSILOperand(src, usilCosSrc, MapMask(dest1.swizzle, src.swizzle), false);

				USILInstruction usilCosInst = new()
				{
					instructionType = USILInstructionType.Cosine,
					destOperand = usilCosDest,
					srcOperands = new() { usilCosSrc }
				};

				Instructions.Add(usilCosInst);
			}
		}

		private void HandleIShl(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // Contains the values to be shifted.
			SHDRInstructionOperand src1 = inst.operands[2]; // Contains the shift amount.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, true);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), true);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), true);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.ShiftLeft,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1
				},
				isIntVariant = true
			};

			Instructions.Add(usilInst);
		}

		private void HandleIShr(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // Contains the result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // Contains the value to be shifted.
			SHDRInstructionOperand src1 = inst.operands[2]; // Contains the shift amount.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, true);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), true);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), true);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.ShiftRight,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1
				},
				isIntVariant = true
			};

			Instructions.Add(usilInst);
		}

		private void HandleDp2(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation. dest = src0.r * src1.r + src0.g * src1.g
			SHDRInstructionOperand src0 = inst.operands[1]; // The components in the operation.
			SHDRInstructionOperand src1 = inst.operands[2]; // The components in the operation.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			int[] mask = new int[] { 0, 1 };
			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(mask, src0.swizzle), false);
			FillUSILOperand(src1, usilSrc1, MapMask(mask, src1.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.DotProduct2,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1
				},
				saturate = inst.saturated
			};

			Instructions.Add(usilInst);
		}

		private void HandleDp3(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The components in the operation.
			SHDRInstructionOperand src1 = inst.operands[2]; // The components in the operation.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			int[] mask = new int[] { 0, 1, 2 };
			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(mask, src0.swizzle), false);
			FillUSILOperand(src1, usilSrc1, MapMask(mask, src1.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.DotProduct3,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1
				},
				saturate = inst.saturated
			};

			Instructions.Add(usilInst);
		}

		private void HandleDp4(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The components in the operation.
			SHDRInstructionOperand src1 = inst.operands[2]; // The components in the operation.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			int[] mask = new int[] { 0, 1, 2, 3 };
			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(mask, src0.swizzle), false);
			FillUSILOperand(src1, usilSrc1, MapMask(mask, src1.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.DotProduct4,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1
				},
				saturate = inst.saturated
			};

			Instructions.Add(usilInst);
		}

		private void HandleSample(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand srcAddress = inst.operands[1]; // A set of texture coordinates. For more information, see the Remarks section.
			SHDRInstructionOperand srcResource = inst.operands[2]; // A texture register. For more information, see the Remarks section.
			SHDRInstructionOperand srcSampler = inst.operands[3]; // A sampler register. For more information, see the Remarks section.

			USILOperand usilDest = new();
			USILOperand usilSrcAddress = new();
			USILOperand usilSrcResource = new();
			USILOperand usilSrcSampler = new();
			USILOperand usilSamplerType = new(0); // i.e. unity_ProbeVolumeSH (handled by USILSamplerTypeFixer)

			int[] mask = new int[] { 0, 1, 2, 3 };

			ResourceDimension dimension = _resourceToDimension[srcResource.arraySizes[0]];
			int[] uvMask = GetSampleLocationMask(dimension);

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(srcAddress, usilSrcAddress, MapMask(uvMask, srcAddress.swizzle), false);
			FillUSILOperand(srcResource, usilSrcResource, MapMask(dest.swizzle, srcResource.swizzle), false);
			FillUSILOperand(srcSampler, usilSrcSampler, mask, false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Sample,
				destOperand = usilDest,
                saturate = inst.saturated,
                textureOffsets = SampleOffsets(inst, dimension),
				srcOperands = new()
				{
					usilSrcAddress,
					usilSrcResource,
					usilSrcSampler,
					usilSamplerType
				}
			};

			Instructions.Add(usilInst);
		}

		private void HandleSampleC(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the results of the operation.
			SHDRInstructionOperand srcAddress = inst.operands[1]; // A set of texture coordinates. For more information see the sample instruction.
			SHDRInstructionOperand srcResource = inst.operands[2]; // A texture register. For more information see the sample instruction.
			SHDRInstructionOperand srcSampler = inst.operands[3]; // A sampler register. For more information see the sample instruction.
			SHDRInstructionOperand srcReferenceValue = inst.operands[4]; // A register with a single component selected, which is used in the comparison.

			USILOperand usilDest = new();
			USILOperand usilSrcAddress = new();
			USILOperand usilSrcResource = new();
			USILOperand usilSrcSampler = new();
			USILOperand usilSrcReferenceValue = new();
			USILOperand usilSamplerType = new(0); // i.e. unity_ProbeVolumeSH (handled by USILSamplerTypeFixer)

			int[] mask = new int[] { 0, 1, 2, 3 };

			ResourceDimension dimension = _resourceToDimension[srcResource.arraySizes[0]];
			int[] uvMask = GetSampleLocationMask(dimension);

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(srcAddress, usilSrcAddress, MapMask(uvMask, srcAddress.swizzle), false);
			FillUSILOperand(srcResource, usilSrcResource, MapMask(dest.swizzle, srcResource.swizzle), false);
			FillUSILOperand(srcSampler, usilSrcSampler, mask, false);
			FillUSILOperand(srcReferenceValue, usilSrcReferenceValue, new[] {srcReferenceValue.swizzle[0]}, false);

			USILInstruction usilInst = new()
			{
				instructionType = inst.opcode switch
				{
					Opcode.sample_c => USILInstructionType.SampleComparison,
					Opcode.sample_c_lz => USILInstructionType.SampleComparisonLODZero,
					_ => USILInstructionType.SampleComparisonLODZero,//Throw exception?
				},
				destOperand = usilDest,
                saturate = inst.saturated,
                textureOffsets = SampleOffsets(inst, dimension),
				srcOperands = new()
				{
					usilSrcAddress,
					usilSrcResource,
					usilSrcSampler,
					usilSrcReferenceValue,
					usilSamplerType
				}
			};

			Instructions.Add(usilInst);
		}

		private void HandleSampleL(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand srcAddress = inst.operands[1]; // A set of texture coordinates. For more information, see the Remarks section.
			SHDRInstructionOperand srcResource = inst.operands[2]; // A texture register. For more information, see the Remarks section.
			SHDRInstructionOperand srcSampler = inst.operands[3]; // A sampler register. For more information, see the Remarks section.
			SHDRInstructionOperand srcLOD = inst.operands[4]; // The LOD.

			USILOperand usilDest = new();
			USILOperand usilSrcAddress = new();
			USILOperand usilSrcResource = new();
			USILOperand usilSrcSampler = new();
			USILOperand usilSrcLOD = new();
			USILOperand usilSamplerType = new(0); // i.e. unity_ProbeVolumeSH (handled by USILSamplerTypeFixer)

			int[] mask = new int[] { 0, 1, 2, 3 };

			ResourceDimension dimension = _resourceToDimension[srcResource.arraySizes[0]];
			int[] uvMask = GetSampleLocationMask(dimension);

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(srcAddress, usilSrcAddress, MapMask(uvMask, srcAddress.swizzle), false);
			FillUSILOperand(srcResource, usilSrcResource, MapMask(dest.swizzle, srcResource.swizzle), false);
			FillUSILOperand(srcSampler, usilSrcSampler, mask, false);
			FillUSILOperand(srcLOD, usilSrcLOD, srcLOD.swizzle, false);

			USILInstruction usilInst = new()
			{
				instructionType = inst.opcode switch
				{
					Opcode.sample_b => USILInstructionType.SampleLODBias,
					Opcode.sample_l => USILInstructionType.SampleLOD,
					_ => USILInstructionType.SampleLOD, //Throw exception?
				},
				destOperand = usilDest,
                saturate = inst.saturated,
                textureOffsets = SampleOffsets(inst, dimension),
				srcOperands = new()
				{
					usilSrcAddress,
					usilSrcResource,
					usilSrcSampler,
					usilSrcLOD,
					usilSamplerType
				}
			};

			Instructions.Add(usilInst);
		}

		private void HandleSampleD(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the results of the operation.
			SHDRInstructionOperand srcAddress = inst.operands[1]; // A set of texture coordinates. For more information see the sample instruction.
			SHDRInstructionOperand srcResource = inst.operands[2]; // A texture register. For more information see the sample instruction.
			SHDRInstructionOperand srcSampler = inst.operands[3]; // A sampler register. For more information see the sample instruction.
			SHDRInstructionOperand srcXDerivatives = inst.operands[4]; // The derivatives for the source address in the x direction. For more information, see the Remarks section.
			SHDRInstructionOperand srcYDerivatives = inst.operands[5]; // The derivatives for the source address in the y direction. For more information, see the Remarks section.

			USILOperand usilDest = new();
			USILOperand usilSrcAddress = new();
			USILOperand usilSrcResource = new();
			USILOperand usilSrcSampler = new();
			USILOperand usilSrcXDerivatives = new();
			USILOperand usilSrcYDerivatives = new();

			int[] mask = new int[] { 0, 1, 2, 3 };

			ResourceDimension dimension = _resourceToDimension[srcResource.arraySizes[0]];
			int[] uvMask = GetSampleLocationMask(dimension);

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(srcAddress, usilSrcAddress, MapMask(uvMask, srcAddress.swizzle), false);
			FillUSILOperand(srcResource, usilSrcResource, MapMask(dest.swizzle, srcResource.swizzle), false);
			FillUSILOperand(srcSampler, usilSrcSampler, mask, false);
			FillUSILOperand(srcXDerivatives, usilSrcXDerivatives, MapMask(GetSampleGradientMask(dimension), srcXDerivatives.swizzle), false);
			FillUSILOperand(srcYDerivatives, usilSrcYDerivatives, MapMask(GetSampleGradientMask(dimension), srcYDerivatives.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.SampleDerivative,
				destOperand = usilDest,
                saturate = inst.saturated,
                textureOffsets = SampleOffsets(inst, dimension),
				srcOperands = new()
				{
					usilSrcAddress,
					usilSrcResource,
					usilSrcSampler,
					usilSrcXDerivatives,
					usilSrcYDerivatives
				}
			};

			Instructions.Add(usilInst);
		}

		private void HandleGather(SHDRInstruction inst)
		{
			var dest = inst.operands[0]; var address = inst.operands[1];
			var resource = inst.operands[2]; var sampler = inst.operands[3];
			var dimension = _resourceToDimension[resource.arraySizes[0]];
			if (dimension is not (ResourceDimension.texture2d or ResourceDimension.texture2darray or ResourceDimension.texturecube or ResourceDimension.texturecubearray))
				throw new NotSupportedException("Gather on " + dimension);
			if (sampler.swizzle.Length != 1 || sampler.swizzle[0] < 0 || sampler.swizzle[0] > 3)
				throw new InvalidDataException("Gather sampler must select exactly one component.");
			USILOperand dst = new(), uv = new(), tex = new(), samp = new();
			FillUSILOperand(dest, dst, dest.swizzle, false);
			FillUSILOperand(address, uv, MapMask(GetSampleLocationMask(dimension), address.swizzle), false);
			FillUSILOperand(resource, tex, MapMask(dest.swizzle, resource.swizzle), false);
			FillUSILOperand(sampler, samp, sampler.swizzle, false);
			var operands = new List<USILOperand> { uv, tex, samp };
			Instructions.Add(new USILInstruction { instructionType = USILInstructionType.Gather,
				destOperand = dst, srcOperands = operands, gatherComponent = sampler.swizzle[0],
				textureOffsets = SampleOffsets(inst, dimension), saturate = inst.saturated });
		}

		private void RejectMultisampledLoad(SHDRInstruction inst) =>
			throw new NotSupportedException("Multisampled resource loads need an explicitly preserved sample index.");

		private void HandleLd(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand srcAddress = inst.operands[1]; // The texture coordinates needed to perform the sample.
			SHDRInstructionOperand srcResource = inst.operands[2]; // A texture register (t#) which must have been declared identifying which Texture or Buffer to fetch from.

			USILOperand usilDest = new();
			USILOperand usilSrcAddress = new();
			USILOperand usilSrcResource = new();

			ResourceDimension dimension = _resourceToDimension[srcResource.arraySizes[0]];
			if (dimension is not (ResourceDimension.buffer or ResourceDimension.texture1d or ResourceDimension.texture1darray or ResourceDimension.texture2d or ResourceDimension.texture2darray or ResourceDimension.texture3d))
				throw new NotSupportedException("Load on " + dimension);
			int[] uvMask = GetLoadLocationMask(dimension);

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(srcAddress, usilSrcAddress, MapMask(uvMask, srcAddress.swizzle), false);
			FillUSILOperand(srcResource, usilSrcResource, MapMask(dest.swizzle, srcResource.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = inst.opcode switch
				{
					Opcode.ld => USILInstructionType.LoadResource,
					Opcode.ldms => USILInstructionType.LoadResourceMultisampled,
					_ => USILInstructionType.LoadResourceMultisampled,//Throw exception?
				},
				destOperand = usilDest,
				textureOffsets = dimension == ResourceDimension.buffer ? Array.Empty<int>() : SampleOffsets(inst, dimension),
				srcOperands = new() { usilSrcAddress, usilSrcResource },
			};

			Instructions.Add(usilInst);
		}

        public static bool TryBuildClosedUnsignedRegion(SHDR shader, int start, out List<UnsignedRegionOperation> operations, out int binding, out int end)
        {
            operations = new(); binding = -1; end = start;
            var instructions = shader.shaderInstructions;
            if (start < 0 || start >= instructions.Count || instructions[start].opcode != Opcode.ld_structured) return false;
            var live = new HashSet<(int, int)>();
            var result = new List<UnsignedRegionOperation>();
            int resource = -1;
            static bool Plain(SHDRInstructionOperand o) => !o.extended && (o.subOperands == null || o.subOperands.All(x => x is null));
            static bool Temp(SHDRInstructionOperand o) => o.operand == Operand.Temp && o.arraySizes.Length == 1 && o.arraySizes[0] >= 0 && Plain(o) && o.swizzle.Length > 0 && o.swizzle.All(x => x >= 0 && x < 4);
            static bool Immediate(SHDRInstructionOperand o) => o.operand == Operand.Immediate32 && o.immValues.Length == 1 && Plain(o);
            static int Lane(SHDRInstructionOperand o, int destinationLane) => o.swizzle.Length == 1 ? o.swizzle[0] : o.swizzle.Length == 4 ? o.swizzle[destinationLane] : -1;
            for (int i = start; i < instructions.Count; i++)
            {
                var ins = instructions[i]; var op = ins.operands;
                if (ins.saturated) return false;
                if (ins.opcode == Opcode.ld_structured && op.Count == 4 && Temp(op[0]) && op[0].swizzle.Length == 1 && Immediate(op[1]) && Immediate(op[2]) && op[2].immValues[0] == 0 && double.IsFinite(op[1].immValues[0]) && op[3].operand == Operand.Resource && Plain(op[3]) && op[3].arraySizes.Length == 1 && op[3].swizzle.SequenceEqual(new[] { 0, 0, 0, 0 }))
                {
                    int candidate = op[3].arraySizes[0];
                    if (resource >= 0 && resource != candidate) return false;
                    resource = candidate;
                    var dst = (op[0].arraySizes[0], op[0].swizzle[0]);
                    live.Add(dst);
                    result.Add(new("load", dst.Item1, dst.Item2, Index: unchecked((uint)BitConverter.SingleToInt32Bits((float)op[1].immValues[0]))));
                }
                else if (ins.opcode == Opcode.umax && op.Count == 3 && op.All(Temp) && op[0].swizzle.Length == 1)
                {
                    int dl = op[0].swizzle[0], l = Lane(op[1], dl), r = Lane(op[2], dl);
                    if (l < 0 || r < 0 || !live.Contains((op[1].arraySizes[0], l)) || !live.Contains((op[2].arraySizes[0], r))) return false;
                    var dst = (op[0].arraySizes[0], dl); live.Add(dst);
                    result.Add(new("max", dst.Item1, dl, op[1].arraySizes[0], l, op[2].arraySizes[0], r));
                }
                else if (ins.opcode == Opcode.utof && op.Count == 2 && op.All(Temp))
                {
                    var lanes = op[0].swizzle;
                    if (lanes.Distinct().Count() != lanes.Length) return false;
                    foreach (int dl in lanes)
                    {
                        int sl = Lane(op[1], dl); var dst = (op[0].arraySizes[0], dl);
                        if (sl != dl || op[1].arraySizes[0] != dst.Item1 || !live.Contains(dst)) return false;
                    }
                    foreach (int dl in lanes)
                    {
                        var dst = (op[0].arraySizes[0], dl); live.Remove(dst);
                        result.Add(new("convert", dst.Item1, dl));
                    }
                    if (live.Count == 0 || DeadBeforeRead(instructions, i + 1, live))
                    {
                        var declarations = instructions.Where(x => x.opcode == Opcode.dcl_resource_structured && x.declData?.operands.Length == 1 && x.declData.operands[0].arraySizes.SequenceEqual(new[] { resource })).ToArray();
                        if (declarations.Length != 1 || declarations[0].declData!.structuredStride != 4) return false;
                        operations = result; binding = resource; end = i; return true;
                    }
                }
                else if (ins.opcode == Opcode.mov && op.Count == 2 && op[0].operand == Operand.Output && Plain(op[0]) &&
                         op[0].arraySizes.Length == 1 && op[0].swizzle.Length > 0 && op[0].swizzle.Distinct().Count() == op[0].swizzle.Length && op[0].swizzle.All(x => x >= 0 && x < 4) &&
                         (op[1].operand == Operand.Input || op[1].operand == Operand.Immediate32) && Plain(op[1]) &&
                         (op[1].operand != Operand.Input || (op[1].arraySizes.Length == 1 && (op[1].swizzle.Length == 1 || op[1].swizzle.Length == 4) && op[1].swizzle.All(x => x >= 0 && x < 4))) &&
                         (op[1].operand != Operand.Immediate32 || op[1].immValues.Length is 1 or 4))
                {
                    // Independent output write; emitted in order after the closed slice.
                }
                else return false;
            }
            return false;
        }

        private static bool DeadBeforeRead(List<SHDRInstruction> instructions, int start, HashSet<(int, int)> live)
        {
            var pending = new HashSet<(int, int)>(live);
            for (int i = start; i < instructions.Count && pending.Count != 0; i++)
            {
                var ins = instructions[i];
                // Limit proof to known straight-line, unpredicated operations with
                // destination first; a branch or unfamiliar opcode declines it.
                if (ins.opcode is not (Opcode.mov or Opcode.add or Opcode.div or Opcode.mad or Opcode.mul)) return false;
                if (ins.operands.Count < 2) return false;
                foreach (var operand in ins.operands.Skip(1))
                {
                    if (operand.subOperands != null && operand.subOperands.Any(x => x != null)) return false;
                    if (operand.operand != Operand.Temp) continue;
                    if (operand.extended || operand.arraySizes.Length != 1 || operand.swizzle.Length == 0) return false;
                    // Treat every swizzle component as read, even unused ones.
                    if (operand.swizzle.Any(lane => pending.Contains((operand.arraySizes[0], lane)))) return false;
                }
                var dst = ins.operands[0];
                if (dst.operand == Operand.Temp)
                {
                    if (dst.extended || dst.arraySizes.Length != 1 || (dst.subOperands != null && dst.subOperands.Any(x => x != null))) return false;
                    foreach (int lane in dst.swizzle) pending.Remove((dst.arraySizes[0], lane));
                }
            }
            return pending.Count == 0;
        }

        public static bool IsDynamicUnsignedLoadPair(SHDR shader, int start, out int binding)
        {
            binding = -1;
            var ins = shader.shaderInstructions;
            if (start < 0 || start + 3 >= ins.Count) return false;
            var a = ins[start]; var b = ins[start + 1]; var c = ins[start + 2]; var d = ins[start + 3];
            if (a.opcode != Opcode.ftou || b.opcode != Opcode.iadd || c.opcode != Opcode.ld_structured || d.opcode != Opcode.utof ||
                a.saturated || b.saturated || c.saturated || d.saturated || a.operands.Count != 2 || b.operands.Count != 3 || c.operands.Count != 4 || d.operands.Count != 2) return false;
            static bool Plain(SHDRInstructionOperand x) => !x.extended && (x.subOperands == null || x.subOperands.All(y => y == null));
            static bool Temp(SHDRInstructionOperand x) => x.operand == Operand.Temp && x.arraySizes.Length == 1 && x.arraySizes[0] >= 0 && x.swizzle.Length == 1 && x.swizzle[0] is >= 0 and < 4 && Plain(x);
            static bool Same(SHDRInstructionOperand x, SHDRInstructionOperand y) => Temp(x) && Temp(y) && x.arraySizes[0] == y.arraySizes[0] && x.swizzle[0] == y.swizzle[0];
            var lane = a.operands[0]; var next = b.operands[0];
            if (!Same(lane, a.operands[1]) || !Same(lane, b.operands[1]) || !Same(lane, c.operands[0]) || !Same(lane, c.operands[1]) ||
                !Temp(next) || next.arraySizes[0] != lane.arraySizes[0] || next.swizzle[0] == lane.swizzle[0]) return false;
            var one = b.operands[2]; var offset = c.operands[2]; var res = c.operands[3];
            if (one.operand != Operand.Immediate32 || one.immValues.Length != 1 || !Plain(one) || BitConverter.SingleToInt32Bits((float)one.immValues[0]) != 1 ||
                offset.operand != Operand.Immediate32 || offset.immValues.Length != 1 || offset.immValues[0] != 0 || !Plain(offset) ||
                res.operand != Operand.Resource || !Plain(res) || res.arraySizes.Length != 1 || !res.swizzle.SequenceEqual(new[] { 0, 0, 0, 0 })) return false;
            var dst = d.operands[0]; var src = d.operands[1];
            if (dst.operand != Operand.Temp || src.operand != Operand.Temp || !Plain(dst) || !Plain(src) ||
                dst.arraySizes.Length != 1 || src.arraySizes.Length != 1 || dst.arraySizes[0] != lane.arraySizes[0] || src.arraySizes[0] != lane.arraySizes[0] ||
                dst.swizzle.Length != 2 || !dst.swizzle.SequenceEqual(new[] { lane.swizzle[0], next.swizzle[0] }.OrderBy(x => x)) ||
                src.swizzle.Length != 4 || dst.swizzle.Any(x => src.swizzle[x] != x)) return false;
            int candidate = res.arraySizes[0];
            var declarations = ins.Where(x => x.opcode == Opcode.dcl_resource_structured && x.declData?.operands.Length == 1 && x.declData.operands[0].arraySizes.SequenceEqual(new[] { candidate })).ToArray();
            if (declarations.Length != 1 || declarations[0].declData!.structuredStride != 4) return false;
            binding = candidate; return true;
        }

		private bool TryFuseStructuredUintLoad(SHDRInstruction convert, SHDRInstruction load, SHDRInstruction result)
		{
			if (!IsSupportedStructuredUintLoad(_dxShader.Shdr, convert, load, result, out _)) return false;
			USILOperand dst = new(), address = new(), buffer = new();
			FillUSILOperand(result.operands[0], dst, result.operands[0].swizzle, false);
			FillUSILOperand(convert.operands[1], address, convert.operands[1].swizzle, false);
			FillUSILOperand(load.operands[3], buffer, new[] { 0 }, false);
			Instructions.Add(new USILInstruction { instructionType = USILInstructionType.LoadStructuredUintToFloat, destOperand = dst, srcOperands = new() { address, buffer } });
			return true;
		}

		public static bool IsSupportedStructuredUintLoad(SHDR shader, SHDRInstruction convert, SHDRInstruction load, SHDRInstruction result, out int binding)
		{
			binding = -1;
			if (convert.opcode != Opcode.ftou || load.opcode != Opcode.ld_structured || result.opcode != Opcode.utof ||
				convert.operands.Count != 2 || load.operands.Count != 4 || result.operands.Count != 2 ||
				convert.saturated || load.saturated || result.saturated) return false;
			static bool SameLane(SHDRInstructionOperand a, SHDRInstructionOperand b) =>
				a.operand == Operand.Temp && b.operand == Operand.Temp && a.arraySizes.SequenceEqual(b.arraySizes) &&
				a.swizzle.Length == 1 && b.swizzle.Length == 1 && a.swizzle[0] == b.swizzle[0] &&
				!a.extended && !b.extended && a.subOperands.All(x => x is null) && b.subOperands.All(x => x is null);
			var lane = convert.operands[0];
			if (!SameLane(lane, load.operands[0]) || !SameLane(lane, load.operands[1]) ||
				!SameLane(lane, result.operands[0]) || !SameLane(lane, result.operands[1]) ||
				convert.operands[1].swizzle.Length != 1 ||
				load.operands[2].operand != Operand.Immediate32 || load.operands[2].immValues.Length != 1 || load.operands[2].immValues[0] != 0 ||
				load.operands[3].operand != Operand.Resource || load.operands[3].arraySizes.Length != 1 ||
				load.operands[3].swizzle.Length != 4 || load.operands[3].swizzle.Any(x => x != 0)) return false;
			int candidate = load.operands[3].arraySizes[0];
			if (shader.GetStructuredResourceStride(candidate) != 4) return false;
			binding = candidate;
			return true;
		}

		public static bool IsSupportedStructuredVertexLoad(DirectXCompiledShader shader, SHDRInstruction inst, out int binding)
		{
			binding = -1;
			if (inst.opcode != Opcode.ld_structured || shader.Shdr.shaderType != DirectXDisassembler.Type.Vertex ||
				inst.saturated || inst.operands.Count != 4)
			{
				return false;
			}

			var dest = inst.operands[0];
			var address = inst.operands[1];
			var offset = inst.operands[2];
			var resource = inst.operands[3];
			if (dest is not { operand: Operand.Temp, arraySizes: [_], swizzle: [0, 1, 2] } ||
				address is not { operand: Operand.Input, arraySizes: [int inputRegister], swizzle: [0] } ||
				offset is not { operand: Operand.Immediate32, arraySizes: [], immValues: [double byteOffset] } ||
				BitConverter.SingleToInt32Bits((float)byteOffset) != 0 ||
				resource is not { operand: Operand.Resource, arraySizes: [int candidate], swizzle: [0, 1, 2, _] } ||
				inst.operands.Any(operand => operand.extended || operand.subOperands.Any(relative => relative is not null)) ||
				shader.Shdr.GetStructuredResourceStride(candidate) != 12 ||
				!shader.Isgn.inputs.Any(input => input.register == inputRegister && input.IsVertexId))
			{
				return false;
			}
			binding = candidate;
			return true;
		}

		private void LoadStructuredWords3(SHDRInstruction inst)
		{
			var dest = inst.operands[0];
			var address = inst.operands[1];
			var resource = inst.operands[3];
			USILOperand usilDest = new(), usilAddress = new(), usilResource = new();
			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(address, usilAddress, address.swizzle, true);
			FillUSILOperand(resource, usilResource, [0], false);
			Instructions.Add(new USILInstruction
			{
				instructionType = USILInstructionType.LoadStructuredWords3,
				destOperand = usilDest,
				srcOperands = new() { usilAddress, usilResource },
			});
		}

		private void HandleLdStructured(SHDRInstruction inst)
		{
			if (IsSupportedStructuredVertexLoad(_dxShader, inst, out _))
			{
				LoadStructuredWords3(inst);
				return;
			}

			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand srcAddress = inst.operands[1]; // Specifies the index of the structure to read.
			SHDRInstructionOperand srcByteOffset = inst.operands[2]; // Specifies the byte offset in the structure to start reading from. 
			SHDRInstructionOperand src0 = inst.operands[3]; // The buffer to read from. This parameter must be a SRV (t#), UAV (u#). In the compute shader it can also be thread group shared memory (g#). 

			USILOperand usilDest = new();
			USILOperand usilSrcAddress = new();
			USILOperand usilSrcByteOffset = new();
			USILOperand usilSrc0 = new();

			if (src0.operand == Operand.UnorderedAccessView)
			{
				throw new NotSupportedException("uav on ld_structured not supported");
			}
			else if (src0.operand == Operand.ThreadGroupSharedMemory)
			{
				throw new NotSupportedException("gsm on ld_structured not supported");
			}

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(srcAddress, usilSrcAddress, srcAddress.swizzle, true);
			FillUSILOperand(srcByteOffset, usilSrcByteOffset, srcByteOffset.swizzle, true);
			FillUSILOperand(src0, usilSrc0, src0.swizzle, false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.LoadResourceStructured,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrcAddress,
					usilSrcByteOffset,
					usilSrc0
				}
			};

			Instructions.Add(usilInst);
		}

		private void HandleDiscard(SHDRInstruction inst)
		{
			SHDRInstructionOperand src0 = inst.operands[0]; // The value that determines whether to discard the current pixel being processed.

			USILOperand usilSrc0 = new();

			FillUSILOperand(src0, usilSrc0, src0.swizzle, false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Discard,
				destOperand = null,
				srcOperands = new()
				{
					usilSrc0
				}
			};

			// if works the same way, so we can pretend the discard is an if
			HandleIf(inst);
			Instructions.Add(usilInst);
			HandleEndIf(inst);
		}

		private void HandleResInfo(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The mip level.
			SHDRInstructionOperand src1 = inst.operands[2]; // A t# or u# input texture for which the dimensions are being queried. 

			USILOperand usilResource = new();
			USILOperand usilMipLevel = new();
			USILOperand usilWidth = new();
			USILOperand usilHeight = new();
			USILOperand usilDepthOrArraySize = new();
			USILOperand usilMipCount = new();

			bool hasWidth = false;
			bool hasHeight = false;
			bool hasDepth = false;
			bool hasMipCount = false;
			// resinfo_indexable*** r0.xy, l(0), t0.xwyz ->
			// r0.xy means two outputs, t0.xw means hasWidth and hasMipCount
			for (int i = 0; i < dest.swizzle.Length; i++)
			{
				int index = src1.swizzle[i];
				switch (index)
				{
					case 0: hasWidth = true; break;
					case 1: hasHeight = true; break;
					case 2: hasDepth = true; break;
					case 3: hasMipCount = true; break;
				}
			}

			FillUSILOperand(src1, usilResource, new int[] { 0, 1, 2, 3 }, false);
			FillUSILOperand(src0, usilMipLevel, src0.swizzle, false);

			if (hasWidth)
			{
				FillUSILOperand(dest, usilWidth, new int[] { 0 }, false);
			}
			else
			{
				usilWidth.operandType = USILOperandType.Null;
			}

			if (hasHeight)
			{
				FillUSILOperand(dest, usilHeight, new int[] { 1 }, false);
			}
			else
			{
				usilHeight.operandType = USILOperandType.Null;
			}

			if (hasDepth)
			{
				FillUSILOperand(dest, usilDepthOrArraySize, new int[] { 2 }, false);
			}
			else
			{
				usilDepthOrArraySize.operandType = USILOperandType.Null;
			}

			if (hasMipCount)
			{
				FillUSILOperand(dest, usilMipCount, new int[] { 3 }, false);
			}
			else
			{
				usilMipCount.operandType = USILOperandType.Null;
			}

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.ResourceDimensionInfo,
				destOperand = null,
				srcOperands = new()
				{
					usilResource,
					usilMipLevel,
					usilWidth,
					usilHeight,
					usilDepthOrArraySize,
					usilMipCount
				}
			};

			Instructions.Add(usilInst);
		}

		private void HandleSampleInfo(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the results of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The shader resource.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, src0.swizzle, false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.SampleCountInfo,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0
				}
			};

			Instructions.Add(usilInst);
		}

		private void HandleDerivRt(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The component in the operation.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();

			FillUSILOperand(dest, usilDest, dest.swizzle, false);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), false);

			USILInstruction usilInst = new()
			{
				instructionType = inst.opcode switch
				{
					Opcode.deriv_rtx => USILInstructionType.DerivativeRenderTargetX,
					Opcode.deriv_rty => USILInstructionType.DerivativeRenderTargetY,
					Opcode.deriv_rtx_coarse => USILInstructionType.DerivativeRenderTargetXCoarse,
					Opcode.deriv_rty_coarse => USILInstructionType.DerivativeRenderTargetYCoarse,
					Opcode.deriv_rtx_fine => USILInstructionType.DerivativeRenderTargetXFine,
					Opcode.deriv_rty_fine => USILInstructionType.DerivativeRenderTargetYFine,
					_ => USILInstructionType.DerivativeRenderTargetX
				},
				destOperand = usilDest,
				srcOperands = new() { usilSrc0 }
			};

			Instructions.Add(usilInst);
		}

		private void HandleIf(SHDRInstruction inst)
		{
			SHDRInstructionOperand src0 = inst.operands[0]; // The value that determines whether to discard the current pixel being processed.

			USILOperand usilSrc0 = new();

			FillUSILOperand(src0, usilSrc0, src0.swizzle, false);

			USILInstruction usilInst = new()
			{
				instructionType = (inst.instData & 0x40000) != 0 ? USILInstructionType.IfTrue : USILInstructionType.IfFalse,
				destOperand = null,
				srcOperands = new()
				{
					usilSrc0
				}
			};

			Instructions.Add(usilInst);
		}

		private void HandleElse(SHDRInstruction inst)
		{
			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Else,
				destOperand = null,
				srcOperands = new()
			};

			Instructions.Add(usilInst);
		}

		private void HandleEndIf(SHDRInstruction inst)
		{
			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.EndIf,
				destOperand = null,
				srcOperands = new()
			};

			Instructions.Add(usilInst);
		}

		private void HandleLoop(SHDRInstruction inst)
		{
			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Loop,
				destOperand = null,
				srcOperands = new()
			};

			Instructions.Add(usilInst);
		}

		private void HandleEndLoop(SHDRInstruction inst)
		{
			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.EndLoop,
				destOperand = null,
				srcOperands = new()
			};

			Instructions.Add(usilInst);
		}

		private void HandleBreak(SHDRInstruction inst)
		{
			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Break,
				destOperand = null,
				srcOperands = new()
			};

			Instructions.Add(usilInst);
		}

		private void HandleBreakc(SHDRInstruction inst)
		{
			SHDRInstructionOperand src0 = inst.operands[0];

			USILOperand usilSrc0 = new();

			FillUSILOperand(src0, usilSrc0, src0.swizzle, false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Break,
				destOperand = null,
				srcOperands = new() { usilSrc0 }
			};

			// if works the same way, so we can pretend the discard is an if
			HandleIf(inst);
			Instructions.Add(usilInst);
			HandleEndIf(inst);
		}


		private void HandleContinue(SHDRInstruction inst)
		{
			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Continue,
				destOperand = null,
				srcOperands = new()
			};

			Instructions.Add(usilInst);
		}

		private void HandleContinuec(SHDRInstruction inst)
		{
			SHDRInstructionOperand src0 = inst.operands[0];

			USILOperand usilSrc0 = new();
			FillUSILOperand(src0, usilSrc0, src0.swizzle, false);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Continue,
				destOperand = null,
				srcOperands = new() { usilSrc0 }
			};

			// if works the same way, so we can pretend the discard is an if
			HandleIf(inst);
			Instructions.Add(usilInst);
			HandleEndIf(inst);
		}

		private void HandleEq(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The component to comapre to src1.
			SHDRInstructionOperand src1 = inst.operands[2]; // The component to comapre to src0.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			bool isInt = inst.opcode == Opcode.ieq;

			FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Equal,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1
				},
				isIntVariant = isInt
			};

			Instructions.Add(usilInst);
		}

		private void HandleNeq(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The components to compare to src1.
			SHDRInstructionOperand src1 = inst.operands[2]; // The components to compare to src0.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			bool isInt = inst.opcode == Opcode.ine;

			FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.NotEqual,
				destOperand = usilDest,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1
				},
				isIntVariant = isInt
			};

			Instructions.Add(usilInst);
		}

		private void HandleLt(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The value to compare to src1.
			SHDRInstructionOperand src1 = inst.operands[2]; // The value to compare to src0.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			bool isInt = inst.opcode is Opcode.ilt or Opcode.ult;

			FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.LessThan,
				destOperand = usilDest,
				isIntVariant = isInt,
				isIntUnsigned = inst.opcode == Opcode.ult,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1
				}
			};

			Instructions.Add(usilInst);
		}

		private void HandleGe(SHDRInstruction inst)
		{
			SHDRInstructionOperand dest = inst.operands[0]; // The address of the result of the operation.
			SHDRInstructionOperand src0 = inst.operands[1]; // The value to compare to src1.
			SHDRInstructionOperand src1 = inst.operands[2]; // The value to compare to src0.

			USILOperand usilDest = new();
			USILOperand usilSrc0 = new();
			USILOperand usilSrc1 = new();

			bool isInt = inst.opcode is Opcode.ige or Opcode.uge;

			FillUSILOperand(dest, usilDest, dest.swizzle, isInt);
			FillUSILOperand(src0, usilSrc0, MapMask(dest.swizzle, src0.swizzle), isInt);
			FillUSILOperand(src1, usilSrc1, MapMask(dest.swizzle, src1.swizzle), isInt);

			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.GreaterThanOrEqual,
				destOperand = usilDest,
				isIntVariant = isInt,
				isIntUnsigned = inst.opcode == Opcode.uge,
				srcOperands = new()
				{
					usilSrc0,
					usilSrc1
				}
			};

			Instructions.Add(usilInst);
		}


		private void HandleRet(SHDRInstruction inst)
		{
			USILInstruction usilInst = new()
			{
				instructionType = USILInstructionType.Return,
				srcOperands = new()
			};
			Instructions.Add(usilInst);
		}


		private void HandleTemps(SHDRInstruction inst)
		{
			Debug.Assert(inst.declData is not null);
			int tempCount = inst.declData.numTemps;
			for (int i = 0; i < tempCount; i++)
			{
				string tmpString = USILOperand.GetTypeShortForm(USILOperandType.TempRegister) + i;
				USILLocal local = new("float4", tmpString, USILLocalType.Vector4);
				Locals.Add(local);
			}
		}

		private void HandleResource(SHDRInstruction inst)
		{
			Debug.Assert(inst.declData is not null);
			ResourceDimension dimension = inst.declData.resourceDimension;
			int regIndex = inst.declData.operands[0].arraySizes[0]; // todo: not always tX
			_resourceToDimension[regIndex] = dimension;
		}

		private void HandleCustomData(SHDRInstruction inst)
		{
			USILLocal local = new("const float4", "icb", USILLocalType.Vector4, true);
			Debug.Assert(inst.declData is not null);
			foreach (float[] vector in inst.declData.customDataArray)
			{
				USILOperand vectorOperand = new();
				vectorOperand.operandType = USILOperandType.ImmediateFloat;
				vectorOperand.immValueFloat = vector;
				vectorOperand.mask = USILConstants.XYZW_MASK;
				local.defaultValues.Add(vectorOperand);
			}
			Locals.Add(local);
		}

		private void AddOutputLocal()
		{
			string outputName = _dxShader.Shdr.shaderType switch
			{
				DirectXDisassembler.Type.Vertex => USILConstants.VERT_OUTPUT_LOCAL_NAME,
				DirectXDisassembler.Type.Pixel => USILConstants.FRAG_OUTPUT_LOCAL_NAME,
				_ => "o" // ?
			};
			string outputStructName = _dxShader.Shdr.shaderType switch
			{
				DirectXDisassembler.Type.Vertex => USILConstants.VERT_TO_FRAG_STRUCT_NAME,
				DirectXDisassembler.Type.Pixel => USILConstants.FRAG_OUTPUT_STRUCT_NAME,
				_ => "o" // ?
			};

			Locals.Add(new USILLocal(outputStructName, outputName, USILLocalType.Vector4));
		}

		private static int[] SampleOffsets(SHDRInstruction inst, ResourceDimension dimension)
        {
            int[] all = { inst.addrOffsetIU, inst.addrOffsetIV, inst.addrOffsetIW };
            if (all.All(v => v == 0)) return Array.Empty<int>();
            int count = dimension switch {
                ResourceDimension.texture1d or ResourceDimension.texture1darray => 1,
                ResourceDimension.texture2d or ResourceDimension.texture2darray => 2,
                ResourceDimension.texture3d => 3,
                _ => throw new NotSupportedException("Texel offsets on " + dimension)
            };
            if (all.Skip(count).Any(v => v != 0))
                throw new InvalidDataException("Nonzero offset outside texture dimensions");
            return all.Take(count).ToArray();
        }

        private static int[] GetSampleGradientMask(ResourceDimension dimension)
        {
            int count = dimension switch {
                ResourceDimension.texture1d or ResourceDimension.texture1darray => 1,
                ResourceDimension.texture2d or ResourceDimension.texture2darray => 2,
                ResourceDimension.texture3d or ResourceDimension.texturecube or ResourceDimension.texturecubearray => 3,
                _ => throw new NotSupportedException("Sample gradients on " + dimension)
            };
            return GetMaskOfSize(count);
        }

        private static int[] GetSampleLocationMask(ResourceDimension dimension)
		{
			switch (dimension)
			{
				case ResourceDimension.texture1d:
					return GetMaskOfSize(1);
				case ResourceDimension.texture1darray:
				case ResourceDimension.texture2d:
					return GetMaskOfSize(2);
				case ResourceDimension.texture2darray:
				case ResourceDimension.texture3d:
				case ResourceDimension.texturecube:
					return GetMaskOfSize(3);
				case ResourceDimension.texturecubearray:
					return GetMaskOfSize(4);
				default:
					return GetMaskOfSize(2); // not handled yet
			}
		}

		private static int[] GetSampleOffsetMask(ResourceDimension dimension)
		{
			switch (dimension)
			{
				case ResourceDimension.texture1d:
				case ResourceDimension.texture1darray:
					return GetMaskOfSize(1);
				case ResourceDimension.texture2d:
				case ResourceDimension.texture2darray:
					return GetMaskOfSize(2);
				case ResourceDimension.texture3d:
					return GetMaskOfSize(3);
				case ResourceDimension.texturecube:
				case ResourceDimension.texturecubearray:
					return GetMaskOfSize(4);
				default:
					return GetMaskOfSize(2); // not handled yet
			}
		}

		private static int[] GetLoadLocationMask(ResourceDimension dimension)
		{
			switch (dimension)
			{
				case ResourceDimension.buffer:
					return GetMaskOfSize(1);
				case ResourceDimension.texture1d:
				case ResourceDimension.texture2dms:
					return GetMaskOfSize(2);
				case ResourceDimension.texture1darray:
				case ResourceDimension.texture2d:
				case ResourceDimension.texture2dmsarray:
					return GetMaskOfSize(3);
				case ResourceDimension.texture2darray:
				case ResourceDimension.texture3d:
					return GetMaskOfSize(4);
				default:
					return GetMaskOfSize(2); // not handled yet
			}
		}

		private static int[] GetLoadSampleIndexMask(ResourceDimension dimension)
		{
			switch (dimension)
			{
				case ResourceDimension.texture2dms:
				case ResourceDimension.texture2dmsarray:
					return GetMaskOfSize(2);
				default:
					return GetMaskOfSize(2); // not handled yet
			}
		}

		private static int[] GetLoadOffsetMask(ResourceDimension dimension)
		{
			switch (dimension)
			{
				case ResourceDimension.texture1d:
				case ResourceDimension.texture1darray:
					return GetMaskOfSize(1);
				case ResourceDimension.texture2d:
				case ResourceDimension.texture2darray:
				case ResourceDimension.texture2dms:
				case ResourceDimension.texture2dmsarray:
					return GetMaskOfSize(2);
				case ResourceDimension.texture3d:
					return GetMaskOfSize(3);
				default:
					return GetMaskOfSize(2); // not handled yet
			}
		}

		public static int[] GetMaskOfSize(int size) => size switch
		{
			0 => Array.Empty<int>(),
			1 => new[] { 0 },
			2 => new[] { 0, 1 },
			3 => new[] { 0, 1, 2 },
			4 => new[] { 0, 1, 2, 3 },
			_ => throw new ArgumentOutOfRangeException(nameof(size)),
		};

		///////////////////////
		// stuff to be moved to other classes

		private static int ConvertFloatToInt(float f)
		{
			return BitConverter.SingleToInt32Bits(f);
		}
	}
}
