# The shaders the game needs on PC. An Android export holds only placeholders: the name and properties of each shader,
# with no working code. Each one keeps that header from the player's own export, and gets a body from here. These
# bodies were written for the port from what each shader is for: its name, its properties and how the game calls it.
#   shaders.py <Assets folder> <folder of Unity's built-in shader source>
import os
import re
import sys

COMMON = """
		#include "UnityCG.cginc"
"""

PARTICLE_V2F = """
		struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
		struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
"""


def particle(state, colour, queue="Transparent", extra_tags=""):
    """Unlit, vertex coloured, for particles and effect meshes."""
    return """
	SubShader {
		Tags { "Queue"="%s" "IgnoreProjector"="True" "RenderType"="Transparent" %s }
		%s
		Cull Off Lighting Off ZWrite Off
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_particles
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _TintColor;
			struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
			struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
			v2f vert(appdata v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.color = v.color;
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				return o;
			}
			fixed4 frag(v2f i) : SV_Target {
				fixed4 tex = tex2D(_MainTex, i.uv);
				%s
			}
			ENDCG
		}
	}
""" % (queue, extra_tags, state, colour)


TINTED = "return 2.0 * i.color * _TintColor * tex;"
PLAIN = "return i.color * tex;"


def soft_particle(state):
    """Particles that fade out where they meet the scene, by _InvFade."""
    return """
	SubShader {
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
		%s
		Cull Off Lighting Off ZWrite Off
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_particles
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _TintColor;
			float _InvFade;
			sampler2D_float _CameraDepthTexture;
			struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
			struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 screen : TEXCOORD1; };
			v2f vert(appdata v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.screen = ComputeScreenPos(o.pos);
				COMPUTE_EYEDEPTH(o.screen.z);
				o.color = v.color;
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				return o;
			}
			fixed4 frag(v2f i) : SV_Target {
				float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screen)));
				i.color.a *= saturate(_InvFade * (scene - i.screen.z));
				return 2.0 * i.color * _TintColor * tex2D(_MainTex, i.uv);
			}
			ENDCG
		}
	}
""" % state


def screen(fragment, uniforms="", passes=1, state="ZTest Always Cull Off ZWrite Off"):
    """A full screen effect drawn with Graphics.Blit."""
    return """
	SubShader {
		Pass {
			%s
			CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			float4 _MainTex_TexelSize;
			%s
			fixed4 frag(v2f_img i) : SV_Target {
				%s
			}
			ENDCG
		}
	}
""" % (state, uniforms, fragment)


LUMA = "dot(c.rgb, fixed3(0.299, 0.587, 0.114))"
# The game's brightness setting: _Gamma stays near 1 and lower is brighter. It never sets _Exposure.
TONE_MAP = "fixed4 c = tex2D(_MainTex, i.uv);\n\t\t\t\tc.rgb = pow(max(c.rgb, 0.0), _Gamma);\n\t\t\t\treturn c;"


def rim_surface(colour):
    return """
	SubShader {
		Tags { "RenderType"="Opaque" }
		CGPROGRAM
		#pragma surface surf Lambert
		sampler2D _MainTex;
		fixed4 _RimColor;
		float _RimPower;
		%s
		struct Input { float2 uv_MainTex; float3 viewDir; };
		void surf(Input IN, inout SurfaceOutput o) {
			fixed4 c = tex2D(_MainTex, IN.uv_MainTex) %s;
			o.Albedo = c.rgb;
			o.Alpha = c.a;
			float rim = 1.0 - saturate(dot(normalize(IN.viewDir), o.Normal));
			o.Emission = _RimColor.rgb * pow(rim, _RimPower);
		}
		ENDCG
	}
	Fallback "Diffuse"
""" % ("fixed4 _Color;" if colour else "", "* _Color" if colour else "")


def interlace(state):
    return """
	SubShader {
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
		%s
		Cull Off Lighting Off ZWrite Off
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			sampler2D _InterlacePattern;
			sampler2D _Illum;
			float4 _MainTex_ST;
			float4 _InterlacePattern_ST;
			fixed4 _TintColor;
			struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
			struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float2 lines : TEXCOORD1; };
			v2f vert(appdata v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.color = v.color;
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.lines = TRANSFORM_TEX(v.uv, _InterlacePattern) + float2(0.0, frac(_Time.x));
				return o;
			}
			fixed4 frag(v2f i) : SV_Target {
				fixed4 c = tex2D(_MainTex, i.uv) * i.color * _TintColor;
				c.rgb *= 1.0 + tex2D(_Illum, i.uv).a;
				return c * tex2D(_InterlacePattern, i.lines);
			}
			ENDCG
		}
	}
""" % state


