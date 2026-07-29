using System.Diagnostics;
using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Contracts.OpenClaw;
using TaskRunner.Helpers;
using TaskRunner.Models;

namespace TaskRunner.Services
{

    public partial class LlamaCppService
    {
        #region Running Models

        public async Task<List<RunningModelDto>> GetRunningModelsAsync(CancellationToken ct = default)
        {
            var list = new List<RunningModelDto>();
            var config = await localAiConfig.GetLocalAiConfigAsync();
            var llamaCpp = config.LlamaCpp;

            if (llamaCpp == null || !llamaCpp.Enabled || string.IsNullOrWhiteSpace(llamaCpp.ModelPath))
                return list;

            try
            {
                using var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(3);
                var response = await client.GetAsync($"{llamaCpp.BaseUrl.TrimEnd('/')}/v1/models", ct);
                if (!response.IsSuccessStatusCode) return list;

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("data", out var dataArray)) return list;

                var modelFileName = Path.GetFileNameWithoutExtension(llamaCpp.ModelPath);
                var modelFileSize = 0L;
                try { modelFileSize = new FileInfo(llamaCpp.ModelPath).Length; }
                catch (Exception ex) { logger.LogDebug(ex, "文件系统操作失败"); }

                foreach (var item in dataArray.EnumerateArray())
                {
                    var modelId = JsonHelper.GetString(item, "id");
                    if (string.IsNullOrEmpty(modelId)) continue;

                    list.Add(new RunningModelDto
                    {
                        ToolId = "llamacpp",
                        ToolName = "llama.cpp",
                        ModelName = modelId,
                        DisplayName = modelFileName,
                        Status = "running",
                        SizeBytes = modelFileSize,
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "获取 llama.cpp 运行中模型失败");
            }

            return list;
        }

        #endregion

        #region Available Models

        public async Task<List<string>> GetAvailableModelsAsync(CancellationToken ct = default)
        {
            var list = new List<string>();
            var config = await localAiConfig.GetLocalAiConfigAsync();
            var llamaCpp = config.LlamaCpp;

            if (llamaCpp == null || !llamaCpp.Enabled)
                return list;

            var searchDir = !string.IsNullOrWhiteSpace(llamaCpp.ModelPath)
                ? Path.GetDirectoryName(llamaCpp.ModelPath)
                : null;

            if (string.IsNullOrWhiteSpace(searchDir) || !Directory.Exists(searchDir))
                return list;

            try
            {
                var ggufFiles = Directory.EnumerateFiles(searchDir, "*.gguf", SearchOption.TopDirectoryOnly);
                foreach (var file in ggufFiles)
                {
                    list.Add(Path.GetFileNameWithoutExtension(file));
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "扫描 llama.cpp 模型目录失败");
            }

            return list;
        }

        #endregion

        #region Downloaded Models

        public async Task<List<DownloadedModelDto>> GetDownloadedModelsAsync(List<RunningModelDto> runningModels, CancellationToken ct = default)
        {
            var list = new List<DownloadedModelDto>();
            var runningSet = new HashSet<string>(runningModels
                .Where(r => r.ToolId.Equals("llamacpp", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.ModelName), StringComparer.OrdinalIgnoreCase);

            try
            {
                var config = await localAiConfig.GetLocalAiConfigAsync();
                var llamaCpp = config.LlamaCpp;
                if (llamaCpp == null || !llamaCpp.Enabled)
                    return list;

                var searchDir = !string.IsNullOrWhiteSpace(llamaCpp.ModelPath)
                    ? Path.GetDirectoryName(llamaCpp.ModelPath)
                    : null;

                if (string.IsNullOrWhiteSpace(searchDir) || !Directory.Exists(searchDir))
                    return list;

                var ggufFiles = Directory.EnumerateFiles(searchDir, "*.gguf", SearchOption.TopDirectoryOnly);
                foreach (var file in ggufFiles)
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    long size = 0;
                    DateTime? modifiedAt = null;
                    try
                    {
                        var fi = new FileInfo(file);
                        size = fi.Length;
                        modifiedAt = fi.LastWriteTime;
                    }
                    catch (Exception ex) { logger.LogDebug(ex, "文件系统操作失败"); }

                    list.Add(new DownloadedModelDto
                    {
                        Name = name,
                        ToolId = "llamacpp",
                        ToolName = "llama.cpp",
                        SizeBytes = size,
                        ModifiedAt = modifiedAt,
                        IsRunning = runningSet.Contains(name),
                    });
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "扫描 llama.cpp 已下载模型目录失败");
            }

            return list;
        }

        #endregion
    }
}
