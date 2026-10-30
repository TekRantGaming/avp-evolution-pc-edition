# Replace Plugins/Aperture.dll (Unity 5.6 silently refuses to reference it) with the decompiled Aperture source
# from the Decompiled-mode baseline commit, and re-point every asset reference from the DLL to the source files.
# The DLL->source mapping comes from comparing the same m_Script lines in the two exports.
#   usage: aperture_to_source.py <project dir> <decompiled baseline commit>
import collections, os, re, subprocess, sys

PROJ, BASE = sys.argv[1], sys.argv[2]
DLL_GUID = None
os.chdir(PROJ)
meta = open("Assets/Plugins/Aperture.dll.meta", encoding="utf-8").read()
DLL_GUID = re.search(r"guid: ([0-9a-f]+)", meta).group(1)


def git(*a):
    return subprocess.run(["git"] + list(a), capture_output=True, text=True, encoding="utf-8", errors="replace").stdout


ref = re.compile(r"m_Script: \{fileID: (-?\d+), guid: ([0-9a-f]+), type: 3\}")
files = [f for f in git("ls-files").split("\n") if f.endswith((".unity", ".prefab", ".asset"))]
mapping = collections.defaultdict(collections.Counter)
targets = []
for f in files:
    if not os.path.exists(f):
        continue
    cur = open(f, encoding="utf-8", errors="replace").read()
    if DLL_GUID not in cur:
        continue
    targets.append(f)
    old = git("show", "%s:%s" % (BASE, f))
    a, b = ref.findall(cur), ref.findall(old)
    if len(a) != len(b):
        print("skip mapping (ref count differs):", f)
        continue
    for (fid, g), (ofid, og) in zip(a, b):
        if g == DLL_GUID:
            mapping[fid][og] += 1
final = {}
for fid, c in mapping.items():
    og, n = c.most_common(1)[0]
    if len(c) > 1:
        print("ambiguous mapping for", fid, dict(c))
    final[fid] = og
print(len(final), "Aperture classes referenced from", len(targets), "assets")

# restore the decompiled source (with its .metas) from the baseline
paths = [p for p in git("ls-tree", "-r", "--name-only", BASE, "Assets/Scripts/Aperture").split("\n") if p]
for p in paths + ["Assets/Scripts/Aperture.meta"]:
    data = subprocess.run(["git", "show", "%s:%s" % (BASE, p)], capture_output=True).stdout
    os.makedirs(os.path.dirname(p), exist_ok=True)
    open(p, "wb").write(data)
print("restored", len(paths), "Aperture source files")
have = set()
for p in paths:
    if p.endswith(".cs.meta"):
        have.add(re.search(rb"guid: ([0-9a-f]+)", open(p, "rb").read()).group(1).decode())
missing = [g for g in final.values() if g not in have]
if missing:
    sys.exit("mapped GUIDs not found in restored source: %s" % missing)

n = 0
for f in targets:
    t = open(f, encoding="utf-8", errors="replace").read()

    def sub(m):
        fid, g = m.group(1), m.group(2)
        if g == DLL_GUID and fid in final:
            return "m_Script: {fileID: 11500000, guid: %s, type: 3}" % final[fid]
        return m.group(0)

    t2, k = ref.subn(sub, t)
    if t2 != t:
        open(f, "w", encoding="utf-8", newline="\n").write(t2)
        n += 1
left = sum(open(f, encoding="utf-8", errors="replace").read().count(DLL_GUID) for f in targets)
os.remove("Assets/Plugins/Aperture.dll")
os.remove("Assets/Plugins/Aperture.dll.meta")
print("re-pointed references in", n, "assets; remaining DLL references:", left)
