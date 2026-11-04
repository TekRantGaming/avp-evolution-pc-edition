# Development tool, not shipped to players: writes patches.json from the difference between the clean export and the
# finished project. The output holds hashes of the game's lines and only the lines the port adds.
#   make_patches.py <project git folder> <clean commit> <output json>
import difflib
import hashlib
import json
import os
import subprocess
import sys

PROJ, CLEAN, OUT = sys.argv[1], sys.argv[2], sys.argv[3]


def git(*a):
    return subprocess.run(["git", "-C", PROJ] + list(a), capture_output=True, check=True).stdout


def text(ref, path):
    data = git("show", ref + ":" + path).decode("utf-8-sig", "replace")
    return data.replace("\r\n", "\n").split("\n")


def line_hash(line):
    return hashlib.sha1(line.strip().encode("utf8")).hexdigest()[:12]


def file_hash(lines):
    return hashlib.sha1("\n".join(l.rstrip() for l in lines).encode("utf8")).hexdigest()


def diff(old, new):
    hunks = []
    for tag, i1, i2, j1, j2 in difflib.SequenceMatcher(None, old, new, autojunk=False).get_opcodes():
        if tag == "equal":
            continue
        if tag == "replace" and i2 - i1 == j2 - j1:
            # The same number of lines: keep only the characters that change, so an edited line of the game's
            # isn't carried whole.
            for offset in range(i2 - i1):
                before, after = old[i1 + offset], new[j1 + offset]
                start = 0
                while start < min(len(before), len(after)) and before[start] == after[start]:
                    start += 1
                end = 0
                while end < min(len(before), len(after)) - start and before[-1 - end] == after[-1 - end]:
                    end += 1
                hunks.append({"at": i1 + offset, "line": line_hash(before), "keep": [start, end], "text": after[start:len(after) - end]})
            continue
        hunks.append({"at": i1, "removed": [line_hash(l) for l in old[i1:i2]], "added": new[j1:j2]})
    return {"sha1": file_hash(old), "result_sha1": file_hash(new), "hunks": hunks}


rows = git("diff", "--name-status", "-M30%", CLEAN, "HEAD", "--", "Assets/Scripts", "Assets/Plugins").decode().splitlines()
files, deleted, moved, added = {}, [], [], []
for row in rows:
    parts = row.split("\t")
    status, path = parts[0], parts[1]
    if path.endswith(".meta") or "/PCPort/" in path or path.endswith((".dll", ".so")):
        continue
    rel = path[len("Assets/"):]
    if status.startswith("R"):
        new_path = parts[2]
        moved.append([rel, new_path[len("Assets/"):]])
        if status != "R100" and new_path.endswith(".cs"):
            files[new_path[len("Assets/"):]] = diff(text(CLEAN, path), text("HEAD", new_path))
    elif status == "D":
        deleted.append(rel)
    elif status == "A":
        added.append(parts[1])
    elif status == "M" and path.endswith(".cs"):
        files[rel] = diff(text(CLEAN, path), text("HEAD", path))

# Detonator's spray helper, a class whose name C# can't express, is written by convert.py's aperture stage from the
# Decompiled export. It keeps the original's .meta, so the prefabs stay bound.
meta_moves = [["Scripts/Assembly-UnityScript/Detonator Spray Helper.cs.meta", "Scripts/Assembly-UnityScript/DetonatorSprayHelper.cs.meta"]]
json.dump({"files": files, "delete": deleted, "move": moved, "move_meta": meta_moves}, open(OUT, "w", encoding="utf8"), indent=0, ensure_ascii=False)
print("patched files", len(files), "deleted", len(deleted), "moved", len(moved))
print("added lines", sum(len(h.get("added", [])) for e in files.values() for h in e["hunks"]))
print("edited lines", sum(1 for e in files.values() for h in e["hunks"] if "line" in h), "longest new text", max(len(h["text"]) for e in files.values() for h in e["hunks"] if "line" in h))
print("new files that aren't ours yet to sort out:")
for a in added:
    print("  ", a)
