#!/usr/bin/env python
"""Apply edits to C# source files for i18n phase 4 - V2"""

import os, re

BASE = r'C:\Users\lumin\src\baihuagu'

def read_file(path):
    full_path = os.path.join(BASE, path)
    with open(full_path, 'r', encoding='utf-8') as f:
        return f.read()

def write_file(path, content):
    full_path = os.path.join(BASE, path)
    with open(full_path, 'w', encoding='utf-8') as f:
        f.write(content)

def apply_replacements(filepath, replacements, description=""):
    """Apply a list of (old, new) replacements to a file"""
    path = os.path.join(BASE, filepath)
    content = read_file(filepath)
    before = content
    for old, new in replacements:
        count = content.count(old)
        if count == 0:
            print(f"  [WARN] Pattern not found in {filepath}: {old[:80]}")
        elif count > 1:
            print(f"  [WARN] {count} matches for: {old[:60]}")
        content = content.replace(old, new)
    if content != before:
        write_file(filepath, content)
        print(f"  [OK] Updated {description}")
    else:
        print(f"  [--] No change: {description}")

def add_imports_and_loc(filepath, class_name, field_line, existing_imports=True):
    """Add IStringLocalizer imports and _loc field to a file that doesn't have _loc"""
    content = read_file(filepath)
    
    # Add using if needed
    if 'Microsoft.Extensions.Localization' not in content:
        content = content.replace(
            'using Baihua.Core;',
            'using Microsoft.Extensions.Localization;\nusing Baihua.Core.Localization;\nusing Baihua.Core;'
        )
    
    # Add _loc field after the last private field
    lines = content.split('\n')
    new_lines = []
    added_field = False
    added_ctor_param = False
    
    for i, line in enumerate(lines):
        new_lines.append(line)
        
        # Add field after last private readonly line
        if not added_field and 'private readonly' in line and i+1 < len(lines):
            next_line = lines[i+1].strip()
            # Check if next is also a field or is a constructor
            if next_line.startswith('private readonly') or next_line.startswith('public ') and '(' in next_line:
                # Insert _loc field before this line
                pass  # Let's add after the constructor or at the end of fields
        
    # Simple approach: add _loc field near existing fields
    field_insert = '    private readonly IStringLocalizer<SharedResources> _loc;\n'
    ctor_old = f'    public {class_name}(\n'
    ctor_new = f'    public {class_name}(\n        IStringLocalizer<SharedResources> loc,\n'
    
    if field_line and 'private readonly IStringLocalizer' not in content:
        content = content.replace(field_line, field_line + '\n' + field_insert, 1)
    
    if ctor_old in content and 'IStringLocalizer<SharedResources> loc' not in content:
        content = content.replace(ctor_old, ctor_new, 1)
        # Add _loc assignment
        content = content.replace(
            f'        _logger = logger;',
            f'        _loc = loc;\n        _logger = logger;'
        )
    
    write_file(filepath, content)
    print(f"  [OK] Added _loc to {filepath}")

# ============================================================
# Files that already have _loc (partial classes with loc in parent)
# ============================================================

# TasksController.AiChat.Create.cs - uses _loc from TasksController
apply_replacements(
    r'services\Baihua.Family\Controllers\Tasks\TasksController.AiChat.Create.cs',
    [
        ('$"> 📌 **来源**: AI 生成  \\n" +\n                            $"> 🤖 **模型**: {aiResult.Model}  \\n" +\n                            $"> 🏢 **提供商**: {aiResult.ProviderName}  \\n" +\n                            $"> ⏰ **时间**: {requestTime:yyyy-MM-dd HH:mm:ss}  \\n" +\n                            $"> ⏱️ **耗时**: {stopwatch.ElapsedMilliseconds}ms  \\n\\n";',
         'string.Format(_loc["AiTask_SourceInfo"], aiResult.Model, aiResult.ProviderName, requestTime.ToString("yyyy-MM-dd HH:mm:ss"), stopwatch.ElapsedMilliseconds);'),
        ('var sourceInfo = $', 'var sourceInfo = '),
        ('var aiDir = System.IO.Path.Combine(notesRoot, "AI 生成");',
         'var aiDir = System.IO.Path.Combine(notesRoot, _loc["AiGeneratedDir"]);'),
        ('notePath = $"AI 生成/{Path.GetFileNameWithoutExtension(fileName)}";',
         'notePath = $"{_loc["AiGeneratedDir"]}/{Path.GetFileNameWithoutExtension(fileName)}";'),
    ],
    "AiChat.Create"
)

