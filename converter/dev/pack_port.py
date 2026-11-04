# Development tool: gathers what the converter adds to a fresh export into converter/pack/port: the port's own
# Assets, its DLSS/XeSS plugin and the Post-processing Stack (Unity's, MIT licensed). Nothing in it comes from the game.
#   pack_port.py <Assets folder>
# The folder is the local project's Assets, or the repository's unity/Assets once Assets/PostProcessing and
# Assets/Plugins/x86_64/pc_upscalers.dll have been added to it (see BUILDING.md).
import os
import shutil
import sys

A = sys.argv[1]
HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PORT = os.path.join(HERE, "pack", "port", "Assets")

shutil.rmtree(PORT, ignore_errors=True)
os.makedirs(os.path.join(PORT, "Plugins", "x86_64"))
os.makedirs(os.path.join(PORT, "Editor"))
for sub in ("PCPort", "Plugins/PCPort", "PostProcessing"):
    if not os.path.isdir(os.path.join(A, sub)):
        sys.exit("Missing %s: see BUILDING.md" % os.path.join(A, sub))
    shutil.copytree(os.path.join(A, sub), os.path.join(PORT, sub))
    if os.path.exists(os.path.join(A, sub + ".meta")):
        shutil.copy(os.path.join(A, sub + ".meta"), os.path.join(PORT, sub + ".meta"))
for f in os.listdir(os.path.join(A, "Editor")):
    if f.startswith("PCPort") and not f.startswith("PCPortLightmaps") and not f.startswith("PCPortNavMesh"):
        shutil.copy(os.path.join(A, "Editor", f), os.path.join(PORT, "Editor", f))
dll = os.path.join(A, "Plugins", "x86_64", "pc_upscalers.dll")
if not os.path.exists(dll):
    sys.exit("Missing %s: build native/pc_upscalers first" % dll)
for f in (dll, dll + ".meta"):
    if os.path.exists(f):
        shutil.copy(f, os.path.join(PORT, "Plugins", "x86_64"))
print("packed", sum(len(fs) for _, _, fs in os.walk(PORT)), "files")
