#!/usr/bin/env python3
"""Apply targeted edits for i18n phase 4"""

import os

BASE = r'C:\Users\lumin\src\baihuagu'

def edit_file(relpath, old, new):
    path = os.path.join(BASE, relpath)
    with open(path, 'r', encoding='utf-8') as f:
        content = f.read()
    count = content.count(old)
    if count == 0:
        print(f"  NOT FOUND in {relpath}")
        print(f"    Looking for: {repr(old[:80])}")
        return
    content = content.replace(old, new)
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)
    print(f"  OK ({count} match): {relpath}")

# ================================================================
# 1. TasksController.AiChat.Create.cs (has _loc from TasksController partial)
# ================================================================
# Source info with emoji characters
src_create = r'services/Baihua.Family/Controllers/Tasks/TasksController.AiChat.Create.cs'

# Read the exact content to find the source info block
with open(os.path.join(BASE, src_create), 'r', encoding='utf-8') as f:
    content = f.read()

# Find the source info block
old_block = '''                        var sourceInfo = $"> 📌 **来源**: AI 生成  \n" +
                            $"> 🤖 **模型**: {aiResult.Model}  \n" +
                            $"> 🏢 **提供商**: {aiResult.ProviderName}  \n" +
                            $"> ⏰ **时间**: {requestTime:yyyy-MM-dd HH:mm:ss}  \n" +
                            $"> ⏱️ **耗时**: {stopwatch.ElapsedMilliseconds}ms  \n\n";'''

new_block = '''                        var sourceInfo = string.Format(_loc["AiTask_SourceInfo"], aiResult.Model, aiResult.ProviderName, requestTime.ToString("yyyy-MM-dd HH:mm:ss"), stopwatch.ElapsedMilliseconds);'''

content = content.replace(old_block, new_block)
count1 = old_block in content
if old_block not in content:
    print(f"  OK: TasksController.AiChat.Create.cs - source info replaced")

# Replace AI 生成 dir
content = content.replace(
    'var aiDir = System.IO.Path.Combine(notesRoot, "AI 生成");',
    'var aiDir = System.IO.Path.Combine(notesRoot, _loc["AiGeneratedDir"]);'
)
content = content.replace(
    'notePath = $"AI 生成/{Path.GetFileNameWithoutExtension(fileName)}";',
    'notePath = $"{_loc["AiGeneratedDir"]}/{Path.GetFileNameWithoutExtension(fileName)}";'
)

