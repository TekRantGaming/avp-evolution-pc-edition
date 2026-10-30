# List every AnimationClip in the APK's Unity 4 data with a content signature,
# so AssetRipper's renamed copies ("Idle_3") can be matched back to their real names ("Idle").
import collections, json, os, sys
import UnityPy

data_dir, out = sys.argv[1], sys.argv[2]
env = UnityPy.load(data_dir)
clips = []
for obj in env.objects:
    if obj.type.name != "AnimationClip":
        continue
    try:
        tree = obj.read_typetree()
    except Exception as e:
        print("skip", obj.path_id, e)
        continue
    name = tree.get("m_Name", "")
    curves = []
    for key in ("m_RotationCurves", "m_PositionCurves", "m_ScaleCurves", "m_FloatCurves", "m_CompressedRotationCurves"):
        for c in tree.get(key, []) or []:
            curves.append(c.get("path", c.get("m_Path", "")))
    events = len(tree.get("m_Events", []) or [])
    sig = {"rate": round(float(tree.get("m_SampleRate", 0)), 3), "wrap": tree.get("m_WrapMode", 0),
           "ncurves": len(curves), "paths": sorted(set(curves))[:6], "events": events}
    clips.append({"name": name, "path_id": obj.path_id, "sig": sig})
json.dump(clips, open(out, "w"), indent=0)
cnt = collections.Counter(c["name"] for c in clips)
print(len(clips), "clips;", sum(1 for n, k in cnt.items() if k > 1), "names used more than once")
print([(n, k) for n, k in cnt.most_common(8)])
