using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

static USILOperand Named(string name, int[] mask) => new() { operandType = USILOperandType.ResourceRegister, metadataName = name, metadataNameAssigned = true, mask = mask };
static string Emit(USILInstruction instruction) { var p = new UShaderProgram(); p.instructions.Add(instruction); return new UShaderFunctionToHLSL(p).Convert(0); }
static void Expect(string text, string expected) { if (!text.Contains(expected, StringComparison.Ordinal)) throw new Exception($"Expected {expected}; got {text}"); }
var dst = Named("result", [0, 2]); var coords = Named("coord", [0, 1, 2]);
var tex = Named("_Camera", [3, 0]); var sampler = Named("_Camera", [0]);
var load = new USILInstruction { instructionType = USILInstructionType.LoadResource, destOperand = dst, srcOperands = [coords, tex], textureOffsets = [1, -2] };
Expect(Emit(load), "_Camera.Load(int3(coord.xyz), int2(1, -2)).wx");
var gather = new USILInstruction { instructionType = USILInstructionType.Gather, destOperand = dst, srcOperands = [coords, tex, sampler], gatherComponent = 2, textureOffsets = [1, -2] };
Expect(Emit(gather), "_Camera.GatherBlue(sampler_Camera, coord.xyz, int2(1, -2)).wx");
try { Emit(new USILInstruction { instructionType = USILInstructionType.LoadResource, destOperand = dst, srcOperands = [coords, new USILOperand { mask = [0] }] }); throw new Exception("missing binding accepted"); } catch (InvalidDataException) { }
Console.WriteLine("PASS typed load, gather component, offsets, result swizzle and missing binding");
