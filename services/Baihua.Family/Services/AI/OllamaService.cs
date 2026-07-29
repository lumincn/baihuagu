using System.Diagnostics;
using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Helpers;
using TaskRunner.Models;

namespace TaskRunner.Services;
    /// <summary>
    /// Ollama 本地模型服务：处理 Ollama 的检测、部署、运行、模型管理
    /// </summary>
    public partial class OllamaService(
        ILogger<OllamaService> logger,
        IHttpClientFactory httpClientFactory,
        LocalModelSettingsService localModelSettings)
    {
        #region Tool Info

        public async Task<string?> GetVersionAsync(CancellationToken ct = default)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ollama",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                if (process == null) return null;

                await process.WaitForExitAsync(ct);
                if (process.ExitCode != 0) return null;

                var output = await process.StandardOutput.ReadToEndAsync(ct);
                var version = Regex.Match(output, @"ollama version is ([\d.]+)")?.Groups[1].Value;
                if (string.IsNullOrEmpty(version))
                    version = Regex.Match(output, @"([\d.]+)")?.Groups[1].Value;
                return version;
            }
            catch { return null; }
        }

        public long GetModelsDirFreeSpace()
        {
            try
            {
                var customDir = localModelSettings.LocalModelDownloadDirectory;
                var dir = string.IsNullOrEmpty(customDir)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models")
                    : customDir;

                if (!Directory.Exists(dir))
                    dir = Path.GetDirectoryName(dir) ?? dir;

                var drive = new DriveInfo(Path.GetPathRoot(dir) ?? dir);
                return drive.IsReady ? drive.AvailableFreeSpace : 0;
            }
            catch { return 0; }
        }

        public string GetDefaultModelsPath()
        {
            var custom = localModelSettings.LocalModelDownloadDirectory;
            if (!string.IsNullOrEmpty(custom))
                return custom;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".ollama", "models");
        }

        #endregion

}
