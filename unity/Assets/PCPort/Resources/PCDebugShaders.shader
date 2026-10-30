// PC port: debugging aids for PCConsole's "shader" command. Texture only, both sides, no lighting.
Shader "Hidden/PCPort/DebugUnlit"
{
	Properties { _MainTex ("Texture", 2D) = "white" {} }
	SubShader
	{
		Tags { "RenderType"="Opaque" }
		Cull Off
		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
			v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
			fixed4 frag(v2f i) : SV_Target { return fixed4(tex2D(_MainTex, i.uv).rgb * 0.8 + 0.2, 1); }
			ENDCG
		}
	}
}