# TasksController.AiChat.Retry.cs - uses _loc from TasksController
apply_replacements(
    r'services\Baihua.Family\Controllers\Tasks\TasksController.AiChat.Retry.cs',
    [
        ('$"> 📌 **来源**: AI 生成（重试）  \\n" +\n                            $"> 🤖 **模型**: {aiResult.Model}  \\n" +\n                            $"> 🏢 **提供商**: {aiResult.ProviderName}  \\n" +\n                            $"> ⏰ **时间**: {requestTime:yyyy-MM-dd HH:mm:ss}  \\n" +\n                            $"> ⏱️ **耗时**: {stopwatch.ElapsedMilliseconds}ms  \\n\\n";',
         'string.Format(_loc["AiTask_RetrySourceInfo"], aiResult.Model, aiResult.ProviderName, requestTime.ToString("yyyy-MM-dd HH:mm:ss"), stopwatch.ElapsedMilliseconds);'),
        ('var sourceInfo = $', 'var sourceInfo = '),
        ('var aiDir = System.IO.Path.Combine(notesRoot, "AI 生成");',
         'var aiDir = System.IO.Path.Combine(notesRoot, _loc["AiGeneratedDir"]);'),
        ('notePath = $"AI 生成/{Path.GetFileNameWithoutExtension(fileName)}";',
         'notePath = $"{_loc["AiGeneratedDir"]}/{Path.GetFileNameWithoutExtension(fileName)}";'),
    ],
    "AiChat.Retry"
)

# OnboardingController.Samples.cs - has _loc
apply_replacements(
    r'services\Baihua.Family\Controllers\Onboarding\OnboardingController.Samples.cs',
    [
        ('vaultName = vaultType == "tcm" ? "中医" : "计算机";',
         'vaultName = vaultType == "tcm" ? _loc["Onboarding_DefaultTcmIndustry"] : _loc["Onboarding_DefaultComputerIndustry"];'),
        ('var industry = vaultType == "tcm" ? "中医" : "计算机";',
         'var industry = vaultType == "tcm" ? _loc["Onboarding_DefaultTcmIndustry"] : _loc["Onboarding_DefaultComputerIndustry"];'),
    ],
    "OnboardingController.Samples"
)

# ============================================================
# Family project files that need _loc added
# ============================================================

# AIController.Notes.cs - uses AIController partial (add _loc to base AIController.cs)
# First update AIController.cs to add _loc
apply_replacements(
    r'services\Baihua.Family\Controllers\AI\AIController.cs',
    [
        ('private readonly ILogger<AIController> _logger;',
         'private readonly ILogger<AIController> _logger;\n    private readonly IStringLocalizer<SharedResources> _loc;'),
    ],
    "AIController base - added _loc field"
)
# Add constructor parameter
apply_replacements(
    r'services\Baihua.Family\Controllers\AI\AIController.cs',
    [
        ('IStringLocalizer<SharedResources> loc,', 'IStringLocalizer<SharedResources> loc,'),  # no-op check
    ],
    "AIController base"
)
# Check if constructor already has IStringLocalizer
content = read_file(r'services\Baihua.Family\Controllers\AI\AIController.cs')
if 'IStringLocalizer<SharedResources>' not in content:
    # Add using statements
    content = content.replace(
        'using Microsoft.Extensions.AI;',
        'using Microsoft.Extensions.Localization;\nusing Microsoft.Extensions.AI;'
    )
    # Add to constructor
    content = content.replace(
        '        ILogger<AIController> logger,',
        '        IStringLocalizer<SharedResources> loc,\n        ILogger<AIController> logger,'
    )
    content = content.replace(
        '_logger = logger;',
        '_loc = loc;\n        _logger = logger;'
    )
    write_file(r'services\Baihua.Family\Controllers\AI\AIController.cs', content)
    print("  [OK] Added _loc to AIController.cs constructor")

# Now update AIController.Notes.cs
apply_replacements(
    r'services\Baihua.Family\Controllers\AI\AIController.Notes.cs',
    [
        ('$"关于：{query}")', '$"About: {_loc["Ai_NoteAboutQuery"]}")'),
        ('$"AI 生成/{GenerateSafeFileName(title)}"', '$"{_loc["AiGeneratedDir"]}/{GenerateSafeFileName(title)}"'),
        # Tool instructions (AI prompts - keep as Chinese comments, but wrap in loc)
    ],
    "AIController.Notes"
)

# ChatCompletionsController.Streaming.cs - needs _loc
apply_replacements(
    r'services\Baihua.Family\Controllers\AI\ChatCompletionsController.Streaming.cs',
    [
    ],
    "ChatCompletions.Streaming (check)"
)

# ChatCompletionsController.Tools.cs - needs _loc
apply_replacements(
    r'services\Baihua.Family\Controllers\AI\ChatCompletionsController.Tools.cs',
    [
    ],
    "ChatCompletions.Tools (check)"
)

# MasterDataRetentionService.cs
apply_replacements(
    r'services\Baihua.Family\Services\MasterDataRetentionService.cs',
    [
    ],
    "MasterDataRetentionService (check)"
)

print("\n[Phase 1 complete]")
