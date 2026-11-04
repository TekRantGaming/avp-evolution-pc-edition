# AVP Evolution: PC Edition's converter: builds the game on the player's PC from their own .apk and .obb.
# It runs as stages, each of which can be repeated, and prints one "STAGE <name>" or "PROGRESS <text>" line at a time
# so the launcher can show them. Every step finds things by name: AssetRipper gives files new IDs on each export.
#   convert.py --apk <file> --obb <file> --work <folder> --out <game folder> [--tools <folder>] [--stages a,b]
import argparse
import collections
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.parse
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
PACK = os.path.join(HERE, "pack")
AR_PORT = 50123
AR_URL = "http://127.0.0.1:%d" % AR_PORT
NO_WINDOW = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0


def say(kind, text):
    print("%s %s" % (kind, text), flush=True)


def project(args):
    return os.path.join(args.work, "export", "ExportedProject")


def assets(args):
    return os.path.join(project(args), "Assets")


def merge(args):
    """AssetRipper only recognises the game when the .apk and .obb are unpacked into one folder."""
    out = os.path.join(args.work, "merged")
    if os.path.exists(os.path.join(out, "assets", "bin", "Data", "globalgamemanagers")):
        say("PROGRESS", "already unpacked")
        return
    os.makedirs(out, exist_ok=True)
    for path in (args.apk, args.obb):
        with zipfile.ZipFile(path) as z:
            z.extractall(out)
        say("PROGRESS", "unpacked " + os.path.basename(path))


def ar_get(path):
    return urllib.request.urlopen(AR_URL + path, timeout=60).read().decode("utf8", "replace")


def ar_post(path, fields, timeout=120):
    data = urllib.parse.urlencode(fields).encode()
    return urllib.request.urlopen(urllib.request.Request(AR_URL + path, data), timeout=timeout)


def ar_settings(changes):
    """The settings page is a form: read what it holds, change ours and send it all back."""
    page = ar_get("/Settings/Edit")
    form = {}
    for m in re.finditer(r'<select[^>]*name="(\w+)"(.*?)</select>', page, re.S):
        chosen = re.search(r'<option value="([^"]*)" selected', m.group(2))
        if chosen:
            form[m.group(1)] = chosen.group(1)
    for tag in re.findall(r"<input[^>]*>", page):
        name = re.search(r'name="(\w+)"', tag)
        if not name:
            continue
        if 'type="checkbox"' in tag:
            if "checked" in tag:
                form[name.group(1)] = "true"
        else:
            value = re.search(r'value="([^"]*)"', tag)
            form[name.group(1)] = value.group(1) if value else ""
    form.update(changes)
    ar_post("/Settings/Update", form)


def ripper(args, mode, out):
    """Runs AssetRipper with no window and exports the Unity project."""
    if os.path.exists(os.path.join(out, "ExportedProject", "Assets")):
        say("PROGRESS", "already exported: " + mode)
        return
    exe = os.path.join(args.tools, "AssetRipper", "AssetRipper.GUI.Free.exe")
    log = os.path.join(args.work, "assetripper_%s.log" % mode)
    server = subprocess.Popen([exe, "--headless", "--port", str(AR_PORT), "--log-path", log], cwd=os.path.dirname(exe),
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, creationflags=NO_WINDOW)
    try:
        for _ in range(120):
            try:
                ar_get("/")
                break
            except OSError:
                time.sleep(1)
        ar_settings({"ScriptExportMode": mode, "ShaderExportMode": "Decompile"})
        say("PROGRESS", "loading the game")
        ar_post("/LoadFolder", {"Path": os.path.join(args.work, "merged")}, 3600)
        say("PROGRESS", "exporting (%s)" % mode)
        ar_post("/Export/UnityProject", {"Path": out}, 7200)
    finally:
        server.kill()
        server.wait()
    if not os.path.exists(os.path.join(out, "ExportedProject", "Assets")):
        raise SystemExit("The export failed: see " + log)


def export(args):
    ripper(args, "Hybrid", os.path.join(args.work, "export"))


