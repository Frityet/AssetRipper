using System.Reflection;
using System.Runtime.CompilerServices;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler.Blocks;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.DirectX;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL;

static USILOperand Named(string name, int[] mask) => new()
{
    operandType = USILOperandType.ResourceRegister,
    metadataName = name,
    metadataNameAssigned = true,
    mask = mask
};

static string Emit(USILInstruction instruction)
{
    var program = new UShaderProgram();
    program.instructions.Add(instruction);
    return new UShaderFunctionToHLSL(program).Convert(0);
}

static void Expect(string text, string expected)
{
    if (!text.Contains(expected, StringComparison.Ordinal))
        throw new Exception($"Expected {expected}; got {text}");
}

var destination = Named("result", [0, 2]);
var coordinates = Named("coord", [0, 1, 2]);
var texture = Named("_FixtureTexture", [3, 0]);
var sampler = Named("_FixtureTexture", [0]);
var load = new USILInstruction
{
    instructionType = USILInstructionType.LoadResource,
    destOperand = destination,
    srcOperands = [coordinates, texture],
    textureOffsets = [1, -2]
};
Expect(Emit(load), "_FixtureTexture.Load(int3(coord.xyz), int2(1, -2)).wx");
var gather = new USILInstruction
{
    instructionType = USILInstructionType.Gather,
    destOperand = destination,
    srcOperands = [coordinates, texture, sampler],
    gatherComponent = 2,
    textureOffsets = [1, -2]
};
Expect(Emit(gather), "_FixtureTexture.GatherBlue(sampler_FixtureTexture, coord.xyz, int2(1, -2)).wx");
var structured = new USILInstruction
{
    instructionType = USILInstructionType.LoadStructuredUintToFloat,
    destOperand = Named("result", [2]),
    srcOperands = [Named("coord", [2]), Named("_Values", [0])]
};
Expect(Emit(structured), "result.z = NativeStructuredUintLoad_Values(coord.z)");
try
{
    Emit(new USILInstruction
    {
        instructionType = USILInstructionType.LoadResource,
        destOperand = destination,
        srcOperands = [coordinates, new USILOperand { mask = [0] }]
    });
    throw new Exception("Missing binding was accepted");
}
catch (InvalidDataException) { }
Console.WriteLine("PASS typed load, gather component, offsets, result swizzle and missing binding");

// Synthetic DXBC-shaped operands. Only the proven aliased scalar conversion
// with a four-byte stride and a zero byte offset may be fused.
static T Blank<T>() where T : class => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
static SHDRInstruction Instruction(Opcode opcode, params SHDRInstructionOperand[] operands)
{
    var instruction = Blank<SHDRInstruction>();
    instruction.opcode = opcode;
    instruction.operands = operands.ToList();
    return instruction;
}

var lane = Blank<SHDRInstructionOperand>();
lane.operand = Operand.Temp;
lane.arraySizes = [0];
lane.swizzle = [2];
lane.subOperands = new SHDRInstructionOperand[1];
var resource = Blank<SHDRInstructionOperand>();
resource.operand = Operand.Resource;
resource.arraySizes = [0];
resource.swizzle = [0, 0, 0, 0];
resource.subOperands = new SHDRInstructionOperand[1];
var zero = Blank<SHDRInstructionOperand>();
zero.operand = Operand.Immediate32;
zero.immValues = [0];
var declaration = Blank<SHDRDeclData>();
declaration.structuredStride = 4;
declaration.operands = [resource];
var declarationInstruction = Instruction(Opcode.dcl_resource_structured);
declarationInstruction.declData = declaration;
var shader = Blank<SHDR>();
shader.shaderInstructions = [declarationInstruction];
var compiled = Blank<DirectXCompiledShader>();
compiled.blocks = [shader];
var converter = new DirectXProgramToUSIL(compiled);
var fuse = converter.GetType().GetMethod("TryFuseStructuredUintLoad", BindingFlags.Instance | BindingFlags.NonPublic)
    ?? throw new Exception("Structured-load fusion not found");
var ftou = Instruction(Opcode.ftou, lane, lane);
var structuredLoad = Instruction(Opcode.ld_structured, lane, lane, zero, resource);
var utof = Instruction(Opcode.utof, lane, lane);
static bool InvokeFusion(MethodInfo method, DirectXProgramToUSIL converter,
    SHDRInstruction ftou, SHDRInstruction load, SHDRInstruction utof) =>
    (bool)method.Invoke(converter, [ftou, load, utof])!;
if (!InvokeFusion(fuse, converter, ftou, structuredLoad, utof))
    throw new Exception("Proven scalar pattern was rejected");
declaration.structuredStride = 16;
if (InvokeFusion(fuse, converter, ftou, structuredLoad, utof))
    throw new Exception("Stride mismatch was accepted");
declaration.structuredStride = 4;
zero.immValues = [1];
if (InvokeFusion(fuse, converter, ftou, structuredLoad, utof))
    throw new Exception("Nonzero byte offset was accepted");
Console.WriteLine("PASS structured scalar fusion and fail-closed stride/offset checks");

// A minimal serialized declaration verifies the binary reader, not just the
// higher-level instruction objects.
using var declarationBytes = new MemoryStream();
using (var writer = new BinaryWriter(declarationBytes, System.Text.Encoding.UTF8, true))
{
    writer.Write((short)0x50);
    writer.Write((short)0);
    writer.Write(6);
    writer.Write((int)Opcode.dcl_resource_structured | (4 << 24));
    writer.Write(((int)Operand.Resource << 12) | (1 << 20));
    writer.Write(3);
    writer.Write(16);
}
declarationBytes.Position = 0;
var parsed = new SHDR(declarationBytes, []);
var parsedDeclaration = parsed.shaderInstructions.Single().declData!;
if (parsedDeclaration.structuredStride != 16 || parsedDeclaration.operands.Single().arraySizes.Single() != 3)
    throw new Exception("Structured DXBC declaration lost resource index or stride");
Console.WriteLine("PASS synthetic DXBC structured declaration retains resource index and stride");

StructuredVertexLoadTest.Run();
