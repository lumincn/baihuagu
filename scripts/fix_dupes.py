"""Fix duplicate keys in resx files"""
import re

for fname in ['services/Baihua.Core/Localization/SharedResources.resx',
              'services/Baihua.Core/Localization/SharedResources.zh-CN.resx']:
    with open(fname, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Find all data name attributes
    names = re.findall(r'name="([^"]+)"', content)
    dupes = [n for n in names if names.count(n) > 1]
    unique_dupes = list(set(dupes))
    
    if unique_dupes:
        print(f'{fname}: {len(unique_dupes)} duplicate keys')
        for d in unique_dupes:
            # Find all <data> blocks for this name
            escaped = re.escape(d)
            pattern = re.compile(rf'<data\s+name="{escaped}"[^>]*>.*?</data>', re.DOTALL)
            matches = pattern.findall(content)
            if len(matches) > 1:
                # Remove the second (duplicate) <data> block
                # Find where the second match starts in the content
                idx = content.find(matches[1])
                content = content[:idx] + content[idx + len(matches[1]):]
                print(f'  Removed duplicate: {d}')
        
        with open(fname, 'w', encoding='utf-8') as f:
            f.write(content)
    else:
        print(f'{fname}: No duplicates')

print('Done!')
