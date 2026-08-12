using Baihua.Contracts;

namespace Baihua.Family.Services;

/// <summary>
/// 本地 AI 模型存储配置（绑定配置节 LocalAI）
/// </summary>
public class LocalAiOptions
{
    /// <summary>模型下载/存储目录，为空则使用默认路径（百花数据目录下 models/，跟随 BAIHUA_HOME）</summary>
    public string DownloadDirectory { get; set; } = "";

    /// <summary>
    /// 获取模型根目录：
    /// 1. 优先使用 DownloadDirectory 配置值
    /// 2. 未配置时回退到 $BAIHUA_HOME/models（与百花统一数据根目录一致）
    /// </summary>
    public string GetModelRoot()
    {
        if (!string.IsNullOrWhiteSpace(DownloadDirectory))
            return DownloadDirectory;
        return Path.Combine(BaihuaPaths.Home, "models");
    }
}
