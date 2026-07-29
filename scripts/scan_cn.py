"""Quick scan for remaining Chinese user-facing strings in .cs files."""
import os, re, sys

basedir = os.path.join(os.path.dirname(__file__), '..')
services_dir = os.path.join(basedir, 'services')

cn_re = re.compile(r'[\u4e00-\u9fff\u3400-\u4dbf\uf900-\ufaff]{2,}')

# Skip patterns
skip_dirs = {'obj', 'bin', 'node_modules', '.git', '.github'}
skip_prefixes = ('//', '///', '/*', '* ', '    //', '    ///', '<!--', '-->')

def should_skip_line(line):
    stripped = line.strip()
    if not stripped:
        return True
    # Skip comments
    if any(stripped.startswith(p) for p in ('//', '///', '/*', '* ')):
        return True
    # Skip XML docs
    if any(stripped.startswith(p) for p in ('///', '<summary>', '</summary>', '<param', '<returns', '<remarks')):
        return True
    # Skip resx files
    if line.strip().endswith('.resx'):
        return True
    return False

results = []
for root, dirs, files in os.walk(services_dir):
    dirs[:] = [d for d in dirs if d not in skip_dirs]
    for f in files:
        if not f.endswith('.cs'):
            continue
        fpath = os.path.join(root, f)
        rel = os.path.relpath(fpath, basedir)
        with open(fpath, 'r', encoding='utf-8', errors='replace') as fh:
            for lineno, line in enumerate(fh, 1):
                if should_skip_line(line):
                    continue
                m = cn_re.search(line)
                if m:
                    results.append((rel, lineno, line.rstrip()))

# Group by file
from collections import defaultdict
by_file = defaultdict(list)
for rel, lineno, line in results:
    by_file[rel].append((lineno, line))

print(f"Files with Chinese strings: {len(by_file)}")
print("=" * 80)
for fname in sorted(by_file.keys()):
    items = by_file[fname]
    print(f"\n{fname} ({len(items)} strings):")
    for lineno, line in items:
        print(f"  L{lineno}: {line.strip()[:120]}")
