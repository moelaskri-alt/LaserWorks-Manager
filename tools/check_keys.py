#!/usr/bin/env python3
"""Lists localization keys referenced in src/ (or enum values) that are missing from tools/strings."""
import os, re, sys
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
keys = set()
for f in os.listdir(os.path.join(root, 'tools/strings')):
    for line in open(os.path.join(root, 'tools/strings', f), encoding='utf-8'):
        if line.strip() and not line.startswith('#'): keys.add(line.split('|')[0])
prefixes = {k.split('.')[0] for k in keys}
lit = re.compile(r'"((?:[A-Z][A-Za-z0-9]*)\.(?:[A-Za-z0-9_]+)(?:\.[A-Za-z0-9_]+)*)"')
mk = re.compile(r'\{l:T ([A-Za-z0-9_.]+)\}')
missing = set()
for d, _, fs in os.walk(os.path.join(root, 'src')):
    if '/obj' in d or '/bin' in d: continue
    for fn in fs:
        if not (fn.endswith('.cs') or fn.endswith('.axaml')) or 'Migrations' in d: continue
        t = open(os.path.join(d, fn), encoding='utf-8').read()
        for m in mk.finditer(t):
            if m.group(1) not in keys: missing.add(m.group(1))
        for m in lit.finditer(t):
            k = m.group(1)
            if k.split('.')[0] not in prefixes: continue
            if re.search(r'\.(json|ttf|png|ico|db|log|axaml|xlsx|pdf|csv|lwbak|txt|flag)$', k, re.I): continue
            if k.startswith('Enum.') and len(k.split('.')) < 3: continue
            if k not in keys: missing.add(k)
# enums
t = open(os.path.join(root, 'src/LaserWorks.Domain/Enums/Enums.cs'), encoding='utf-8').read()
for m in re.finditer(r'public enum (\w+)[^{]*\{([^}]*)\}', t):
    for v in re.findall(r'^\s*(\w+)', re.sub(r'//[^\n]*|/\*.*?\*/|\[[^\]]*\]', '', m.group(2), flags=re.S).replace(',', '\n'), re.M):
        if v not in ('None', 'All') and f'Enum.{m.group(1)}.{v}' not in keys: missing.add(f'Enum.{m.group(1)}.{v}')
print('\n'.join(sorted(missing)))
