using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Baihua.Contracts.Ai;
using Microsoft.Extensions.Localization;
using Baihua.Contracts.Anki;
using Baihua.Contracts.Achievements;
using Baihua.Contracts.Benchmark;
using Baihua.Contracts.Health;
using Baihua.Contracts.LocalModels;
using Baihua.Contracts.Metrics;
using Baihua.Contracts.OpenClaw;
using Baihua.Contracts.Scene;
using Baihua.Contracts.Master;
using Baihua.Contracts.Onboarding;

namespace Baihua.Web.Services
{
    /// <summary>
    /// AI 流式响应事件
    /// </summary>
    public class ChatStreamEvent
    {
        public string Type { get; set; } = "";
        public string? Content { get; set; }
        public string? ToolName { get; set; }
        public Dictionary<string, object?>? ToolArguments { get; set; }
    }

    public interface IApiService : ITaskRunnerHealthApi
    {
        /// <summary>快速健康检查，后台不可用时抛出异常（3秒超时）</summary>
        Task CheckHealthFastAsync(CancellationToken cancellationToken = default);
        Task<HealthFixResultDto> FixHealthIssuesAsync(CancellationToken cancellationToken = default);
        Task<JsonElement> SetupOpenClawAsync(CancellationToken cancellationToken = default);

        Task<List<TaskInfo>> GetTasksAsync();
        Task<TaskInfo?> GetTaskAsync(string taskId);
        Task<OnboardingStatusDto> GetOnboardingStatusAsync();
        Task<bool> CompleteOnboardingAsync();
        Task<VaultGenerationResponse> CreateVaultGenerationTaskAsync(string industry, string keyword, string? model = null, int noteCount = 30, bool generateCards = false);
        Task<List<AiProviderInfo>> GetAiProvidersAsync();
        Task<SearchResponse> SearchAsync(string query, string vaultId);
        Task<IndexStatusDto> GetIndexStatusAsync(string vaultId);
        Task<bool> RebuildIndexAsync(string vaultId, CancellationToken cancellationToken = default);
        Task<AiNoteResponse> AskAIAsync(string query, bool saveToVault);
        Task<AiTaskResponse> CreateAiTaskAsync(string query, bool saveToVault, string vaultId, string? model = null, bool autoSplit = false, string? systemPrompt = null, string? industry = null);
        Task<ChatResponse> ChatAsync(string message, CancellationToken cancellationToken = default);
        IAsyncEnumerable<string> StreamChatAsync(string message, string providerId, string model, List<(bool IsUser, string Content)>? history = null, CancellationToken cancellationToken = default);
        IAsyncEnumerable<ChatStreamEvent> StreamChatWithEventsAsync(string message, string providerId, string model, List<(bool IsUser, string Content)>? history = null, string? sessionId = null, CancellationToken cancellationToken = default);

        // 直接调用 TaskRunner.AI（纯 AI，无 RAG/记忆/Function Calling）
        Task<ChatResponse> ChatDirectAsync(string message, string? providerId = null, string? model = null, CancellationToken cancellationToken = default);
        IAsyncEnumerable<string> StreamChatDirectAsync(string message, string? providerId = null, string? model = null, List<(bool IsUser, string Content)>? history = null, CancellationToken cancellationToken = default);
        IAsyncEnumerable<string> StreamLocalChatAsync(string message, string modelPath, string modelType, List<(bool IsUser, string Content)>? history = null, CancellationToken cancellationToken = default);
        IAsyncEnumerable<string> StreamChatWithVaultAsync(string message, string model, List<(bool IsUser, string Content)>? history = null, CancellationToken cancellationToken = default);
        Task<List<LocalModelInfo>> ScanLocalModelsAsync(string? directory = null);

        // 本地视觉识别（Qwen2.5-VL + OpenVINO）
        Task<VisionStatusDto> GetVisionStatusAsync(CancellationToken cancellationToken = default);
        Task<VisionStatusDto> StartVisionServerAsync(CancellationToken cancellationToken = default);
        Task<VisionResultDto> RecognizeImageAsync(byte[] imageBytes, string prompt, string model, CancellationToken cancellationToken = default);

        // AI 绘图（ComfyUI）
        Task<ComfyStatusDto> GetComfyStatusAsync(CancellationToken cancellationToken = default);
        Task<ComfyGenerateResultDto> GenerateComfyImageAsync(string prompt, string negativePrompt, int width, int height, int steps, CancellationToken cancellationToken = default);
        Task<ComfyGenerateResultDto> GenerateComfyVideoAsync(string prompt, string negativePrompt, CancellationToken cancellationToken = default);
        Task<List<ComfyHistoryItemDto>> GetComfyHistoryAsync(int limit = 50, string? kind = null, CancellationToken cancellationToken = default);

        string GetBackendBaseUrl();
        Task<bool> DeleteTaskAsync(string taskId);
        Task<bool> DeleteAllTasksAsync();
        Task<bool> CancelTaskAsync(string taskId);
        Task<AiTaskResponse> RetryAiTaskAsync(string taskId, int timeoutMinutes = 0, string? model = null);

        Task<VaultNoteResponse?> ReadVaultNoteAsync(string path, string vaultId);
        Task<bool> WriteVaultNoteAsync(string path, string content, string vaultId);
        Task<GenerateMissingNoteResponse?> GenerateMissingNoteAsync(string linkPath, string vaultId);
        Task<VaultBrowseResponse?> GetVaultBrowseAsync(string vaultId, string? path = null);
        Task<VaultNotesBatchResponse?> GetVaultNotesBatchAsync(string vaultId);

        Task<string?> GetVaultRootAsync();
        Task<bool> SetVaultRootAsync(string vaultPath);

        // AI 配置管理
        Task<List<AiConfigProvider>> GetAiConfigProvidersAsync();
        Task<AiConfigProvider?> GetAiConfigProviderAsync(string providerId);
        Task<SaveAiProviderResult> SaveAiConfigProviderAsync(SaveAiProviderRequest request);
        Task<bool> DeleteAiConfigProviderAsync(string providerId);
        Task<EnvConfigHelp?> GetAiEnvConfigHelpAsync();
        Task<List<AiProviderPreset>> GetAiProviderPresetsAsync();

        // Embedding 配置
        Task<EmbeddingConfigDto> GetEmbeddingConfigAsync();
        Task<SaveAiProviderResult> SaveEmbeddingConfigAsync(SaveEmbeddingConfigRequest request);

        // 每日一帖 Anki
        Task<DailyCardResultDto> GetDailyCardAsync(string vaultId);
        Task<(bool Success, DailyProgressDto? Progress)> SubmitDailyAnswerAsync(string vaultId, string cardId, string result);
        Task<DailyProgressDto> GetDailyProgressAsync(string vaultId);
        Task<bool> SaveCustomCardAsync(string vaultId, CustomCardRequestDto request);
        Task<int> GetAnkiCardCountAsync(string vaultId);
        Task<BatchGenerateResultDto?> GenerateAnkiCardsBatchAsync(string vaultId, string directory, bool recursive = true);
        Task<int> GetVaultCardCountAsync(string vaultId);
        Task<int> GetVaultNoteCountAsync(string vaultId);
        Task<GenerateCardsTaskDto?> GenerateAllCardsAsync(string vaultId);
        Task<AnkiSearchResult> SearchAnkiCardsAsync(string? query, string? vaultId, int limit = 100);
        Task<DeckListResult> GetAnkiDecksAsync(string? vaultId);

        // 成就与赛舟榜
        Task<List<LearnerDto>> GetLearnersAsync();
        Task<LearnerDto> CreateLearnerAsync(CreateLearnerRequest request);
        Task<bool> SetDefaultLearnerAsync(int id);
        Task<bool> DeleteLearnerAsync(int id);
        Task<List<AchievementDto>> GetAchievementsAsync(int learnerId);
        Task<List<AchievementDto>> CheckAchievementsAsync(int learnerId);
        Task<List<RewardProgressDto>> GetRewardProgressAsync(string? vaultId = null);
        Task<RewardConfigDto> CreateRewardAsync(CreateRewardRequest request);
        Task<List<RewardClaimDto>> TriggerRewardsAsync(string? vaultId = null);
        Task<QuizSessionDto> CreateQuizAsync(CreateQuizRequest request);
        Task<QuizResultDto> SubmitQuizAnswerAsync(SubmitAnswerRequest request);
        Task<List<LeaderboardEntryDto>> GetLeaderboardAsync(string type, string? vaultId = null);
        Task<DashboardDataDto> GetDashboardAsync(string? vaultId = null, int? learnerId = null);
        Task<CheckinDataDto> GetCheckinDataAsync(string? vaultId = null);
        Task<CheckinMakeupResultDto> MakeupCheckinAsync(CheckinMakeupRequest request);
        Task<WeeklyCompareResultDto> GetWeeklyCompareAsync(string? vaultId = null, int? learnerId = null);
        Task<List<LeaderboardEntryDto>> GetRoleLeaderboardAsync(string role, string? vaultId = null);
        Task<LeaderboardSettingsDto> GetAllFamilyTabSettingAsync();
        Task<LeaderboardSettingsDto> SetAllFamilyTabSettingAsync(bool enabled);

        // Obsidian 操作
        Task<bool> OpenInObsidianAsync(CancellationToken cancellationToken = default);
        Task<bool> OpenVaultInObsidianAsync(string path);

        // NotesMD CLI
        Task<NotesMdCliStatus?> GetNotesMdCliStatusAsync();
        Task<bool> AddVaultToNotesMdCliAsync(string path);
        Task<NotesMdBatchResult?> BatchAddVaultsToNotesMdCliAsync(List<string> paths);

        // 平台信息
        Task<PlatformInfoResponse?> GetPlatformAsync(CancellationToken cancellationToken = default);

        // 本地模型部署
        Task<HardwareInfoDto?> GetHardwareInfoAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
        Task<List<RecommendedModelDto>> GetRecommendedModelsAsync(string? scenario = null, bool forceRefresh = false, CancellationToken cancellationToken = default);
        Task<bool> RefreshLibraryAsync(CancellationToken cancellationToken = default);
        Task<OpenVinoLlmStatusDto?> GetOpenVinoLlmStatusAsync(CancellationToken cancellationToken = default);
        Task<(bool Success, string Message)> ControlOpenVinoLlmAsync(string action, int port, CancellationToken cancellationToken = default);
        Task<DeployLocalModelResult> DeployLocalModelAsync(DeployLocalModelRequest request, CancellationToken cancellationToken = default);
        Task<DeployTaskStatusDto?> GetDeployTaskStatusAsync(string taskId, CancellationToken cancellationToken = default);
        Task<bool> CancelDeployTaskAsync(string taskId, CancellationToken cancellationToken = default);
        Task<List<LocalToolInfoDto>> GetLocalToolsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
        Task<List<DownloadSourceDto>> GetDownloadSourcesAsync(CancellationToken cancellationToken = default);
        Task<DownloadDirectoryConfigDto?> GetDownloadConfigAsync(CancellationToken cancellationToken = default);
        Task<bool> SaveDownloadConfigAsync(DownloadDirectoryConfigDto config, CancellationToken cancellationToken = default);

        // 运行中模型管理
        Task<List<RunningModelDto>> GetRunningModelsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
        Task<List<string>> GetAvailableModelsAsync(string toolId, CancellationToken cancellationToken = default);
        Task<List<DownloadedModelDto>> GetDownloadedModelsAsync(CancellationToken cancellationToken = default);
        Task<bool> DeleteModelAsync(DeleteModelRequest request, CancellationToken cancellationToken = default);
        Task<ModelDetailsDto?> GetModelDetailsAsync(ModelDetailsRequest request, CancellationToken cancellationToken = default);
        Task<bool> LoadModelAsync(LoadModelRequest request, CancellationToken cancellationToken = default);
        Task<bool> UnloadModelAsync(UnloadModelRequest request, CancellationToken cancellationToken = default);
        Task<LocalAiServiceStatusDto> StartLlamaCppAsync(CancellationToken cancellationToken = default);
        Task<bool> StopLlamaCppAsync(CancellationToken cancellationToken = default);

