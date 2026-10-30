import json, os, sys

S = sys.argv[1]
src = os.path.join(S, "yaml_export", "ExportedProject", "Assets", "Shader")
dst = os.path.join(S, "glsl")
os.makedirs(dst, exist_ok=True)


def yaml_dq(s, i):
    # Parse a YAML double-quoted scalar starting at s[i] == '"'.
    out, i = [], i + 1
    while True:
        c = s[i]
        if c == '"':
            return "".join(out)
        if c == "\\":
            n = s[i + 1]
            if n == "\n":
                i += 2
                while s[i] in " \t":
                    i += 1
                continue
            esc = {"n": "\n", "t": "\t", '"': '"', "\\": "\\", "/": "/", "r": "\r", "0": "\0", " ": " "}
            if n in esc:
                out.append(esc[n]); i += 2; continue
            if n == "x":
                out.append(chr(int(s[i + 2:i + 4], 16))); i += 4; continue
            if n == "u":
                out.append(chr(int(s[i + 2:i + 6], 16))); i += 6; continue
            out.append(n); i += 2; continue
        if c == "\n":
            # line folding: newline + indentation -> single space, blank lines -> \n
            j = i + 1
            blanks = 0
            while True:
                while s[j] in " \t":
                    j += 1
                if s[j] == "\n":
                    blanks += 1; j += 1; continue
                break
            out.append("\n" * blanks if blanks else " ")
            i = j
            continue
        out.append(c); i += 1


n = 0
for f in sorted(os.listdir(src)):
    if not f.endswith(".asset"):
        continue
    t = open(os.path.join(src, f), encoding="utf-8").read()
    k = t.find("m_Script: \"")
    if k < 0:
        print("no script", f); continue
    txt = yaml_dq(t, k + len("m_Script: "))
    open(os.path.join(dst, f[:-6] + ".txt"), "w", encoding="utf-8").write(txt)
    n += 1
print("extracted", n)
