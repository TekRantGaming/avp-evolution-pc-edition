// PC port: copies the camera's depth and motion vectors into PCDlssCapture's own textures for DLSS.
Shader "Hidden/PCPort/DlssInputs"
{
	SubShader
	{
		Cull Off ZWrite Off ZTest Always
		Pass
		{
			CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D_float _CameraDepthTexture;
			float4 frag(v2f_img i) : SV_Target { return SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv); }
			ENDCG
		}
		Pass
		{
			CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D_half _CameraMotionVectorsTexture;
			float4 frag(v2f_img i) : SV_Target { return float4(tex2D(_CameraMotionVectorsTexture, i.uv).rg, 0, 0); }
			ENDCG
		}
	}
}