with open(os.path.join(BASE, src_create), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: TasksController.AiChat.Create.cs")

# ================================================================
# 2. TasksController.AiChat.Retry.cs (has _loc from TasksController partial)
# ================================================================
src_retry = r'services/Baihua.Family/Controllers/Tasks/TasksController.AiChat.Retry.cs'
with open(os.path.join(BASE, src_retry), 'r', encoding='utf-8') as f:
    content = f.read()

old_block2 = '''                        var sourceInfo = $"> 📌 **来源**: AI 生成（重试）  \n" +
                            $"> 🤖 **模型**: {aiResult.Model}  \n" +
                            $"> 🏢 **提供商**: {aiResult.ProviderName}  \n" +
                            $"> ⏰ **时间**: {requestTime:yyyy-MM-dd HH:mm:ss}  \n" +
                            $"> ⏱️ **耗时**: {stopwatch.ElapsedMilliseconds}ms  \n\n";'''

new_block2 = '''                        var sourceInfo = string.Format(_loc["AiTask_RetrySourceInfo"], aiResult.Model, aiResult.ProviderName, requestTime.ToString("yyyy-MM-dd HH:mm:ss"), stopwatch.ElapsedMilliseconds);'''

content = content.replace(old_block2, new_block2)
content = content.replace(
    'var aiDir = System.IO.Path.Combine(notesRoot, "AI 生成");',
    'var aiDir = System.IO.Path.Combine(notesRoot, _loc["AiGeneratedDir"]);'
)
content = content.replace(
    'notePath = $"AI 生成/{Path.GetFileNameWithoutExtension(fileName)}";',
    'notePath = $"{_loc["AiGeneratedDir"]}/{Path.GetFileNameWithoutExtension(fileName)}";'
)

with open(os.path.join(BASE, src_retry), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: TasksController.AiChat.Retry.cs")

# ================================================================
# 3. AIController.Notes.cs - add _loc to base AIController.cs first
# ================================================================
# Check if AIController base has _loc already
src_ai_base = r'services/Baihua.Family/Controllers/AI/AIController.cs'
with open(os.path.join(BASE, src_ai_base), 'r', encoding='utf-8') as f:
    content = f.read()

if 'IStringLocalizer<SharedResources>' not in content:
    # Add using
    content = content.replace(
        'using Microsoft.Extensions.AI;',
        'using Microsoft.Extensions.Localization;\nusing Baihua.Core.Localization;\nusing Microsoft.Extensions.AI;'
    )
    # Add field
    content = content.replace(
        'private readonly ILogger<AIController> _logger;',
        'private readonly ILogger<AIController> _logger;\n    private readonly IStringLocalizer<SharedResources> _loc;'
    )
    # Add constructor param
    content = content.replace(
        '        ILogger<AIController> logger,',
        '        IStringLocalizer<SharedResources> loc,\n        ILogger<AIController> logger,'
    )
    content = content.replace(
        '_logger = logger;',
        '_loc = loc;\n        _logger = logger;'
    )
    with open(os.path.join(BASE, src_ai_base), 'w', encoding='utf-8') as f:
        f.write(content)
    print("  Done: Added _loc to AIController.cs")
else:
    print("  Already has _loc: AIController.cs")

# Now update AIController.Notes.cs
src_notes = r'services/Baihua.Family/Controllers/AI/AIController.Notes.cs'
with open(os.path.join(BASE, src_notes), 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace(
    '$"关于：{query}")',
    '$"About: {_loc["Ai_NoteAboutQuery"]}")'
)
content = content.replace(
    '$"AI 生成/{GenerateSafeFileName(title)}"',
    '$"{_loc["AiGeneratedDir"]}/{GenerateSafeFileName(title)}"'
)

with open(os.path.join(BASE, src_notes), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: AIController.Notes.cs")

# ================================================================
# 4. AiFunctionService.cs (already has _loc)
# ================================================================
src_fn = r'services/Baihua.Family/Services/AI/AiFunctionService.cs'
with open(os.path.join(BASE, src_fn), 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace(
    'Path.Combine(notesRoot, $"AI 生成/{safeTitle}.md")',
    'Path.Combine(notesRoot, _loc["AiGeneratedDir"], $"{safeTitle}.md")'
)

with open(os.path.join(BASE, src_fn), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: AiFunctionService.cs")

# ================================================================
# 5. Baihua.AI/Program.cs - top level, use English directly
# ================================================================
ai_prog = r'services/Baihua.AI/Program.cs'
with open(os.path.join(BASE, ai_prog), 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace(
    'Description = "百花 AI 服务 - 模型、聊天、搜索、指标"',
    'Description = "Baihua AI Service - Models, Chat, Search, Metrics"'
)
with open(os.path.join(BASE, ai_prog), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: Baihua.AI/Program.cs")

# ================================================================
# 6. Core files - use English directly (no _loc available)
# ================================================================
# DeviceService.cs
dev = r'services/Baihua.Core/DeviceService.cs'
with open(os.path.join(BASE, dev), 'r', encoding='utf-8') as f:
    content = f.read()
content = content.replace(
    'return (false, null, "请求不存在或已处理");',
    'return (false, null, "Request not found or already processed");'
)
with open(os.path.join(BASE, dev), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: DeviceService.cs")

# ApiKeyProtectionService.cs
akps = r'services/Baihua.Core/Security/ApiKeyProtectionService.cs'
with open(os.path.join(BASE, akps), 'r', encoding='utf-8') as f:
    content = f.read()
content = content.replace(
    'throw new InvalidOperationException("无法加密 API Key", ex);',
    'throw new InvalidOperationException("Unable to encrypt API Key", ex);'
)
with open(os.path.join(BASE, akps), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: ApiKeyProtectionService.cs")

# EmbeddingService.cs
emb = r'services/Baihua.Core/Services/EmbeddingService.cs'
with open(os.path.join(BASE, emb), 'r', encoding='utf-8') as f:
    content = f.read()
content = content.replace(
    'false, "返回空结果");',
    'false, "Empty result");'
)
with open(os.path.join(BASE, emb), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: EmbeddingService.cs")

# ================================================================
# 7. AI project files - use English directly
# ================================================================
# LlamaSharpInference.cs
llama = r'services/Baihua.AI/Services/LocalAI/LlamaSharpInference.cs'
with open(os.path.join(BASE, llama), 'r', encoding='utf-8') as f:
    content = f.read()
content = content.replace(
    'throw new FileNotFoundException("GGUF 模型文件不存在", modelPath);',
    'throw new FileNotFoundException("GGUF model file not found", modelPath);'
)
with open(os.path.join(BASE, llama), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: LlamaSharpInference.cs")

# OnnxRuntimeGenAIInference.cs
onnx = r'services/Baihua.AI/Services/LocalAI/OnnxRuntimeGenAIInference.cs'
with open(os.path.join(BASE, onnx), 'r', encoding='utf-8') as f:
    content = f.read()
content = content.replace(
    'throw new DirectoryNotFoundException($"ONNX 模型目录不存在: {modelPath}");',
    'throw new DirectoryNotFoundException($"ONNX model directory not found: {modelPath}");'
)
with open(os.path.join(BASE, onnx), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: OnnxRuntimeGenAIInference.cs")

# ================================================================
# 8. Vault project - use English directly
# ================================================================
vault_sync = r'services/Baihua.Vault/Controllers/Core/VaultController.Mobile.Sync.cs'
with open(os.path.join(BASE, vault_sync), 'r', encoding='utf-8') as f:
    content = f.read()
content = content.replace(
    'migrated ? "（已迁移路径）" : "");',
    'migrated ? " (path migrated)" : "");'
)
with open(os.path.join(BASE, vault_sync), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: VaultController.Mobile.Sync.cs")

# ================================================================
# 9. OpenClawModelProfileService.cs - add _loc
# ================================================================
profile = r'services/Baihua.Family/Services/OpenClaw/OpenClawModelProfileService.cs'
with open(os.path.join(BASE, profile), 'r', encoding='utf-8') as f:
    content = f.read()

if 'IStringLocalizer<SharedResources>' not in content:
    # Add usings
    content = content.replace(
        'using Baihua.Contracts.OpenClaw;',
        'using Microsoft.Extensions.Localization;\nusing Baihua.Core.Localization;\nusing Baihua.Contracts.OpenClaw;'
    )
    # Add field
    content = content.replace(
        'private readonly ILogger<OpenClawModelProfileService> _logger;',
        'private readonly ILogger<OpenClawModelProfileService> _logger;\n    private readonly IStringLocalizer<SharedResources> _loc;'
    )
    # Add constructor param - find the constructor
    content = content.replace(
        'ILogger<OpenClawModelProfileService> logger)',
        'IStringLocalizer<SharedResources> loc,\n        ILogger<OpenClawModelProfileService> logger)'
    )
    content = content.replace(
        '_logger = logger;',
        '_loc = loc;\n        _logger = logger;'
    )

# Replace Chinese strings
content = content.replace('Name = "快速",', 'Name = _loc["OpenClaw_Profile_Quick_Name"],')
content = content.replace('Description = "671MB 超轻量模型，响应极快，适合简单问答和日常查询"', 'Description = _loc["OpenClaw_Profile_Quick_Desc"]')
content = content.replace('SpeedLabel = "⚡ 极快"', 'SpeedLabel = _loc["OpenClaw_Profile_Quick_Speed"]')
content = content.replace('Name = "平衡",', 'Name = _loc["OpenClaw_Profile_Balanced_Name"],')
content = content.replace('Description = "4.7GB 量化模型，在知识库内容上表现均衡，推荐日常使用"', 'Description = _loc["OpenClaw_Profile_Balanced_Desc"]')
content = content.replace('Name = "强力",', 'Name = _loc["OpenClaw_Profile_Powerful_Name"],')
content = content.replace('Description = "27B 大参数模型，推理能力强，适合复杂辨证分析和深度问答"', 'Description = _loc["OpenClaw_Profile_Powerful_Desc"]')
content = content.replace('SpeedLabel = "🐢 较慢"', 'SpeedLabel = _loc["OpenClaw_Profile_Powerful_Speed"]')

with open(os.path.join(BASE, profile), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: OpenClawModelProfileService.cs")

# ================================================================
# 10. LocalModelDeploymentService.Lifecycle.cs - use English directly
# ================================================================
lifecycle = r'services/Baihua.Family/Services/AI/LocalModelDeploymentService.Lifecycle.cs'
with open(os.path.join(BASE, lifecycle), 'r', encoding='utf-8') as f:
    content = f.read()
content = content.replace(
    'Name = "Hugging Face 镜像 (hf-mirror.com)",',
    'Name = "Hugging Face Mirror (hf-mirror.com)",'
)
content = content.replace(
    'Name = "魔搭社区 (ModelScope)",',
    'Name = "ModelScope Community",'
)
with open(os.path.join(BASE, lifecycle), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: LocalModelDeploymentService.Lifecycle.cs")

# ================================================================
# 11. LocalModelDeploymentService.Provider.cs - use English directly
# ================================================================
provider = r'services/Baihua.Family/Services/AI/LocalModelDeploymentService.Provider.cs'
with open(os.path.join(BASE, provider), 'r', encoding='utf-8') as f:
    content = f.read()
content = content.replace(
    'const string defaultProviderName = "本地 Ollama";',
    'const string defaultProviderName = "Local Ollama";'
)
content = content.replace(
    'const string defaultProviderName = "本地 LM Studio";',
    'const string defaultProviderName = "Local LM Studio";'
)
with open(os.path.join(BASE, provider), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: LocalModelDeploymentService.Provider.cs")

# ================================================================
# 12. LocalAiConfigService.Scan.cs - add _loc
# ================================================================
scan = r'services/Baihua.Family/Services/OpenClaw/LocalAiConfigService.Scan.cs'
with open(os.path.join(BASE, scan), 'r', encoding='utf-8') as f:
    content = f.read()

if 'IStringLocalizer<SharedResources>' not in content:
    content = content.replace(
        'using Baihua.Contracts.OpenClaw;',
        'using Microsoft.Extensions.Localization;\nusing Baihua.Core.Localization;\nusing Baihua.Contracts.OpenClaw;'
    )
    content = content.replace(
        'private readonly ILogger<LocalAiConfigService> _logger;',
        'private readonly ILogger<LocalAiConfigService> _logger;\n    private readonly IStringLocalizer<SharedResources> _loc;'
    )
    content = content.replace(
        'ILogger<LocalAiConfigService> logger)',
        'IStringLocalizer<SharedResources> loc,\n        ILogger<LocalAiConfigService> logger)'
    )
    content = content.replace(
        '_logger = logger;',
        '_loc = loc;\n        _logger = logger;'
    )

content = content.replace(
    'Name = $"{modelName} (需启动服务)",',
    'Name = string.Format(_loc["LocalModel_NeedsStart"], modelName),'
)

with open(os.path.join(BASE, scan), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: LocalAiConfigService.Scan.cs")

# ================================================================
# 13. OllamaLibraryClient.cs - add _loc
# ================================================================
ollama = r'services/Baihua.Family/Services/AI/OllamaLibraryClient.cs'
with open(os.path.join(BASE, ollama), 'r', encoding='utf-8') as f:
    content = f.read()

if 'IStringLocalizer<SharedResources>' not in content:
    content = content.replace(
        'using Microsoft.Extensions.AI;',
        'using Microsoft.Extensions.Localization;\nusing Baihua.Core.Localization;\nusing Microsoft.Extensions.AI;'
    )
    content = content.replace(
        'private readonly ILogger<OllamaLibraryClient> _logger;',
        'private readonly ILogger<OllamaLibraryClient> _logger;\n    private readonly IStringLocalizer<SharedResources> _loc;'
    )
    content = content.replace(
        'ILogger<OllamaLibraryClient> logger)',
        'IStringLocalizer<SharedResources> loc,\n        ILogger<OllamaLibraryClient> logger)'
    )
    content = content.replace(
        '_logger = logger;',
        '_loc = loc;\n        _logger = logger;'
    )

content = content.replace(
    'Description = string.IsNullOrEmpty(description) ? $"Ollama Library 官方模型: {name}" : description,',
    'Description = string.IsNullOrEmpty(description) ? string.Format(_loc["OllamaLibrary_DefaultDesc"], name) : description,'
)

with open(os.path.join(BASE, ollama), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: OllamaLibraryClient.cs")

# ================================================================
# 14. EndToEndPerformanceService.cs (Web) - use English directly
# ================================================================
e2e = r'services/Baihua.Web/Services/EndToEndPerformanceService.cs'
with open(os.path.join(BASE, e2e), 'r', encoding='utf-8') as f:
    content = f.read()
# This file has string interpolation with {NetworkMs} and {RenderMs} in Chinese context
# Replace the Chinese parameter names in the log messages
content = content.replace(
    '"(网络: {NetworkMs}ms, 渲染: {RenderMs}ms)"',
    '"(Network: {NetworkMs}ms, Render: {RenderMs}ms)"'
)
with open(os.path.join(BASE, e2e), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: EndToEndPerformanceService.cs")

# ================================================================
# 15. DailyCardService.Answer.cs - add _loc
# ================================================================
answer = r'services/Baihua.Family/Services/Learning/DailyCardService.Answer.cs'
with open(os.path.join(BASE, answer), 'r', encoding='utf-8') as f:
    content = f.read()

if 'IStringLocalizer<SharedResources>' not in content:
    content = content.replace(
        'using Baihua.Family.Services;',
        'using Microsoft.Extensions.Localization;\nusing Baihua.Core.Localization;\nusing Baihua.Family.Services;'
    )
    content = content.replace(
        'private readonly ILogger<DailyCardService> _logger;',
        'private readonly ILogger<DailyCardService> _logger;\n    private readonly IStringLocalizer<SharedResources> _loc;'
    )
    content = content.replace(
        'ILogger<DailyCardService> logger)',
        'IStringLocalizer<SharedResources> loc,\n        ILogger<DailyCardService> logger)'
    )
    content = content.replace(
        '_logger = logger;',
        '_loc = loc;\n        _logger = logger;'
    )

content = content.replace(
    'await _learnerService.CreateAsync("默认学习者");',
    'await _learnerService.CreateAsync(_loc["Default_Learner"]);'
)

with open(os.path.join(BASE, answer), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: DailyCardService.Answer.cs")

# ================================================================
# 16. DailyCardService.Cards.cs - same class, _loc already added above
# ================================================================
cards = r'services/Baihua.Family/Services/Learning/DailyCardService.Cards.cs'
with open(os.path.join(BASE, cards), 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace(
    'Name = request.Deck ?? "家长出题",',
    'Name = request.Deck ?? _loc["DailyCard_DefaultDeck"],'
)

with open(os.path.join(BASE, cards), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: DailyCardService.Cards.cs")

# ================================================================
# 17. StartupMonitor.cs - add _loc
# ================================================================
monitor = r'services/Baihua.Family/Services/Core/StartupMonitor.cs'
with open(os.path.join(BASE, monitor), 'r', encoding='utf-8') as f:
    content = f.read()

if 'IStringLocalizer<SharedResources>' not in content:
    content = content.replace(
        'using System.Text;',
        'using Microsoft.Extensions.Localization;\nusing Baihua.Core.Localization;\nusing System.Text;'
    )
    content = content.replace(
        'private readonly ILogger<StartupMonitor> _logger;',
        'private readonly ILogger<StartupMonitor> _logger;\n    private readonly IStringLocalizer<SharedResources> _loc;'
    )
    content = content.replace(
        'ILogger<StartupMonitor> logger)',
        'IStringLocalizer<SharedResources> loc,\n        ILogger<StartupMonitor> logger)'
    )
    content = content.replace(
        '_logger = logger;',
        '_loc = loc;\n        _logger = logger;'
    )

content = content.replace(
    'line.Contains("服务启动")',
    'line.Contains(_loc["StartupMonitor_ServiceStarted"])'
)

with open(os.path.join(BASE, monitor), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: StartupMonitor.cs")

# ================================================================
# 18. NotesMdCliService.cs - add _loc
# ================================================================
notes_cli = r'services/Baihua.Family/Services/Core/NotesMdCliService.cs'
with open(os.path.join(BASE, notes_cli), 'r', encoding='utf-8') as f:
    content = f.read()

if 'IStringLocalizer<SharedResources>' not in content:
    content = content.replace(
        'using System.Diagnostics;',
        'using Microsoft.Extensions.Localization;\nusing Baihua.Core.Localization;\nusing System.Diagnostics;'
    )
    content = content.replace(
        'private readonly ILogger<NotesMdCliService> _logger;',
        'private readonly ILogger<NotesMdCliService> _logger;\n    private readonly IStringLocalizer<SharedResources> _loc;'
    )
    content = content.replace(
        'ILogger<NotesMdCliService> logger)',
        'IStringLocalizer<SharedResources> loc,\n        ILogger<NotesMdCliService> logger)'
    )
    content = content.replace(
        '_logger = logger;',
        '_loc = loc;\n        _logger = logger;'
    )

with open(os.path.join(BASE, notes_cli), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: NotesMdCliService.cs")

# ================================================================
# 19. OnboardingController.Samples.cs (already has _loc)
# ================================================================
samples = r'services/Baihua.Family/Controllers/Onboarding/OnboardingController.Samples.cs'
with open(os.path.join(BASE, samples), 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace(
    'vaultName = vaultType == "tcm" ? "中医" : "计算机";',
    'vaultName = vaultType == "tcm" ? _loc["Onboarding_DefaultTcmIndustry"] : _loc["Onboarding_DefaultComputerIndustry"];'
)
content = content.replace(
    'var industry = vaultType == "tcm" ? "中医" : "计算机";',
    'var industry = vaultType == "tcm" ? _loc["Onboarding_DefaultTcmIndustry"] : _loc["Onboarding_DefaultComputerIndustry"];'
)

with open(os.path.join(BASE, samples), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: OnboardingController.Samples.cs")

# ================================================================
# 20. RAG context prompt - ChatCompletionsController.Streaming.cs -> add _loc
# ================================================================
stream = r'services/Baihua.Family/Controllers/AI/ChatCompletionsController.Streaming.cs'
with open(os.path.join(BASE, stream), 'r', encoding='utf-8') as f:
    content = f.read()

if 'IStringLocalizer<SharedResources>' not in content:
    content = content.replace(
        'using Microsoft.Extensions.AI;',
        'using Microsoft.Extensions.Localization;\nusing Baihua.Core.Localization;\nusing Microsoft.Extensions.AI;'
    )
    content = content.replace(
        'private readonly ILogger<ChatCompletionsController> _logger;',
        'private readonly ILogger<ChatCompletionsController> _logger;\n    private readonly IStringLocalizer<SharedResources> _loc;'
    )
    content = content.replace(
        'ILogger<ChatCompletionsController> logger)',
        'IStringLocalizer<SharedResources> loc,\n        ILogger<ChatCompletionsController> logger)'
    )
    content = content.replace(
        '_logger = logger;',
        '_loc = loc;\n        _logger = logger;'
    )

with open(os.path.join(BASE, stream), 'w', encoding='utf-8') as f:
    f.write(content)
print("  Done: ChatCompletionsController.Streaming.cs")

print("\n=== All edits applied! ===")
