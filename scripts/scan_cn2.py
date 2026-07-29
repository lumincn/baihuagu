"""Scan for Chinese user-facing strings in .cs files (non-log, non-comment)."""
import os, re

basedir = os.path.join(os.path.dirname(__file__), '..')
services_dir = os.path.join(basedir, 'services')

cn_re = re.compile(r'[\u4e00-\u9fff\u3400-\u4dbf]{2,}')

skip_dirs = {'obj', 'bin', 'node_modules', '.git', '.github', 'Migrations'}

def is_skip_line(line):
    s = line.strip()
    if not s:
        return True
    # Comments
    if s.startswith('//') or s.startswith('///') or s.startswith('/*') or s.startswith('* '):
        return True
    # Logger
    if '_logger.Log' in s or 'logger.Log' in s:
        return True
    # #region / #endregion
    if s.startswith('#region') or s.startswith('#endregion'):
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
        try:
            with open(fpath, 'r', encoding='utf-8', errors='replace') as fh:
                for lineno, line in enumerate(fh, 1):
                    if is_skip_line(line):
                        continue
                    m = cn_re.search(line)
                    if m:
                        results.append((rel, lineno, line.rstrip()))
        except Exception as e:
            print(f"Error reading {rel}: {e}")

from collections import defaultdict
by_file = defaultdict(list)
for rel, lineno, line in results:
    by_file[rel].append((lineno, line))

print(f"Files with Chinese user-facing strings: {len(by_file)}")
print("=" * 80)
for fname in sorted(by_file.keys()):
    items = by_file[fname]
    print(f"\n{fname} ({len(items)} strings):")
    for lineno, line in items:
        s = line.strip()[:130]
        try:
            print(f"  L{lineno}: {s}")
        except:
            print(f"  L{lineno}: [encoding error]")
