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

// Closed unsigned slices retain the integer values through max and only
// convert at the original utof. Distinct lanes expose aliasing mistakes.
static SHDRInstructionOperand TempLane(int register, params int[] lanes)
{
    var operand = Blank<SHDRInstructionOperand>();
    operand.operand = Operand.Temp;
    operand.arraySizes = [register];
    operand.swizzle = lanes;
    operand.subOperands = new SHDRInstructionOperand[1];
    return operand;
}
static SHDRInstructionOperand ImmediateIndex(uint value)
{
    var operand = Blank<SHDRInstructionOperand>();
    operand.operand = Operand.Immediate32;
    operand.immValues = [BitConverter.Int32BitsToSingle(unchecked((int)value))];
    operand.subOperands = new SHDRInstructionOperand[1];
    return operand;
}
var x = TempLane(0, 0);
var y = TempLane(0, 1);
var z = TempLane(0, 2);
zero.immValues = [0];
var rawLoad0 = Instruction(Opcode.ld_structured, x, ImmediateIndex(0), zero, resource);
var rawLoad1 = Instruction(Opcode.ld_structured, y, ImmediateIndex(1), zero, resource);
var unsignedMax = Instruction(Opcode.umax, z, x, y);
var conversions = Instruction(Opcode.utof, TempLane(0, 0, 1, 2), TempLane(0, 0, 1, 2, 3));
shader.shaderInstructions = [declarationInstruction, rawLoad0, rawLoad1, unsignedMax, conversions];
if (!DirectXProgramToUSIL.TryBuildClosedUnsignedRegion(shader, 1, out var region, out int resourceIndex, out int regionEnd) || resourceIndex != 0 || regionEnd != 4)
    throw new Exception("Valid unsigned region rejected");
var rendered = Emit(new USILInstruction { instructionType = USILInstructionType.ClosedUnsignedRegion, srcOperands = [Named("_Values", [0])], unsignedRegion = region });
Expect(rendered, "nativeUnsigned0.z = max(nativeUnsigned0.x, nativeUnsigned0.y)");
Expect(rendered, "tmp0.z = (float)nativeUnsigned0.z");
foreach (var values in new[] { new uint[] { 16777216, 16777217 }, new uint[] { 0xffffffff, 16777217 } })
{
    var registers = new uint[4];
    var floats = new float[4];
    foreach (var item in region)
        if (item.Kind == "load") registers[item.DestinationLane] = values[item.Index];
        else if (item.Kind == "max") registers[item.DestinationLane] = Math.Max(registers[item.LeftLane], registers[item.RightLane]);
        else floats[item.DestinationLane] = registers[item.DestinationLane];
    if (floats[2] != (float)Math.Max(values[0], values[1]) || registers[2] != Math.Max(values[0], values[1]))
        throw new Exception("Unsigned max or conversion changed");
}
unsignedMax.operands[1] = TempLane(1, 0);
if (DirectXProgramToUSIL.TryBuildClosedUnsignedRegion(shader, 1, out _, out _, out _))
    throw new Exception("Uninitialized source was accepted");
unsignedMax.operands[1] = x;
conversions.operands[1] = TempLane(1, 0, 1, 2, 3);
if (DirectXProgramToUSIL.TryBuildClosedUnsignedRegion(shader, 1, out _, out _, out _))
    throw new Exception("Cross-register conversion was accepted");
Console.WriteLine("PASS closed unsigned region, precision, uninitialized and alias rejection");

// Output-only moves can cross a closed integer slice; a stale scratch lane
// may be discarded only when a later straight-line instruction overwrites it.
var input = Blank<SHDRInstructionOperand>(); input.operand = Operand.Input; input.arraySizes = [0]; input.swizzle = [0, 1, 0, 0]; input.subOperands = new SHDRInstructionOperand[1];
var output = Blank<SHDRInstructionOperand>(); output.operand = Operand.Output; output.arraySizes = [0]; output.swizzle = [0, 1]; output.subOperands = new SHDRInstructionOperand[1];
var moveOutput = Instruction(Opcode.mov, output, input);
var convertMax = Instruction(Opcode.utof, z, z);
var overwriteScratch = Instruction(Opcode.mov, TempLane(0, 0, 1), input);
shader.shaderInstructions = [declarationInstruction, rawLoad0, rawLoad1, unsignedMax, moveOutput, convertMax, overwriteScratch];
if (!DirectXProgramToUSIL.TryBuildClosedUnsignedRegion(shader, 1, out _, out _, out _)) throw new Exception("Independent move and dead scratch were rejected");
moveOutput.operands[1] = x;
if (DirectXProgramToUSIL.TryBuildClosedUnsignedRegion(shader, 1, out _, out _, out _)) throw new Exception("Move reading unsigned state accepted");
moveOutput.operands[1] = input;
overwriteScratch.operands[1] = y;
if (DirectXProgramToUSIL.TryBuildClosedUnsignedRegion(shader, 1, out _, out _, out _)) throw new Exception("Read before overwrite accepted");
overwriteScratch.operands[1] = input;

// Dynamic index plus unsigned increment and two-lane utof.
var pairConvert = Instruction(Opcode.ftou, y, y);
var pairAdd = Instruction(Opcode.iadd, z, y, ImmediateIndex(1));
var pairLoad = Instruction(Opcode.ld_structured, y, y, zero, resource);
var pairResult = Instruction(Opcode.utof, TempLane(0, 1, 2), TempLane(0, 1, 1, 2, 1));
shader.shaderInstructions = [declarationInstruction, pairConvert, pairAdd, pairLoad, pairResult];
if (!DirectXProgramToUSIL.IsDynamicUnsignedLoadPair(shader, 1, out _)) throw new Exception("Valid dynamic pair rejected");
pairResult.operands[1] = TempLane(0, 1, 1, 1, 1);
if (DirectXProgramToUSIL.IsDynamicUnsignedLoadPair(shader, 1, out _)) throw new Exception("Wrong pair source swizzle accepted");
pairResult.operands[1] = TempLane(0, 1, 1, 2, 1);
pairAdd.operands[2] = ImmediateIndex(2);
if (DirectXProgramToUSIL.IsDynamicUnsignedLoadPair(shader, 1, out _)) throw new Exception("Wrong integer increment accepted");
Console.WriteLine("PASS independent moves, dead scratch, dynamic pair and negative cases");
