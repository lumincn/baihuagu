namespace Baihua.Contracts.Assistant;

/// <summary>助理设置（开关等）</summary>
public class AssistantSettingsDto
{
    /// <summary>总开关：关闭则停止采集与每日分析</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>每日分析时间（小时，0-23，默认 23 点）</summary>
    public int AnalyzeHour { get; set; } = 23;

    /// <summary>活动数据保留天数（默认 30）</summary>
    public int RetentionDays { get; set; } = 30;
}

/// <summary>单条用户活动</summary>
public class UserActivityDto
{
    public DateTime Time { get; set; }
    public string Type { get; set; } = "";   // chat / search / task / note / checkin
    public string Text { get; set; } = "";
    public int Length { get; set; }
}

/// <summary>兴趣主题（AI 推测）</summary>
public class InterestTopicDto
{
    public string Topic { get; set; } = "";
    public double Confidence { get; set; }
    public string? Evidence { get; set; }
}

/// <summary>每日分析结果</summary>
public class AssistantAnalysisDto
{
    public string Date { get; set; } = "";          // yyyy-MM-dd
    public string? Summary { get; set; }            // 今日活动摘要
    public List<InterestTopicDto> Interests { get; set; } = new();
    public List<GeneratedVaultDto> GeneratedVaults { get; set; } = new();
    public int ActivityCount { get; set; }
    public DateTime AnalyzedAt { get; set; }
    public string? Model { get; set; }
    public string? Raw { get; set; }
}

/// <summary>为兴趣生成的知识库</summary>
public class GeneratedVaultDto
{
    public string Topic { get; set; } = "";
    public string? VaultName { get; set; }
    public string? TaskId { get; set; }
    public string Status { get; set; } = "started";  // started / failed
    public string? Error { get; set; }
}