def projector(state, colour):
    return """
	SubShader {
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" }
		Pass {
			ZWrite Off
			Offset -1, -1
			%s
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _ShadowTex;
			sampler2D _FalloffTex;
			float4x4 unity_Projector;
			float4x4 unity_ProjectorClip;
			struct v2f { float4 pos : SV_POSITION; float4 uv : TEXCOORD0; float4 fall : TEXCOORD1; };
			v2f vert(float4 vertex : POSITION) {
				v2f o;
				o.pos = UnityObjectToClipPos(vertex);
				o.uv = mul(unity_Projector, vertex);
				o.fall = mul(unity_ProjectorClip, vertex);
				return o;
			}
			fixed4 frag(v2f i) : SV_Target {
				fixed4 tex = tex2Dproj(_ShadowTex, UNITY_PROJ_COORD(i.uv));
				fixed fall = tex2Dproj(_FalloffTex, UNITY_PROJ_COORD(i.fall)).a;
				%s
			}
			ENDCG
		}
	}
""" % (state, colour)


def refraction(outline):
    extra = ""
    uniforms = ""
    scroll = "0.0"
    if outline:
        uniforms = "float _Speed;"
        scroll = "_Time.x * _Speed"
        extra = """
		Pass {
			Cull Front ZWrite Off
			Blend One One
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			float _Outline;
			float _Mult;
			fixed4 _OutlineColor;
			struct v2f { float4 pos : SV_POSITION; float edge : TEXCOORD0; };
			v2f vert(appdata_base v) {
				v2f o;
				o.pos = UnityObjectToClipPos(float4(v.vertex.xyz + v.normal * _Outline, 1.0));
				float3 eye = normalize(ObjSpaceViewDir(v.vertex));
				o.edge = saturate(_Mult * _Outline * (1.0 - abs(dot(eye, v.normal))));
				return o;
			}
			fixed4 frag(v2f i) : SV_Target { return _OutlineColor * i.edge; }
			ENDCG
		}
"""
    return """
	SubShader {
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
		%s
		Pass {
			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			samplerCUBE _Cube;
			sampler2D _BumpMap;
			sampler2D _NoiseTex;
			fixed4 _ReflectColor;
			float _Fresnel;
			float _ETA;
			float _Transfac;
			%s
			struct v2f {
				float4 pos : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 eye : TEXCOORD1;
				float3 normal : TEXCOORD2;
			};
			v2f vert(appdata_base v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.texcoord.xy;
				o.eye = WorldSpaceViewDir(v.vertex);
				o.normal = UnityObjectToWorldNormal(v.normal);
				return o;
			}
			fixed4 frag(v2f i) : SV_Target {
				float2 drift = tex2D(_NoiseTex, i.uv + %s).rg - 0.5;
				float3 bump = UnpackNormal(tex2D(_BumpMap, i.uv + drift * 0.05));
				float3 n = normalize(i.normal + bump * 0.3);
				float3 eye = normalize(i.eye);
				fixed3 through = texCUBE(_Cube, refract(-eye, n, 1.0 / _ETA)).rgb;
				fixed3 mirror = texCUBE(_Cube, reflect(-eye, n)).rgb;
				float edge = saturate(pow(1.0 - saturate(dot(eye, n)), 1.0 / max(_Fresnel, 0.01)));
				fixed4 c;
				c.rgb = lerp(through, mirror * _ReflectColor.rgb, edge);
				c.a = saturate(_Transfac * (edge + 0.15));
				return c;
			}
			ENDCG
		}
	}
""" % (extra, uniforms, scroll)


