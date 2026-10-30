// PC port: debugging aid for PCConsole's "shader" command. Shows only the decoded lightmap.
Shader "Hidden/PCPort/DebugLightmap"
{
	SubShader
	{
		Tags { "RenderType"="Opaque" }
		Cull Off
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile _ LIGHTMAP_ON
			#include "UnityCG.cginc"
			struct v2f { float4 pos : SV_POSITION; float2 lm : TEXCOORD0; };
			v2f vert(appdata_full v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.lm = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw; return o; }
			fixed4 frag(v2f i) : SV_Target
			{
				#ifdef LIGHTMAP_ON
				return fixed4(DecodeLightmap(UNITY_SAMPLE_TEX2D(unity_Lightmap, i.lm)), 1);
				#else
				return fixed4(1, 0, 1, 1);
				#endif
			}
			ENDCG
		}
	}
}