def aperture(args):
    """Unity 5.6 refuses to reference Aperture.dll, so the cutscene library comes from a decompiled export of the
    player's own copy, and every asset that uses the DLL is pointed at the source instead."""
    A = assets(args)
    dll_meta = os.path.join(A, "Plugins", "Aperture.dll.meta")
    if not os.path.exists(dll_meta):
        say("PROGRESS", "already done")
        return
    other = os.path.join(args.work, "export_decompiled")
    ripper(args, "Decompiled", other)
    B = os.path.join(other, "ExportedProject", "Assets")
    dll = re.search(r"guid: ([0-9a-f]+)", open(dll_meta, encoding="utf8").read()).group(1)
    ref = re.compile(r"m_Script: \{fileID: (-?\d+), guid: ([0-9a-f]+), type: 3\}")

    # The script's name is the same in both exports; its ID isn't.
    def script_at(root, guid_to_name, text):
        return [(fid, guid_to_name.get(g, g)) for fid, g in ref.findall(text)]

    names = {}
    for f in glob.glob(os.path.join(B, "Scripts", "Aperture", "**", "*.cs.meta"), recursive=True):
        names[re.search(r"guid: ([0-9a-f]+)", open(f, encoding="utf8").read()).group(1)] = f

    targets = []
    mapping = collections.defaultdict(collections.Counter)
    for pattern in ("*.unity", "*.prefab", "*.asset"):
        for f in glob.glob(os.path.join(A, "**", pattern), recursive=True):
            with open(f, encoding="utf8", errors="replace") as h:
                cur = h.read()
            if dll not in cur:
                continue
            targets.append((f, cur))
            twin = os.path.join(B, os.path.relpath(f, A))
            with open(twin, encoding="utf8", errors="replace") as h:
                old = h.read()
            a, b = ref.findall(cur), ref.findall(old)
            if len(a) != len(b):
                raise SystemExit("The two exports don't line up: " + f)
            for (fid, g), (_, og) in zip(a, b):
                if g == dll:
                    mapping[fid][og] += 1
    final = {fid: c.most_common(1)[0][0] for fid, c in mapping.items()}
    missing = [g for g in final.values() if g not in names]
    if missing:
        raise SystemExit("Cutscene scripts not found: %s" % missing[:4])
    for f in (os.path.join(B, "Scripts", "Aperture"), ):
        shutil.copytree(f, os.path.join(A, "Scripts", "Aperture"), dirs_exist_ok=True)

    def repoint(m):
        fid, g = m.group(1), m.group(2)
        if g == dll and fid in final:
            return "m_Script: {fileID: 11500000, guid: %s, type: 3}" % final[fid]
        return m.group(0)

    for f, cur in targets:
        with open(f, "w", encoding="utf8", newline="\n") as h:
            h.write(ref.sub(repoint, cur))
    # The same export holds Detonator's spray helper, a UnityScript class whose name C# can't spell. It's saved under a
    # name C# can, and keeps the ID the prefabs use: patches.json moves its .meta.
    with open(os.path.join(B, "Scripts", "Assembly-UnityScript", "Detonator-Spray-Helper.cs"), encoding="utf8") as h:
        helper = h.read().replace("Detonator_0020Spray_0020Helper", "DetonatorSprayHelper")
    with open(os.path.join(A, "Scripts", "Assembly-UnityScript", "DetonatorSprayHelper.cs"), "w", encoding="utf8", newline="\n") as h:
        h.write(helper)
    os.remove(os.path.join(A, "Plugins", "Aperture.dll"))
    os.remove(dll_meta)
    say("PROGRESS", "%d cutscene classes, %d assets" % (len(final), len(targets)))


def patch(args):
    subprocess.run([sys.executable, os.path.join(HERE, "apply_patches.py"), assets(args), os.path.join(HERE, "patches.json")], check=True)


def rewired(args):
    """The gamepad layout in the game's input settings, for pads Rewired reads itself (PCPad reads XInput pads): B and
    Y swap so execution is on B and ranged on Y, and the guide button no longer pauses. Found by value, not by line."""
    p = os.path.join(assets(args), "Resources", "Rewired Input Manager.prefab")
    with open(p, encoding="utf8") as h:
        lines = h.read().split("\n")
    start = lines.index("    joystickMaps:")
    # The gameplay map of Rewired's gamepad template: categoryId 2.
    first = next(i for i in range(start, len(lines)) if lines[i] == "      categoryId: 2" and lines[i - 1].startswith("    - id:"))
    last = next(i for i in range(first, len(lines)) if lines[i].startswith("    - id:") or re.match(r"    \w", lines[i]))
    entry = re.compile(r"      - _actionCategoryId: (\d+)$")
    swaps = {("1", "8", "5"): "8", ("1", "19", "8"): "5"}
    done = {("1", "8", "8"), ("1", "19", "5")}
    out, seen, i = lines[:first], set(), first
    while i < last:
        m = entry.match(lines[i])
        if not m:
            out.append(lines[i])
            i += 1
            continue
        j = i + 1
        while j < last and lines[j].startswith("        "):
            j += 1
        block = lines[i:j]
        fields = dict(l.strip().lstrip("- ").split(": ", 1) for l in block)
        key = (fields["_actionCategoryId"], fields["_actionId"], fields["_elementIdentifierId"])
        if key == ("1", "12", "16"):
            seen.add("guide")
        else:
            if key in swaps:
                block = [re.sub(r"(_elementIdentifierId: )\d+", r"\g<1>" + swaps[key], l) for l in block]
                seen.add(key[1])
            elif key in done:
                seen.add(key[1] + " already")
            out += block
        i = j
    out += lines[last:]
    if seen <= {"8 already", "19 already"} and len(seen) == 2:
        say("PROGRESS", "already done")
        return
    if seen != {"guide", "8", "19"}:
        raise SystemExit("The game's input settings aren't the ones this was made for: %s" % sorted(seen))
    with open(p, "w", encoding="utf8", newline="\n") as h:
        h.write("\n".join(out))
    say("PROGRESS", "gamepad layout changed")


