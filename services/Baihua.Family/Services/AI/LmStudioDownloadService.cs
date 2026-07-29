using System.Diagnostics;
using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Helpers;
using TaskRunner.Models;

namespace TaskRunner.Services;

/// <summary>
/// LM Studio 模型下载服务
/// </summary>
public class LmStudioDownloadService(ILogger<LmStudioDownloadService> logger)
{
    public async Task PullModelAsync(DeployTaskStatusDto task, ModelEntry model, string preferredSource, CancellationToken ct)
    {
        var searchName = model.LmStudioSearchName ?? model.Id;
        var downloadTarget = ResolveDownloadTarget(model, preferredSource);
        task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] 开始下载: lms get {downloadTarget} --gguf -y");

        var psi = new ProcessStartInfo
        {
            FileName = "lms",
            Arguments = $"get {downloadTarget} --gguf -y",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("无法启动 lms get 进程");

        var stdoutReader = Task.Run(async () =>
        {
            while (!process.HasExited && !ct.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(ct);
                if (line == null) break;
                ParseGetOutput(task, line);
            }
        }, ct);

        var stderrReader = Task.Run(async () =>
        {
            while (!process.HasExited && !ct.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(ct);
                if (line == null) break;
                if (!string.IsNullOrWhiteSpace(line))
                {
                    task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {line.Trim()}");
                }
            }
        }, ct);

        await process.WaitForExitAsync(ct);
        await Task.WhenAll(stdoutReader, stderrReader);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"lms get 退出码 {process.ExitCode}。可能模型名称未找到或 LM Studio GUI 未运行。");
        }
    }

    private void ParseGetOutput(DeployTaskStatusDto task, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        task.Logs.Add($"[{DateTime.Now:HH:mm:ss}] {line.Trim()}");

        var progressMatch = Regex.Match(line, @"(\d+)%");
        if (progressMatch.Success && int.TryParse(progressMatch.Groups[1].Value, out var percent))
        {
            task.ProgressPercent = Math.Min(100, Math.Max(0, percent));
        }

        if (line.Contains("downloading", StringComparison.OrdinalIgnoreCase))
            task.CurrentStep = "下载模型文件";
        else if (line.Contains("downloaded", StringComparison.OrdinalIgnoreCase) || line.Contains("finished", StringComparison.OrdinalIgnoreCase))
            task.CurrentStep = "下载完成";
        else if (line.Contains("searching", StringComparison.OrdinalIgnoreCase))
            task.CurrentStep = "搜索模型";
    }

    public async Task<bool> VerifyModelAsync(string searchName, CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "lms",
                Arguments = "ls --json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync(ct);
                if (process.ExitCode == 0)
                {
                    var output = await process.StandardOutput.ReadToEndAsync(ct);
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(output);
                        if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var searchLower = searchName.Replace("-", "").Replace("_", "").ToLowerInvariant();
                            foreach (var item in doc.RootElement.EnumerateArray())
                            {
                                var key = JsonHelper.GetString(item, "modelKey");
                                if (string.IsNullOrEmpty(key)) key = JsonHelper.GetString(item, "name");
                                if (string.IsNullOrEmpty(key)) key = JsonHelper.GetString(item, "id");
                                if (string.IsNullOrEmpty(key)) continue;

                                if (key.Equals(searchName, StringComparison.OrdinalIgnoreCase))
                                    return true;

                                var keyLower = key.Replace("-", "").Replace("_", "").Replace("/", "").ToLowerInvariant();
                                if (keyLower.Contains(searchLower) || searchLower.Contains(keyLower))
                                    return true;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex) { logger.LogDebug(ex, "操作失败"); }

        try
        {
            var modelsPath = GetDefaultModelsPath();
            if (!Directory.Exists(modelsPath))
                return false;

            var searchLower = searchName.Replace("-", "").Replace("_", "").ToLowerInvariant();
            var ggufFiles = Directory.EnumerateFiles(modelsPath, "*.gguf", SearchOption.AllDirectories);
            foreach (var file in ggufFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file).Replace("-", "").Replace("_", "").ToLowerInvariant();
                if (fileName.Contains(searchLower) || searchLower.Contains(fileName))
                    return true;
            }
        }
        catch (Exception ex) { logger.LogDebug(ex, "操作失败"); }

        return false;
    }

    public static string ResolveDownloadTarget(ModelEntry model, string preferredSource)
    {
        var searchName = model.LmStudioSearchName ?? model.Id;

        if (preferredSource.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || preferredSource.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
        {
            return searchName;
        }

        if (!string.IsNullOrEmpty(model.HuggingFaceRepo) && !string.IsNullOrEmpty(model.GgufFilename))
        {
            if (preferredSource.Equals("huggingface", StringComparison.OrdinalIgnoreCase))
                return $"https://huggingface.co/{model.HuggingFaceRepo}/resolve/main/{model.GgufFilename}";

            if (preferredSource.Equals("hf-mirror", StringComparison.OrdinalIgnoreCase))
                return $"https://hf-mirror.com/{model.HuggingFaceRepo}/resolve/main/{model.GgufFilename}";

            if (preferredSource.Equals("modelscope", StringComparison.OrdinalIgnoreCase))
                return $"https://modelscope.cn/models/{model.HuggingFaceRepo}/resolve/master/{model.GgufFilename}";
        }

        return searchName;
    }

    public static string GetDefaultModelsPath()
    {
        var home = Environment.GetEnvironmentVariable("HOME")
            ?? Environment.GetEnvironmentVariable("USERPROFILE")
            ?? AppDomain.CurrentDomain.BaseDirectory;

        // LM Studio 默认模型目录
        var lmStudioModels = Path.Combine(home, ".cache", "lm-studio", "models");
        if (Directory.Exists(lmStudioModels))
            return lmStudioModels;

        // macOS
        var macModels = Path.Combine(home, ".lmstudio", "models");
        if (Directory.Exists(macModels))
            return macModels;

        return lmStudioModels;
    }
}
