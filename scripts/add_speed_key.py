"""Add OpenClaw_Profile_Balanced_Speed key to resx files"""
import os

basedir = r'C:\Users\lumin\src\baihuagu\services\Baihua.Core\Localization'

for fname in ['SharedResources.resx', 'SharedResources.zh-CN.resx']:
    path = os.path.join(basedir, fname)
    with open(path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Find last </data> and insert before that
    last_data = content.rfind('</data>')
    insert_at = content.index('\n', last_data) + 1
    
    if 'OpenClaw_Profile_Balanced_Speed' in content:
        print(f'{fname}: Already has key, checking value')
        continue
    
    if fname == 'SharedResources.resx':
        new_entry = '''  <data name="OpenClaw_Profile_Balanced_Speed" xml:space="preserve">
    <value>🚀 Fast</value>
  </data>
'''
    else:
        new_entry = '''  <data name="OpenClaw_Profile_Balanced_Speed" xml:space="preserve">
    <value>🚀 快</value>
  </data>
'''
    
    content = content[:insert_at] + new_entry + content[insert_at:]
    
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)
    
    print(f'{fname}: Added key')

print('Done!')
