# Applies patches.json to a fresh export of the game. The file holds no text from the game's scripts: it finds its
# places by the hash of the lines around them, and carries only the port's own new lines. It refuses to touch a file
# that isn't the one it expects, so a different copy of the game fails loudly instead of building something broken.
#   apply_patches.py <Assets folder> <patches.json>
import hashlib
import json
import os
import sys


def line_hash(line):
    return hashlib.sha1(line.strip().encode("utf8")).hexdigest()[:12]


def file_hash(lines):
    return hashlib.sha1("\n".join(l.rstrip() for l in lines).encode("utf8")).hexdigest()


def apply_file(path, entry):
    with open(path, encoding="utf-8-sig", newline="") as f:
        text = f.read()
    newline = "\r\n" if "\r\n" in text else "\n"
    lines = text.replace("\r\n", "\n").split("\n")
    if file_hash(lines) != entry["sha1"]:
        raise SystemExit("Not the expected file: " + path)
    # From the end of the file, so earlier positions stay right.
    for hunk in reversed(entry["hunks"]):
        at = hunk["at"]
        if "line" in hunk:
            if line_hash(lines[at]) != hunk["line"]:
                raise SystemExit("A patch doesn't fit: %s line %d" % (path, at + 1))
            start, end = hunk["keep"]
            tail = lines[at][len(lines[at]) - end:] if end else ""
            lines[at] = lines[at][:start] + hunk["text"] + tail
            continue
        for offset, expected in enumerate(hunk["removed"]):
            if line_hash(lines[at + offset]) != expected:
                raise SystemExit("A patch doesn't fit: %s line %d" % (path, at + offset + 1))
        lines[at:at + len(hunk["removed"])] = hunk["added"]
    if file_hash(lines) != entry["result_sha1"]:
        raise SystemExit("A patch gave the wrong result: " + path)
    with open(path, "w", encoding="utf8", newline="") as f:
        f.write(newline.join(lines))


def main():
    assets, patches = sys.argv[1], sys.argv[2]
    data = json.load(open(patches, encoding="utf8"))
    for old, new in data.get("move_meta", []):
        src = os.path.join(assets, old)
        if os.path.exists(src):
            os.replace(src, os.path.join(assets, new))
    for rel in data.get("delete", []):
        for p in (os.path.join(assets, rel), os.path.join(assets, rel + ".meta")):
            if os.path.exists(p):
                os.remove(p)
    for old, new in data.get("move", []):
        for suffix in ("", ".meta"):
            src, dst = os.path.join(assets, old + suffix), os.path.join(assets, new + suffix)
            if os.path.exists(src):
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                os.replace(src, dst)
    for rel, entry in data["files"].items():
        apply_file(os.path.join(assets, rel), entry)
    print("patched %d files" % len(data["files"]))


if __name__ == "__main__":
    main()
