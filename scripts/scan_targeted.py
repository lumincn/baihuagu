"""Scan ONLY the 44 listed files for Chinese user-facing strings."""
import os, re

basedir = os.path.join(os.path.dirname(__file__), '..')

files = [
    # >1 string files
    "services/Baihua.Family/Controllers/Tasks/TasksController.VaultGen.Helpers.cs",
    "services/Baihua.Family/Controllers/Tasks/TasksController.AiChat.Create.cs",
    "services/Baihua.Family/Controllers/Tasks/TasksController.AiChat.Retry.cs",
    "services/Baihua.Family/Program.cs",
    "services/Baihua.Family/Services/AI/AiFunctionService.cs",
    "services/Baihua.Family/Controllers/AI/AIController.Notes.cs",
    "services/Baihua.Family/Controllers/AI/ChatCompletionsController.Tools.cs",
    "services/Baihua.Family/Services/MasterDataRetentionService.cs",
    "services/Baihua.Family/Services/OpenClaw/OpenClawModelProfileService.cs",
    "services/Baihua.Family/Controllers/Onboarding/OnboardingController.Samples.cs",
    "services/Baihua.Family/Services/AI/ChatMemoryService.Memory.cs",
    "services/Baihua.Family/Services/AI/ChatMemoryService.Summary.cs",
    "services/Baihua.Family/Services/AI/LocalModelDeploymentService.Lifecycle.cs",
    "services/Baihua.Family/Services/AI/LocalModelDeploymentService.Provider.cs",
    "services/Baihua.Family/Services/Core/RestoreService.cs",
    "services/Baihua.Family/Services/Core/BackupService.cs",
    "services/Baihua.Family/Services/Core/Adapters/MobileDeviceServiceAdapter.cs",
    "services/Baihua.Family/Services/Core/Strategies/FamilyPairingStrategy.cs",
    "services/Baihua.Family/Services/Learning/DailyCardService.cs",
    "services/Baihua.Family/Services/AI/AtomNoteSplitter.Ai.cs",
    "services/Baihua.Family/Services/AI/AtomNoteSplitter.Save.cs",
    "services/Baihua.Web/Services/EndToEndPerformanceService.cs",
    # 1 string files
    "services/Baihua.Family/Controllers/AI/ChatCompletionsController.Streaming.cs",
    "services/Baihua.Family/Controllers/AI/MasterController.cs",
    "services/Baihua.Family/Services/MasterPromptBuilder.cs",
    "services/Baihua.Family/Services/AI/AtomNoteSplitter.Split.cs",
    "services/Baihua.Family/Services/AI/ModelRecommendationEngine.cs",
    "services/Baihua.Family/Services/AI/OllamaLibraryClient.cs",
    "services/Baihua.Family/Services/AI/RagService.cs",
    "services/Baihua.Family/Services/Anki/AnkiCardGenerator.cs",
    "services/Baihua.Family/Services/Core/NotesMdCliService.cs",
    "services/Baihua.Family/Services/Core/StartupMonitor.cs",
    "services/Baihua.Family/Services/Core/SystemHealthService.Components.cs",
    "services/Baihua.Family/Services/Learning/DailyCardService.Answer.cs",
    "services/Baihua.Family/Services/OpenClaw/LocalAiConfigService.Scan.cs",
    "services/Baihua.Core/DeviceService.cs",
    "services/Baihua.Core/HardwareInfoService.cs",
    "services/Baihua.Core/HardwareInfoService.Gpu.cs",
    "services/Baihua.Core/Security/ApiKeyProtectionService.cs",
    "services/Baihua.Core/Services/EmbeddingService.cs",
    "services/Baihua.Vault/Controllers/Core/VaultController.Mobile.Sync.cs",
    "services/Baihua.AI/Program.cs",
    "services/Baihua.AI/Services/LocalAI/LlamaSharpInference.cs",
    "services/Baihua.AI/Services/LocalAI/OnnxRuntimeGenAIInference.cs",
]

cn_re = re.compile(r'[\u4e00-\u9fff\u3400-\u4dbf]{2,}')

for relpath in files:
    fpath = os.path.join(basedir, relpath.replace('/', os.sep))
    if not os.path.exists(fpath):
        print(f"NOT FOUND: {relpath}")
        continue
    with open(fpath, 'r', encoding='utf-8', errors='replace') as fh:
        lines = fh.readlines()
    found = False
    for lineno, line in enumerate(lines, 1):
        s = line.strip()
        if not s:
            continue
        # Skip comments
        if s.startswith('//') or s.startswith('///') or s.startswith('/*') or s.startswith('* '):
            continue
        # Skip logger
        if '_logger.Log' in s or 'logger.Log' in s:
            continue
        # Skip #region / #endregion
        if s.startswith('#region') or s.startswith('#endregion'):
            continue
        m = cn_re.search(s)
        if m:
            if not found:
                print(f"\n{relpath}:")
                found = True
            print(f"  L{lineno}: {s[:150]}")
