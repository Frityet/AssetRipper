// Synthetic only: no game assets or game code are needed.
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class StructuredProbe
{
    public static void Run()
    {
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal) throw new Exception("Metal device required");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Fixtures/StructuredBufferProbe.shader");
        if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Synthetic shader did not compile");
        var material = new Material(shader);
        var data = new uint[] { 7, 1, 16777217, UInt32.MaxValue };
        var buffer = new ComputeBuffer(data.Length, 4); buffer.SetData(data);
        var target = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-1,-1,0), new Vector3(3,-1,0), new Vector3(-1,3,0) };
        mesh.uv = new[] { new Vector2(0,0), new Vector2(2,0), new Vector2(0,2) };
        mesh.triangles = new[] { 0, 1, 2 };
        var commands = new CommandBuffer();
        try
        {
            material.SetBuffer("_Values", buffer);
            var inputs = new float[] { 0,0,1,2,3,4,-2,4294967296f,float.NaN };
            var counts = new int[] { 0,4,4,4,4,4,4,4,4 };
            var expected = new float[] { 0,7,1,16777216f,4294967296f,0,7,0,7 };
            for (int i=0;i<inputs.Length;i++)
            {
                material.SetFloat("_Index", inputs[i]); material.SetInteger("_NativeH3VRBufferCount_Values", counts[i]);
                Graphics.Blit(Texture2D.whiteTexture, target, material, 0);
                RenderTexture.active = target; read.ReadPixels(new Rect(0,0,1,1),0,0); read.Apply();
                if (read.GetPixel(0,0).r != expected[i]) throw new Exception("Material readback mismatch " + i);
            }
            var fallback = new ComputeBuffer(1, 4);
            try
            {
                fallback.SetData(new uint[] { 0 });
                material.SetBuffer("_Values", fallback);
                material.SetInteger("_NativeH3VRBufferCount_Values", 0);
                material.SetFloat("_Index", 4294967296f);
                Graphics.Blit(Texture2D.whiteTexture, target, material, 0);
                RenderTexture.active = target; read.ReadPixels(new Rect(0,0,1,1),0,0); read.Apply();
                if (read.GetPixel(0,0).r != 0) throw new Exception("Fallback readback mismatch");
                material.SetBuffer("_Values", buffer);
            }
            finally { fallback.Release(); }
            var block = new MaterialPropertyBlock();
            block.SetBuffer(Shader.PropertyToID("_Values"), buffer); block.SetFloat(Shader.PropertyToID("_Index"), 2f);
            material.SetInteger("_NativeH3VRBufferCount_Values", 0);
            for (int i=0;i<2;i++)
            {
                block.SetInteger(Shader.PropertyToID("_NativeH3VRBufferCount_Values"), i==0 ? 4 : 0);
                commands.Clear(); commands.SetRenderTarget(target); commands.ClearRenderTarget(true,true,Color.clear);
                commands.SetViewProjectionMatrices(Matrix4x4.identity,Matrix4x4.identity);
                commands.DrawMesh(mesh,Matrix4x4.identity,material,0,0,block); Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active=target; read.ReadPixels(new Rect(8,8,1,1),0,0); read.Apply();
                if (read.GetPixel(0,0).r != (i==0 ? 16777216f : 0f)) throw new Exception("Property block readback mismatch " + i);
            }
            Debug.Log("NATIVE_STRUCTURED_PROBE PASS 9 Material and 2 MPB readbacks on " + SystemInfo.graphicsDeviceName);
        }
        finally
        {
            commands.Release(); buffer.Release(); RenderTexture.active=null; RenderTexture.ReleaseTemporary(target);
            UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(read); UnityEngine.Object.DestroyImmediate(material);
        }
    }
}
