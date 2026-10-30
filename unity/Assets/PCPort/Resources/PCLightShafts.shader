// PC port: screen space light shafts for PCLightShafts. Bright, distant pixels around the main light are blurred
// toward its position on screen and added back over the image.
Shader "Hidden/PCPort/LightShafts"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "black" {}
	}
	CGINCLUDE
	#include "UnityCG.cginc"
	sampler2D _MainTex;
	float4 _MainTex_TexelSize;
	sampler2D _ShaftsTex;
	sampler2D_float _CameraDepthTexture;
	float4 _SunPos;       // xy: the light on screen (uv), z: fade, w: blur length
	float4 _SunColor;     // rgb: colour times intensity
	float4 _ShaftParams;  // x: brightness threshold, y: use depth (0 or 1), z: decay
	ENDCG
	SubShader
	{
		Cull Off ZWrite Off ZTest Always

		// 0: the parts of the image that cast shafts: bright and far away (the sky, lamps), fading with distance from
		// the light.
		Pass
		{
			CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag
			float4 frag(v2f_img i) : SV_Target
			{
				float3 c = tex2D(_MainTex, i.uv).rgb;
				float lum = dot(c, float3(0.2126, 0.7152, 0.0722));
				float bright = saturate((lum - _ShaftParams.x) / max(1.0 - _ShaftParams.x, 0.001));
				float far = 1.0;
				if (_ShaftParams.y > 0.5)
				{
					float d = Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv));
					far = smoothstep(0.35, 0.8, d);
				}
				float2 toSun = (i.uv - _SunPos.xy) * float2(_MainTex_TexelSize.y * _MainTex_TexelSize.z, 1.0);
				float around = saturate(1.0 - length(toSun) * 1.1);
				return float4(c * bright * far * around, 1.0);
			}
			ENDCG
		}

		// 1: radial blur toward the light.
		Pass
		{
			CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag
			#pragma target 3.0
			float4 frag(v2f_img i) : SV_Target
			{
				const int kSamples = 16;
				float2 step = (_SunPos.xy - i.uv) * (_SunPos.w / kSamples);
				float2 uv = i.uv;
				float3 sum = 0;
				float weight = 1.0;
				float total = 0;
				for (int s = 0; s < kSamples; s++)
				{
					sum += tex2D(_MainTex, uv).rgb * weight;
					total += weight;
					weight *= _ShaftParams.z;
					uv += step;
				}
				return float4(sum / total, 1.0);
			}
			ENDCG
		}

		// 2: add the shafts to the image.
		Pass
		{
			CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag
			float4 frag(v2f_img i) : SV_Target
			{
				float4 c = tex2D(_MainTex, i.uv);
				float3 shafts = tex2D(_ShaftsTex, i.uv).rgb * _SunColor.rgb * _SunPos.z;
				c.rgb += shafts * (1.0 - saturate(c.rgb * 0.5));
				return c;
			}
			ENDCG
		}
	}
}