        // 请求指标统计
        Task<MetricsSummary?> GetMetricsSummaryAsync(CancellationToken cancellationToken = default);
        Task<List<RequestMetric>> GetSlowestRequestsAsync(int count = 10, CancellationToken cancellationToken = default);
        Task<List<PathFrequency>> GetFrequentPathsAsync(int count = 10, CancellationToken cancellationToken = default);
        Task<List<RequestMetric>> GetRecentErrorsAsync(int count = 10, CancellationToken cancellationToken = default);
        Task<bool> ClearMetricsAsync(CancellationToken cancellationToken = default);

        // OpenClaw 任务
        Task<OpenClawTaskDto> CreateOpenClawTaskAsync(string prompt);
        Task<List<OpenClawTaskDto>> GetOpenClawTasksAsync();
        Task<OpenClawTaskDto?> GetOpenClawTaskAsync(int id);
        Task<string?> GetOpenClawReportAsync(int id);
        Task<bool> DeleteOpenClawTaskAsync(int id);
        Task<bool> CancelOpenClawTaskAsync(int id);
        Task<OpenClawLocalAiConfigDto> GetOpenClawLocalAiConfigAsync();
        Task<bool> SaveOpenClawLocalAiConfigAsync(SaveOpenClawLocalAiConfigRequest request);
        Task<List<OpenClawLocalModelDto>> ScanOpenClawLocalModelsAsync(string provider);
        Task<LocalAiServiceStatusDto> DetectAndStartOpenClawLocalAiAsync(string provider);
        Task<OpenClawDefaultModelDto> GetOpenClawDefaultModelAsync();
        Task<bool> SetOpenClawDefaultModelAsync(string model);
        Task<ModelProfileListDto> GetModelProfilesAsync();
        Task<bool> SetModelProfileAsync(string profile);
        Task<bool> SyncLocalModelsToOpenClawAsync(string provider);

        // 模型基准测试
        Task<List<RecommendedBenchmarkModel>> GetBenchmarkModelsAsync(string? category = null);
        Task<VramTierResponse> GetBenchmarkVramTiersAsync(string? category = null);
        Task<List<BenchmarkPrompt>> GetBenchmarkPromptsAsync(string? category = null);
        Task<bool> RunBenchmarkAsync(BenchmarkModelConfig model, string[]? promptIds = null);
        Task<bool> StopBenchmarkAsync();
        Task<BenchmarkStatusDto> GetBenchmarkStatusAsync();
        Task<List<BenchmarkSession>> GetBenchmarkHistoryAsync(string? category = null);
        Task<List<BenchmarkLeaderboardEntry>> GetBenchmarkLeaderboardAsync(string? category = null);
        Task<bool> DeleteBenchmarkSessionAsync(string sessionId);
        Task<bool> ClearBenchmarkHistoryAsync();

        // AI 调用性能指标
        Task<AiMetricsSummaryDto?> GetAiMetricsSummaryAsync(int days = 7, CancellationToken cancellationToken = default);
        Task<List<AiProviderMetricsDto>> GetAiProviderMetricsAsync(int days = 7, CancellationToken cancellationToken = default);
        Task<List<AiModelMetricsDto>> GetAiModelMetricsAsync(int days = 7, CancellationToken cancellationToken = default);
        Task<List<AiMetricsTrendDto>> GetAiMetricsTrendsAsync(int days = 7, CancellationToken cancellationToken = default);
        Task<List<AiUsageMetricDto>> GetAiRecentMetricsAsync(int limit = 50, int days = 7, CancellationToken cancellationToken = default);

        // 场景管理
        Task SetSceneAsync(Baihua.Contracts.Scene.AppScene scene, CancellationToken cancellationToken = default);

        // 虚拟师父
        Task<CreateMasterResponse> CreateMasterAsync(string goal, string industry, CancellationToken cancellationToken = default);
        IAsyncEnumerable<ChatStreamEvent> StreamMasterChatAsync(string masterId, string message, string stage, List<(bool IsUser, string Content)>? history = null, CancellationToken cancellationToken = default);
        Task<StageCompleteResponse> MasterStageCompleteAsync(string masterId, string stageName, CancellationToken cancellationToken = default);
        Task<ApprenticeProfileResponse> GetMasterProfileAsync(string masterId, CancellationToken cancellationToken = default);
        Task<AssessResponse> MasterAssessAsync(string masterId, string type = "capability", CancellationToken cancellationToken = default);
        Task<List<MasterListItem>> GetMastersAsync(CancellationToken cancellationToken = default);
        Task<bool> DeleteMasterAsync(string masterId, CancellationToken cancellationToken = default);
        Task<MasterEvictResponse> EvictMasterAsync(string masterId, CancellationToken cancellationToken = default);
        Task<ApprenticeProfileResponse> UpdateMasterProfileAsync(string masterId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
        Task<VaultFocusListResponse> GetVaultFocusAsync(string masterId, CancellationToken cancellationToken = default);
        Task<VaultFocusUpdateResponse> UpdateVaultFocusAsync(string masterId, VaultFocusUpdateRequest request, CancellationToken cancellationToken = default);
        Task<VaultFocusUpdateResponse> RemoveVaultFocusAsync(string masterId, string vaultId, CancellationToken cancellationToken = default);
    }

    public class ApiService : IApiService

    {
        /// <summary>任务列表、提供方、删除等；后台不可达时尽快失败，避免整页卡住。</summary>
        private static readonly TimeSpan QuickCallTimeout = TimeSpan.FromSeconds(15);

        /// <summary>拆笔记、AI 等长请求，仅用 HttpClient 全局超时。</summary>
        private static readonly TimeSpan LongHttpTimeout = TimeSpan.FromMinutes(5);
        private static readonly JsonSerializerOptions _caseInsensitiveJsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly HttpClient _httpClient;
        private readonly HttpClient _aiHttpClient;
        private readonly HttpClient _vaultHttpClient;
        private readonly SettingsService _settingsService;
        private readonly ILogger<ApiService> _logger;
        private readonly IStringLocalizer<Baihua.Web.Localization.SharedResources> _loc;
        private readonly ApiCallMetricsService? _metricsService;
        private readonly EndToEndPerformanceService? _e2eService;
        private string? _currentTraceId;
        private readonly string _fallbackBaseUrl = "http://127.0.0.1:8788";

        public ApiService(IHttpClientFactory httpClientFactory, SettingsService settingsService, ILogger<ApiService> logger, IStringLocalizer<Baihua.Web.Localization.SharedResources> loc, IServiceProvider serviceProvider)
        {
            _settingsService = settingsService;
            _logger = logger;
            _loc = loc;
            _httpClient = httpClientFactory.CreateClient("TaskRunnerApi");
            _aiHttpClient = httpClientFactory.CreateClient("TaskRunnerAiApi");
            _vaultHttpClient = httpClientFactory.CreateClient("TaskRunnerVaultApi");
            
            // 延迟获取服务避免循环依赖
            _metricsService = serviceProvider.GetService<ApiCallMetricsService>();
            _e2eService = serviceProvider.GetService<EndToEndPerformanceService>();
            
            EnsurePrimaryBaseAddress();
        }
        
        public async Task<SystemHealthReportDto> GetFullHealthAsync(CancellationToken cancellationToken = default)
        {
            // 完整健康报告在后端有 25s 预算；这里加一个额外超时上限避免前端长时间等待。
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            var response = await GetWithFallbackAsync("/api/health/full", linked.Token);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<SystemHealthReportDto>(linked.Token)
                   ?? new SystemHealthReportDto();
        }

        public async Task CheckHealthFastAsync(CancellationToken cancellationToken = default)
        {
            // 快速健康检查：3秒超时，后台不可用时抛出异常
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            var response = await GetWithFallbackAsync("/api/health/simple", linked.Token);
            response.EnsureSuccessStatusCode();
        }

        public async Task<HealthFixResultDto> FixHealthIssuesAsync(CancellationToken cancellationToken = default)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            var response = await _httpClient.PostAsync("/api/health/fix", null, linked.Token);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<HealthFixResultDto>(linked.Token)
                   ?? new HealthFixResultDto();
        }

        public async Task<JsonElement> SetupOpenClawAsync(CancellationToken cancellationToken = default)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            var response = await _httpClient.PostAsync("/api/health/setup-openclaw", null, linked.Token);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(linked.Token);
            return JsonSerializer.Deserialize<JsonElement>(json);
        }

        private string GetPrimaryBaseUrl()
        {
            // Docker 部署：compose 通过环境变量注入 TaskRunnerApi__BaseUrl（服务名 DNS），
            // 必须优先于 settings 文件，否则容器内会回退到 127.0.0.1 导致 connection refused
            var envBase = Environment.GetEnvironmentVariable("TaskRunnerApi__BaseUrl");
            if (!string.IsNullOrWhiteSpace(envBase))
            {
                return TaskRunnerEndpointHelper.NormalizeOutboundBaseUrl(envBase, _fallbackBaseUrl);
            }
            return TaskRunnerEndpointHelper.NormalizeOutboundBaseUrl(_settingsService.BackendUrl, _fallbackBaseUrl);
        }

        private void EnsurePrimaryBaseAddress()
        {
            var current = GetPrimaryBaseUrl();
            if (_httpClient.BaseAddress == null || _httpClient.BaseAddress.ToString().TrimEnd('/') != current)
            {
                _httpClient.BaseAddress = new Uri(current);
            }
        }

        private static bool ShouldFallback(System.Net.HttpStatusCode code)
        {
            return code == System.Net.HttpStatusCode.MethodNotAllowed ||
                   code == System.Net.HttpStatusCode.NotFound;
        }

        private async Task<HttpResponseMessage> GetWithFallbackAsync(string path, CancellationToken cancellationToken = default)
        {
            EnsurePrimaryBaseAddress();
            var primaryBaseUrl = GetPrimaryBaseUrl();
            var response = await _httpClient.GetAsync(path, cancellationToken);
            if (!ShouldFallback(response.StatusCode) || primaryBaseUrl == _fallbackBaseUrl)
            {
                return response;
            }

            response.Dispose();
            using var fallbackClient = new HttpClient();
            fallbackClient.BaseAddress = new Uri(_fallbackBaseUrl);
            fallbackClient.Timeout = LongHttpTimeout;
            return await fallbackClient.GetAsync(path, cancellationToken);
        }

        private async Task<HttpResponseMessage> PostWithFallbackAsync(string path, HttpContent? body, CancellationToken cancellationToken = default)
        {
            EnsurePrimaryBaseAddress();
            var primaryBaseUrl = GetPrimaryBaseUrl();
            var response = await _httpClient.PostAsync(path, body, cancellationToken);
            if (!ShouldFallback(response.StatusCode) || primaryBaseUrl == _fallbackBaseUrl)
            {
                return response;
            }

            response.Dispose();
            using var fallbackClient = new HttpClient();
            fallbackClient.BaseAddress = new Uri(_fallbackBaseUrl);
            fallbackClient.Timeout = LongHttpTimeout;
            if (body == null)
            {
                return await fallbackClient.PostAsync(path, null);
            }
            var fallbackBody = new StringContent(await body.ReadAsStringAsync(), Encoding.UTF8, "application/json");
            return await fallbackClient.PostAsync(path, fallbackBody);
        }

