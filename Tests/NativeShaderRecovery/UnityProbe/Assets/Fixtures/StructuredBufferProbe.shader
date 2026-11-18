Shader "Hidden/NativeStructuredProbe" { SubShader { Cull Off ZWrite Off ZTest Always Pass { CGPROGRAM
#pragma target 4.5
#pragma vertex vert_img
#pragma fragment frag
#include "UnityCG.cginc"
float _Index;
StructuredBuffer<uint> _Values;
			int _NativeH3VRBufferCount_Values;
			float NativeStructuredUintLoad_Values(float v) {
				uint index; if (isnan(v) || v <= 0.0) index = 0u; else if (v >= 4294967296.0) index = 0xffffffffu; else index = (uint)v;
				return _NativeH3VRBufferCount_Values > 0 && index < (uint)_NativeH3VRBufferCount_Values ? (float)_Values[index] : 0.0;
			}
float4 frag(v2f_img i) : SV_Target { return float4(NativeStructuredUintLoad_Values(_Index),0,0,1); }
ENDCG } } }