def cleanup(args):
    """Things an export brings that a Unity project can't hold: each assembly's own AssemblyInfo clashes once they
    compile together, and two runtime DLLs aren't part of the game."""
    A = assets(args)
    doomed = glob.glob(os.path.join(A, "**", "Properties", "AssemblyInfo.cs"), recursive=True)
    doomed += [os.path.join(A, "Plugins", n) for n in ("System.Runtime.CompilerServices.Unsafe.dll", "System.Runtime.InteropServices.dll")]
    for p in doomed:
        for f in (p, p + ".meta"):
            if os.path.exists(f):
                os.remove(f)
    say("PROGRESS", "%d files removed" % len(doomed))


def plugins(args):
    """AssetRipper turns the Editor platform off on plugin DLLs, and the build needs to see them."""
    folder = os.path.join(assets(args), "Plugins")
    for f in glob.glob(os.path.join(folder, "*.dll.meta")):
        with open(f, encoding="utf8") as h:
            t = h.read()
        new = re.sub(r"(\n    Editor:\n      enabled: )0", r"\g<1>1", t)
        if new != t:
            with open(f, "w", encoding="utf8", newline="\n") as h:
                h.write(new)


def textures(args):
    """Textures forced to Android formats can't be sampled on a PC: back to Automatic, from the full-quality images.
    Native texture assets in those formats are decoded."""
    android = {"34", "36", "45", "46", "47", "13"}
    changed = 0
    for root, _, files in os.walk(assets(args)):
        for name in files:
            if not name.endswith(".meta") or name.endswith(".cs.meta"):
                continue
            p = os.path.join(root, name)
            with open(p, encoding="utf8", errors="replace") as h:
                t = h.read()
            if "TextureImporter:" not in t:
                continue
            new = re.sub(r"(\n\s+textureFormat: )(-?\d+)", lambda m: m.group(1) + ("-1" if m.group(2) in android else m.group(2)), t)
            if new != t:
                with open(p, "w", encoding="utf8", newline="\n") as h:
                    h.write(new)
                changed += 1
    say("PROGRESS", "%d texture formats" % changed)
    subprocess.run([sys.executable, os.path.join(HERE, "decode_etc_textures.py"), assets(args)], check=True)


def shaders(args):
    """AssetRipper can only export placeholders for the game's compiled shaders. shaders.py and family_shaders.py give
    each one working code, keeping its header from this export; Unity's own come from its public shader source."""
    subprocess.run([sys.executable, os.path.join(HERE, "shaders.py"), assets(args), os.path.join(args.tools, "builtin_shaders")], check=True)
    subprocess.run([sys.executable, os.path.join(HERE, "family_shaders.py"), assets(args)], check=True)


def clips(args):
    """AssetRipper suffixes clips that share a name ("Idle_3"), and the game finds clips by name."""
    subprocess.run([sys.executable, os.path.join(HERE, "restore_clipnames.py"), assets(args)], check=True)


def port(args):
    shutil.copytree(os.path.join(PACK, "port", "Assets"), assets(args), dirs_exist_ok=True)


def unity(args):
    """Upgrades the project, un-batches the levels and builds the game, with no window."""
    exe = os.path.join(args.tools, "Unity567", "Editor", "Unity.exe")
    log = os.path.join(args.work, "unity.log")
    # Un-batching only works on freshly exported scenes, so a repeat of this stage only builds.
    done = os.path.join(args.work, "unbatched.txt")
    method = "PCPortBuild.BuildWindows64" if os.path.exists(done) else "PCPortAll.UnbatchAndBuild"
    cmd = [exe, "-batchmode", "-quit", "-accept-apiupdate", "-projectPath", project(args), "-buildTarget", "win64",
           "-executeMethod", method, "-pcPortOut", args.out, "-pcPortExt", os.path.join(args.tools, "ext"), "-logFile", log]
    code = subprocess.run(cmd, creationflags=NO_WINDOW).returncode
    if code != 0:
        raise SystemExit("Unity stopped with code %d: see %s" % (code, log))
    open(done, "w").write("done")


STAGES = collections.OrderedDict([
    ("merge", merge), ("export", export), ("aperture", aperture), ("patch", patch), ("rewired", rewired), ("cleanup", cleanup), ("plugins", plugins),
    ("shaders", shaders), ("clips", clips), ("textures", textures), ("port", port), ("unity", unity),
])


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--apk", required=True)
    p.add_argument("--obb", required=True)
    p.add_argument("--work", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--tools", default=os.path.join(HERE, "tools"))
    p.add_argument("--stages", default=",".join(STAGES))
    args = p.parse_args()
    os.makedirs(args.work, exist_ok=True)
    for name in args.stages.split(","):
        started = time.time()
        say("STAGE", name)
        STAGES[name](args)
        say("PROGRESS", "%s took %d seconds" % (name, time.time() - started))
    say("STAGE", "done")


if __name__ == "__main__":
    main()
