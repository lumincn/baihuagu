"""Fix resx files - replace literal \UXXXX with actual unicode chars, fix \n to real newlines"""
import re

base_dir = r'C:\Users\lumin\src\baihuagu\services\Baihua.Core\Localization'

EMOJI_MAP = {
    r'\U0001f4cc': '\U0001F4CC',  # 📌
    r'\U0001f916': '\U0001F916',  # 🤖
    r'\U0001f3e2': '\U0001F3E2',  # 🏢
    r'\u23f0': '\u23F0',          # ⏰
    r'\u23f1\ufe0f': '\u23F1\uFE0F',  # ⏱️
    r'\u26a0': '\u26A0',          # ⚠️
    r'\ufe0f': '\uFE0F',          # ️ (variation selector)
    r'\U0001f422': '\U0001F422',  # 🐢
}

def fix_file(fname):
    path = f'{base_dir}\\{fname}'
    with open(path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    for escaped, actual in EMOJI_MAP.items():
        content = content.replace(escaped, actual)
    
    # Replace literal \n with actual newlines (only in value tags)
    # But first, the \n might have been escaped differently
    content = content.replace('\\n', '\n')
    
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)
    print(f"Fixed {fname}")

fix_file('SharedResources.resx')
fix_file('SharedResources.zh-CN.resx')
print("Done!")
