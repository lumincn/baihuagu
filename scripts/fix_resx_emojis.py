"""Fix emoji escape sequences in resx files - replace literal \UXXXX with actual emoji chars"""
import re
import os

def fix_emoji_escapes(text):
    """Replace literal \UXXXXXXXX with actual unicode chars"""
    def replace_emoji(m):
        hex_str = m.group(1)
        try:
            return chr(int(hex_str, 16))
        except:
            return m.group(0)
    
    # Replace \UXXXXXXXX (8 hex digits) and \uXXXX (4 hex digits) literal escape sequences
    text = re.sub(r'\\U([0-9A-Fa-f]{8})', replace_emoji, text)
    text = re.sub(r'\\u([0-9A-Fa-f]{4})', replace_emoji, text)
    # Replace literal \n with actual newlines
    text = text.replace('\\n', '\n')
    return text

base_dir = r'C:\Users\lumin\src\baihuagu\services\Baihua.Core\Localization'

for fname in ['SharedResources.resx', 'SharedResources.zh-CN.resx']:
    path = os.path.join(base_dir, fname)
    with open(path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    fixed = fix_emoji_escapes(content)
    
    with open(path, 'w', encoding='utf-8') as f:
        f.write(fixed)
    
    print(f"Fixed {fname}")
print("Done!")
