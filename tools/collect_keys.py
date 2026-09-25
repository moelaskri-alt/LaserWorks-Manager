"""Collects localization keys referenced from C# and XAML sources (string literals shaped like 'Group.Name')."""
import re, os, sys, json
ROOT = sys.argv[1] if len(sys.argv) > 1 else "src"
lit = re.compile(r'"((?:[A-Z][A-Za-z0-9]*)\.(?:[A-Za-z0-9_]+)(?:\.[A-Za-z0-9_]+)*)"')
tx = re.compile(r'\{l:T ([A-Za-z0-9_.]+)\}')
skip_prefix = ("System.", "Microsoft.", "LaserWorks.", "Avalonia.", "Serilog.", "QuestPDF.", "ClosedXML.")
keys = {}
for d, _, files in os.walk(ROOT):
    if "/bin" in d or "/obj" in d or "Migrations" in d: continue
    for f in files:
        if not f.endswith((".cs", ".axaml")): continue
        p = os.path.join(d, f)
        s = open(p, encoding="utf-8").read()
        found = set(tx.findall(s))
        for m in lit.findall(s):
            if m.startswith(skip_prefix) or re.search(r'\.(json|ttf|png|ico|db|log|axaml|xlsx|pdf|csv|lwbak|txt|flag)$', m, re.I): continue
            found.add(m)
        for k in found: keys.setdefault(k, set()).add(os.path.relpath(p, ROOT))
json.dump({k: sorted(v) for k, v in sorted(keys.items())}, sys.stdout, ensure_ascii=False, indent=0)
