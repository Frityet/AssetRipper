Shader "Hidden/NativeStructuredVertexProbe"
{
    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct NativeStructuredWords3_ProbePositions { uint x; uint y; uint z; };
            StructuredBuffer<NativeStructuredWords3_ProbePositions> _ProbePositions;
            #if defined(SHADER_API_METAL)
            int _NativeH3VRBufferCount_ProbePositions;
            #endif
            float3 NativeStructuredWords3Load_ProbePositions(uint index) {
            #if defined(SHADER_API_METAL)
                uint count = (uint)max(_NativeH3VRBufferCount_ProbePositions, 0);
            #elif defined(SHADER_API_D3D11)
                uint count, stride; _ProbePositions.GetDimensions(count, stride);
            #else
            #error Structured buffer bounds are unsupported on this shader backend
            #endif
                uint safeIndex = min(index, max(count, 1u) - 1u);
                NativeStructuredWords3_ProbePositions words = _ProbePositions[safeIndex];
                return index < count ? asfloat(uint3(words.x, words.y, words.z)) : float3(0.0, 0.0, 0.0);
            }

            struct Varyings
            {
                float4 position : SV_POSITION;
                float pointSize : PSIZE;
                nointerpolation float3 value : TEXCOORD0;
            };

            Varyings vert(appdata_full v, uint vertexId : SV_VertexID)
            {
                Varyings result;
                result.position = float4((float(vertexId) + 0.5) * 0.25 - 1.0, 0.0, 0.0, 1.0);
                result.pointSize = 1.0;
                result.value = NativeStructuredWords3Load_ProbePositions(vertexId);
                return result;
            }

            float4 frag(Varyings input) : SV_Target
            {
                return float4(input.value, 1.0);
            }
            ENDCG
        }
    }
}
