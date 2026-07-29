using System.Diagnostics;
using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;
using TaskRunner.Helpers;
using TaskRunner.Models;

namespace TaskRunner.Services;
    /// <summary>
    /// LM Studio 本地模型服务：处理 LM Studio 的检测、部署、运行、模型管理
    /// </summary>
    public partial class LmStudioService(
        ILogger<LmStudioService> logger,
        IHttpClientFactory httpClientFactory)
    {
        #region Tool Info

        public async Task<string?> GetVersionAsync(CancellationToken ct = default)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "lms",
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

                var output = (await process.StandardOutput.ReadToEndAsync(ct)).Trim();
                var versionMatch = Regex.Match(output, @"(\d+\.\d+\.?\d*)");
                if (versionMatch.Success)
                    return versionMatch.Groups[1].Value;

                return string.IsNullOrEmpty(output) ? null : output;
            }
            catch { return null; }
        }

        public long GetModelsDirFreeSpace()
        {
            try
            {
                var dir = LmStudioDownloadService.GetDefaultModelsPath();
                if (!Directory.Exists(dir))
                    dir = Path.GetDirectoryName(dir) ?? dir;

                var drive = new DriveInfo(Path.GetPathRoot(dir) ?? dir);
                return drive.IsReady ? drive.AvailableFreeSpace : 0;
            }
            catch { return 0; }
        }


        #endregion

        #region Deploy

        #endregion

}
