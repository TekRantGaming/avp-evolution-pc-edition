# Restore animation clip names AssetRipper suffixed ("Idle" -> "Idle_3"): the game finds clips by name. Until Unity
# imports a clip the name inside it is still the true one, and the import overwrites it with the file's, so this runs
# before Unity opens the project. Each renamed clip moves (with its .meta, so its GUID is kept) into its own subfolder
# under its true file name.  usage: restore_clipnames.py <Assets dir>
import glob, os, re, shutil, sys

A = sys.argv[1]
os.chdir(A)
root = "AnimationClip/PCNames"
plan = {}
already = 0
total = 0
for f in glob.glob("**/*.anim", recursive=True):
    if f.replace("\\", "/").startswith(root + "/"):
        already += 1
        continue
    total += 1
    stem = os.path.splitext(os.path.basename(f))[0]
    with open(f, encoding="utf-8") as h:
        m = re.search(r"\n  m_Name: ([^\n]*)\n", h.read(4000))
    if not m or m.group(1).strip() == stem:
        continue
    name = m.group(1).strip()
    # AssetRipper only ever adds a number. Anything else means Unity has already renamed the clips.
    if not re.fullmatch(re.escape(name) + r"_\d+", stem):
        sys.exit("%s holds a clip named %r: Unity seems to have imported this export already; export it again." % (f, name))
    plan[f] = name
os.makedirs(root, exist_ok=True)
start = len(os.listdir(root))
for i, (f, new) in enumerate(sorted(plan.items())):
    d = os.path.join(root, "%03d" % (start + i))
    os.makedirs(d, exist_ok=True)
    dst = os.path.join(d, re.sub(r'[\\/:*?"<>|]', "_", new) + ".anim")
    shutil.move(f, dst)
    shutil.move(f + ".meta", dst + ".meta")
print("exported", total + already, "clips; restored", len(plan), "names")
if not plan and not already:
    sys.exit("No clip names were restored: Unity seems to have imported this export already; export it again.")