        public async Task<List<TaskInfo>> GetTasksAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync("/api/tasks", quick.Token);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<TasksResponse>(quick.Token);
                return result?.Tasks ?? new List<TaskInfo>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取任务列表失败，URL: {Url}, 超时: {Timeout}s", 
                    GetPrimaryBaseUrl(), QuickCallTimeout.TotalSeconds);
                return new List<TaskInfo>();
            }
        }

        public async Task<TaskInfo?> GetTaskAsync(string taskId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync($"/api/tasks/{taskId}", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<TaskInfo>(quick.Token);
            }
            catch (Exception ex) 
            { 
                _logger.LogError(ex, "获取任务详情失败，TaskId: {TaskId}", taskId); 
                return null; 
            }
        }

        public async Task<OnboardingStatusDto> GetOnboardingStatusAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync("/api/onboarding/status", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<OnboardingStatusDto>(quick.Token) ?? new OnboardingStatusDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 Onboarding 状态失败");
                return new OnboardingStatusDto();
            }
        }

        public async Task<bool> CompleteOnboardingAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await PostWithMetricsAsync("/api/onboarding/complete", null, quick.Token);
                response.EnsureSuccessStatusCode();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "完成 Onboarding 失败");
                return false;
            }
        }

        public async Task<VaultGenerationResponse> CreateVaultGenerationTaskAsync(string industry, string keyword, string? model = null, int noteCount = 30, bool generateCards = false)
        {
            try
            {
                var body = new Dictionary<string, object?>
                {
                    ["industry"] = industry,
                    ["keyword"] = keyword,
                    ["noteCount"] = noteCount,
                    ["generateCards"] = generateCards
                };
                if (!string.IsNullOrWhiteSpace(model))
                    body["model"] = model;
                var json = JsonSerializer.Serialize(body);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/tasks/vault-generation", httpContent);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<VaultGenerationResponse>() ?? new VaultGenerationResponse { Success = false, Message = _loc["Api_TaskCreateFailed"] };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建知识库生成任务失败，行业: {Industry}, 关键词: {Keyword}", industry, keyword);
                return new VaultGenerationResponse { Success = false, Message = _loc["Api_CreateFailedWithError", ex.Message] };
            }
        }

        private static string? GetToolIdFromProviderUrl(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;
            var lower = baseUrl.ToLowerInvariant();
            if (lower.Contains("localhost:11434") || lower.Contains("127.0.0.1:11434")) return "ollama";
            if (lower.Contains("localhost:1234") || lower.Contains("127.0.0.1:1234")) return "lmstudio";
            return null;
        }

        private static string? GetToolIdFromProviderId(string? providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return null;
            var lower = providerId.ToLowerInvariant();
            if (lower == "ollama") return "ollama";
            if (lower == "lmstudio") return "lmstudio";
            return null;
        }

        private static void MergeModels(List<AiModelInfo> existingModels, List<string> availableModels)
        {
            var existingSet = new HashSet<string>(existingModels.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var modelName in availableModels)
            {
                if (!existingSet.Contains(modelName))
                {
                    existingModels.Add(new AiModelInfo { Name = modelName, IsPaid = false, IsMain = false });
                }
            }
        }

        private static void MergeModels(List<AiConfigModel> existingModels, List<string> availableModels)
        {
            var existingSet = new HashSet<string>(existingModels.Select(m => m.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var modelName in availableModels)
            {
                if (!existingSet.Contains(modelName))
                {
                    existingModels.Add(new AiConfigModel { Name = modelName, IsPaid = false, IsMain = false });
                }
            }
        }

        public async Task<List<AiProviderInfo>> GetAiProvidersAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync("/api/ai/providers", quick.Token);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<List<AiProviderInfo>>(quick.Token);
                if (result != null)
                {
                    foreach (var provider in result)
                    {
                        var toolId = GetToolIdFromProviderId(provider.Id);
                        if (toolId != null)
                        {
                            try
                            {
                                using var localCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                                var availableModels = await GetAvailableModelsAsync(toolId, localCts.Token);
                                MergeModels(provider.Models, availableModels);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "动态获取本地模型列表失败，ProviderId: {ProviderId}", provider.Id);
                            }
                        }
                    }
                }
                return result ?? new List<AiProviderInfo>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 AI 提供方列表失败");
                return new List<AiProviderInfo>();
            }
        }

        public async Task<SearchResponse> SearchAsync(string query, string vaultId)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _vaultHttpClient.GetAsync($"/api/search?q={Uri.EscapeDataString(query)}&vaultId={Uri.EscapeDataString(vaultId)}", cts.Token);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<SearchResponse>(cts.Token);
                return result ?? new SearchResponse();
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("搜索超时，查询: {Query}", query);
                return new SearchResponse { Status = new SearchStatusInfo { ErrorMessage = _loc["Api_SearchTimeout"] } }; 
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜索失败，查询: {Query}", query);
                return new SearchResponse { Status = new SearchStatusInfo { ErrorMessage = _loc["Api_SearchUnavailable"] } }; 
            }
        }

        public async Task<IndexStatusDto> GetIndexStatusAsync(string vaultId)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await _vaultHttpClient.GetAsync($"/api/search/index-status?vaultId={Uri.EscapeDataString(vaultId)}", cts.Token);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<IndexStatusDto>(cts.Token);
                return result ?? new IndexStatusDto();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "获取索引状态失败");
                return new IndexStatusDto();
            }
        }

        public async Task<bool> RebuildIndexAsync(string vaultId, CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(300));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
                var body = new { vaultId };
                var response = await _vaultHttpClient.PostAsJsonAsync("/api/search/reindex", body, linked.Token);
                return response.IsSuccessStatusCode;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("重建索引被取消");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "重建索引失败");
                return false;
            }
        }

        public async Task<AiNoteResponse> AskAIAsync(string query, bool saveToVault)
        {
            try
            {
                var body = new Dictionary<string, object?>
                {
                    ["query"] = query,
                    ["saveToVault"] = saveToVault,
                };
                var json = JsonSerializer.Serialize(body);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/ai/ask", httpContent);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<AiNoteResponse>() ?? new AiNoteResponse { Success = false, Message = "AI 查询失败" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI 查询失败，问题: {Query}", query);
                return new AiNoteResponse { Success = false, Message = $"查询失败：{ex.Message}" };
            }
        }

        public async Task<AiTaskResponse> CreateAiTaskAsync(string query, bool saveToVault, string vaultId, string? model = null, bool autoSplit = false, string? systemPrompt = null, string? industry = null)
        {
            try
            {
                var body = new Dictionary<string, object?>
                {
                    ["query"] = query,
                    ["saveToVault"] = saveToVault,
                    ["vaultId"] = vaultId,
                    ["autoSplit"] = autoSplit,
                };
                if (!string.IsNullOrWhiteSpace(model))
                    body["model"] = model;
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                    body["systemPrompt"] = systemPrompt;
                if (!string.IsNullOrWhiteSpace(industry))
                    body["industry"] = industry;
                var json = JsonSerializer.Serialize(body);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/tasks/ai-query", httpContent);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<AiTaskResponse>() ?? new AiTaskResponse { Success = false, Message = _loc["Api_TaskCreateFailed"] };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建 AI 任务失败，问题: {Query}, 模型: {Model}", query, model ?? "默认");
                return new AiTaskResponse { Success = false, Message = _loc["Api_CreateFailedWithError", ex.Message] };
            }
        }





        public string GetBackendBaseUrl() => GetPrimaryBaseUrl();

        public async IAsyncEnumerable<string> StreamLocalChatAsync(
            string message,
            string modelPath,
            string modelType,
            List<(bool IsUser, string Content)>? history = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["message"] = message,
                ["modelPath"] = modelPath,
                ["modelType"] = modelType
            };
            if (history != null && history.Count > 0)
                payload["history"] = history.Select(h => new { role = h.IsUser ? "user" : "assistant", content = h.Content }).ToList();

            var json = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _aiHttpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "/api/local-ai/chat/stream") { Content = httpContent },
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? currentEvent = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) break;

                if (line.StartsWith("event: "))
                {
                    currentEvent = line.Substring(7).Trim();
                }
                else if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6);
                    if (currentEvent == "delta")
                    {
                        var text = TryExtractContent(data);
                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return text;
                        }
                    }
                    else if (currentEvent == "done")
                    {
                        yield break;
                    }
                    else if (currentEvent == "error")
                    {
                        throw new InvalidOperationException(_loc["Api_LocalStreamError", data!]);
                    }
                }
                else if (string.IsNullOrEmpty(line))
                {
                    currentEvent = null;
                }
            }
        }

        public async Task<List<LocalModelInfo>> ScanLocalModelsAsync(string? directory = null)
        {
            try
            {
                var url = "/api/local-ai/scan";
                if (!string.IsNullOrWhiteSpace(directory))
                    url += $"?directory={Uri.EscapeDataString(directory)}";

                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await _aiHttpClient.GetAsync(url, quick.Token);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<List<LocalModelInfo>>(quick.Token);
                return result ?? new List<LocalModelInfo>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扫描本地模型失败");
                return new List<LocalModelInfo>();
            }
        }

        // ========== 本地视觉识别（Qwen2.5-VL + OpenVINO）==========

        public async Task<VisionStatusDto> GetVisionStatusAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await _aiHttpClient.GetAsync("/api/local-ai/vision/status", linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<VisionStatusDto>(linked.Token)
                       ?? new VisionStatusDto { Enabled = false };
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "获取本地视觉状态失败");
                return new VisionStatusDto { Enabled = false, Message = ex.Message };
            }
        }

        public async Task<VisionStatusDto> StartVisionServerAsync(CancellationToken cancellationToken = default)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(100));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
            var response = await _aiHttpClient.PostAsync("/api/local-ai/vision/start", null, linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadFromJsonAsync<VisionStatusDto>(linked.Token);
                return err ?? new VisionStatusDto { Enabled = false, ServerRunning = false };
            }
            return await response.Content.ReadFromJsonAsync<VisionStatusDto>(linked.Token)
                   ?? new VisionStatusDto();
        }

        public async Task<VisionResultDto> RecognizeImageAsync(
            byte[] imageBytes, string prompt, string model, CancellationToken cancellationToken = default)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
            var request = new VisionRequestDto
            {
                ImageBase64 = Convert.ToBase64String(imageBytes),
                Prompt = prompt,
                Model = model,
            };
            var response = await _aiHttpClient.PostAsJsonAsync("/api/local-ai/vision", request, linked.Token);
            var result = await response.Content.ReadFromJsonAsync<VisionResultDto>(linked.Token)
                         ?? new VisionResultDto { Text = "识别失败（无响应）" };
            if (!response.IsSuccessStatusCode && string.IsNullOrEmpty(result.Text))
            {
                result.Text = $"识别失败（HTTP {(int)response.StatusCode}）";
            }
            return result;
        }

        public async Task<ChatResponse> ChatAsync(string message, CancellationToken cancellationToken = default)
        {
            try
            {
                var request = new { message = message };
                var json = JsonSerializer.Serialize(request);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/ai/chat", httpContent, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: cancellationToken) ?? new ChatResponse();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "聊天请求失败");
                return new ChatResponse { Success = false, Message = $"聊天失败：{ex.Message}" };
            }
        }

        public async IAsyncEnumerable<string> StreamChatAsync(
            string message,
            string providerId,
            string model,
            List<(bool IsUser, string Content)>? history = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["message"] = message
            };
            if (!string.IsNullOrWhiteSpace(providerId))
                payload["providerId"] = providerId;
            if (!string.IsNullOrWhiteSpace(model))
                payload["model"] = model;
            if (history != null && history.Count > 0)
                payload["history"] = history.Select(h => new { role = h.IsUser ? "user" : "assistant", content = h.Content }).ToList();

            var json = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "/api/ai/chat/stream") { Content = httpContent },
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? currentEvent = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) break;

                if (line.StartsWith("event: "))
                {
                    currentEvent = line.Substring(7).Trim();
                }
                else if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6);
                    if (currentEvent == "delta")
                    {
                        var text = TryExtractContent(data);
                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return text;
                        }
                    }
                    else if (currentEvent == "done")
                    {
                        yield break;
                    }
                    else if (currentEvent == "error")
                    {
                        throw new InvalidOperationException(_loc["Api_StreamError", data!]);
                    }
                }
                else if (string.IsNullOrEmpty(line))
                {
                    currentEvent = null;
                }
            }
        }

        public async IAsyncEnumerable<ChatStreamEvent> StreamChatWithEventsAsync(
            string message,
            string providerId,
            string model,
            List<(bool IsUser, string Content)>? history = null,
            string? sessionId = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["message"] = message
            };
            if (!string.IsNullOrWhiteSpace(providerId))
                payload["providerId"] = providerId;
            if (!string.IsNullOrWhiteSpace(model))
                payload["model"] = model;
            if (!string.IsNullOrWhiteSpace(sessionId))
                payload["sessionId"] = sessionId;
            if (history != null && history.Count > 0)
                payload["history"] = history.Select(h => new { role = h.IsUser ? "user" : "assistant", content = h.Content }).ToList();

            var json = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "/api/ai/chat/stream") { Content = httpContent },
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? currentEvent = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) break;

                if (line.StartsWith("event: "))
                {
                    currentEvent = line.Substring(7).Trim();
                }
                else if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6);
                    if (currentEvent == "delta")
                    {
                        var text = TryExtractContent(data);
                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return new ChatStreamEvent { Type = "delta", Content = text };
                        }
                    }
                    else if (currentEvent == "tool_call")
                    {
                        var toolEvent = ParseToolCallEvent(data);
                        if (toolEvent != null)
                            yield return toolEvent;
                    }
                    else if (currentEvent == "done")
                    {
                        yield return new ChatStreamEvent { Type = "done" };
                        yield break;
                    }
                    else if (currentEvent == "error")
                    {
                        throw new InvalidOperationException(_loc["Api_StreamError", data!]);
                    }
                }
                else if (string.IsNullOrEmpty(line))
                {
                    currentEvent = null;
                }
            }
        }

        public async Task<ChatResponse> ChatDirectAsync(string message, string? providerId = null, string? model = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var payload = new Dictionary<string, object?> { ["message"] = message };
                if (!string.IsNullOrWhiteSpace(providerId)) payload["providerId"] = providerId;
                if (!string.IsNullOrWhiteSpace(model)) payload["model"] = model;

                var json = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _aiHttpClient.PostAsync("/api/ai/chat/completion", httpContent, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: cancellationToken) ?? new ChatResponse();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "直接 AI 聊天请求失败");
                return new ChatResponse { Success = false, Message = $"聊天失败：{ex.Message}" };
            }
        }

        public async IAsyncEnumerable<string> StreamChatDirectAsync(
            string message,
            string? providerId = null,
            string? model = null,
            List<(bool IsUser, string Content)>? history = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?> { ["message"] = message };
            if (!string.IsNullOrWhiteSpace(providerId)) payload["providerId"] = providerId;
            if (!string.IsNullOrWhiteSpace(model)) payload["model"] = model;
            if (history != null && history.Count > 0)
                payload["history"] = history.Select(h => new { role = h.IsUser ? "user" : "assistant", content = h.Content }).ToList();

            var json = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _aiHttpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "/api/ai/chat/stream") { Content = httpContent },
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? currentEvent = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) break;

                if (line.StartsWith("event: "))
                {
                    currentEvent = line.Substring(7).Trim();
                }
                else if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6);
                    if (currentEvent == "delta")
                    {
                        var text = TryExtractContent(data);
                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return text;
                        }
                    }
                    else if (currentEvent == "done")
                    {
                        yield break;
                    }
                    else if (currentEvent == "error")
                    {
                        throw new InvalidOperationException(_loc["Api_StreamError", data!]);
                    }
                }
                else if (string.IsNullOrEmpty(line))
                {
                    currentEvent = null;
                }
            }
        }

        private static ChatStreamEvent? ParseToolCallEvent(string data)
        {
            try
            {
                using var doc = JsonDocument.Parse(data);
                var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() : null;
                var args = new Dictionary<string, object?>();
                if (doc.RootElement.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in a.EnumerateObject())
                    {
                        args[prop.Name] = prop.Value.ToString();
                    }
                }
                return new ChatStreamEvent { Type = "tool_call", ToolName = name, ToolArguments = args };
            }
            catch { }
            return null;
        }

        private static string? TryExtractContent(string data)
        {
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("content", out var contentProp) &&
                    contentProp.ValueKind == JsonValueKind.String)
                {
                    return contentProp.GetString();
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 使用 OpenAI 兼容端点进行流式聊天，自动支持知识库检索增强
        /// </summary>
        public async IAsyncEnumerable<string> StreamChatWithVaultAsync(
            string message,
            string model,
            List<(bool IsUser, string Content)>? history = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var msgList = new List<object>();
            if (history != null)
                msgList.AddRange(history.Select(h => new { role = h.IsUser ? "user" : "assistant", content = h.Content }));
            msgList.Add(new { role = "user", content = message });

            var payload = new
            {
                model = string.IsNullOrWhiteSpace(model) ? "ollama/biancang:latest" : model,
                messages = msgList,
                stream = true
            };

            var json = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "/api/chat/completions") { Content = httpContent },
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) break;
                if (!line.StartsWith("data: ")) continue;

                var data = line.Substring(6).Trim();
                if (data == "[DONE]") yield break;

                var text = TryExtractOpenAiDelta(data);
                if (!string.IsNullOrEmpty(text))
                {
                    yield return text;
                }
            }
        }

        private static string? TryExtractOpenAiDelta(string data)
        {
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    if (first.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var contentProp) &&
                        contentProp.ValueKind == JsonValueKind.String)
                    {
                        return contentProp.GetString();
                    }
                }
            }
            catch { }
            return null;
        }

        public async Task<bool> DeleteTaskAsync(string taskId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await DeleteWithMetricsAsync($"/api/tasks/{taskId}", quick.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除任务失败，TaskId: {TaskId}", taskId);
                return false;
            }
        }

        public async Task<bool> DeleteAllTasksAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await DeleteWithMetricsAsync("/api/tasks/all", quick.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清空所有任务失败");
                return false;
            }
        }

        public async Task<bool> CancelTaskAsync(string taskId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var content = new StringContent("", Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync($"/api/tasks/{taskId}/cancel", content, quick.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消任务失败，TaskId: {TaskId}", taskId);
                return false;
            }
        }

        public async Task<AiTaskResponse> RetryAiTaskAsync(string taskId, int timeoutMinutes = 0, string? model = null)
        {
            try
            {
                var body = new Dictionary<string, object?>
                {
                    ["timeoutMinutes"] = timeoutMinutes,
                    ["model"] = model
                };
                var json = JsonSerializer.Serialize(body);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync($"/api/tasks/{taskId}/retry", httpContent);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<AiTaskResponse>() ?? new AiTaskResponse { Success = false, Message = _loc["Api_RetryFailed"] };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重试任务失败，TaskId: {TaskId}", taskId);
                return new AiTaskResponse { Success = false, Message = _loc["Api_RetryFailedWithError", ex.Message] };
            }
        }

        public async Task<VaultNoteResponse?> ReadVaultNoteAsync(string path, string vaultId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var escaped = EscapeVaultPath(path);
                var response = await _vaultHttpClient.GetAsync($"/vault/read/{escaped}?vaultId={Uri.EscapeDataString(vaultId)}", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<VaultNoteResponse>(quick.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取笔记失败，路径: {Path}", path);
                return null;
            }
        }

        public async Task<VaultBrowseResponse?> GetVaultBrowseAsync(string vaultId, string? path = null)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var query = string.IsNullOrEmpty(path) ? "" : $"?path={Uri.EscapeDataString(path)}";
                var response = await _vaultHttpClient.GetAsync($"/api/vaults/{vaultId}/browse{query}", quick.Token);
                if (!response.IsSuccessStatusCode)
                    return null;
                return await response.Content.ReadFromJsonAsync<VaultBrowseResponse>(quick.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "浏览知识库失败，vaultId: {VaultId}", vaultId);
                return null;
            }
        }

        public async Task<VaultNotesBatchResponse?> GetVaultNotesBatchAsync(string vaultId)
        {
            try
            {
                using var cts = new CancellationTokenSource(LongHttpTimeout);
                var response = await _vaultHttpClient.GetAsync($"/api/vaults/{vaultId}/notes-batch", cts.Token);
                if (!response.IsSuccessStatusCode)
                    return null;
                return await response.Content.ReadFromJsonAsync<VaultNotesBatchResponse>(_caseInsensitiveJsonOptions, cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量获取笔记失败，vaultId: {VaultId}", vaultId);
                return null;
            }
        }

        public async Task<bool> WriteVaultNoteAsync(string path, string content, string vaultId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var escaped = EscapeVaultPath(path);
                var body = new { content = content };
                var json = JsonSerializer.Serialize(body);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _vaultHttpClient.PostAsync($"/vault/write/{escaped}?vaultId={Uri.EscapeDataString(vaultId)}", httpContent, quick.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写入笔记失败，路径: {Path}", path);
                return false;
            }
        }

        public async Task<GenerateMissingNoteResponse?> GenerateMissingNoteAsync(string linkPath, string vaultId)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var body = new { linkPath, vaultId };
                var json = JsonSerializer.Serialize(body);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/ai/generate-missing-note", httpContent, cts.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<GenerateMissingNoteResponse>(cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成缺失笔记失败，链接: {LinkPath}", linkPath);
                return new GenerateMissingNoteResponse { Success = false, Message = _loc["Api_RequestFailedWithError", ex.Message] };
            }
        }

        public async Task<string?> GetVaultRootAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await _vaultHttpClient.GetAsync("/api/settings/vault-root", quick.Token);
                if (!response.IsSuccessStatusCode)
                    return null;
                var payload = await response.Content.ReadFromJsonAsync<VaultRootResponse>(quick.Token);
                return payload?.VaultPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 Vault 根目录失败");
                return null;
            }
        }

        public async Task<bool> SetVaultRootAsync(string vaultPath)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var payload = new { vaultPath = vaultPath };
                var json = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _vaultHttpClient.PostAsync("/api/settings/vault-root", httpContent, quick.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置 Vault 根目录失败，路径: {Path}", vaultPath);
                return false;
            }
        }

        private static string EscapeVaultPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            var normalized = path.Trim().Replace("\\", "/").Trim('/');
            return string.Join("/", normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));
        }

        #region Metrics Tracking

        /// <summary>
        /// 记录 API 调用到端到端追踪（指标由 MetricsRecordingHandler 自动记录）
        /// </summary>
        private void RecordApiCall(string endpoint, string method, long elapsedMs, bool success, int? statusCode = null, string? error = null)
        {
            // 记录到端到端追踪（如果有活跃的 trace）
            if (_e2eService != null && _currentTraceId != null)
            {
                _e2eService.RecordApiCallEnd(_currentTraceId, endpoint, elapsedMs, success);
            }
        }
        
        /// <summary>
        /// 包装 GET 请求并记录指标
        /// </summary>
        private async Task<HttpResponseMessage> GetWithMetricsAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var response = await GetWithFallbackAsync(endpoint, cancellationToken);
                stopwatch.Stop();
                RecordApiCall(endpoint, "GET", stopwatch.ElapsedMilliseconds, response.IsSuccessStatusCode, (int)response.StatusCode);
                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                RecordApiCall(endpoint, "GET", stopwatch.ElapsedMilliseconds, false, null, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// 包装 POST 请求并记录指标
        /// </summary>
        private async Task<HttpResponseMessage> PostWithMetricsAsync(string endpoint, HttpContent? content, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var response = await PostWithFallbackAsync(endpoint, content, cancellationToken);
                stopwatch.Stop();
                RecordApiCall(endpoint, "POST", stopwatch.ElapsedMilliseconds, response.IsSuccessStatusCode, (int)response.StatusCode);
                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                RecordApiCall(endpoint, "POST", stopwatch.ElapsedMilliseconds, false, null, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// 包装 DELETE 请求并记录指标
        /// </summary>
        private async Task<HttpResponseMessage> DeleteWithMetricsAsync(string endpoint, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var response = await _httpClient.DeleteAsync(endpoint, cancellationToken);
                stopwatch.Stop();
                RecordApiCall(endpoint, "DELETE", stopwatch.ElapsedMilliseconds, response.IsSuccessStatusCode, (int)response.StatusCode);
                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                RecordApiCall(endpoint, "DELETE", stopwatch.ElapsedMilliseconds, false, null, ex.Message);
                throw;
            }
        }

        #endregion

        // AI 配置管理 API
        public async Task<List<AiConfigProvider>> GetAiConfigProvidersAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await _aiHttpClient.GetAsync("/api/ai/config/providers", quick.Token);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<List<AiConfigProvider>>(quick.Token);
                if (result != null)
                {
                    foreach (var provider in result)
                    {
                        var toolId = GetToolIdFromProviderUrl(provider.BaseUrl);
                        if (toolId != null)
                        {
                            try
                            {
                                using var localCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                                var availableModels = await GetAvailableModelsAsync(toolId, localCts.Token);
                                MergeModels(provider.Models, availableModels);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "动态获取本地模型列表失败，ProviderId: {ProviderId}", provider.Id);
                            }
                        }
                    }
                }
                return result ?? new List<AiConfigProvider>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 AI 配置列表失败");
                return new List<AiConfigProvider>();
            }
        }

        public async Task<AiConfigProvider?> GetAiConfigProviderAsync(string providerId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await _aiHttpClient.GetAsync($"/api/ai/config/providers/{Uri.EscapeDataString(providerId)}", quick.Token);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<AiConfigProvider>(quick.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 AI 配置详情失败，ProviderId: {ProviderId}", providerId);
                return null;
            }
        }

        public async Task<SaveAiProviderResult> SaveAiConfigProviderAsync(SaveAiProviderRequest request)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var json = JsonSerializer.Serialize(request);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _aiHttpClient.PostAsync("/api/ai/config/providers", httpContent, quick.Token);
                
                if (response.IsSuccessStatusCode)
                {
                    return new SaveAiProviderResult { Success = true, Message = _loc["Api_SaveSuccess"] };
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return new SaveAiProviderResult 
                    { 
                        Success = false, 
                        Message = _loc["Api_SaveFailedWithError", error!]
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存 AI 配置失败，ProviderId: {ProviderId}", request.Id);
                return new SaveAiProviderResult
                {
                    Success = false,
                    Message = _loc["Api_SaveFailedWithError", ex.Message]
                };
            }
        }

        public async Task<bool> DeleteAiConfigProviderAsync(string providerId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await _aiHttpClient.DeleteAsync($"/api/ai/config/providers/{Uri.EscapeDataString(providerId)}", quick.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除 AI 配置失败，ProviderId: {ProviderId}", providerId);
                return false;
            }
        }

        public async Task<EnvConfigHelp?> GetAiEnvConfigHelpAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await _aiHttpClient.GetAsync("/api/ai/config/env-help", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<EnvConfigHelp>(quick.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取环境变量配置帮助失败");
                return null;
            }
        }

        public async Task<List<AiProviderPreset>> GetAiProviderPresetsAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await _aiHttpClient.GetAsync("/api/ai/config/presets", quick.Token);
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<List<AiProviderPreset>>(quick.Token);
                return result ?? new List<AiProviderPreset>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 AI 提供商预设失败");
                return new List<AiProviderPreset>();
            }
        }

        public async Task<EmbeddingConfigDto> GetEmbeddingConfigAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await _aiHttpClient.GetAsync("/api/embedding/config", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<EmbeddingConfigDto>(quick.Token) ?? new EmbeddingConfigDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 Embedding 配置失败");
                return new EmbeddingConfigDto();
            }
        }

        public async Task<SaveAiProviderResult> SaveEmbeddingConfigAsync(SaveEmbeddingConfigRequest request)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await _aiHttpClient.PostAsync("/api/embedding/config", JsonContent.Create(request), quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<SaveAiProviderResult>(quick.Token)
                       ?? new SaveAiProviderResult { Success = false, Message = _loc["Api_UnknownError"] };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存 Embedding 配置失败");
                return new SaveAiProviderResult { Success = false, Message = ex.Message };
            }
        }

        public async Task<DailyCardResultDto> GetDailyCardAsync(string vaultId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync($"/api/anki/daily?vaultId={Uri.EscapeDataString(vaultId)}", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<DailyCardResultDto>(quick.Token) ?? new DailyCardResultDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取每日卡片失败");
                return new DailyCardResultDto { HasCard = false, Message = _loc["Api_FetchFailed"] };
            }
        }

        public async Task<(bool Success, DailyProgressDto? Progress)> SubmitDailyAnswerAsync(string vaultId, string cardId, string result)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var request = new DailyAnswerRequestDto { CardId = cardId, Result = result };
                var response = await PostWithMetricsAsync($"/api/anki/daily/answer?vaultId={Uri.EscapeDataString(vaultId)}", JsonContent.Create(request), quick.Token);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(quick.Token);
                var success = json.GetProperty("success").GetBoolean();
                var progress = json.TryGetProperty("progress", out var p) ? JsonSerializer.Deserialize<DailyProgressDto>(p.GetRawText()) : null;
                return (success, progress);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "提交每日卡片答案失败");
                return (false, null);
            }
        }

        public async Task<DailyProgressDto> GetDailyProgressAsync(string vaultId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync($"/api/anki/daily/progress?vaultId={Uri.EscapeDataString(vaultId)}", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<DailyProgressDto>(quick.Token) ?? new DailyProgressDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取每日进度失败");
                return new DailyProgressDto();
            }
        }

        public async Task<bool> SaveCustomCardAsync(string vaultId, CustomCardRequestDto request)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await PostWithMetricsAsync($"/api/anki/custom-card?vaultId={Uri.EscapeDataString(vaultId)}", JsonContent.Create(request), quick.Token);
                response.EnsureSuccessStatusCode();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存自定义卡片失败");
                return false;
            }
        }

        public async Task<int> GetAnkiCardCountAsync(string vaultId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync($"/api/anki/card-count?vaultId={Uri.EscapeDataString(vaultId)}", quick.Token);
                response.EnsureSuccessStatusCode();
                var count = await response.Content.ReadFromJsonAsync<int>(quick.Token);
                return count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取卡片数量失败");
                return 0;
            }
        }

        public async Task<BatchGenerateResultDto?> GenerateAnkiCardsBatchAsync(string vaultId, string directory, bool recursive = true)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var request = new BatchGenerateRequestDto { Directory = directory, Recursive = recursive };
                var response = await PostWithMetricsAsync($"/api/anki/generate-batch?vaultId={Uri.EscapeDataString(vaultId)}", JsonContent.Create(request), quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<BatchGenerateResultDto>(quick.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量生成记忆卡片失败");
                return null;
            }
        }

        public async Task<int> GetVaultCardCountAsync(string vaultId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync($"/api/anki/vault-card-count?vaultId={Uri.EscapeDataString(vaultId)}", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<int>(quick.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取知识库卡片数量失败");
                return 0;
            }
        }

        public async Task<int> GetVaultNoteCountAsync(string vaultId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await _vaultHttpClient.GetAsync($"/vault/note-count?vaultId={Uri.EscapeDataString(vaultId)}", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<int>(quick.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取知识库笔记数量失败");
                return 0;
            }
        }

        public async Task<AnkiSearchResult> SearchAnkiCardsAsync(string? query, string? vaultId, int limit = 100)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var url = $"/api/anki/search?limit={limit}";
                if (!string.IsNullOrWhiteSpace(query))
                    url += $"&q={Uri.EscapeDataString(query)}";
                if (!string.IsNullOrWhiteSpace(vaultId))
                    url += $"&vaultId={Uri.EscapeDataString(vaultId)}";
                var response = await GetWithMetricsAsync(url, quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<AnkiSearchResult>(quick.Token) ?? new AnkiSearchResult { Cards = new() };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜索 Anki 卡片失败");
                return new AnkiSearchResult { Cards = new() };
            }
        }

        public async Task<DeckListResult> GetAnkiDecksAsync(string? vaultId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var url = "/api/anki/decks";
                if (!string.IsNullOrWhiteSpace(vaultId))
                    url += $"?vaultId={Uri.EscapeDataString(vaultId)}";
                var response = await GetWithMetricsAsync(url, quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<DeckListResult>(quick.Token) ?? new DeckListResult { Decks = new() };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 Anki 牌组失败");
                return new DeckListResult { Decks = new() };
            }
        }

        public async Task<GenerateCardsTaskDto?> GenerateAllCardsAsync(string vaultId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync($"/api/anki/generate-all-ai?vaultId={Uri.EscapeDataString(vaultId)}", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<GenerateCardsTaskDto>(quick.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "为知识库生成全部记忆卡片失败");
                return null;
            }
        }

        public async Task<List<LearnerDto>> GetLearnersAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync("/api/achievements/learners", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<LearnerDto>>(quick.Token) ?? new List<LearnerDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取学习者失败");
                return new List<LearnerDto>();
            }
        }

        public async Task<LearnerDto> CreateLearnerAsync(CreateLearnerRequest request)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await PostWithMetricsAsync("/api/achievements/learners", JsonContent.Create(request), quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<LearnerDto>(quick.Token) ?? new LearnerDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建学习者失败");
                return new LearnerDto();
            }
        }

        public async Task<bool> SetDefaultLearnerAsync(int id)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await PostWithMetricsAsync($"/api/achievements/learners/{id}/default", null, quick.Token);
                response.EnsureSuccessStatusCode();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置默认学习者失败");
                return false;
            }
        }

        public async Task<bool> DeleteLearnerAsync(int id)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await _httpClient.DeleteAsync($"/api/achievements/learners/{id}", quick.Token);
                response.EnsureSuccessStatusCode();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除学习者失败");
                return false;
            }
        }

        public async Task<List<AchievementDto>> GetAchievementsAsync(int learnerId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync($"/api/achievements?learnerId={learnerId}", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<AchievementDto>>(quick.Token) ?? new List<AchievementDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取成就失败");
                return new List<AchievementDto>();
            }
        }

        public async Task<List<AchievementDto>> CheckAchievementsAsync(int learnerId)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await PostWithMetricsAsync($"/api/achievements/check?learnerId={learnerId}", null, quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<AchievementDto>>(quick.Token) ?? new List<AchievementDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查成就失败");
                return new List<AchievementDto>();
            }
        }

        // ===== FAM-31 家庭奖励 =====

        public async Task<List<RewardProgressDto>> GetRewardProgressAsync(string? vaultId = null)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var url = "/api/rewards/progress";
                if (!string.IsNullOrEmpty(vaultId))
                    url += $"?vaultId={Uri.EscapeDataString(vaultId)}";
                var response = await GetWithMetricsAsync(url, quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<RewardProgressDto>>(quick.Token) ?? new List<RewardProgressDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取奖励进度失败");
                return new List<RewardProgressDto>();
            }
        }

        public async Task<RewardConfigDto> CreateRewardAsync(CreateRewardRequest request)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await PostWithMetricsAsync("/api/rewards", JsonContent.Create(request), quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<RewardConfigDto>(quick.Token) ?? new RewardConfigDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建奖励失败");
                return new RewardConfigDto();
            }
        }

        public async Task<List<RewardClaimDto>> TriggerRewardsAsync(string? vaultId = null)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var url = "/api/rewards/trigger";
                if (!string.IsNullOrEmpty(vaultId))
                    url += $"?vaultId={Uri.EscapeDataString(vaultId)}";
                var response = await PostWithMetricsAsync(url, null, quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<RewardClaimDto>>(quick.Token) ?? new List<RewardClaimDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发奖励检查失败");
                return new List<RewardClaimDto>();
            }
        }

        // ===== FAM-30 亲子互考 =====

        public async Task<QuizSessionDto> CreateQuizAsync(CreateQuizRequest request)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await PostWithMetricsAsync("/api/quiz/create", JsonContent.Create(request), quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<QuizSessionDto>(quick.Token) ?? new QuizSessionDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建互考失败");
                throw;
            }
        }

        public async Task<QuizResultDto> SubmitQuizAnswerAsync(SubmitAnswerRequest request)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await PostWithMetricsAsync("/api/quiz/answer", JsonContent.Create(request), quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<QuizResultDto>(quick.Token) ?? new QuizResultDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "提交互考答案失败");
                throw;
            }
        }

        public async Task<List<LeaderboardEntryDto>> GetLeaderboardAsync(string type, string? vaultId = null)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var url = $"/api/achievements/leaderboard/{type}";
                if (!string.IsNullOrEmpty(vaultId))
                    url += $"?vaultId={Uri.EscapeDataString(vaultId)}";
                var response = await GetWithMetricsAsync(url, quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<LeaderboardEntryDto>>(quick.Token) ?? new List<LeaderboardEntryDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取赛舟榜失败");
                return new List<LeaderboardEntryDto>();
            }
        }

        public async Task<DashboardDataDto> GetDashboardAsync(string? vaultId = null, int? learnerId = null)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var url = "/api/achievements/dashboard";
                var query = new List<string>();
                if (!string.IsNullOrEmpty(vaultId))
                    query.Add($"vaultId={Uri.EscapeDataString(vaultId)}");
                if (learnerId.HasValue)
                    query.Add($"learnerId={learnerId.Value}");
                if (query.Count > 0)
                    url += "?" + string.Join("&", query);
                var response = await GetWithMetricsAsync(url, quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<DashboardDataDto>(quick.Token) ?? new DashboardDataDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取家长看板失败");
                return new DashboardDataDto();
            }
        }

        public async Task<CheckinDataDto> GetCheckinDataAsync(string? vaultId = null)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var url = "/api/checkin";
                if (!string.IsNullOrEmpty(vaultId))
                    url += $"?vaultId={Uri.EscapeDataString(vaultId)}";
                var response = await GetWithMetricsAsync(url, quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<CheckinDataDto>(quick.Token) ?? new CheckinDataDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取学习打卡数据失败");
                return new CheckinDataDto();
            }
        }

        public async Task<CheckinMakeupResultDto> MakeupCheckinAsync(CheckinMakeupRequest request)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await PostWithMetricsAsync("/api/checkin/makeup", JsonContent.Create(request), quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<CheckinMakeupResultDto>(quick.Token) ?? new CheckinMakeupResultDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "补签失败");
                return new CheckinMakeupResultDto { Success = false, Message = "补签失败" };
            }
        }

        public async Task<WeeklyCompareResultDto> GetWeeklyCompareAsync(string? vaultId = null, int? learnerId = null)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var url = "/api/achievements/leaderboard/compare";
                var query = new List<string>();
                if (!string.IsNullOrEmpty(vaultId)) query.Add($"vaultId={Uri.EscapeDataString(vaultId)}");
                if (learnerId.HasValue) query.Add($"learnerId={learnerId.Value}");
                if (query.Count > 0) url += "?" + string.Join("&", query);
                var response = await GetWithMetricsAsync(url, quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<WeeklyCompareResultDto>(quick.Token) ?? new WeeklyCompareResultDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取和自己比数据失败");
                return new WeeklyCompareResultDto();
            }
        }

        public async Task<List<LeaderboardEntryDto>> GetRoleLeaderboardAsync(string role, string? vaultId = null)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var url = $"/api/achievements/leaderboard/role?role={Uri.EscapeDataString(role)}";
                if (!string.IsNullOrEmpty(vaultId)) url += $"&vaultId={Uri.EscapeDataString(vaultId)}";
                var response = await GetWithMetricsAsync(url, quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<LeaderboardEntryDto>>(quick.Token) ?? new List<LeaderboardEntryDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取角色排行榜失败");
                return new List<LeaderboardEntryDto>();
            }
        }

        public async Task<LeaderboardSettingsDto> GetAllFamilyTabSettingAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync("/api/achievements/leaderboard/settings/all-family-tab", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<LeaderboardSettingsDto>(quick.Token) ?? new LeaderboardSettingsDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取全家排行设置失败");
                return new LeaderboardSettingsDto();
            }
        }

        public async Task<LeaderboardSettingsDto> SetAllFamilyTabSettingAsync(bool enabled)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var body = JsonSerializer.Serialize(new LeaderboardSettingsDto { AllFamilyTabEnabled = enabled });
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/achievements/leaderboard/settings/all-family-tab", content, quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<LeaderboardSettingsDto>(quick.Token) ?? new LeaderboardSettingsDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置全家排行失败");
                return new LeaderboardSettingsDto();
            }
        }

        public async Task<bool> OpenInObsidianAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, 
                    new CancellationTokenSource(QuickCallTimeout).Token);
                var content = new StringContent("", Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/obsidian/open-current-vault", content, linked.Token);
                return response.IsSuccessStatusCode;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 调用方取消，向上传播
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "在 Obsidian 中打开知识库失败");
                return false;
            }
        }

        public async Task<bool> OpenVaultInObsidianAsync(string path)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var payload = new { path = path };
                var json = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/obsidian/open", httpContent, quick.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "在 Obsidian 中打开知识库失败，路径: {Path}", path);
                return false;
            }
        }

        public async Task<NotesMdCliStatus?> GetNotesMdCliStatusAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithFallbackAsync("/api/notesmd-cli/status", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<NotesMdCliStatus>(quick.Token);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "获取 NotesMD CLI 状态失败");
                return null;
            }
        }

        public async Task<bool> AddVaultToNotesMdCliAsync(string path)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var payload = new { path };
                var json = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/notesmd-cli/add-vault", httpContent, quick.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "添加知识库到 NotesMD CLI 失败，路径: {Path}", path);
                return false;
            }
        }

        public async Task<NotesMdBatchResult?> BatchAddVaultsToNotesMdCliAsync(List<string> paths)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var payload = new { paths };
                var json = JsonSerializer.Serialize(payload);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/notesmd-cli/batch-add", httpContent, quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<NotesMdBatchResult>(quick.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量添加知识库到 NotesMD CLI 失败");
                return null;
            }
        }

        public async Task<PlatformInfoResponse?> GetPlatformAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await GetWithMetricsAsync("/api/health/os", linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<PlatformInfoResponse>(linked.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取平台信息失败");
                return null;
            }
        }

        // 请求指标统计 API
        public async Task<MetricsSummary?> GetMetricsSummaryAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await GetWithFallbackAsync("/api/metrics/summary", linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<MetricsSummary>(linked.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取请求指标摘要失败");
                return null;
            }
        }

        public async Task<List<RequestMetric>> GetSlowestRequestsAsync(int count = 10, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await GetWithFallbackAsync($"/api/metrics/slowest?count={count}", linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<RequestMetric>>(linked.Token) ?? new List<RequestMetric>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取慢请求列表失败");
                return new List<RequestMetric>();
            }
        }

        public async Task<List<PathFrequency>> GetFrequentPathsAsync(int count = 10, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await GetWithFallbackAsync($"/api/metrics/frequent?count={count}", linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<PathFrequency>>(linked.Token) ?? new List<PathFrequency>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取高频请求路径失败");
                return new List<PathFrequency>();
            }
        }

        public async Task<List<RequestMetric>> GetRecentErrorsAsync(int count = 10, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await GetWithFallbackAsync($"/api/metrics/errors?count={count}", linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<RequestMetric>>(linked.Token) ?? new List<RequestMetric>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最近错误请求失败");
                return new List<RequestMetric>();
            }
        }

        public async Task<bool> ClearMetricsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await _httpClient.PostAsync("/api/metrics/clear", null, linked.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清空请求指标失败");
                return false;
            }
        }

        #region 本地模型部署

        public async Task<HardwareInfoDto?> GetHardwareInfoAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var url = "/api/local-models/hardware" + (forceRefresh ? "?forceRefresh=true" : "");
                var response = await GetWithMetricsAsync(url, linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<HardwareInfoDto>(linked.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取硬件信息失败");
                return null;
            }
        }

        public async Task<List<RecommendedModelDto>> GetRecommendedModelsAsync(string? scenario = null, bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var query = new List<string>();
                if (!string.IsNullOrEmpty(scenario))
                    query.Add($"scenario={Uri.EscapeDataString(scenario)}");
                if (forceRefresh)
                    query.Add("forceRefresh=true");
                var url = "/api/local-models/recommend" + (query.Count > 0 ? "?" + string.Join("&", query) : "");
                var response = await GetWithMetricsAsync(url, linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<RecommendedModelDto>>(linked.Token) ?? new List<RecommendedModelDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取推荐模型失败");
                return new List<RecommendedModelDto>();
            }
        }

        public async Task<bool> RefreshLibraryAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                // 刷新模型库涉及多次网络请求，使用较长超时
                using var quick = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await PostWithMetricsAsync("/api/local-models/refresh-library", null!, linked.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "刷新模型库失败");
                return false;
            }
        }

        /// <summary>OpenVINO LLM 服务托管状态（宿主机 openvino_host.py）</summary>
        public async Task<OpenVinoLlmStatusDto?> GetOpenVinoLlmStatusAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await GetWithMetricsAsync("/api/local-models/openvino-llm/status", linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<OpenVinoLlmStatusDto>(linked.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 OpenVINO LLM 托管状态失败");
                return null;
            }
        }

        /// <summary>启动/停止 OpenVINO LLM 实例（转发宿主机托管服务）</summary>
        public async Task<(bool Success, string Message)> ControlOpenVinoLlmAsync(string action, int port, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var content = new StringContent(JsonSerializer.Serialize(new { port }), Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync($"/api/local-models/openvino-llm/{action}", content, linked.Token);
                var body = await response.Content.ReadAsStringAsync(linked.Token);
                try
                {
                    var obj = JsonSerializer.Deserialize<JsonElement>(body);
                    var msg = obj.TryGetProperty("message", out var m) ? m.GetString() : obj.TryGetProperty("error", out var e) ? e.GetString() : body;
                    return (response.IsSuccessStatusCode, msg ?? body);
                }
                catch { return (response.IsSuccessStatusCode, body); }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "控制 OpenVINO LLM 实例失败: {Action}:{Port}", action, port);
                return (false, ex.Message);
            }
        }

        public async Task<DeployLocalModelResult> DeployLocalModelAsync(DeployLocalModelRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var json = JsonSerializer.Serialize(request);                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/local-models/deploy", content, linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<DeployLocalModelResult>(linked.Token)
                       ?? new DeployLocalModelResult { Success = false, Message = _loc["Api_ResponseParseFailed"] };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动模型部署失败");
                return new DeployLocalModelResult { Success = false, Message = _loc["Api_StartFailedWithError", ex.Message] };
            }
        }

        public async Task<DeployTaskStatusDto?> GetDeployTaskStatusAsync(string taskId, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await GetWithMetricsAsync($"/api/local-models/deploy/{Uri.EscapeDataString(taskId)}", linked.Token);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<DeployTaskStatusDto>(linked.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取部署任务状态失败");
                return null;
            }
        }

        public async Task<bool> CancelDeployTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await PostWithMetricsAsync($"/api/local-models/deploy/{Uri.EscapeDataString(taskId)}/cancel", new StringContent("", Encoding.UTF8, "application/json"), linked.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消模型部署任务失败，TaskId: {TaskId}", taskId);
                return false;
            }
        }

        public async Task<List<LocalToolInfoDto>> GetLocalToolsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var url = "/api/local-models/tools" + (forceRefresh ? "?forceRefresh=true" : "");
                var response = await GetWithMetricsAsync(url, linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<LocalToolInfoDto>>(linked.Token) ?? new List<LocalToolInfoDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取本地工具信息失败");
                return new List<LocalToolInfoDto>();
            }
        }

        public async Task<List<DownloadSourceDto>> GetDownloadSourcesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await GetWithMetricsAsync("/api/local-models/sources", linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<DownloadSourceDto>>(linked.Token) ?? new List<DownloadSourceDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取下载源失败");
                return new List<DownloadSourceDto>();
            }
        }

        public async Task<DownloadDirectoryConfigDto?> GetDownloadConfigAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await GetWithMetricsAsync("/api/local-models/config", linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<DownloadDirectoryConfigDto>(linked.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取下载配置失败");
                return null;
            }
        }

        public async Task<bool> SaveDownloadConfigAsync(DownloadDirectoryConfigDto config, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var json = JsonSerializer.Serialize(config);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/local-models/config", content, linked.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存下载配置失败");
                return false;
            }
        }

        // 运行中模型管理
        public async Task<List<RunningModelDto>> GetRunningModelsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var url = "/api/local-models/running" + (forceRefresh ? "?forceRefresh=true" : "");
                var response = await GetWithMetricsAsync(url, linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<RunningModelDto>>(linked.Token) ?? new List<RunningModelDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取运行中模型失败");
                return new List<RunningModelDto>();
            }
        }

        public async Task<List<string>> GetAvailableModelsAsync(string toolId, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await GetWithMetricsAsync($"/api/local-models/available?toolId={Uri.EscapeDataString(toolId)}", linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<string>>(linked.Token) ?? new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取可用模型列表失败");
                return new List<string>();
            }
        }

        public async Task<List<DownloadedModelDto>> GetDownloadedModelsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await GetWithMetricsAsync("/api/local-models/downloaded", linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<DownloadedModelDto>>(linked.Token) ?? new List<DownloadedModelDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取已下载模型列表失败");
                return new List<DownloadedModelDto>();
            }
        }

        public async Task<bool> DeleteModelAsync(DeleteModelRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/local-models/delete", content, linked.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除模型失败");
                return false;
            }
        }

        public async Task<ModelDetailsDto?> GetModelDetailsAsync(ModelDetailsRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/local-models/details", content, linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<ModelDetailsDto>(linked.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取模型详情失败");
                return null;
            }
        }

        public async Task<bool> LoadModelAsync(LoadModelRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/local-models/running/load", content, linked.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载模型失败");
                return false;
            }
        }

        public async Task<bool> UnloadModelAsync(UnloadModelRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/local-models/running/unload", content, linked.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "卸载模型失败");
                return false;
            }
        }

        public async Task<LocalAiServiceStatusDto> StartLlamaCppAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);
                var response = await PostWithMetricsAsync("/api/local-models/llamacpp/start", new StringContent("", Encoding.UTF8, "application/json"), linked.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<LocalAiServiceStatusDto>(linked.Token) ?? new LocalAiServiceStatusDto { Provider = "llamacpp" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动 llama.cpp 失败");
                return new LocalAiServiceStatusDto { Provider = "llamacpp", Message = ex.Message };
            }
        }

        public async Task<bool> StopLlamaCppAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
                var response = await PostWithMetricsAsync("/api/local-models/llamacpp/stop", new StringContent("", Encoding.UTF8, "application/json"), linked.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止 llama.cpp 失败");
                return false;
            }
        }

        #endregion

        #region OpenClaw 任务

        public async Task<OpenClawTaskDto> CreateOpenClawTaskAsync(string prompt)
        {
            try
            {
                var payload = new { prompt = prompt };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/openclaw/tasks", content);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<OpenClawTaskDto>()
                       ?? new OpenClawTaskDto { Prompt = prompt, Status = "unknown" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建 OpenClaw 任务失败");
                return new OpenClawTaskDto { Prompt = prompt, Status = "failed", ErrorMessage = ex.Message };
            }
        }

        public async Task<List<OpenClawTaskDto>> GetOpenClawTasksAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync("/api/openclaw/tasks", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<OpenClawTaskDto>>(quick.Token) ?? new List<OpenClawTaskDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 OpenClaw 任务列表失败");
                return new List<OpenClawTaskDto>();
            }
        }

        public async Task<OpenClawTaskDto?> GetOpenClawTaskAsync(int id)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync($"/api/openclaw/tasks/{id}", quick.Token);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<OpenClawTaskDto>(quick.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 OpenClaw 任务详情失败，Id: {Id}", id);
                return null;
            }
        }

        public async Task<string?> GetOpenClawReportAsync(int id)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync($"/api/openclaw/tasks/{id}/report", quick.Token);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(quick.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 OpenClaw 报告失败，Id: {Id}", id);
                return null;
            }
        }

        public async Task<bool> DeleteOpenClawTaskAsync(int id)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await DeleteWithMetricsAsync($"/api/openclaw/tasks/{id}", quick.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除 OpenClaw 任务失败，Id: {Id}", id);
                return false;
            }
        }

        public async Task<bool> CancelOpenClawTaskAsync(int id)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await PostWithMetricsAsync($"/api/openclaw/tasks/{id}/cancel", new StringContent(""), quick.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消 OpenClaw 任务失败，Id: {Id}", id);
                return false;
            }
        }

        public async Task<OpenClawLocalAiConfigDto> GetOpenClawLocalAiConfigAsync()
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync("/api/openclaw/local-ai-config", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<OpenClawLocalAiConfigDto>(quick.Token)
                       ?? new OpenClawLocalAiConfigDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 OpenClaw 本地 AI 配置失败");
                return new OpenClawLocalAiConfigDto();
            }
        }

        public async Task<bool> SaveOpenClawLocalAiConfigAsync(SaveOpenClawLocalAiConfigRequest request)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/openclaw/local-ai-config", content, quick.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存 OpenClaw 本地 AI 配置失败");
                return false;
            }
        }

        public async Task<List<OpenClawLocalModelDto>> ScanOpenClawLocalModelsAsync(string provider)
        {
            try
            {
                using var quick = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync($"/api/openclaw/local-ai-models?provider={Uri.EscapeDataString(provider)}", quick.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<OpenClawLocalModelDto>>(quick.Token)
                       ?? new List<OpenClawLocalModelDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扫描 OpenClaw 本地模型失败，Provider: {Provider}", provider);
                return new List<OpenClawLocalModelDto>();
            }
        }

        public async Task<LocalAiServiceStatusDto> DetectAndStartOpenClawLocalAiAsync(string provider)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var payload = new { provider = provider };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/openclaw/local-ai-detect", content, cts.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<LocalAiServiceStatusDto>(cts.Token)
                       ?? new LocalAiServiceStatusDto { Provider = provider, Message = _loc["Api_DetectFailed"] };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检测/启动本地 AI 服务失败，Provider: {Provider}", provider);
                return new LocalAiServiceStatusDto { Provider = provider, Message = _loc["Api_DetectFailedWithError", ex.Message] };
            }
        }

        public async Task<OpenClawDefaultModelDto> GetOpenClawDefaultModelAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync("/api/openclaw/default-model", cts.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<OpenClawDefaultModelDto>(cts.Token)
                       ?? new OpenClawDefaultModelDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 OpenClaw 默认模型失败");
                return new OpenClawDefaultModelDto();
            }
        }

        public async Task<bool> SetOpenClawDefaultModelAsync(string model)
        {
            try
            {
                using var cts = new CancellationTokenSource(QuickCallTimeout);
                var payload = new { model = model };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/openclaw/default-model", content, cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置 OpenClaw 默认模型失败，Model: {Model}", model);
                return false;
            }
        }

        public async Task<ModelProfileListDto> GetModelProfilesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("api/openclaw/model-profiles");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<ModelProfileListDto>() ?? new ModelProfileListDto();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取模型配置列表失败");
            }
            return new ModelProfileListDto();
        }

        public async Task<bool> SetModelProfileAsync(string profile)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("api/openclaw/model-profiles", new SetModelProfileRequest { Profile = profile });
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置模型配置失败，Profile: {Profile}", profile);
                return false;
            }
        }

        public async Task<bool> SyncLocalModelsToOpenClawAsync(string provider)
        {
            try
            {
                using var cts = new CancellationTokenSource(QuickCallTimeout);
                var payload = new SyncLocalModelsRequest { Provider = provider };
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/openclaw/sync-local-models", content, cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步 {Provider} 模型到 OpenClaw 失败", provider);
                return false;
            }
        }

        #endregion

        #region Model Benchmark

        public async Task<List<RecommendedBenchmarkModel>> GetBenchmarkModelsAsync(string? category = null)
        {
            try
            {
                using var cts = new CancellationTokenSource(QuickCallTimeout);
                var url = "/api/benchmark/models";
                if (!string.IsNullOrEmpty(category)) url += $"?category={Uri.EscapeDataString(category)}";
                var response = await GetWithMetricsAsync(url, cts.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<RecommendedBenchmarkModel>>(cts.Token) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取基准测试模型列表失败");
                return new();
            }
        }

        public async Task<VramTierResponse> GetBenchmarkVramTiersAsync(string? category = null)
        {
            try
            {
                using var cts = new CancellationTokenSource(QuickCallTimeout);
                var url = "/api/benchmark/vram-tiers";
                if (!string.IsNullOrEmpty(category)) url += $"?category={Uri.EscapeDataString(category)}";
                var response = await GetWithMetricsAsync(url, cts.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<VramTierResponse>(cts.Token) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取显存等级推荐失败");
                return new();
            }
        }

        public async Task<List<BenchmarkPrompt>> GetBenchmarkPromptsAsync(string? category = null)
        {
            try
            {
                using var cts = new CancellationTokenSource(QuickCallTimeout);
                var url = "/api/benchmark/prompts";
                if (!string.IsNullOrEmpty(category)) url += $"?category={Uri.EscapeDataString(category)}";
                var response = await GetWithMetricsAsync(url, cts.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<BenchmarkPrompt>>(cts.Token) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取基准测试提示词失败");
                return new();
            }
        }

        public async Task<bool> RunBenchmarkAsync(BenchmarkModelConfig model, string[]? promptIds = null)
        {
            try
            {
                var request = new RunBenchmarkRequest { Model = model, PromptIds = promptIds };
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await PostWithMetricsAsync("/api/benchmark/run", content);
                return response.StatusCode == System.Net.HttpStatusCode.Accepted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动基准测试失败");
                return false;
            }
        }

        public async Task<bool> StopBenchmarkAsync()
        {
            try
            {
                var response = await PostWithMetricsAsync("/api/benchmark/stop", new StringContent(""));
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止基准测试失败");
                return false;
            }
        }

        public async Task<BenchmarkStatusDto> GetBenchmarkStatusAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(QuickCallTimeout);
                var response = await GetWithMetricsAsync("/api/benchmark/status", cts.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<BenchmarkStatusDto>(cts.Token) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "获取基准测试状态失败");
                return new();
            }
        }

        public async Task<List<BenchmarkSession>> GetBenchmarkHistoryAsync(string? category = null)
        {
            try
            {
                using var cts = new CancellationTokenSource(QuickCallTimeout);
                var url = "/api/benchmark/history";
                if (!string.IsNullOrEmpty(category)) url += $"?category={Uri.EscapeDataString(category)}";
                var response = await GetWithMetricsAsync(url, cts.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<BenchmarkSession>>(cts.Token) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取基准测试历史失败");
                return new();
            }
        }

        public async Task<List<BenchmarkLeaderboardEntry>> GetBenchmarkLeaderboardAsync(string? category = null)
        {
            try
            {
                using var cts = new CancellationTokenSource(QuickCallTimeout);
                var url = "/api/benchmark/leaderboard";
                if (!string.IsNullOrEmpty(category)) url += $"?category={Uri.EscapeDataString(category)}";
                var response = await GetWithMetricsAsync(url, cts.Token);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<BenchmarkLeaderboardEntry>>(cts.Token) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取基准测试排行榜失败");
                return new();
            }
        }

        public async Task<bool> DeleteBenchmarkSessionAsync(string sessionId)
        {
            try
            {
                using var cts = new CancellationTokenSource(QuickCallTimeout);
                var response = await _httpClient.DeleteAsync($"/api/benchmark/history/{sessionId}", cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除基准测试记录失败");
                return false;
            }
        }

        public async Task<bool> ClearBenchmarkHistoryAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(QuickCallTimeout);
                var response = await _httpClient.DeleteAsync("/api/benchmark/history", cts.Token);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清空基准测试历史失败");
                return false;
            }
        }

        #endregion

        #region AI 调用性能指标

        public async Task<AiMetricsSummaryDto?> GetAiMetricsSummaryAsync(int days = 7, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _aiHttpClient.GetAsync($"/api/ai/metrics/summary?days={days}", cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<AiMetricsSummaryDto>(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 AI 指标总览失败");
                return null;
            }
        }

        public async Task<List<AiProviderMetricsDto>> GetAiProviderMetricsAsync(int days = 7, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _aiHttpClient.GetAsync($"/api/ai/metrics/providers?days={days}", cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<AiProviderMetricsDto>>(cancellationToken) ?? new List<AiProviderMetricsDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 Provider 指标失败");
                return new List<AiProviderMetricsDto>();
            }
        }

        public async Task<List<AiModelMetricsDto>> GetAiModelMetricsAsync(int days = 7, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _aiHttpClient.GetAsync($"/api/ai/metrics/models?days={days}", cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<AiModelMetricsDto>>(cancellationToken) ?? new List<AiModelMetricsDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取模型指标失败");
                return new List<AiModelMetricsDto>();
            }
        }

        public async Task<List<AiMetricsTrendDto>> GetAiMetricsTrendsAsync(int days = 7, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _aiHttpClient.GetAsync($"/api/ai/metrics/trends?days={days}", cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<AiMetricsTrendDto>>(cancellationToken) ?? new List<AiMetricsTrendDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取 AI 指标趋势失败");
                return new List<AiMetricsTrendDto>();
            }
        }

        public async Task<List<AiUsageMetricDto>> GetAiRecentMetricsAsync(int limit = 50, int days = 7, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _aiHttpClient.GetAsync($"/api/ai/metrics/recent?limit={limit}&days={days}", cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<AiUsageMetricDto>>(cancellationToken) ?? new List<AiUsageMetricDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最近 AI 调用记录失败");
                return new List<AiUsageMetricDto>();
            }
        }

        public async Task SetSceneAsync(Baihua.Contracts.Scene.AppScene scene, CancellationToken cancellationToken = default)
        {
            var payload = new { scene = (int)scene };
            var response = await _httpClient.PostAsJsonAsync("/api/scene", payload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        #endregion

        #region 虚拟师父

        public async Task<CreateMasterResponse> CreateMasterAsync(string goal, string industry, CancellationToken cancellationToken = default)
        {
            var payload = new { goal, industry };
            var response = await _httpClient.PostAsJsonAsync("/api/master/create", payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await TryGetErrorMessageAsync(response);
                throw new HttpRequestException(error ?? _loc["Api_MasterCreateFailed", (int)response.StatusCode]);
            }
            return await response.Content.ReadFromJsonAsync<CreateMasterResponse>(cancellationToken)
                   ?? new CreateMasterResponse { Success = false, Message = _loc["Api_CreateFailed"] };
        }

        private static async Task<string?> TryGetErrorMessageAsync(HttpResponseMessage response)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body)) return null;

                using var doc = JsonDocument.Parse(body);
                // Try both PascalCase and camelCase (server uses PascalCase, some endpoints use camelCase)
                foreach (var key in new[] { "Message", "message", "Title", "title", "error", "Error" })
                {
                    if (doc.RootElement.TryGetProperty(key, out var val) && val.ValueKind == JsonValueKind.String)
                        return val.GetString();
                }
            }
            catch { }
            return null;
        }

        public async IAsyncEnumerable<ChatStreamEvent> StreamMasterChatAsync(
            string masterId,
            string message,
            string stage,
            List<(bool IsUser, string Content)>? history = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var payload = new Dictionary<string, object?>
            {
                ["masterId"] = masterId,
                ["message"] = message,
                ["stage"] = stage
            };
            if (history != null && history.Count > 0)
                payload["history"] = history.Select(h => new { role = h.IsUser ? "user" : "assistant", content = h.Content }).ToList();

            var json = JsonSerializer.Serialize(payload);
            var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Post, "/api/master/chat/stream") { Content = httpContent },
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await TryGetErrorMessageAsync(response);
                throw new HttpRequestException(error ?? _loc["Api_MasterChatFailed", (int)response.StatusCode]);
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? currentEvent = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) break;

                if (line.StartsWith("event: "))
                {
                    currentEvent = line.Substring(7).Trim();
                }
                else if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6);
                    if (currentEvent == "delta")
                    {
                        var text = TryExtractContent(data);
                        if (!string.IsNullOrEmpty(text))
                        {
                            yield return new ChatStreamEvent { Type = "delta", Content = text };
                        }
                    }
                    else if (currentEvent == "done")
                    {
                        yield return new ChatStreamEvent { Type = "done" };
                        yield break;
                    }
                    else if (currentEvent == "error")
                    {
                        throw new InvalidOperationException(_loc["Api_MasterChatError", data!]);
                    }
                }
                else if (string.IsNullOrEmpty(line))
                {
                    currentEvent = null;
                }
            }
        }

        public async Task<StageCompleteResponse> MasterStageCompleteAsync(string masterId, string stageName, CancellationToken cancellationToken = default)
        {
            var payload = new { stageName };
            var response = await _httpClient.PostAsJsonAsync($"/api/master/{masterId}/stage-complete", payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await TryGetErrorMessageAsync(response);
                throw new HttpRequestException(error ?? _loc["Api_StageCompleteFailed", (int)response.StatusCode]);
            }
            return await response.Content.ReadFromJsonAsync<StageCompleteResponse>(cancellationToken)
                   ?? new StageCompleteResponse { Success = false, Message = _loc["Api_OperationFailed"] };
        }

        public async Task<ApprenticeProfileResponse> GetMasterProfileAsync(string masterId, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.GetAsync($"/api/master/{masterId}/profile", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await TryGetErrorMessageAsync(response);
                throw new HttpRequestException(error ?? _loc["Api_ProfileFetchFailed", (int)response.StatusCode]);
            }
            return await response.Content.ReadFromJsonAsync<ApprenticeProfileResponse>(cancellationToken)
                   ?? new ApprenticeProfileResponse { Success = false, Message = _loc["Api_FetchFailed"] };
        }

        public async Task<AssessResponse> MasterAssessAsync(string masterId, string type = "capability", CancellationToken cancellationToken = default)
        {
            var payload = new { type };
            var response = await _httpClient.PostAsJsonAsync($"/api/master/{masterId}/assess", payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await TryGetErrorMessageAsync(response);
                throw new HttpRequestException(error ?? _loc["Api_AssessFailedWithHttp", (int)response.StatusCode]);
            }
            return await response.Content.ReadFromJsonAsync<AssessResponse>(cancellationToken)
                   ?? new AssessResponse { Success = false, Message = _loc["Api_AssessFailed"] };
        }

        public async Task<List<MasterListItem>> GetMastersAsync(CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.GetAsync("/api/master", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await TryGetErrorMessageAsync(response);
                throw new HttpRequestException(error ?? $"获取师父列表失败 (HTTP {(int)response.StatusCode})");
            }
            return await response.Content.ReadFromJsonAsync<List<MasterListItem>>(cancellationToken)
                   ?? new List<MasterListItem>();
        }

        public async Task<bool> DeleteMasterAsync(string masterId, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.DeleteAsync($"/api/master/{masterId}", cancellationToken);
            return response.IsSuccessStatusCode;
        }

        public async Task<MasterEvictResponse> EvictMasterAsync(string masterId, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.PostAsync($"/api/master/{masterId}/evict", null, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<MasterEvictResponse>(cancellationToken)
                    ?? new MasterEvictResponse { Success = false, Message = _loc["Api_CleanupFailed"] };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理师父数据失败，MasterId: {MasterId}", masterId);
                return new MasterEvictResponse { Success = false, Message = _loc["Api_CleanupFailedWithError", ex.Message] };
            }
        }

        public async Task<ApprenticeProfileResponse> UpdateMasterProfileAsync(string masterId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                var json = JsonSerializer.Serialize(request);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PutAsync($"/api/master/{masterId}/profile", httpContent, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<ApprenticeProfileResponse>(cancellationToken)
                    ?? new ApprenticeProfileResponse { Success = false, Message = _loc["Api_UpdateFailed"] };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新师父画像失败，MasterId: {MasterId}", masterId);
                return new ApprenticeProfileResponse { Success = false, Message = _loc["Api_UpdateFailedWithError", ex.Message] };
            }
        }

        public async Task<VaultFocusListResponse> GetVaultFocusAsync(string masterId, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/api/master/{masterId}/vault-focus", cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<VaultFocusListResponse>(cancellationToken)
                    ?? new VaultFocusListResponse { Success = false, Message = _loc["Api_FetchFailed"] };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取知识库关联失败，MasterId: {MasterId}", masterId);
                return new VaultFocusListResponse { Success = false, Message = _loc["Api_FetchFailedWithError", ex.Message] };
            }
        }

        public async Task<VaultFocusUpdateResponse> UpdateVaultFocusAsync(string masterId, VaultFocusUpdateRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                var json = JsonSerializer.Serialize(request);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"/api/master/{masterId}/vault-focus", httpContent, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<VaultFocusUpdateResponse>(cancellationToken)
                    ?? new VaultFocusUpdateResponse { Success = false, Message = _loc["Api_OperationFailed"] };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新知识库关联失败，MasterId: {MasterId}", masterId);
                return new VaultFocusUpdateResponse { Success = false, Message = $"操作失败：{ex.Message}" };
            }
        }

        public async Task<VaultFocusUpdateResponse> RemoveVaultFocusAsync(string masterId, string vaultId, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"/api/master/{masterId}/vault-focus/{vaultId}", cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<VaultFocusUpdateResponse>(cancellationToken)
                    ?? new VaultFocusUpdateResponse { Success = false, Message = _loc["Api_OperationFailed"] };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "取消知识库关联失败，MasterId: {MasterId}", masterId);
                return new VaultFocusUpdateResponse { Success = false, Message = $"操作失败：{ex.Message}" };
            }
        }

        // ============ AI 绘图（ComfyUI） ============

        public async Task<ComfyStatusDto> GetComfyStatusAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/comfy/status", cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<ComfyStatusDto>(cancellationToken)
                    ?? new ComfyStatusDto { Available = false };
            }
            catch
            {
                return new ComfyStatusDto { Available = false };
            }
        }

        public async Task<ComfyGenerateResultDto> GenerateComfyImageAsync(string prompt, string negativePrompt, int width, int height, int steps, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.PostAsJsonAsync("/api/comfy/generate-image", new
            {
                Prompt = prompt,
                NegativePrompt = negativePrompt,
                Width = width,
                Height = height,
                Steps = steps
            }, cancellationToken);
            return await ReadComfyGenerateResultAsync(response, cancellationToken);
        }

        public async Task<ComfyGenerateResultDto> GenerateComfyVideoAsync(string prompt, string negativePrompt, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.PostAsJsonAsync("/api/comfy/generate-video", new
            {
                Prompt = prompt,
                NegativePrompt = negativePrompt
            }, cancellationToken);
            return await ReadComfyGenerateResultAsync(response, cancellationToken);
        }

        public async Task<List<ComfyHistoryItemDto>> GetComfyHistoryAsync(int limit = 50, string? kind = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var path = $"/api/comfy/history?limit={limit}" + (string.IsNullOrEmpty(kind) ? "" : $"&kind={kind}");
                var response = await _httpClient.GetAsync(path, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<ComfyHistoryItemDto>>(cancellationToken) ?? [];
            }
            catch
            {
                return [];
            }
        }

        private static async Task<ComfyGenerateResultDto> ReadComfyGenerateResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string? error = null;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind == System.Text.Json.JsonValueKind.String)
                        error = err.GetString();
                }
                catch { }
                throw new InvalidOperationException(error ?? $"HTTP {(int)response.StatusCode}");
            }
            return System.Text.Json.JsonSerializer.Deserialize<ComfyGenerateResultDto>(body)
                ?? throw new InvalidOperationException("空响应");
        }

        #endregion
    }

    public class GenerateCardsTaskDto
    {
        public bool Success { get; set; }
        public string TaskId { get; set; } = "";
        public string Message { get; set; } = "";
        public string VaultName { get; set; } = "";
    }

    public class ComfyStatusDto
    {
        public bool Available { get; set; }
    }

    public class ComfyGenerateResultDto
    {
        public int Id { get; set; }
        public string Url { get; set; } = "";
        public double DurationSeconds { get; set; }
    }

    public class ComfyHistoryItemDto
    {
        public int Id { get; set; }
        public string Kind { get; set; } = "";
        public string Prompt { get; set; } = "";
        public string Url { get; set; } = "";
        public bool IsSuccess { get; set; }
        public DateTime CreatedAt { get; set; }
    }

}