BODIES = {
    "AMG/Blend-Max": particle("Blend One One\n\t\tBlendOp Max", TINTED),
    "AMG/Particles/Additive": particle("Blend SrcAlpha One", TINTED),
    "AMG/Mobile-Particles-Additive-SeeThrough": particle("Blend SrcAlpha One\n\t\tZTest Always", PLAIN),
    "AMG/Particles - Alpha Blended ZFront": particle("Blend SrcAlpha OneMinusSrcAlpha\n\t\tOffset -8, -8", TINTED),
    "AngryBots/FX/AlphaBlend": particle("Blend SrcAlpha OneMinusSrcAlpha", TINTED),
    "AngryBots/FX/Rain": particle("Blend SrcAlpha One", "return i.color * tex2D(_MainTex, i.uv + float2(0.0, frac(_Time.y)));"),
    "Particles/Additive-layer": soft_particle("Blend SrcAlpha One"),
    "Particles/Additive_Layer": soft_particle("Blend SrcAlpha One"),
    "Particles/Alpha Blended_layer": soft_particle("Blend SrcAlpha OneMinusSrcAlpha"),
    "Self-Illumin/AngryBots/InterlacePatternAdditive": interlace("Blend SrcAlpha One"),
    "Self-Illumin/AngryBots/InterlacePatternBlend": interlace("Blend SrcAlpha OneMinusSrcAlpha"),
    "Projector/Additive": projector("Blend One One", "return tex * fall;"),
    "Projector/Multiply": projector("Blend DstColor Zero", "return lerp(fixed4(1.0, 1.0, 1.0, 1.0), tex, fall * tex.a);"),
    "Custom/FakedRefraction": refraction(False),
    "Custom/FakedRefraction Outline": refraction(True),
    "AMG/Color Correction Thermal Effect": screen(
        "fixed4 c = tex2D(_MainTex, i.uv);\n\t\t\t\treturn tex2D(_ThermalTex, float2(" + LUMA + ", 0.5));", "sampler2D _ThermalTex;"),
    "Hidden/Aubergine/ThermalVisionNoNoise": screen(
        "fixed4 c = tex2D(_MainTex, i.uv);\n\t\t\t\tfixed heat = " + LUMA + ";\n\t\t\t\tfixed4 ramp = tex2D(_ThermalTex, float2(heat, 0.5));\n\t\t\t\treturn ramp + _RadiationColor * c.a * heat;",
        "sampler2D _ThermalTex;\n\t\t\tfixed4 _RadiationColor;"),
    "AMG/ToneMap": screen(TONE_MAP, "float _Gamma;"),
    "Hidden/Aubergine/ToneMap": screen(TONE_MAP, "float _Gamma;"),
    "Hidden/ColorCorrectionCurvesSimple": screen(
        """fixed4 c = tex2D(_MainTex, i.uv);
				fixed3 r = fixed3(tex2D(_RgbTex, float2(c.r, 0.125)).r, tex2D(_RgbTex, float2(c.g, 0.375)).g, tex2D(_RgbTex, float2(c.b, 0.625)).b);
				fixed grey = dot(r, fixed3(0.299, 0.587, 0.114));
				return fixed4(lerp(fixed3(grey, grey, grey), r, _Saturation), c.a);""",
        "sampler2D _RgbTex;\n\t\t\tfloat _Saturation;"),
    "Hidden/ColorCorrectionCurves": screen(
        """fixed4 c = tex2D(_MainTex, i.uv);
				float depth = Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv));
				fixed far = tex2D(_ZCurve, float2(depth, 0.5)).r;
				fixed3 a = fixed3(tex2D(_RgbTex, float2(c.r, 0.125)).r, tex2D(_RgbTex, float2(c.g, 0.375)).g, tex2D(_RgbTex, float2(c.b, 0.625)).b);
				fixed3 b = fixed3(tex2D(_RgbDepthTex, float2(c.r, 0.125)).r, tex2D(_RgbDepthTex, float2(c.g, 0.375)).g, tex2D(_RgbDepthTex, float2(c.b, 0.625)).b);
				fixed3 r = lerp(a, b, far);
				fixed grey = dot(r, fixed3(0.299, 0.587, 0.114));
				return fixed4(lerp(fixed3(grey, grey, grey), r, _Saturation), c.a);""",
        "sampler2D _RgbTex;\n\t\t\tsampler2D _ZCurve;\n\t\t\tsampler2D _RgbDepthTex;\n\t\t\tfloat _Saturation;\n\t\t\tsampler2D_float _CameraDepthTexture;"),
    "Hidden/ColorCorrectionSelective": screen(
        """fixed4 c = tex2D(_MainTex, i.uv);
				fixed amount = saturate(2.0 - length(c.rgb - selColor.rgb) * 4.0);
				return fixed4(lerp(c.rgb, targetColor.rgb, amount), c.a);""",
        "fixed4 selColor;\n\t\t\tfixed4 targetColor;"),
    "Hidden/TextureCopy": screen("return tex2D(_MainTex, i.uv);"),
    "Hidden/Glow Downsample": screen(
        """float2 d = _MainTex_TexelSize.xy;
				fixed4 c = tex2D(_MainTex, i.uv + d * float2(-1.0, -1.0)) + tex2D(_MainTex, i.uv + d * float2(1.0, -1.0));
				c += tex2D(_MainTex, i.uv + d * float2(-1.0, 1.0)) + tex2D(_MainTex, i.uv + d * float2(1.0, 1.0));
				c *= 0.25;
				return fixed4(c.rgb * c.a, c.a) * _Color;""",
        "fixed4 _Color;"),
    "Hidden/GlowCompose": screen("return tex2D(_MainTex, i.uv) * _Color;", "fixed4 _Color;", state="ZTest Always Cull Off ZWrite Off\n\t\t\tBlend One One"),
    "Hidden/GlowConeTap": """
	SubShader {
		Pass {
			ZTest Always Cull Off ZWrite Off
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			fixed4 _Color;
			struct appdata { float4 vertex : POSITION; float2 a : TEXCOORD0; float2 b : TEXCOORD1; float2 c : TEXCOORD2; float2 d : TEXCOORD3; };
			struct v2f { float4 pos : SV_POSITION; float2 a : TEXCOORD0; float2 b : TEXCOORD1; float2 c : TEXCOORD2; float2 d : TEXCOORD3; };
			v2f vert(appdata v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.a = v.a; o.b = v.b; o.c = v.c; o.d = v.d;
				return o;
			}
			fixed4 frag(v2f i) : SV_Target {
				return (tex2D(_MainTex, i.a) + tex2D(_MainTex, i.b) + tex2D(_MainTex, i.c) + tex2D(_MainTex, i.d)) * _Color * 0.25;
			}
			ENDCG
		}
	}
""",
    "AMG/Blend/Blend2TexturesStrumpy": """
	SubShader {
		Tags { "RenderType"="Opaque" }
		CGPROGRAM
		#pragma surface surf Lambert
		sampler2D _MainTex;
		sampler2D _BlackTex;
		struct Input { float2 uv_MainTex; float2 uv_BlackTex; fixed4 color : COLOR; };
		void surf(Input IN, inout SurfaceOutput o) {
			o.Albedo = lerp(tex2D(_BlackTex, IN.uv_BlackTex).rgb, tex2D(_MainTex, IN.uv_MainTex).rgb, IN.color.a);
		}
		ENDCG
	}
	Fallback "Diffuse"
""",
    "AMG/Bumped Specular RimLight": """
	SubShader {
		Tags { "RenderType"="Opaque" }
		CGPROGRAM
		#pragma surface surf MobileSpec
		sampler2D _MainTex;
		sampler2D _BumpMap;
		half _Shininess;
		fixed4 _RimColor;
		float _RimPower;
		struct Input { float2 uv_MainTex; float3 viewDir; };
		inline fixed4 LightingMobileSpec(SurfaceOutput s, fixed3 lightDir, fixed3 halfDir, fixed atten) {
			fixed diff = max(0.0, dot(s.Normal, lightDir));
			fixed spec = pow(max(0.0, dot(s.Normal, halfDir)), s.Specular * 128.0) * s.Gloss;
			fixed4 c;
			c.rgb = (s.Albedo * diff + spec) * _LightColor0.rgb * atten;
			c.a = s.Alpha;
			return c;
		}
		void surf(Input IN, inout SurfaceOutput o) {
			fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
			o.Albedo = c.rgb;
			o.Gloss = c.a;
			o.Alpha = c.a;
			o.Specular = _Shininess;
			o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
			float rim = 1.0 - saturate(dot(normalize(IN.viewDir), o.Normal));
			o.Emission = _RimColor.rgb * pow(rim, _RimPower);
		}
		ENDCG
		Pass {
			Name "OUTLINE"
			Cull Front
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			float _Outline;
			fixed4 _OutlineColor;
			float4 vert(appdata_base v) : SV_POSITION {
				return UnityObjectToClipPos(float4(v.vertex.xyz + v.normal * _Outline, 1.0));
			}
			fixed4 frag() : SV_Target { return _OutlineColor; }
			ENDCG
		}
	}
	Fallback "Mobile/VertexLit"
""",
    "AMG/ReflectiveBackgroundArbitraryGeometry": """
	SubShader {
		Tags { "RenderType"="Opaque" }
		CGPROGRAM
		#pragma surface surf Lambert
		sampler2D _MainTex;
		samplerCUBE _Cube;
		fixed4 _Color;
		float _Reflectiveness;
		struct Input { float2 uv_MainTex; float3 worldRefl; };
		void surf(Input IN, inout SurfaceOutput o) {
			fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
			o.Albedo = c.rgb;
			o.Emission = texCUBE(_Cube, IN.worldRefl).rgb * c.a * _Reflectiveness;
		}
		ENDCG
	}
	Fallback "Diffuse"
""",
    "AMG/ReflectiveTransparentArbitrary": """
	SubShader {
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
		CGPROGRAM
		#pragma surface surf Lambert alpha
		sampler2D _MainTex;
		sampler2D _Normal;
		samplerCUBE _Cube;
		fixed4 _Color;
		float _OneMinusReflectivity;
		struct Input { float2 uv_MainTex; float3 worldRefl; INTERNAL_DATA };
		void surf(Input IN, inout SurfaceOutput o) {
			fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
			o.Normal = UnpackNormal(tex2D(_Normal, IN.uv_MainTex));
			o.Albedo = c.rgb * _OneMinusReflectivity;
			o.Emission = texCUBE(_Cube, WorldReflectionVector(IN, o.Normal)).rgb * (1.0 - _OneMinusReflectivity);
			o.Alpha = c.a;
		}
		ENDCG
	}
	Fallback "Transparent/Diffuse"
""",
    "AngryBots/ReflectiveBackgroundArbitraryGeometry": """
	SubShader {
		Tags { "RenderType"="Opaque" }
		CGPROGRAM
		#pragma surface surf Lambert
		sampler2D _MainTex;
		sampler2D _Normal;
		samplerCUBE _Cube;
		fixed4 _Color;
		float _OneMinusReflectivity;
		struct Input { float2 uv_MainTex; float3 worldRefl; INTERNAL_DATA };
		void surf(Input IN, inout SurfaceOutput o) {
			fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
			o.Normal = UnpackNormal(tex2D(_Normal, IN.uv_MainTex));
			o.Albedo = c.rgb;
			o.Emission = texCUBE(_Cube, WorldReflectionVector(IN, o.Normal)).rgb * c.a * (1.0 - _OneMinusReflectivity);
		}
		ENDCG
	}
	Fallback "Diffuse"
""",
    "AMG/UnlitSimple": """
	SubShader {
		Tags { "RenderType"="Opaque" }
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			float4 _MainTex_ST;
			struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
			v2f vert(appdata_base v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
				return o;
			}
			fixed4 frag(v2f i) : SV_Target { return tex2D(_MainTex, i.uv); }
			ENDCG
		}
	}
""",
    "AngryBots/SimpleSelfIllumination": """
	SubShader {
		Tags { "RenderType"="Opaque" }
		CGPROGRAM
		#pragma surface surf Lambert
		sampler2D _MainTex;
		float _SelfIllumination;
		struct Input { float2 uv_MainTex; };
		void surf(Input IN, inout SurfaceOutput o) {
			fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
			o.Albedo = c.rgb;
			o.Emission = c.rgb * min(_SelfIllumination, 1.0);
		}
		ENDCG
	}
	Fallback "Diffuse"
""",
    "AngryBots/FX/LaserScope": """
	SubShader {
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
		Blend SrcAlpha One
		Cull Off Lighting Off ZWrite Off
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			sampler2D _NoiseTex;
			float4 _MainTex_ST;
			fixed4 _TintColor;
			struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
			struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float2 noise : TEXCOORD1; };
			v2f vert(appdata v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.color = v.color;
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.noise = v.uv + float2(frac(_Time.y * 0.7), 0.0);
				return o;
			}
			fixed4 frag(v2f i) : SV_Target {
				return 2.0 * i.color * _TintColor * tex2D(_MainTex, i.uv) * (0.6 + 0.8 * tex2D(_NoiseTex, i.noise).r);
			}
			ENDCG
		}
	}
""",
    "AngryBots/FX/RainSplash": """
	SubShader {
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
		Blend SrcAlpha One
		Cull Off Lighting Off ZWrite Off
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			float4 _MainTex_ST;
			float _Intensity;
			struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
			struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
			v2f vert(appdata v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.color = v.color;
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				return o;
			}
			fixed4 frag(v2f i) : SV_Target { return i.color * tex2D(_MainTex, i.uv) * _Intensity; }
			ENDCG
		}
	}
""",
    "Aubergine/Objects/NoLights/Electric": """
	SubShader {
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
		Blend SrcAlpha One
		Cull Off Lighting Off ZWrite Off
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			fixed4 _Color;
			sampler2D _NoiseTex;
			float _SampleDist;
			float _Speed;
			float _Noise;
			float _Height;
			float _Glow;
			float _GlowScale;
			float _GlowFallOff;
			float _GlowPower;
			struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
			v2f vert(appdata_base v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.texcoord.xy;
				return o;
			}
			fixed4 frag(v2f i) : SV_Target {
				float t = frac(_Time.y * _Speed * 0.05);
				float wave = tex2D(_NoiseTex, float2(i.uv.x * _SampleDist * 40.0 + t, t * 0.5)).r - 0.5;
				float line_ = 0.5 + wave * _Height * _Noise;
				float d = abs(i.uv.y - line_);
				float core = saturate(1.0 - d / max(_GlowFallOff, 0.001));
				float glow = pow(saturate(1.0 - d * _GlowScale), max(_GlowPower * 0.1, 1.0)) * _Glow;
				return _Color * (core + glow);
			}
			ENDCG
		}
	}
""",
    "GUI/3D Text Shader": """
	SubShader {
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
		Blend SrcAlpha OneMinusSrcAlpha
		Cull Off Lighting Off ZWrite Off
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _Color;
			struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
			struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
			v2f vert(appdata v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.color = v.color * _Color;
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				return o;
			}
			fixed4 frag(v2f i) : SV_Target { return fixed4(i.color.rgb, i.color.a * tex2D(_MainTex, i.uv).a); }
			ENDCG
		}
	}
""",
    "HeatDistort": """
	SubShader {
		Tags { "Queue"="Transparent+10" "IgnoreProjector"="True" "RenderType"="Transparent" }
		GrabPass { "_PCHeatGrab" }
		Pass {
			Cull Off Lighting Off ZWrite Off
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _PCHeatGrab;
			float4 _PCHeatGrab_TexelSize;
			sampler2D _MainTex;
			sampler2D _BumpMap;
			float _BumpAmt;
			struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
			struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 grab : TEXCOORD1; };
			v2f vert(appdata v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.color = v.color;
				o.uv = v.uv;
				o.grab = ComputeGrabScreenPos(o.pos);
				return o;
			}
			fixed4 frag(v2f i) : SV_Target {
				float2 bump = UnpackNormal(tex2D(_BumpMap, i.uv)).xy;
				i.grab.xy += bump * _BumpAmt * _PCHeatGrab_TexelSize.xy * i.grab.z * i.color.a;
				fixed4 c = tex2Dproj(_PCHeatGrab, UNITY_PROJ_COORD(i.grab));
				c.rgb *= tex2D(_MainTex, i.uv).rgb;
				return c;
			}
			ENDCG
		}
	}
""",
    "MADFINGER/Environment/Lightmap + Wind": """
	SubShader {
		Tags { "RenderType"="Opaque" }
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile _ LIGHTMAP_ON
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			float4 _MainTex_ST;
			fixed4 _Color;
			float4 _Wind;
			float _WindEdgeFlutter;
			float _WindEdgeFlutterFreqScale;
			struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
			struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 lm : TEXCOORD1; };
			v2f vert(appdata v) {
				v2f o;
				float phase = dot(v.vertex.xyz, float3(1.0, 1.0, 1.0));
				float sway = sin(_Time.y * _WindEdgeFlutterFreqScale * 4.0 + phase) * _WindEdgeFlutter * 0.1;
				v.vertex.xyz += _Wind.xyz * _Wind.w * v.color.a * (1.0 + sway);
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				o.lm = v.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
				return o;
			}
			fixed4 frag(v2f i) : SV_Target {
				fixed4 c = tex2D(_MainTex, i.uv) * _Color;
				#ifdef LIGHTMAP_ON
				c.rgb *= DecodeLightmap(UNITY_SAMPLE_TEX2D(unity_Lightmap, i.lm));
				#endif
				return c;
			}
			ENDCG
		}
	}
	Fallback "Diffuse"
""",
    "MADFINGER/Transparent/Blinking GodRays": """
	SubShader {
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
		Blend One One
		Cull Off Lighting Off ZWrite Off
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			float4 _MainTex_ST;
			float _FadeOutDistNear;
			float _FadeOutDistFar;
			float _Multiplier;
			float _Bias;
			float _TimeOnDuration;
			float _TimeOffDuration;
			float _BlinkingTimeOffsScale;
			float _SizeGrowStartDist;
			float _SizeGrowEndDist;
			float _MaxGrowSize;
			float _NoiseAmount;
			fixed4 _Color;
			struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
			struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
			v2f vert(appdata v) {
				v2f o;
				float distance_ = length(ObjSpaceViewDir(v.vertex));
				float grow = saturate((distance_ - _SizeGrowStartDist) / max(_SizeGrowEndDist - _SizeGrowStartDist, 0.001));
				v.vertex.xyz += v.normal * grow * _MaxGrowSize * v.color.a;
				float fade = saturate(distance_ / max(_FadeOutDistNear, 0.001)) * saturate(1.0 - distance_ / max(_FadeOutDistFar, 0.001));
				float period = max(_TimeOnDuration + _TimeOffDuration, 0.001);
				float t = frac((_Time.y + v.color.b * _BlinkingTimeOffsScale) / period) * period;
				float blink = smoothstep(0.0, 0.1, t) * (1.0 - smoothstep(_TimeOnDuration, _TimeOnDuration + 0.1, t));
				blink = lerp(blink, 0.75 + 0.25 * sin(_Time.y * 37.0 + v.color.b * 19.0), step(0.001, _NoiseAmount) * _NoiseAmount * 2.0);
				o.pos = UnityObjectToClipPos(v.vertex);
				o.color = saturate(_Color * fade * blink * _Multiplier * 0.5 + _Bias);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				return o;
			}
			fixed4 frag(v2f i) : SV_Target { return tex2D(_MainTex, i.uv) * i.color; }
			ENDCG
		}
	}
""",
    "Transparent Effects/CheapForcefield": """
	SubShader {
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
		Blend SrcAlpha One
		Cull Off Lighting Off ZWrite Off
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			fixed4 _Color;
			float _Inside;
			float _Rim;
			sampler2D _Texture;
			float _Speed;
			float _Tile;
			float _Strength;
			struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float edge : TEXCOORD1; };
			v2f vert(appdata_base v) {
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = v.texcoord.xy * _Tile + float2(0.0, frac(_Time.x * _Speed));
				o.edge = pow(1.0 - abs(dot(normalize(ObjSpaceViewDir(v.vertex)), v.normal)), _Rim * 2.0);
				return o;
			}
			fixed4 frag(v2f i) : SV_Target {
				fixed4 c = _Color * (i.edge + _Inside) * tex2D(_Texture, i.uv) * _Strength;
				return c;
			}
			ENDCG
		}
	}
""",
    "VertexBlend": """
	SubShader {
		Tags { "RenderType"="Opaque" }
		CGPROGRAM
		#pragma surface surf Lambert
		#pragma target 3.0
		sampler2D _MainTex;
		sampler2D _Diffuse2;
		sampler2D _BlendMask;
		sampler2D _Normal1;
		sampler2D _Normal2;
		float _BlendPower;
		struct Input { float2 uv_MainTex; float2 uv_Diffuse2; fixed4 color : COLOR; };
		void surf(Input IN, inout SurfaceOutput o) {
			fixed mask = tex2D(_BlendMask, IN.uv_MainTex).r;
			fixed blend = saturate(pow(saturate(IN.color.r + (mask - 0.5) * IN.color.r), max(_BlendPower, 0.01)));
			o.Albedo = lerp(tex2D(_MainTex, IN.uv_MainTex).rgb, tex2D(_Diffuse2, IN.uv_Diffuse2).rgb, blend);
			o.Normal = normalize(lerp(UnpackNormal(tex2D(_Normal1, IN.uv_MainTex)), UnpackNormal(tex2D(_Normal2, IN.uv_Diffuse2)), blend));
		}
		ENDCG
	}
	Fallback "Diffuse"
""",
    "WetDiffuseReflective": """
	SubShader {
		Tags { "RenderType"="Opaque" }
		CGPROGRAM
		#pragma surface surf Lambert
		sampler2D _DiffuseTexture;
		sampler2D _WetMap;
		samplerCUBE _Reflection;
		float _DiffuseIntensity;
		fixed4 _DiffuseColor;
		float _WetIntensity;
		float _ReflectionIntensity;
		struct Input { float2 uv_DiffuseTexture; float3 worldRefl; };
		void surf(Input IN, inout SurfaceOutput o) {
			fixed4 c = tex2D(_DiffuseTexture, IN.uv_DiffuseTexture);
			fixed wet = tex2D(_WetMap, IN.uv_DiffuseTexture).r * _WetIntensity;
			o.Albedo = c.rgb * _DiffuseColor.rgb * _DiffuseIntensity * (1.0 - wet * 0.5);
			o.Emission = texCUBE(_Reflection, IN.worldRefl).rgb * wet * _ReflectionIntensity;
		}
		ENDCG
	}
	Fallback "Diffuse"
""",
}

