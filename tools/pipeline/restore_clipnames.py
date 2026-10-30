# Restore animation clip names AssetRipper suffixed ("Idle" -> "Idle_3"), verified against the original
# name counts from the game data. Each renamed clip moves (with its .meta, so its GUID is kept) into its own
# subfolder under its true file name.  usage: restore_clipnames.py <Assets dir> <clips json from clipnames.py>
import collections, glob, json, os, re, shutil, sys

A, CLIPS = sys.argv[1], sys.argv[2]
os.chdir(A)
orig = collections.Counter(c["name"] for c in json.load(open(CLIPS)))
exp = {}
for f in glob.glob("**/*.anim", recursive=True):
    if f.replace("\\", "/").startswith("AnimationClip/PCNames/"):
        continue
    m = re.search(r"\n  m_Name: (.*)\n", open(f, encoding="utf-8").read(4000))
    exp[f] = m.group(1).strip() if m else None
plan = {}
for f, e in exp.items():
    if e in orig:
        plan[f] = e
    else:
        b = re.sub(r"_\d+$", "", e)
        plan[f] = b if b in orig else e
newc = collections.Counter(plan.values())
already = collections.Counter()
for f in glob.glob("AnimationClip/PCNames/**/*.anim", recursive=True):
    already[os.path.splitext(os.path.basename(f))[0]] += 1
total = newc + already
over = {k: total[k] - orig[k] for k in total if total[k] > orig[k]}
under = {k: orig[k] - total[k] for k in orig if orig[k] > total[k]}
if over or under:
    sys.exit("name counts don't match the original; not renaming. over=%s under=%s" % (over, under))
root = "AnimationClip/PCNames"
os.makedirs(root, exist_ok=True)
start = len(os.listdir(root))
n = 0
for i, (f, new) in enumerate(sorted((f, v) for f, v in plan.items() if v != exp[f])):
    d = os.path.join(root, "%03d" % (start + i))
    os.makedirs(d, exist_ok=True)
    dst = os.path.join(d, re.sub(r'[\\/:*?"<>|]', "_", new) + ".anim")
    t = open(f, encoding="utf-8").read()
    t2, k = re.subn(r"(\n  m_Name: )" + re.escape(exp[f]) + r"\n", lambda m: m.group(1) + new + "\n", t, count=1)
    assert k == 1, f
    open(dst, "w", encoding="utf-8", newline="\n").write(t2)
    shutil.move(f + ".meta", dst + ".meta")
    os.remove(f)
    n += 1
print("exported", len(exp), "clips; restored", n, "names; counts match the original")
