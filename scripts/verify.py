"""Verify the resx fix"""
import os

path = r'C:\Users\lumin\src\baihuagu\services\Baihua.Core\Localization\SharedResources.resx'

with open(path, 'rb') as f:
    data = f.read()

idx = data.find(b'AiTask_SourceInfo"')
start = max(0, idx - 5)
chunk = data[start:start+180]

print('Hex:')
for i in range(0, len(chunk), 30):
    line = chunk[i:i+30]
    hex_str = ' '.join(f'{b:02x}' for b in line)
    print(f'  {hex_str}')

print()
s = chunk.decode('utf-8', errors='replace')
print('As repr:', repr(s))
print()
print('Contains emoji 📌:', '📌' in s)
print('Contains newline:', '\n' in s)