# These two share a name in the game, one with a colour property and one without.
RIM = "AMG/DiffuseRimAnimated"


def body_for(name, header):
    if name == RIM:
        return rim_surface("_Color" in header)
    return BODIES.get(name)


def split(text):
    """The header of a placeholder: its name and properties, up to where the working code should start."""
    for mark in ("//DummyShaderTextExporter", "SubShader"):
        at = text.find(mark)
        if at >= 0:
            return text[:at]
    return None


def write_body(path, body):
    with open(path, encoding="utf8", errors="replace") as f:
        text = f.read()
    header = split(text)
    if header is None:
        raise SystemExit("Not a placeholder: " + path)
    with open(path, "w", encoding="utf8", newline="\n") as f:
        f.write(header + body + "}\n")


def name_of(text):
    m = re.search(r'Shader\s+"([^"]+)"', text)
    return m.group(1) if m else None


def builtin_table(folder):
    table = {}
    for root, _, files in os.walk(folder):
        for f in files:
            if f.endswith(".shader"):
                with open(os.path.join(root, f), encoding="utf8", errors="replace") as h:
                    text = h.read()
                name = name_of(text)
                if name:
                    table[name] = text
    return table


# Enhanced lighting: these mobile shaders skip extra lights, and the characters need them.
FORWARD_ADD = ("Mobile/Bumped Specular", "Mobile/Bumped Diffuse", "Mobile/Diffuse")


