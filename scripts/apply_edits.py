#!/usr/bin/env python
"""Apply edits to C# source files for i18n phase 4"""

import os, re

BASE = r'C:\Users\lumin\src\baihuagu'

def read_file(path):
    with open(path, 'r', encoding='utf-8') as f:
        return f.read()

def write_file(path, content):
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)

def apply_replacements(filepath, replacements, description=""):
    """Apply a list of (old, new) replacements to a file"""
    path = os.path.join(BASE, filepath)
    content = read_file(path)
    before = content
    for old, new in replacements:
        count = content.count(old)
        if count == 0:
            print(f"  WARNING: Pattern not found in {filepath}: {old[:60]}")
        elif count > 1:
            print(f"  WARNING: {count} matches for pattern in {filepath}: {old[:60]}")
        content = content.replace(old, new)
    if content != before:
        write_file(path, content)
        print(f"  Updated {filepath} ({description})")
    else:
        print(f"  No changes in {filepath}")

# ============================================================
# 1. TasksController.AiChat.Create.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Controllers\Tasks\TasksController.AiChat.Create.cs',
    [
        # Source info replacement
        ('''var sourceInfo = $"> 📌 **来源**: AI 生成  \\n" +
                            $"> 🤖 **模型**: {aiResult.Model}  \\n" +
                            $"> 🏢 **提供商**: {aiResult.ProviderName}  \\n" +
                            $"> ⏰ **时间**: {requestTime:yyyy-MM-dd HH:mm:ss}  \\n" +
                            $"> ⏱️ **耗时**: {stopwatch.ElapsedMilliseconds}ms  \\n\\n";''',
         '''var sourceInfo = string.Format(_loc["AiTask_SourceInfo"], aiResult.Model, aiResult.ProviderName, requestTime.ToString("yyyy-MM-dd HH:mm:ss"), stopwatch.ElapsedMilliseconds);'''),
        # AI-generated directory
        ('var aiDir = System.IO.Path.Combine(notesRoot, "AI 生成");',
         'var aiDir = System.IO.Path.Combine(notesRoot, _loc["AiGeneratedDir"]);'),
        ('notePath = $"AI 生成/{Path.GetFileNameWithoutExtension(fileName)}";',
         'notePath = $"{_loc["AiGeneratedDir"]}/{Path.GetFileNameWithoutExtension(fileName)}";'),
    ],
    "AiChat.Create"
)

# ============================================================
# 2. TasksController.AiChat.Retry.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Controllers\Tasks\TasksController.AiChat.Retry.cs',
    [
        ('''var sourceInfo = $"> 📌 **来源**: AI 生成（重试）  \\n" +
                            $"> 🤖 **模型**: {aiResult.Model}  \\n" +
                            $"> 🏢 **提供商**: {aiResult.ProviderName}  \\n" +
                            $"> ⏰ **时间**: {requestTime:yyyy-MM-dd HH:mm:ss}  \\n" +
                            $"> ⏱️ **耗时**: {stopwatch.ElapsedMilliseconds}ms  \\n\\n";''',
         '''var sourceInfo = string.Format(_loc["AiTask_RetrySourceInfo"], aiResult.Model, aiResult.ProviderName, requestTime.ToString("yyyy-MM-dd HH:mm:ss"), stopwatch.ElapsedMilliseconds);'''),
        ('var aiDir = System.IO.Path.Combine(notesRoot, "AI 生成");',
         'var aiDir = System.IO.Path.Combine(notesRoot, _loc["AiGeneratedDir"]);'),
        ('notePath = $"AI 生成/{Path.GetFileNameWithoutExtension(fileName)}";',
         'notePath = $"{_loc["AiGeneratedDir"]}/{Path.GetFileNameWithoutExtension(fileName)}";'),
    ],
    "AiChat.Retry"
)

# ============================================================
# 3. AIController.Notes.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Controllers\AI\AIController.Notes.cs',
    [
        ('$"关于：{query}")', '$"About: {0}, query)'),
        ('$"AI 生成/{GenerateSafeFileName(title)}"', '$"{_loc["AiGeneratedDir"]}/{GenerateSafeFileName(title)}"'),
    ],
    "AIController.Notes"
)

# ============================================================
# 4. AiFunctionService.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Services\AI\AiFunctionService.cs',
    [
        ('Path.Combine(notesRoot, $"AI 生成/{safeTitle}.md")', 'Path.Combine(notesRoot, _loc["AiGeneratedDir"], $"{safeTitle}.md")'),
    ],
    "AiFunctionService"
)

# ============================================================
# 5. DeviceService.cs (Core)
# ============================================================
apply_replacements(
    r'services\Baihua.Core\DeviceService.cs',
    [
        ('"请求不存在或已处理"', '_loc["Device_RequestNotFound"]'),
    ],
    "DeviceService"
)

