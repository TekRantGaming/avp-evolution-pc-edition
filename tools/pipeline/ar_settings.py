import re, sys, urllib.request, urllib.parse
B = "http://127.0.0.1:50123"
h = urllib.request.urlopen(B + "/Settings/Edit").read().decode()
form = {}
for m in re.finditer(r'<select[^>]*name="(\w+)"(.*?)</select>', h, re.S):
    sel = re.search(r'<option value="([^"]*)" selected', m.group(2))
    if sel: form[m.group(1)] = sel.group(1)
for m in re.finditer(r'<input[^>]*>', h):
    t = m.group(0); n = re.search(r'name="(\w+)"', t)
    if not n: continue
    if 'type="checkbox"' in t:
        if 'checked' in t: form[n.group(1)] = "true"
    else:
        v = re.search(r'value="([^"]*)"', t); form[n.group(1)] = v.group(1) if v else ""
for kv in sys.argv[1:]:
    k, v = kv.split("=", 1); form[k] = v
print(form)
r = urllib.request.urlopen(B + "/Settings/Update", urllib.parse.urlencode(form).encode())
print(r.status)
