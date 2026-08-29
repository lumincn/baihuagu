using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Baihua.AI.Provider;
using Baihua.Core.Services;
using Baihua.Data.Entities;
using Baihua.Contracts.Medical;

namespace Baihua.Family.Services.Medical;

/// <summary>
/// 家庭病历本 AI 诊断服务。
/// 用户提交症状描述，AI 结合成员档案（年龄/性别/血型/过敏史/慢性病）与近期病历给出
/// 仅供参考的健康分析。系统提示词强制要求：
///   1. 不给出确定性诊断，只给可能性分析与就医建议；
///   2. 识别需要立即就医的警示信号；
///   3. 声明"仅作参考，不可代替医生"。
/// 每次诊断结果落库（AiDiagnosis），供家庭成员日后查阅。
/// 模型路由：优先使用扁仓 BianCang 医疗模型（若已运行），回退到主模型。
/// </summary>
public class MedicalAiService
{
    private readonly AiClientService _aiClient;
    private readonly AiSettingsService _aiSettings;
    private readonly MedicalService _medicalService;
    private readonly ILocalRuntimeManager _runtimeManager;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<MedicalAiService> _logger;

    public MedicalAiService(
        AiClientService aiClient,
        AiSettingsService aiSettings,
        MedicalService medicalService,
        ILocalRuntimeManager runtimeManager,
        IHttpClientFactory httpFactory,
        ILogger<MedicalAiService> logger)
    {
        _aiClient = aiClient;
        _aiSettings = aiSettings;
        _medicalService = medicalService;
        _runtimeManager = runtimeManager;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>
    /// 执行一次 AI 健康分析。成功时已把结果落库并返回诊断记录；失败时返回面向用户的错误信息（不落库）。
    /// </summary>
    public async Task<AiDiagnoseResultDto> DiagnoseAsync(int memberId, string symptomText, string? extraContext, CancellationToken ct = default)
    {
        var trimmed = symptomText?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed.Length > 2000)
            return Fail("症状描述不能为空或过长（最多 2000 字）");

        var member = await _medicalService.GetMemberAsync(memberId, ct);
        if (member == null)
            return Fail("家庭成员不存在");

        var provider = _aiSettings.GetMainAiProvider();
        if (provider == null)
        {
            _logger.LogWarning("未找到 AI Provider 配置，无法进行 AI 诊断");
            return Fail("未配置 AI 模型，请先在 AI 设置中配置主模型");
        }

        // 模型路由：优先扁仓 BianCang 医疗模型（若已运行），回退到主模型
        var (model, modelUsed) = TryGetMedicalModel() ?? (provider.GetMainModel(), "main");

        if (string.IsNullOrWhiteSpace(model))
        {
            _logger.LogWarning("主 AI Provider 未配置模型");
            return Fail("主 AI Provider 未配置模型，请在 AI 设置中选择模型");
        }

        try
        {
            var chatClient = _aiClient.CreateChatClient(provider.Id, model);
            var options = new ChatOptions { MaxOutputTokens = 2000 };

            var records = await _medicalService.GetRecordsAsync(memberId, ct);
            var profileText = BuildProfileText(member);
            var historyText = BuildHistoryText(records);
            var userMessage = string.IsNullOrWhiteSpace(extraContext)
                ? $"### 症状描述\n{trimmed}"
                : $"### 症状描述\n{trimmed}\n\n### 补充背景\n{extraContext!.Trim()}";

            var response = await chatClient.GetResponseAsync(
                new List<ChatMessage>
                {
                    new(ChatRole.System, HealthAnalysisSystemPrompt),
                    new(ChatRole.User, $"### 家庭成员档案\n{profileText}\n\n### 近期病历（如有）\n{historyText}\n\n{userMessage}")
                },
                options,
                cancellationToken: ct);

            var text = response.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("AI 诊断返回空内容");
                return Fail("AI 没有生成有效分析，请重试");
            }

            var saved = await _medicalService.SaveDiagnosisAsync(memberId, trimmed, text, modelUsed, ct);
            return new AiDiagnoseResultDto
            {
                Success = true,
                Diagnosis = new AiDiagnosisDto
                {
                    Id = saved.Id,
                    MemberId = saved.MemberId,
                    SymptomText = saved.SymptomText,
                    AiResponse = saved.AiResponse,
                    ModelUsed = saved.ModelUsed,
                    CreatedAt = saved.CreatedAt
                }
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 用户主动取消请求（如关闭页面），不吞掉
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI 诊断请求失败，MemberId={MemberId}", memberId);
            return Fail("AI 诊断失败，请稍后重试");
        }
    }

    private static AiDiagnoseResultDto Fail(string error)
        => new() { Success = false, Error = error };

    /// <summary>
    /// 检查扁仓 BianCang 医疗模型是否已运行，返回 (modelName, "biancang") 或 null。
    /// 先查 Baihua 启动的模型，再查 OVMS REST API（config.json 加载的模型）。
    /// </summary>
    private (string Model, string Label)? TryGetMedicalModel()
    {
        try
        {
            var running = _runtimeManager.GetRunning();
            var biancang = running.FirstOrDefault(r =>
                r.Name.Contains("BianCang", StringComparison.OrdinalIgnoreCase) ||
                r.Name.Contains("biancang", StringComparison.OrdinalIgnoreCase));
            if (biancang != null)
                return (biancang.Name, "biancang");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "检查 BianCang 模型运行状态失败");
        }