# ============================================================
# 6. ApiKeyProtectionService.cs (Core)
# ============================================================
apply_replacements(
    r'services\Baihua.Core\Security\ApiKeyProtectionService.cs',
    [
        ('throw new InvalidOperationException("无法加密 API Key", ex);',
         'throw new InvalidOperationException(string.Format(_loc["Exception_CannotEncryptKey"]), ex);'),
    ],
    "ApiKeyProtectionService"
)

# ============================================================
# 7. NotesMdCliService.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Services\Core\NotesMdCliService.cs',
    [
        ('throw new InvalidOperationException("无法启动 notesmd-cli 进程");',
         'throw new InvalidOperationException(_loc["Exception_NotesMdCliStart"]);'),
    ],
    "NotesMdCliService"
)

# ============================================================
# 8. LlamaSharpInference.cs (AI)
# ============================================================
apply_replacements(
    r'services\Baihua.AI\Services\LocalAI\LlamaSharpInference.cs',
    [
        ('throw new FileNotFoundException("GGUF 模型文件不存在", modelPath);',
         'throw new FileNotFoundException(_loc["Exception_GgufModelNotFound"], modelPath);'),
    ],
    "LlamaSharpInference"
)

# ============================================================
# 9. OnnxRuntimeGenAIInference.cs (AI)
# ============================================================
apply_replacements(
    r'services\Baihua.AI\Services\LocalAI\OnnxRuntimeGenAIInference.cs',
    [
        ('throw new DirectoryNotFoundException($"ONNX 模型目录不存在: {modelPath}");',
         'throw new DirectoryNotFoundException(string.Format(_loc["Exception_OnnxDirNotFound"], modelPath));'),
    ],
    "OnnxRuntimeGenAIInference"
)

# ============================================================
# 10. Baihua.AI/Program.cs
# ============================================================
# This is a top-level file (no _loc available), use English directly
apply_replacements(
    r'services\Baihua.AI\Program.cs',
    [
        ('Description = "百花 AI 服务 - 模型、聊天、搜索、指标"',
         'Description = "Baihua AI Service - Models, Chat, Search, Metrics"'),
    ],
    "AI Program.cs"
)

# ============================================================
# 11. OpenClawModelProfileService.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Services\OpenClaw\OpenClawModelProfileService.cs',
    [
        ('Name = "快速",', 'Name = _loc["OpenClaw_Profile_Quick_Name"],'),
        ('Description = "671MB 超轻量模型，响应极快，适合简单问答和日常查询"', 'Description = _loc["OpenClaw_Profile_Quick_Desc"]'),
        ('SpeedLabel = "⚡ 极快"', 'SpeedLabel = _loc["OpenClaw_Profile_Quick_Speed"]'),
        ('Name = "平衡",', 'Name = _loc["OpenClaw_Profile_Balanced_Name"],'),
        ('Description = "4.7GB 量化模型，在知识库内容上表现均衡，推荐日常使用"', 'Description = _loc["OpenClaw_Profile_Balanced_Desc"]'),
        ('Name = "强力",', 'Name = _loc["OpenClaw_Profile_Powerful_Name"],'),
        ('Description = "27B 大参数模型，推理能力强，适合复杂辨证分析和深度问答"', 'Description = _loc["OpenClaw_Profile_Powerful_Desc"]'),
        ('SpeedLabel = "🐢 较慢"', 'SpeedLabel = _loc["OpenClaw_Profile_Powerful_Speed"]'),
    ],
    "OpenClawModelProfileService"
)

# ============================================================
# 12. LocalModelDeploymentService.Lifecycle.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Services\AI\LocalModelDeploymentService.Lifecycle.cs',
    [
        ('Name = "Hugging Face 镜像 (hf-mirror.com)",', 'Name = _loc["LocalModel_Mirror_HuggingFace"],'),
        ('Name = "魔搭社区 (ModelScope)",', 'Name = _loc["LocalModel_Mirror_ModelScope"],'),
    ],
    "Lifecycle"
)

# ============================================================
# 13. LocalModelDeploymentService.Provider.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Services\AI\LocalModelDeploymentService.Provider.cs',
    [
        ('const string defaultProviderName = "本地 Ollama";', 'const string defaultProviderName = "Local Ollama";'),
        ('const string defaultProviderName = "本地 LM Studio";', 'const string defaultProviderName = "Local LM Studio";'),
    ],
    "Provider"
)

# ============================================================
# 14. LocalAiConfigService.Scan.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Services\OpenClaw\LocalAiConfigService.Scan.cs',
    [
        ('Name = $"{modelName} (需启动服务)",', 'Name = string.Format(_loc["LocalModel_NeedsStart"], modelName),'),
    ],
    "LocalAiConfigService.Scan"
)

# ============================================================
# 15. OllamaLibraryClient.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Services\AI\OllamaLibraryClient.cs',
    [
        ('Description = string.IsNullOrEmpty(description) ? $"Ollama Library 官方模型: {name}" : description,',
         'Description = string.IsNullOrEmpty(description) ? string.Format(_loc["OllamaLibrary_DefaultDesc"], name) : description,'),
    ],
    "OllamaLibraryClient"
)

