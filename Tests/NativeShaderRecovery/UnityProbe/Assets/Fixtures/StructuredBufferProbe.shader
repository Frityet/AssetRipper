Shader "Hidden/NativeStructuredProbe" { SubShader { Cull Off ZWrite Off ZTest Always Pass { CGPROGRAM
#pragma target 4.5
#pragma vertex vert_img
#pragma fragment frag
#include "UnityCG.cginc"
float _Index;
StructuredBuffer<uint> _Values;
#if defined(SHADER_API_METAL)
			int _NativeH3VRBufferCount_Values;
#endif
			float NativeStructuredUintLoad_Values(float v) {
				uint index; if (isnan(v) || v <= 0.0) index = 0u; else if (v >= 4294967296.0) index = 0xffffffffu; else index = (uint)v;
#if defined(SHADER_API_METAL)
				uint count = (uint)max(_NativeH3VRBufferCount_Values, 0);
#elif defined(SHADER_API_D3D11)
				uint count, stride; _Values.GetDimensions(count, stride);
#else
#error Structured buffer bounds are unsupported on this shader backend
#endif
				uint safeIndex = min(index, max(count, 1u) - 1u);
				float value = (float)_Values[safeIndex];
				return index < count ? value : 0.0;
			}
float4 frag(v2f_img i) : SV_Target { return float4(NativeStructuredUintLoad_Values(_Index),0,0,1); }
ENDCG } } }
