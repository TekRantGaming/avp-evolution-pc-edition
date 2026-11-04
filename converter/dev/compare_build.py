# Development tool: how close is the converter's build to the hand-made one?
#   compare_build.py <converter build> <hand-made build>
import hashlib
import os
import sys

NEW, OLD = sys.argv[1], sys.argv[2]


def walk(root):
    out = {}
    for r, ds, fs in os.walk(root):
        for f in fs:
            p = os.path.join(r, f)
            out[os.path.relpath(p, root).replace("\\", "/")] = p
    return out


def sha(p):
    h = hashlib.sha1()
    with open(p, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


a, b = walk(NEW), walk(OLD)
skip = (".png", "output_log.txt", "pc_console.txt", "pc_console_out.txt", "avp_evolution.toml")
a = {k: v for k, v in a.items() if not k.endswith(skip)}
b = {k: v for k, v in b.items() if not k.endswith(skip)}
print("files new", len(a), "old", len(b), "bytes new", sum(os.path.getsize(p) for p in a.values()), "old", sum(os.path.getsize(p) for p in b.values()))
print("only in new", sorted(set(a) - set(b))[:12])
print("only in old", sorted(set(b) - set(a))[:12])
same = different = 0
rows = []
for k in sorted(set(a) & set(b)):
    if os.path.getsize(a[k]) == os.path.getsize(b[k]) and sha(a[k]) == sha(b[k]):
        same += 1
    else:
        different += 1
        rows.append((abs(os.path.getsize(a[k]) - os.path.getsize(b[k])), k, os.path.getsize(a[k]), os.path.getsize(b[k])))
print("identical", same, "different", different)
rows.sort(reverse=True)
for d, k, x, y in rows[:22]:
    print("%10d  %-52s new %d old %d" % (d, k[:52], x, y))
print("same size, different bytes:", sum(1 for r in rows if r[0] == 0), "within 1%:", sum(1 for r in rows if r[0] <= r[3] * 0.01))