# ============================================================
# 16. EmbeddingService.cs (Core)
# ============================================================
apply_replacements(
    r'services\Baihua.Core\Services\EmbeddingService.cs',
    [
        ('false, "返回空结果");', 'false, _loc["Embedding_EmptyResult"]);'),
    ],
    "EmbeddingService"
)

# ============================================================
# 17. ChatCompletionsController.Streaming.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Controllers\AI\ChatCompletionsController.Streaming.cs',
    [
        ('systemPrompt += $"\n\n知识库：{activeVault.Name}。回答问题时请结合知识库内容。";',
         'systemPrompt += string.Format(_loc["Ai_KnowledgeBasePrompt"], activeVault.Name);'),
    ],
    "ChatCompletions.Streaming"
)

# ============================================================
# 18. MasterController.cs - only line 150 (RAG context prompt)
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Controllers\AI\MasterController.cs',
    [
        ('return $"以下是关联知识库中的相关内容，请结合这些内容回答：\n\n{string.Join("\n---\n", results)}";',
         'return string.Format(_loc["Ai_RagContextPrompt"], string.Join("\n---\n", results));'),
    ],
    "MasterController"
)

# ============================================================
# 19. HealthController.Fix.cs - case "知识库"
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Controllers\Common\HealthController.Fix.cs',
    [
        ('case "知识库":', 'case "KnowledgeBase":'),
    ],
    "HealthController.Fix"
)

# ============================================================
# 20. EndToEndPerformanceService.cs (Web) - "网络: ..." and "渲染: ..."
# ============================================================
apply_replacements(
    r'services\Baihua.Web\Services\EndToEndPerformanceService.cs',
    [
        ('"(网络: {NetworkMs}ms, 渲染: {RenderMs}ms)"',
         'string.Format(_loc["Performance_NetworkRenderMs"], networkMs, renderMs)'),
    ],
    "EndToEndPerformanceService"
)

# ============================================================
# 21. DailyCardService.Answer.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Services\Learning\DailyCardService.Answer.cs',
    [
        ('await _learnerService.CreateAsync("默认学习者");', 'await _learnerService.CreateAsync(_loc["Default_Learner"]);'),
    ],
    "DailyCardService.Answer"
)

# ============================================================
# 22. DailyCardService.Cards.cs - "家长出题"
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Services\Learning\DailyCardService.Cards.cs',
    [
        ('Name = request.Deck ?? "家长出题",', 'Name = request.Deck ?? _loc["DailyCard_DefaultDeck"],'),
        ('Name = request.Deck ?? "家长出题",', 'Name = request.Deck ?? _loc["DailyCard_DefaultDeck"],'),
    ],
    "DailyCardService.Cards"
)

# ============================================================
# 23. StartupMonitor.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Services\Core\StartupMonitor.cs',
    [
        ('$"[{now:yyyy-MM-dd HH:mm:ss}] ⚠️ 检测到快速重启！距离上次启动: {timeSinceLast.TotalSeconds:F1} 秒，重启次数: {RestartCount}\n");',
         'string.Format(_loc["StartupMonitor_RestartDetected"], now.ToString("yyyy-MM-dd HH:mm:ss"), timeSinceLast.TotalSeconds, RestartCount));'),
        ('if (line.Contains("服务启动"))', 'if (line.Contains(_loc["StartupMonitor_ServiceStarted"]))'),
    ],
    "StartupMonitor"
)

# ============================================================
# 24. VaultController.Mobile.Sync.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Vault\Controllers\Core\VaultController.Mobile.Sync.cs',
    [
        ('migrated ? "（已迁移路径）" : "");', 'migrated ? _loc["Vault_MigrationTagMigrated"] : "");'),
    ],
    "VaultController.Mobile.Sync"
)

# ============================================================
# 25. RagService.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Services\AI\RagService.cs',
    [
        ('$"以下是与问题相关的知识库内容，请结合这些内容回答：\n\n{context}\n\n---\n\n用户问题：{query}");',
         'string.Format(_loc["Ai_RagSearchPrompt"], context, query));'),
    ],
    "RagService"
)

# ============================================================
# 26. OnboardingController.Samples.cs
# ============================================================
apply_replacements(
    r'services\Baihua.Family\Controllers\Onboarding\OnboardingController.Samples.cs',
    [
        # Note: these vault types are hardcoded comparisons, change to use constants
        # For now, replace the comparison strings with proper keys
        ('vaultName = vaultType == "tcm" ? "中医" : "计算机";', 
         'vaultName = vaultType == "tcm" ? _loc["Onboarding_DefaultTcmIndustry"] : _loc["Onboarding_DefaultComputerIndustry"];'),
        ('var industry = vaultType == "tcm" ? "中医" : "计算机";',
         'var industry = vaultType == "tcm" ? _loc["Onboarding_DefaultTcmIndustry"] : _loc["Onboarding_DefaultComputerIndustry"];'),
    ],
    "OnboardingController.Samples"
)

print("\nAll edits applied!")
