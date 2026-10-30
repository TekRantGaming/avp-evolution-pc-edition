# Print pass state + first GLES vertex/fragment program of each pass, compactly.
import re, sys

txt = open(sys.argv[1], encoding="utf-8").read()
full = len(sys.argv) > 2 and sys.argv[2] == "full"
# Split into passes by 'Pass {' tokens at shader level.
parts = re.split(r'\n\s*Pass \{', txt)
print(parts[0][:1500].split("SubShader")[0])
for pi, p in enumerate(parts[1:]):
    state = []
    for ln in p.split("\n"):
        s = ln.strip()
        if s.startswith(("Program", "SubProgram", "\"!!", "Keywords")):
            break
        if s:
            state.append(s)
    print(f"--- PASS {pi}: " + " | ".join(state)[:600])
    m = re.search(r'SubProgram "gles "[^"]*"!!GLES(.*?)"\s*\}', p, re.S)
    if not m:
        print("   (no gles program)"); continue
    code = m.group(1)
    v = re.search(r'#ifdef VERTEX(.*?)#endif', code, re.S)
    f = re.search(r'#ifdef FRAGMENT(.*?)#endif', code, re.S)
    if full and v:
        print("VERTEX:" + v.group(1))
    if f:
        print("FRAGMENT:" + f.group(1))
    else:
        print(code[:3000])
