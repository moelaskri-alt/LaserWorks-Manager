"""Builds src/LaserWorks.Localization/Resources/{ar,en}.json from tools/strings/*.txt (key|English|Arabic)."""
import glob, json, os, sys
root = os.path.dirname(os.path.abspath(__file__))
en, ar, errors = {}, {}, []
for f in sorted(glob.glob(os.path.join(root, "strings", "*.txt"))):
    for n, line in enumerate(open(f, encoding="utf-8"), 1):
        line = line.rstrip("\n")
        if not line.strip() or line.startswith("#"): continue
        parts = line.split("|")
        if len(parts) != 3: errors.append(f"{f}:{n}: expected 3 fields"); continue
        k, e, a = (p.strip() for p in parts)
        if k in en: errors.append(f"{f}:{n}: duplicate key {k}")
        if e.count("{") != a.count("{"): errors.append(f"{f}:{n}: placeholder mismatch in {k}")
        en[k], ar[k] = e, a
if errors: print("\n".join(errors)); sys.exit(1)
out = os.path.join(root, "..", "src", "LaserWorks.Localization", "Resources")
for lang, d in (("en", en), ("ar", ar)):
    with open(os.path.join(out, lang + ".json"), "w", encoding="utf-8") as fh:
        json.dump(dict(sorted(d.items())), fh, ensure_ascii=False, indent=1)
print(f"{len(en)} keys written")
