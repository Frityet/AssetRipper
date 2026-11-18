using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class StructuredVertexProbe
{
    private const int Width = 8;
    private const int Stride = 12;

    public static void Run()
    {
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal)
            throw new InvalidOperationException("Metal is required.");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Fixtures/StructuredVertexProbe.shader");
        if (shader == null || ShaderUtil.ShaderHasError(shader))
            throw new InvalidOperationException("The synthetic shader failed to import.");

        var data = new[]
        {
            new Vector3(-1.25f, 0.0625f, 12.5f),
            new Vector3(11.5f, -20.25f, 3.125f),
            new Vector3(0.5f, 16.75f, -200.75f),
            new Vector3(-9.625f, 1.5f, -100000.5f),
        };
        using var buffer = new ComputeBuffer(data.Length, Stride);
        buffer.SetData(data);
        using var commands = new CommandBuffer();
        var material = new Material(shader);
        var target = RenderTexture.GetTemporary(Width, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var readback = new Texture2D(Width, 1, TextureFormat.RGBAFloat, false, true);
        var priorTarget = RenderTexture.active;
        int countId = Shader.PropertyToID("_NativeH3VRBufferCount_ProbePositions");
        try
        {
            material.SetBuffer("_ProbePositions", buffer);
            foreach (int count in new[] { data.Length, data.Length - 1, 0, -1 })
            {
                material.SetInteger(countId, count);
                DrawAndVerify(commands, target, readback, material, null, data, count);
            }

            material.SetInteger(countId, 0);
            var block = new MaterialPropertyBlock();
            block.SetBuffer(Shader.PropertyToID("_ProbePositions"), buffer);
            block.SetInteger(countId, data.Length);
            DrawAndVerify(commands, target, readback, material, block, data, data.Length);
            Debug.Log($"NATIVE_STRUCTURED_VERTEX_PROBE PASS {Width * 5} vertex-ID readbacks on {SystemInfo.graphicsDeviceName}");
        }
        finally
        {
            RenderTexture.active = priorTarget;
            RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(readback);
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static void DrawAndVerify(CommandBuffer commands, RenderTexture target, Texture2D readback,
        Material material, MaterialPropertyBlock properties, Vector3[] data, int count)
    {
        commands.Clear();
        commands.SetRenderTarget(target);
        commands.ClearRenderTarget(false, true, Color.clear);
        commands.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Points, Width, 1, properties);
        Graphics.ExecuteCommandBuffer(commands);
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, Width, 1), 0, 0);
        readback.Apply();
        for (int vertexId = 0; vertexId < Width; vertexId++)
        {
            Vector3 expected = vertexId < count ? data[vertexId] : Vector3.zero;
            var actual = readback.GetPixel(vertexId, 0);
            if (actual.a != 1f || !SameBits(expected.x, actual.r) ||
                !SameBits(expected.y, actual.g) || !SameBits(expected.z, actual.b))
            {
                throw new InvalidOperationException($"Vertex {vertexId}, count {count} expected {expected}, read {actual}.");
            }
        }
    }

    private static bool SameBits(float expected, float actual) =>
        BitConverter.SingleToInt32Bits(expected) == BitConverter.SingleToInt32Bits(actual);
}
