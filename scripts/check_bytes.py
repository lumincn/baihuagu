"""Check the actual bytes in the resx file around AiTask_SourceInfo"""
import os

path = r'C:\Users\lumin\src\baihuagu\services\Baihua.Core\Localization\SharedResources.resx'

with open(path, 'rb') as f:
    data = f.read()

idx = data.find(b'AiTask_SourceInfo"')
start = max(0, idx - 10)
chunk = data[start:start+200]

print("Raw hex bytes:")
for i, b in enumerate(chunk):
    print(f'{b:02x}', end=' ')
    if (i + 1) % 40 == 0:
        print()
print()

print("\nAs text (showing rep):")
print(repr(chunk.decode('utf-8', errors='replace')[:200]))
