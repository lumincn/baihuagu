"""Add new keys to SharedResources.resx and SharedResources.zh-CN.resx"""
import xml.etree.ElementTree as ET
import os, shutil, re

resx_path = r'C:\Users\lumin\src\baihuagu\services\Baihua.Core\Localization\SharedResources.resx'
resx_zh_path = r'C:\Users\lumin\src\baihuagu\services\Baihua.Core\Localization\SharedResources.zh-CN.resx'

# New keys: key -> (neutral_en, zh_cn)
NEW_KEYS = {
    # TasksController.AiChat.Create/Retry source info
    "AiTask_SourceInfo": (
        "> \\U0001f4cc **Source**: AI Generated  \\n> \\U0001f916 **Model**: {0}  \\n> \\U0001f3e2 **Provider**: {1}  \\n> \\u23f0 **Time**: {2}  \\n> \\u23f1\\ufe0f **Duration**: {3}ms  \\n\\n",
        "> \\U0001f4cc **\\u6765\\u6e90**: AI \\u751f\\u6210  \\n> \\U0001f916 **\\u6a21\\u578b**: {0}  \\n> \\U0001f3e2 **\\u63d0\\u4f9b\\u5546**: {1}  \\n> \\u23f0 **\\u65f6\\u95f4**: {2}  \\n> \\u23f1\\ufe0f **\\u8017\\u65f6**: {3}ms  \\n\\n"
    ),
    "AiTask_RetrySourceInfo": (
        "> \\U0001f4cc **Source**: AI Generated (Retry)  \\n> \\U0001f916 **Model**: {0}  \\n> \\U0001f3e2 **Provider**: {1}  \\n> \\u23f0 **Time**: {2}  \\n> \\u23f1\\ufe0f **Duration**: {3}ms  \\n\\n",
        "> \\U0001f4cc **\\u6765\\u6e90**: AI \\u751f\\u6210\\uff08\\u91cd\\u8bd5\\uff09  \\n> \\U0001f916 **\\u6a21\\u578b**: {0}  \\n> \\U0001f3e2 **\\u63d0\\u4f9b\\u5546**: {1}  \\n> \\u23f0 **\\u65f6\\u95f4**: {2}  \\n> \\u23f1\\ufe0f **\\u8017\\u65f6**: {3}ms  \\n\\n"
    ),
    # Directory names
    "AiGeneratedDir": (
        "AI Generated",
        "AI \\u751f\\u6210"
    ),
    # Error messages
    "Device_RequestNotFound": (
        "Request not found or already processed",
        "\\u8bf7\\u6c42\\u4e0d\\u5b58\\u5728\\u6216\\u5df2\\u5904\\u7406"
    ),
    # Exception messages
    "Exception_CannotEncryptKey": (
        "Unable to encrypt API Key",
        "\\u65e0\\u6cd5\\u52a0\\u5bc6 API Key"
    ),
    "Exception_GgufModelNotFound": (
        "GGUF model file not found",
        "GGUF \\u6a21\\u578b\\u6587\\u4ef6\\u4e0d\\u5b58\\u5728"
    ),
    "Exception_OnnxDirNotFound": (
        "ONNX model directory not found: {0}",
        "ONNX \\u6a21\\u578b\\u76ee\\u5f55\\u4e0d\\u5b58\\u5728: {0}"
    ),
    "Exception_NotesMdCliStart": (
        "Unable to start notesmd-cli process",
        "\\u65e0\\u6cd5\\u542f\\u52a8 notesmd-cli \\u8fdb\\u7a0b"
    ),
    # Default names
    "Default_Learner": (
        "Default Learner",
        "\\u9ed8\\u8ba4\\u5b66\\u4e60\\u8005"
    ),
    "Default_VaultCategory": (
        "Other",
        "\\u5176\\u4ed6"
    ),
    "Default_Unnamed": (
        "Unnamed",
        "\\u672a\\u547d\\u540d"
    ),
    # Model profile descriptions
    "OpenClaw_Profile_Quick_Name": (
        "Fast",
        "\\u5feb\\u901f"
    ),
    "OpenClaw_Profile_Quick_Desc": (
        "671MB ultra-lightweight model, extremely fast response, suitable for simple Q&A and daily queries",
        "671MB \\u8d85\\u8f7b\\u91cf\\u6a21\\u578b\\uff0c\\u54cd\\u5e94\\u6781\\u5feb\\uff0c\\u9002\\u5408\\u7b80\\u5355\\u95ee\\u7b54\\u548c\\u65e5\\u5e38\\u67e5\\u8be2"
    ),
    "OpenClaw_Profile_Quick_Speed": (
        "\\u26a1 Extremely Fast",
        "\\u26a1 \\u6781\\u5feb"
    ),
    "OpenClaw_Profile_Balanced_Name": (
        "Balanced",
        "\\u5e73\\u8861"
    ),
    "OpenClaw_Profile_Balanced_Desc": (
        "4.7GB quantized model, well-balanced performance on knowledge base content, recommended for daily use",
        "4.7GB \\u91cf\\u5316\\u6a21\\u578b\\uff0c\\u5728\\u77e5\\u8bc6\\u5e93\\u5185\\u5bb9\\u4e0a\\u8868\\u73b0\\u5747\\u8861\\uff0c\\u63a8\\u8350\\u65e5\\u5e38\\u4f7f\\u7528"
    ),
    "OpenClaw_Profile_Powerful_Name": (
        "Powerful",
        "\\u5f3a\\u529b"
    ),
    "OpenClaw_Profile_Powerful_Desc": (
        "27B large parameter model, strong reasoning capability, suitable for complex dialectical analysis and deep Q&A",
        "27B \\u5927\\u53c2\\u6570\\u6a21\\u578b\\uff0c\\u63a8\\u7406\\u80fd\\u529b\\u5f3a\\uff0c\\u9002\\u5408\\u590d\\u6742\\u8fa8\\u8bc1\\u5206\\u6790\\u548c\\u6df1\\u5ea6\\u95ee\\u7b54"
    ),
    "OpenClaw_Profile_Powerful_Speed": (
        "\\U0001f422 Slower",
        "\\U0001f422 \\u8f83\\u6162"
    ),
    # LocalModelDeploymentService lifecycle
    "LocalModel_Mirror_HuggingFace": (
        "Hugging Face Mirror (hf-mirror.com)",
        "Hugging Face \\u955c\\u50cf (hf-mirror.com)"
    ),
    "LocalModel_Mirror_ModelScope": (
        "ModelScope Community",
        "\\u9b54\\u642d\\u793e\\u533a (ModelScope)"
    ),
    # LocalModelDeploymentService provider names
    "LocalModel_DefaultOllamaProvider": (
        "Local Ollama",
        "\\u672c\\u5730 Ollama"
    ),
    "LocalModel_DefaultLmStudioProvider": (
        "Local LM Studio",
        "\\u672c\\u5730 LM Studio"
    ),
    # LocalAiConfigService.Scan
    "LocalModel_NeedsStart": (
        "{0} (needs service startup)",
        "{0} (\\u9700\\u542f\\u52a8\\u670d\\u52a1)"
    ),
    # OllamaLibraryClient
    "OllamaLibrary_DefaultDesc": (
        "Ollama Library official model: {0}",
        "Ollama Library \\u5b98\\u65b9\\u6a21\\u578b: {0}"
    ),
    # EmbeddingService
    "Embedding_EmptyResult": (
        "Returned empty result",
        "\\u8fd4\\u56de\\u7a7a\\u7ed3\\u679c"
    ),
    # ChatCompletionsController.Streaming
    "Ai_KnowledgeBasePrompt": (
        "Knowledge Base: {0}. Please answer combined with knowledge base content.",
        "\\u77e5\\u8bc6\\u5e93\\uff1a{0}\\u3002\\u56de\\u7b54\\u95ee\\u9898\\u65f6\\u8bf7\\u7ed3\\u5408\\u77e5\\u8bc6\\u5e93\\u5185\\u5bb9\\u3002"
    ),
    # ChatCompletionsController.Tools
    "Ai_Tools_AvailablePrompt": (
        "You have the following tools available:\n{toolDescriptions}\n\nIf you need to call a tool, use the following format (strict JSON) at the beginning of your response:\nTOOL_CALL: {{\"tool\":\"tool_name\",\"arguments\":{{argument_object}}}}\nThen on a new line, give your normal response.",
        "\\u4f60\\u6709\\u4ee5\\u4e0b\\u5de5\\u5177\\u53ef\\u7528\\uff1a\n{toolDescriptions}\n\n\\u5982\\u679c\\u9700\\u8981\\u8c03\\u7528\\u5de5\\u5177\\uff0c\\u8bf7\\u5728\\u56de\\u590d\\u5f00\\u5934\\u4f7f\\u7528\\u4ee5\\u4e0b\\u683c\\u5f0f\\uff08\\u4e25\\u683c JSON\\uff09\\uff1a\nTOOL_CALL: {{\"tool\":\"\\u5de5\\u5177\\u540d\",\"arguments\":{{{arguments: \\u53c2\\u6570\\u5bf9\\u8c61}}}}\n\\u7136\\u540e\\u5728\\u65b0\\u7684\\u4e00\\u884c\\u7ed9\\u51fa\\u4f60\\u7684\\u6b63\\u5e38\\u56de\\u590d\\u3002"
    ),
    "Ai_Tools_CallingTool": (
        "I will call the tool {0} to help you.",
        "\\u6211\\u5c06\\u8c03\\u7528\\u5de5\\u5177 {0} \\u6765\\u5e2e\\u52a9\\u4f60\\u3002"
    ),
    # MasterController - RAG context
    "Ai_RagContextPrompt": (
        "The following are relevant contents from the knowledge base. Please answer in conjunction with these contents:\n\n{0}",
        "\\u4ee5\\u4e0b\\u662f\\u5173\\u8054\\u77e5\\u8bc6\\u5e93\\u4e2d\\u7684\\u76f8\\u5173\\u5185\\u5bb9\\uff0c\\u8bf7\\u7ed3\\u5408\\u8fd9\\u4e9b\\u5185\\u5bb9\\u56de\\u7b54\\uff1a\n\n{0}"
    ),
    # VaultController.Mobile.Sync
    "Vault_MigrationTagMigrated": (
        " (path migrated)",
        "\\uff08\\u5df2\\u8fc1\\u79fb\\u8def\\u5f84\\uff09"
    ),
    # Baihua.AI Program.cs
    "AiService_Description": (
        "Baihua AI Service - Models, Chat, Search, Metrics",
        "\\u767e\\u82b1 AI \\u670d\\u52a1 - \\u6a21\\u578b\\u3001\\u804a\\u5929\\u3001\\u641c\\u7d22\\u3001\\u6307\\u6807"
    ),
    # EndToEndPerformanceService
    "Performance_NetworkRenderMs": (
        "(Network: {0}ms, Render: {1}ms)",
        "(\\u7f51\\u7edc: {0}ms, \\u6e32\\u67d3: {1}ms)"
    ),
    # Onboarding Samples - sample file paths (these are actual paths, keep as-is but comment in code)
    "Onboarding_DefaultTcmIndustry": (
        "Traditional Chinese Medicine",
        "\\u4e2d\\u533b"
    ),
    "Onboarding_DefaultComputerIndustry": (
        "Computer",
        "\\u8ba1\\u7b97\\u673a"
    ),
    # AIController.Notes
    "Ai_NoteAboutQuery": (
        "About: {0}",
        "\\u5173\\u4e8e\\uff1a{0}"
    ),
    # StartupMonitor restart message
    "StartupMonitor_RestartDetected": (
        "[{0}] \\u26a0\\ufe0f Fast restart detected! Time since last start: {1:F1}s, restart count: {2}\n",
        "[{0}] \\u26a0\\ufe0f \\u68c0\\u6d4b\\u5230\\u5feb\\u901f\\u91cd\\u542f\\uff01\\u8ddd\\u79bb\\u4e0a\\u6b21\\u542f\\u52a8: {1:F1} \\u79d2\\uff0c\\u91cd\\u542f\\u6b21\\u6570: {2}\n"
    ),
    "StartupMonitor_ServiceStarted": (
        "Service started",
        "\\u670d\\u52a1\\u542f\\u52a8"
    ),
    # DailyCardService.Answer
    "DailyCard_DefaultDeck": (
        "Parent Questions",
        "\\u5bb6\\u957f\\u51fa\\u9898"
    ),
    # MasterPromptBuilder safety keywords (these stay in Chinese as they're for content filtering)
    # But the "知识库" case in HealthController.Fix
    "Health_FixCategory_Vault": (
        "Knowledge Base",
        "\\u77e5\\u8bc6\\u5e93"
    ),
    # RAG service
    "Ai_RagSearchPrompt": (
        "The following are knowledge base contents related to the question. Please answer in conjunction with these contents:\n\n{context}\n\n---\n\nUser question: {query}",
        "\\u4ee5\\u4e0b\\u662f\\u4e0e\\u95ee\\u9898\\u76f8\\u5173\\u7684\\u77e5\\u8bc6\\u5e93\\u5185\\u5bb9\\uff0c\\u8bf7\\u7ed3\\u5408\\u8fd9\\u4e9b\\u5185\\u5bb9\\u56de\\u7b54\\uff1a\n\n{context}\n\n---\n\n\\u7528\\u6237\\u95ee\\u9898\\uff1a{query}"
    ),
}

def add_keys_to_resx(path, is_zh):
    """Add keys to a .resx file"""
    with open(path, 'r', encoding='utf-8') as f:
        content = f.read()
    
    # Find the last </data> before </root>
    last_data_pos = content.rfind('</data>')
    if last_data_pos == -1:
        print(f"Could not find </data> in {path}")
        return
    
    insert_pos = content.find('\n', last_data_pos) + 1
    
    lines_to_add = []
    for key, (en_val, zh_val) in NEW_KEYS.items():
        val = zh_val if is_zh else en_val
        # Escape XML special chars
        val = val.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')
        val = val.replace('"', '&quot;').replace("'", '&apos;')
        
        line = f'  <data name="{key}" xml:space="preserve">\n    <value>{val}</value>\n  </data>'
        lines_to_add.append(line)
    
    new_content = content[:insert_pos] + '\n' + '\n'.join(lines_to_add) + '\n' + content[insert_pos:]
    
    with open(path, 'w', encoding='utf-8') as f:
        f.write(new_content)
    
    print(f"Added {len(lines_to_add)} keys to {os.path.basename(path)}")

add_keys_to_resx(resx_path, False)
add_keys_to_resx(resx_zh_path, True)
print("Done!")
