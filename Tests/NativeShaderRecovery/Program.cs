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
var structured = new USILInstruction { instructionType = USILInstructionType.LoadStructuredUintToFloat, destOperand = Named("result", [2]), srcOperands = [Named("coord", [2]), Named("_Values", [0])] };
Expect(Emit(structured), "result.z = NativeStructuredUintLoad_Values(coord.z)");
try { Emit(new USILInstruction { instructionType = USILInstructionType.LoadResource, destOperand = dst, srcOperands = [coords, new USILOperand { mask = [0] }] }); throw new Exception("missing binding accepted"); } catch (InvalidDataException) { }
Console.WriteLine("PASS typed load, gather component, offsets, result swizzle and missing binding");

// Synthetic DXBC-shaped operands: only the proven aliased, 4-byte,
// zero-offset scalar conversion may collapse to a typed structured load.
var uninit = typeof(System.Runtime.CompilerServices.RuntimeHelpers).GetMethod("GetUninitializedObject", new[]{typeof(Type)})!;
T Blank<T>() where T : class => (T)uninit.Invoke(null, new object[]{typeof(T)})!;
var shdr = Blank<AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks.SHDR>();
var decl = Blank<AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks.SHDRDeclData>();
var lane = Blank<AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks.SHDRInstructionOperand>();
var res = Blank<AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks.SHDRInstructionOperand>();
var zero = Blank<AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks.SHDRInstructionOperand>();
var op = typeof(AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Operand);
var opc = typeof(AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Opcode);
lane.operand = (AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Operand)Enum.Parse(op,"Temp"); lane.arraySizes=[0]; lane.swizzle=[2]; lane.subOperands=new AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks.SHDRInstructionOperand[1];
res.operand=(AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Operand)Enum.Parse(op,"Resource"); res.arraySizes=[0]; res.swizzle=[0,0,0,0]; res.subOperands=new AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks.SHDRInstructionOperand[1];
zero.operand=(AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Operand)Enum.Parse(op,"Immediate32");zero.immValues=[0];
AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks.SHDRInstruction Inst(string name, params AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks.SHDRInstructionOperand[] operands) { var i=Blank<AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks.SHDRInstruction>();i.opcode=(AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Opcode)Enum.Parse(opc,name);i.operands=operands.ToList();return i; }
decl.structuredStride=4;decl.operands=[res];var declaration=Inst("dcl_resource_structured");declaration.declData=decl;shdr.shaderInstructions=[declaration];
var dx=Blank<AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.DirectXCompiledShader>();dx.blocks=[shdr];
var convert=new AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.DirectX.DirectXProgramToUSIL(dx);
var fuse=convert.GetType().GetMethod("TryFuseStructuredUintLoad",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
var ftou=Inst("ftou",lane,lane);var structuredLoad=Inst("ld_structured",lane,lane,zero,res);var utof=Inst("utof",lane,lane);
if (!(bool)fuse.Invoke(convert,[ftou,structuredLoad,utof])!) throw new Exception("proven scalar pattern rejected");
decl.structuredStride=16;if ((bool)fuse.Invoke(convert,[ftou,structuredLoad,utof])!) throw new Exception("stride mismatch accepted");
decl.structuredStride=4;zero.immValues=[1];if ((bool)fuse.Invoke(convert,[ftou,structuredLoad,utof])!) throw new Exception("nonzero byte offset accepted");
Console.WriteLine("PASS structured scalar fusion and fail-closed stride/offset checks");

// Minimal serialized dcl_resource_structured token stream, preserving register and byte stride.
using var declarationBytes = new MemoryStream();
using (var writer = new BinaryWriter(declarationBytes, System.Text.Encoding.UTF8, true))
{
    writer.Write((short)0x50); writer.Write((short)0); writer.Write(6);
    writer.Write((int)Enum.Parse(opc,"dcl_resource_structured") | (4 << 24));
    writer.Write(((int)Enum.Parse(op,"Resource") << 12) | (1 << 20)); writer.Write(3); writer.Write(16);
}
declarationBytes.Position = 0;
var parsed = new AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks.SHDR(declarationBytes, []);
var parsedDecl = parsed.shaderInstructions.Single().declData!;
if (parsedDecl.structuredStride != 16 || parsedDecl.operands.Single().arraySizes.Single() != 3) throw new Exception("structured DXBC declaration lost register or stride");
Console.WriteLine("PASS synthetic DXBC structured declaration retains resource index and stride");
