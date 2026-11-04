# Generates the two big, simple shader families from their names: the interface shaders (UI quads with clipping) and
# the ShaderWizard set, whose blend mode is spelled out in each name. Runs after shaders.py, and fills in whatever
# placeholders are left. Each keeps its header from the player's export.
#   family_shaders.py <Assets folder>
import os, re, sys

A = sys.argv[1]
MARK = "//DummyShaderTextExporter"

UNLIT = """
	SubShader {
		LOD 100
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
		Pass {
			Cull Off
			Lighting Off
			ZWrite Off
			Fog { Mode Off }
			Offset -1, -1
			%(state)s
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _MainTex;
			float4 _MainTex_ST;
			float2 _ClipSharpness = float2(20.0, 20.0);
			%(decl)s
			struct appdata_t { float4 vertex : POSITION; float2 texcoord : TEXCOORD0; fixed4 color : COLOR; };
			struct v2f { float4 vertex : SV_POSITION; half2 texcoord : TEXCOORD0; fixed4 color : COLOR; float2 worldPos : TEXCOORD1; };
			v2f vert (appdata_t v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.color = v.color;
				o.texcoord = v.texcoord;
				// NGUI 2.x: UIDrawCall encodes the clip rectangle in the material's texture offset/scale.
				o.worldPos = TRANSFORM_TEX(v.vertex.xy, _MainTex);
				return o;
			}
			fixed4 frag (v2f IN) : SV_Target
			{
				%(frag)s
			}
			ENDCG
		}
	}
}
"""

PACKED = """fixed4 glyph = tex2D(_MainTex, IN.texcoord);
				fixed4 selected = step(0.5, IN.color);
				fixed4 col = saturate((IN.color - selected * 0.51) / 0.49);
				col.a *= dot(glyph, selected);
"""
PLAIN = "fixed4 col = tex2D(_MainTex, IN.texcoord) * IN.color;\n"
ALPHA_CLIP = "\t\t\t\tfloat2 factor = abs(IN.worldPos);\n\t\t\t\tclip(1.0 - max(factor.x, factor.y));\n"
HARD_CLIP = "\t\t\t\tfloat2 factor = abs(IN.worldPos);\n\t\t\t\tif (1.0 - max(factor.x, factor.y) < 0.0) col.a = 0.0;\n"
SOFT_CLIP = "\t\t\t\tfloat2 factor = (float2(1.0, 1.0) - abs(IN.worldPos)) * _ClipSharpness;\n\t\t\t\tcol.a *= clamp(min(factor.x, factor.y), 0.0, 1.0);\n"
SOFT_CLIP_PM = "\t\t\t\tfloat2 factor = (float2(1.0, 1.0) - abs(IN.worldPos)) * _ClipSharpness;\n\t\t\t\tcol *= clamp(min(factor.x, factor.y), 0.0, 1.0);\n"
ALPHA = "Blend SrcAlpha OneMinusSrcAlpha"
PREMUL = "Blend One OneMinusSrcAlpha"


def unlit(state, body, decl=""):
    return UNLIT % {"state": state, "frag": body + "\t\t\t\treturn col;", "decl": decl}


NGUI = {
    "Unlit/Transparent Colored (AlphaClip)": unlit(ALPHA, PLAIN + ALPHA_CLIP),
    "Unlit/Transparent Colored (HardClip)": unlit(ALPHA, PLAIN + HARD_CLIP),
    "Unlit/Transparent Colored (SoftClip)": unlit(ALPHA, PLAIN + SOFT_CLIP),
    "Unlit/Transparent Colored (Packed)": unlit(ALPHA, PACKED),
    "Unlit/Transparent Colored (Packed) (AlphaClip)": unlit(ALPHA, PACKED + ALPHA_CLIP),
    "Unlit/Transparent Colored (Packed) (HardClip)": unlit(ALPHA, PACKED + HARD_CLIP),
    "Unlit/Transparent Colored (Packed) (SoftClip)": unlit(ALPHA, PACKED + SOFT_CLIP),
    "Unlit/Premultiplied Colored": unlit(PREMUL, PLAIN),
    "Unlit/Premultiplied Colored (AlphaClip)": unlit(PREMUL, PLAIN + ALPHA_CLIP),
    "Unlit/Premultiplied Colored (SoftClip)": unlit(PREMUL, PLAIN + SOFT_CLIP_PM),
    "Unlit/Masked Colored": unlit(ALPHA, "fixed4 tex = tex2D(_MainTex, IN.texcoord);\n\t\t\t\tfixed4 col = fixed4(lerp(tex.rgb, tex.rgb * _Color.rgb, tex.a), 1.0) * IN.color;\n", "fixed4 _Color;"),
    "ShaderEditor/EditorShaderCache": unlit(ALPHA, PLAIN),
    "Unlit/Transparent Colored": unlit(ALPHA, PLAIN),
    "Unlit/Transparent Colored Overlay": unlit(ALPHA + "\n\t\t\tZTest Always", PLAIN),
    "Unlit/Additive Colored": unlit("Blend SrcAlpha One", PLAIN),
    "Unlit/Depth": unlit("ColorMask 0\n\t\t\tZWrite On", "fixed4 col = fixed4(0.0, 0.0, 0.0, 0.0);\n"),
    "Unlit/Depth Cutout": unlit("ColorMask 0\n\t\t\tZWrite On", "fixed4 col = tex2D(_MainTex, IN.texcoord) * IN.color;\n\t\t\t\tclip(col.a - 0.01);\n"),
}

