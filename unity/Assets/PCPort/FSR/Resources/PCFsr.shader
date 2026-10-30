// PC port: AMD FidelityFX Super Resolution 1 (EASU upscale, then RCAS sharpening) for PCGraphics' upscaler.
// ffx_a.cginc and ffx_fsr1.cginc are AMD's ffx_a.h and ffx_fsr1.h (MIT, see ../LICENSE.txt), unchanged.
// Both passes work in the source texture's own texel space (the blit's uv times its size), so the result is the
// right way up whether Unity flips the destination or not.
Shader "Hidden/PCPort/FSR1"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "black" {}
	}
	SubShader
	{
		Cull Off ZWrite Off ZTest Always

		// 0: EASU. _MainTex is the low resolution image; _FsrInput = (render w, h, texture w, h); _FsrOutput = (w, h).
		Pass
		{
			CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag
			#pragma target 5.0
			#pragma only_renderers d3d11
			#include "UnityCG.cginc"

			#define A_GPU 1
			#define A_HLSL 1
			#include "ffx_a.cginc"

			Texture2D _MainTex;
			SamplerState sampler_MainTex;
			float4 _FsrInput;
			float4 _FsrOutput;

			#define FSR_EASU_F 1
			AF4 FsrEasuRF(AF2 p) { return _MainTex.GatherRed(sampler_MainTex, p); }
			AF4 FsrEasuGF(AF2 p) { return _MainTex.GatherGreen(sampler_MainTex, p); }
			AF4 FsrEasuBF(AF2 p) { return _MainTex.GatherBlue(sampler_MainTex, p); }
			#include "ffx_fsr1.cginc"

			float4 frag(v2f_img i) : SV_Target
			{
				AU4 con0, con1, con2, con3;
				FsrEasuCon(con0, con1, con2, con3, _FsrInput.x, _FsrInput.y, _FsrInput.z, _FsrInput.w, _FsrOutput.x, _FsrOutput.y);
				AU2 ip = AU2(min(i.uv * _FsrOutput.xy, _FsrOutput.xy - 1.0));
				AF3 c;
				FsrEasuF(c, ip, con0, con1, con2, con3);
				return float4(c, 1.0);
			}
			ENDCG
		}

		// 1: RCAS. _MainTex is the upscaled image; _FsrSharpness = sharpening in stops (0 is the most).
		Pass
		{
			CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag
			#pragma target 5.0
			#pragma only_renderers d3d11
			#include "UnityCG.cginc"

			#define A_GPU 1
			#define A_HLSL 1
			#include "ffx_a.cginc"

			Texture2D _MainTex;
			float4 _MainTex_TexelSize;
			float _FsrSharpness;

			#define FSR_RCAS_F 1
			AF4 FsrRcasLoadF(ASU2 p) { return _MainTex.Load(int3(clamp(p, int2(0, 0), int2(_MainTex_TexelSize.zw) - 1), 0)); }
			void FsrRcasInputF(inout AF1 r, inout AF1 g, inout AF1 b) {}
			#include "ffx_fsr1.cginc"

			float4 frag(v2f_img i) : SV_Target
			{
				AU4 con = AU4(asuint(exp2(-_FsrSharpness)), 0, 0, 0);
				AU2 ip = AU2(min(i.uv * _MainTex_TexelSize.zw, _MainTex_TexelSize.zw - 1.0));
				AF1 r, g, b;
				FsrRcasF(r, g, b, ip, con);
				return float4(r, g, b, 1.0);
			}
			ENDCG
		}

		// 2: plain bilinear upscale (when FSR's shaders cannot run).
		Pass
		{
			CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			float4 frag(v2f_img i) : SV_Target { return float4(tex2D(_MainTex, i.uv).rgb, 1.0); }
			ENDCG
		}
	}
}
