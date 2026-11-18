using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.UShader.Function;

var dst = Named("result", [0, 2]);
var coords = Named("coord", [0, 1, 2]);
var tex = Named("_FixtureTexture", [3, 0]);
var sampler = Named("_FixtureTexture", [0]);
var load = new USILInstruction
{
    instructionType = USILInstructionType.LoadResource,
    destOperand = dst,
    srcOperands = [coords, tex],
    textureOffsets = [1, -2]
};
Expect(Emit(load), "_FixtureTexture.Load(int3(coord.xyz), int2(1, -2)).wx");
var gather = new USILInstruction
{
    instructionType = USILInstructionType.Gather,
    destOperand = dst,
    srcOperands = [coords, tex, sampler],
    gatherComponent = 2,
    textureOffsets = [1, -2]
};
Expect(Emit(gather), "_FixtureTexture.GatherBlue(sampler_FixtureTexture, coord.xyz, int2(1, -2)).wx");

var missingBinding = new USILInstruction
{
    instructionType = USILInstructionType.LoadResource,
    destOperand = dst,
    srcOperands = [coords, new USILOperand { mask = [0] }]
};
try
{
    Emit(missingBinding);
    throw new InvalidOperationException("A load without a binding was accepted.");
}
catch (InvalidDataException)
{
}
Console.WriteLine("PASS typed load, gather component, offsets, result swizzle and missing binding");

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
        throw new InvalidOperationException($"Missing expected shader source {expected}.\n{text}");
}
