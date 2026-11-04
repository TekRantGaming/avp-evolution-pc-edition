# Development tool: is a fresh AssetRipper export the same as the one the project was built from?
#   compare_export.py <fresh ExportedProject folder> <project git folder> <clean commit>
import collections
import hashlib
import os
import subprocess
import sys

FRESH, PROJ, CLEAN = sys.argv[1], sys.argv[2], sys.argv[3]


def blob(data):
    return hashlib.sha1(b"blob %d\0" % len(data) + data).hexdigest()


tree = {}
for row in subprocess.run(["git", "-C", PROJ, "ls-tree", "-r", CLEAN], capture_output=True, check=True).stdout.decode("utf8", "replace").splitlines():
    meta, path = row.split("\t", 1)
    tree[path.strip('"')] = meta.split()[2]

fresh = {}
for r, ds, fs in os.walk(FRESH):
    ds[:] = [d for d in ds if d not in ("Library", "Temp", ".git", "obj")]
    for f in fs:
        p = os.path.join(r, f)
        fresh[os.path.relpath(p, FRESH).replace("\\", "/")] = p

same = 0
changed = []
for path, p in fresh.items():
    if path not in tree:
        continue
    data = open(p, "rb").read()
    if tree[path] in (blob(data), blob(data.replace(b"\r\n", b"\n"))):
        same += 1
    else:
        changed.append(path)

only_fresh = [p for p in fresh if p not in tree]
only_old = [p for p in tree if p not in fresh]
print("files: fresh", len(fresh), "original", len(tree))
print("identical", same, "changed", len(changed), "only in fresh", len(only_fresh), "only in original", len(only_old))
for name, rows in (("changed", changed), ("only in fresh", only_fresh), ("only in original", only_old)):
    kinds = collections.Counter(os.path.splitext(p)[1] or "(none)" for p in rows)
    print(name, dict(kinds.most_common(8)))
    for p in rows[:8]:
        print("   ", p[:100])
