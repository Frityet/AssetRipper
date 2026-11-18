using System.Text;
using AssetRipper.Export.Modules.Shaders.ShaderBlob;
using AssetRipper.Export.Modules.Shaders.ShaderBlob.Parameters;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.Converter;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.DirectXDisassembler;
using AssetRipper.Export.Modules.Shaders.UltraShaderConverter.USIL;
using AssetRipper.Primitives;

internal static class StructuredVertexLoadTest
{
    private const int BufferRegister = 7;
    private const int VertexIdRegister = 3;
    private const int IdentityXYZ = 0 | (1 << 2) | (2 << 4);

    public static void Run()
    {
        var shader = ReadFixture();
        Assert(shader.Isgn.inputs.Single().IsVertexId, "DXBC signature lost the vertex ID");
        Assert(shader.Shdr.GetStructuredResourceStride(BufferRegister) == 12, "DXBC declaration lost the stride");
        var converter = Convert(shader);
        var load = converter.ShaderProgram!.instructions.Single(i => i.instructionType == USILInstructionType.LoadStructuredWords3);
        Assert(load.destOperand!.mask.SequenceEqual([0, 1, 2]), "Load changed the destination mask");
        converter.ApplyMetadataToProgram(new ShaderSubProgram
        {
            ProgramType = 16, // Unity 5.6 Direct3D 11 SM5 vertex
            BufferParameters = [new BufferBinding("_SyntheticPositions", BufferRegister)],
        }, UnityVersion.Parse("5.6.7f1"));
        string hlsl = converter.CovnertUShaderProgramToHLSL(0);
        Assert(hlsl.Contains("tmp0.xyz = NativeStructuredWords3Load_SyntheticPositions(vertexId);", StringComparison.Ordinal), hlsl);

        CheckUnsupported(ReadFixture(stride: 16), "stride 16");
        CheckUnsupported(ReadFixture(byteOffset: 4u), "nonzero byte offset");
        CheckUnsupported(ReadFixture(byteOffset: 0x80000000u), "high-bit byte offset");
        CheckUnsupported(ReadFixture(componentFormat: FormatType.@float), "float index");
        CheckUnsupported(ReadFixture(resourceSwizzle: IdentityXYZ | 1), "different resource swizzle");
        CheckUnsupported(ReadFixture(destinationMask: 0b0011), "different destination mask");
        Console.WriteLine("PASS stride-12 structured vertex load, uint index, binding and unsupported shapes");
    }

    private static USCShaderConverter Convert(DirectXCompiledShader shader)
    {
        var converter = new USCShaderConverter { DxShader = shader };
        converter.ConvertShaderToUShaderProgram();
        return converter;
    }

    private static void CheckUnsupported(DirectXCompiledShader shader, string shape)
    {
        var instructions = Convert(shader).ShaderProgram!.instructions;
        Assert(instructions.All(i => i.instructionType != USILInstructionType.LoadStructuredWords3), $"Accepted {shape}");
    }

    private static DirectXCompiledShader ReadFixture(int stride = 12, uint byteOffset = 0,
        FormatType componentFormat = FormatType.@uint, int resourceSwizzle = IdentityXYZ, int destinationMask = 0b0111)
    {
        var chunks = new (string Name, byte[] Bytes)[]
        {
            ("ISGN", Bytes(writer =>
            {
                writer.Write(1); // one signature entry
                writer.Write(8);
                writer.Write(32); // name follows the 24-byte entry
                writer.Write(0); // semantic index
                writer.Write((int)SysValueType.vertid);
                writer.Write((int)componentFormat);
                writer.Write(VertexIdRegister);
                writer.Write(0x0101); // declared and used masks are both x
                writer.Write("SV_VertexID\0"u8);
            })),
            ("OSGN", Bytes(writer => { writer.Write(0); writer.Write(8); })),
            ("SHEX", Bytes(writer =>
            {
                writer.Write(0x00010050); // vertex shader 5.0
                writer.Write(18); // dwords including the header
                writer.Write((int)Opcode.dcl_resource_structured | (4 << 24));
                Register(writer, Operand.Resource, 0, BufferRegister);
                writer.Write(stride);
                writer.Write((int)Opcode.dcl_temps | (2 << 24));
                writer.Write(1);
                writer.Write((int)Opcode.ld_structured | (9 << 24));
                Register(writer, Operand.Temp, 2 | (destinationMask << 4), 0);
                Register(writer, Operand.Input, 2 | (2 << 2), VertexIdRegister);
                writer.Write(((int)Operand.Immediate32 << 12) | 1);
                writer.Write(byteOffset);
                Register(writer, Operand.Resource, 2 | (1 << 2) | (resourceSwizzle << 4), BufferRegister);
                writer.Write((int)Opcode.ret | (1 << 24));
            })),
        };
        int headerLength = 32 + 4 * chunks.Length;
        var dxbc = Bytes(writer =>
        {
            writer.Write("DXBC"u8);
            writer.Write(new byte[16]);
            writer.Write(1);
            writer.Write(headerLength + chunks.Sum(chunk => 8 + chunk.Bytes.Length));
            writer.Write(chunks.Length);
            int offset = headerLength;
            foreach (var chunk in chunks)
            {
                writer.Write(offset);
                offset += 8 + chunk.Bytes.Length;
            }
            foreach (var chunk in chunks)
            {
                writer.Write(Encoding.ASCII.GetBytes(chunk.Name));
                writer.Write(chunk.Bytes.Length);
                writer.Write(chunk.Bytes);
            }
        });
        return new DirectXCompiledShader(new MemoryStream(dxbc, writable: false));
    }

    private static void Register(BinaryWriter writer, Operand operand, int components, int register)
    {
        writer.Write(((int)operand << 12) | (1 << 20) | components);
        writer.Write(register);
    }

    private static byte[] Bytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        write(writer);
        return stream.ToArray();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