        // 回退：查 OVMS REST API（模型可能通过 config.json 加载）
        try
        {
            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(5);
            var resp = http.GetAsync("http://127.0.0.1:8000/v1/models").GetAwaiter().GetResult();
            if (resp.IsSuccessStatusCode)
            {
                var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (json.Contains("\"biancang\"", StringComparison.OrdinalIgnoreCase))
                    return ("biancang", "biancang");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "查询 OVMS 模型列表失败");
        }

        return null;
    }

    /// <summary>构建成员档案文本（年龄/性别/血型/过敏史/慢性病/备注）</summary>
    private static string BuildProfileText(MedicalMember member)
    {
        var parts = new List<string> { $"姓名：{member.Name}" };
        if (!string.IsNullOrWhiteSpace(member.Gender))
            parts.Add($"性别：{member.Gender}");
        if (member.BirthDate.HasValue)
            parts.Add($"年龄：约 {ComputeAge(member.BirthDate.Value)} 岁（出生 {member.BirthDate.Value:yyyy-MM-dd}）");
        if (!string.IsNullOrWhiteSpace(member.BloodType))
            parts.Add($"血型：{member.BloodType}");

        var allergies = MedicalService.DeserializeStringList(member.AllergiesJson);
        if (allergies.Count > 0)
            parts.Add($"过敏史：{string.Join("、", allergies)}");
        var chronic = MedicalService.DeserializeStringList(member.ChronicDiseasesJson);
        if (chronic.Count > 0)
            parts.Add($"慢性病/基础疾病：{string.Join("、", chronic)}");
        if (!string.IsNullOrWhiteSpace(member.Notes))
            parts.Add($"其他备注：{member.Notes}");

        return string.Join("\n", parts);
    }

    /// <summary>构建近期病历文本（最多取最近 5 条，供 AI 参考）</summary>
    private static string BuildHistoryText(List<MedicalRecord> records)
    {
        if (records.Count == 0)
            return "（无近期病历记录）";

        var lines = new List<string>();
        foreach (var record in records.Take(5))
        {
            var symptoms = MedicalService.DeserializeStringList(record.SymptomsJson);
            var diagnoses = MedicalService.DeserializeStringList(record.DiagnosesJson);
            var medications = MedicalService.DeserializeMedications(record.MedicationsJson);

            var line = $"- {record.OccurredAt:yyyy-MM-dd} {record.Title}";
            if (symptoms.Count > 0)
                line += $"｜症状：{string.Join("、", symptoms)}";
            if (diagnoses.Count > 0)
                line += $"｜诊断：{string.Join("、", diagnoses)}";
            if (medications.Count > 0)
                line += $"｜用药：{string.Join("、", medications.Select(m => m.Name))}";
            lines.Add(line);
        }
        return string.Join("\n", lines);
    }

    /// <summary>计算周岁年龄</summary>
    private static int ComputeAge(DateTime birthDate)
    {
        var today = DateTime.Today;
        var age = today.Year - birthDate.Year;
        if (birthDate.Date > today.AddYears(-age))
            age--;
        return Math.Max(age, 0);
    }

    private const string HealthAnalysisSystemPrompt = """"
你是一位谨慎、负责的家庭健康咨询助手，服务对象是普通家庭成员（可能是非医学专业的家属）。用户会提交某位家庭成员的"症状描述"和该成员的档案（年龄/性别/血型/过敏史/慢性病）以及近期病历。请给出**仅供参考**的健康分析，严格遵循以下规则：

1. **绝不给出确定性诊断**。只做可能性分析，按可能性从高到低列出 2-4 个可能方向，并说明每个方向的依据与局限。不确定时明确说"不能确定"。
2. **必须识别警示信号**（red flags）：如高热不退、呼吸困难、剧烈胸痛、意识改变、严重出血、持续恶化等，明确提示"这些情况必须立即就医/拨打 120"。
3. **给出居家护理建议**：休息、饮水、观察要点、何时复诊，但用药建议必须保守——只建议已在用且医生开具的药物，不推荐具体新药、不给出剂量（除非是明确标明的非处方药通用建议，也要加"请按说明书/遵医嘱"）。
4. **考虑个体因素**：结合档案中的年龄、过敏史、慢性病，指出需要特别注意的地方（如"有糖尿病史，需注意……"）。
5. **结构清晰**：用 Markdown 输出，小节标题依次为：
   - **可能的原因分析**（分条，注明不确定性）
   - **居家护理与观察建议**
   - **需要立即就医的情况**（警示信号，若有）
   - **温馨提示**
6. **强制免责声明**：在"温馨提示"中必须包含以下句子：
   > 本内容由 AI 生成，仅供参考，不能代替执业医师的诊断与治疗。如症状持续、加重或出现上述警示信号，请及时就医。

7. 不要编造化验结果、检查数据或具体机构；不确定的信息明确说"建议就医确认"。
8. 回复语言：简体中文。
"""";
}
