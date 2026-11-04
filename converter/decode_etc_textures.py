# Rewrites native .texture2D assets stored in Android-only formats (ETC1/ETC2) as uncompressed RGBA32 with a full
# mip chain, so desktop GPUs can sample them. Keeps every other field, including m_LightmapFormat (RGBM etc.).
#   usage: decode_etc_textures.py <Assets dir>   (run with the venv that has texture2ddecoder + Pillow)
import os, re, sys

import texture2ddecoder as td
from PIL import Image

A = sys.argv[1]
DECODE = {  # Unity TextureFormat -> (decoder, bytes per 4x4 block)
    34: (td.decode_etc1, 8),    # ETC_RGB4
    45: (td.decode_etc2, 8),    # ETC2_RGB
    46: (td.decode_etc2a1, 8),  # ETC2_RGBA1
    47: (td.decode_etc2a8, 16), # ETC2_RGBA8
}


def field(t, name):
    m = re.search(r"\n  %s: (\d+)" % name, t)
    return int(m.group(1)) if m else None


done = 0
for root, _, files in os.walk(A):
    for f in files:
        if not f.endswith(".texture2D"):
            continue
        p = os.path.join(root, f)
        t = open(p, encoding="utf-8").read()
        fmt = field(t, "m_TextureFormat")
        if fmt not in DECODE:
            continue
        w, h = field(t, "m_Width"), field(t, "m_Height")
        m = re.search(r"\n  _typelessdata: ([0-9a-f]*)\n", t)
        raw = bytes.fromhex(m.group(1))
        dec, block = DECODE[fmt]
        top = raw[: ((w + 3) // 4) * ((h + 3) // 4) * block]
        bgra = dec(top, w, h)  # texture2ddecoder returns BGRA
        img = Image.frombytes("RGBA", (w, h), bgra, "raw", "BGRA")
        # Unity stores rows bottom-up and so does the decoded data; keep the order as is.
        data = bytearray()
        mips = 0
        cur = img
        while True:
            data += cur.tobytes()
            mips += 1
            if cur.width == 1 and cur.height == 1:
                break
            cur = cur.resize((max(1, cur.width // 2), max(1, cur.height // 2)), Image.BOX)
        t = re.sub(r"\n  m_TextureFormat: \d+", "\n  m_TextureFormat: 4", t, count=1)
        t = re.sub(r"\n  m_MipCount: \d+", "\n  m_MipCount: %d" % mips, t, count=1)
        t = re.sub(r"\n  m_CompleteImageSize: \d+", "\n  m_CompleteImageSize: %d" % len(data), t, count=1)
        t = re.sub(r"\n  image data: \d+", "\n  image data: %d" % len(data), t, count=1)
        t = t.replace(m.group(0), "\n  _typelessdata: " + data.hex() + "\n", 1)
        open(p, "w", encoding="utf-8", newline="\n").write(t)
        done += 1
        print("decoded", os.path.relpath(p, A), "fmt", fmt, "%dx%d" % (w, h), "mips", mips)
print("done", done)