def main():
    assets, builtin_folder = sys.argv[1], sys.argv[2]
    table = builtin_table(builtin_folder)
    written = copied = 0
    unknown = []
    for root, _, files in os.walk(assets):
        if os.sep + "PCPort" in root or os.sep + "PostProcessing" in root:
            continue
        for f in files:
            if not f.endswith(".shader"):
                continue
            path = os.path.join(root, f)
            with open(path, encoding="utf8", errors="replace") as h:
                text = h.read()
            if "//DummyShaderTextExporter" not in text:
                continue
            name = name_of(text)
            body = body_for(name, text)
            if body:
                write_body(path, body)
                written += 1
                continue
            source = table.get(name) or table.get("Legacy Shaders/" + name) or table.get("Mobile/" + name)
            if source:
                source = source.replace('Shader "%s"' % name_of(source), 'Shader "%s"' % name, 1)
                if name in FORWARD_ADD:
                    source = source.replace(" noforwardadd", "")
                with open(path, "w", encoding="utf8", newline="\n") as h:
                    h.write(source)
                copied += 1
                continue
            unknown.append(name)
    print("%d shaders written, %d from Unity's source" % (written, copied))
    if unknown:
        print("still placeholders: %d" % len(unknown))
        for n in sorted(set(unknown))[:40]:
            print("  ", n)


if __name__ == "__main__":
    main()