# ShaderWizard: blend mode is spelled out in the shader name.
MODES = [  # longest names first
    ("DestAlphaColorizeSubtract", "Blend DstAlpha One\n\t\t\tBlendOp RevSub"),
    ("DestAlphaColorizeAdd", "Blend DstAlpha One"),
    ("DestAlphaBlendSubtract", "Blend DstAlpha One\n\t\t\tBlendOp RevSub"),
    ("DestAlphaBlendAdd", "Blend DstAlpha One"),
    ("DestAlphaBlend", "Blend DstAlpha OneMinusDstAlpha"),
    ("SubtractFromDestAlpha", "ColorMask A\n\t\t\tBlend One One\n\t\t\tBlendOp RevSub"),
    ("MultiplyDestAlpha", "ColorMask A\n\t\t\tBlend DstAlpha Zero"),
    ("AddToDestAlpha", "ColorMask A\n\t\t\tBlend One One"),
    ("AlphaBlendSubtract", "Blend SrcAlpha One\n\t\t\tBlendOp RevSub"),
    ("AlphaBlendAdd", "Blend SrcAlpha One"),
    ("AlphaBlend", "Blend SrcAlpha OneMinusSrcAlpha"),
    ("SubtractReverse", "Blend One One\n\t\t\tBlendOp Sub"),
    ("SubtractSoft", "Blend OneMinusDstColor One\n\t\t\tBlendOp RevSub"),
    ("Subtract", "Blend One One\n\t\t\tBlendOp RevSub"),
    ("MultiplyDouble", "Blend DstColor SrcColor"),
    ("Multiply", "Blend DstColor Zero"),
    ("AddSoft", "Blend OneMinusDstColor One"),
    ("Add", "Blend One One"),
    ("Screen", "Blend OneMinusDstColor One"),
    ("Min", "Blend One One\n\t\t\tBlendOp Min"),
    ("Max", "Blend One One\n\t\t\tBlendOp Max"),
    ("Solid", "Blend Off"),
]

WIZARD = """
	SubShader {
		Tags { "Queue"="%(queue)s" "IgnoreProjector"="True" "RenderType"="%(rtype)s" }
		Pass {
			Lighting Off
			%(zwrite)s
			%(blend)s
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			sampler2D _Texture0;
			float4 _Texture0_ST;
			%(decl)s
			struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
			v2f vert (appdata_full v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.texcoord, _Texture0);
				o.uv1 = v.texcoord.xy;
				return o;
			}
			fixed4 frag (v2f i) : SV_Target
			{
				fixed4 col = %(color)s;
				%(test)s
				return col;
			}
			ENDCG
		}
	}
}
"""


def wizard(name, props):
    tail = name.split("/")[-1]
    m = re.match(r"(?:ShaderWizard-Texture(?:\+Color)?|DemoShader-MultiTexture)-\d+-(.+?)(?:-Tile&Offset.*|-AlphaBlend-ZTest.*)?$", tail)
    mode = m.group(1) if m else "AlphaBlend"
    test = mode.endswith("Test")
    mode = mode.replace("+Test", "").replace("Test", "")
    blend = next((b for k, b in MODES if mode.startswith(k)), "Blend SrcAlpha OneMinusSrcAlpha")
    decl, color = "", "tex2D(_Texture0, i.uv)"
    if "_Color" in props:
        decl += "fixed4 _Color;\n"
        color += " * _Color"
    if "_Texture1" in props:
        decl += "\t\t\tsampler2D _Texture1;\n"
        color = "lerp(tex2D(_Texture1, i.uv1), tex2D(_Texture0, i.uv), tex2D(_Texture0, i.uv).a)"
    solid = blend == "Blend Off"
    return WIZARD % {
        "queue": "Geometry" if solid else "Transparent", "rtype": "Opaque" if solid else "Transparent",
        "zwrite": "ZWrite On" if solid else "ZWrite Off", "blend": blend, "decl": decl, "color": color,
        "test": "clip(col.a - 0.5);" if test else "",
    }


n = 0
left = []
for root, _, fs in os.walk(A):
    for f in fs:
        if not f.endswith(".shader"):
            continue
        p = os.path.join(root, f)
        t = open(p, encoding="utf-8-sig").read()
        k = t.find(MARK)
        if k < 0:
            continue
        name = re.search(r'Shader\s+"([^"]+)"', t).group(1)
        header = t[:k].rstrip()
        if name in NGUI:
            body = NGUI[name]
        elif name.startswith("ShaderWizard/") or "(ShaderWizard)" in name:
            body = wizard(name, header)
        else:
            left.append(name)
            continue
        open(p, "w", encoding="utf-8").write(header + "\n" + body.lstrip("\n"))
        n += 1
print("%d interface and blend shaders generated" % n)
if left:
    sys.exit("Shaders still without code: %s" % left)
