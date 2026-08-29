using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Baihua.Contracts.Ai;
using Baihua.Contracts.Assistant;
using Baihua.Contracts.Budget;
using Baihua.Contracts.Stock;
using Baihua.Contracts.Todo;
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
using Baihua.Contracts.Generate;

namespace Baihua.Web.Services
{
    public partial class ApiService {

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

        public async Task<VaultGenerationResponse> CreateVaultGenerationTaskAsync(string industry, string keyword, string? model = null, int noteCount = 30, bool generateCards = false, string? detailLevel = null)
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
                if (!string.IsNullOrWhiteSpace(detailLevel))
                    body["detailLevel"] = detailLevel;
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

        public async Task<StockRecommendationResponse> GetStockRecommendationsAsync(string? strategy = null, string? industry = null, string? horizon = null, string? prompt = null, string? direction = null, bool refresh = false, CancellationToken cancellationToken = default)
        {
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(strategy)) query.Add($"strategy={Uri.EscapeDataString(strategy)}");
            if (!string.IsNullOrWhiteSpace(industry)) query.Add($"industry={Uri.EscapeDataString(industry)}");
            if (!string.IsNullOrWhiteSpace(horizon)) query.Add($"horizon={Uri.EscapeDataString(horizon)}");
            if (!string.IsNullOrWhiteSpace(prompt)) query.Add($"prompt={Uri.EscapeDataString(prompt)}");
            if (!string.IsNullOrWhiteSpace(direction)) query.Add($"direction={Uri.EscapeDataString(direction)}");
            if (refresh) query.Add("refresh=true");
            var qs = query.Count > 0 ? "?" + string.Join('&', query) : "";

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var response = await GetWithMetricsAsync("/api/stock/recommendations" + qs, linked.Token, _longHttpClient);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<StockRecommendationResponse>(linked.Token)
                   ?? new StockRecommendationResponse();
        }

        public async Task<List<string>> GetStockIndustriesAsync(CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync("/api/stock/industries", linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<string>>(linked.Token) ?? new List<string>();
        }

        public async Task<TopicSuggestionResponse> GetTopicSuggestionsAsync(string? context = null, bool refresh = false, CancellationToken cancellationToken = default)
        {
            var query = new List<string>();
            if (!string.IsNullOrWhiteSpace(context)) query.Add($"context={Uri.EscapeDataString(context)}");
            if (refresh) query.Add("refresh=true");
            var qs = query.Count > 0 ? "?" + string.Join('&', query) : "";

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var response = await GetWithMetricsAsync("/api/generate/topic-suggestions" + qs, linked.Token, _longHttpClient);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TopicSuggestionResponse>(linked.Token)
                   ?? new TopicSuggestionResponse();
        }

        public async Task<StockEvaluationResponse> EvaluateStockAsync(string code, bool refresh = false, CancellationToken cancellationToken = default)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var qs = refresh ? "?refresh=true" : "";
            var response = await PostWithMetricsAsync("/api/stock/evaluate" + qs,
                JsonContent.Create(new StockEvaluationRequest { Code = code }), linked.Token, _longHttpClient);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<StockEvaluationResponse>(linked.Token)
                   ?? new StockEvaluationResponse();
        }

        public async Task<List<BudgetTransaction>> GetBudgetTransactionsAsync(int? year = null, int? month = null, CancellationToken cancellationToken = default)
        {
            var qs = (year.HasValue || month.HasValue)
                ? "?" + string.Join('&', new[]
                {
                    year.HasValue ? $"year={year}" : "",
                    month.HasValue ? $"month={month}" : ""
                }.Where(s => s != ""))
                : "";
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync("/api/budget/transactions" + qs, linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<BudgetTransaction>>(linked.Token) ?? new List<BudgetTransaction>();
        }

        public async Task<BudgetTransaction> AddBudgetTransactionAsync(BudgetCreateRequest request, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await PostWithMetricsAsync("/api/budget/transactions", JsonContent.Create(request), linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BudgetTransaction>(linked.Token) ?? new BudgetTransaction();
        }

        public async Task<bool> DeleteBudgetTransactionAsync(Guid id, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            EnsurePrimaryBaseAddress();
            var response = await _httpClient.DeleteAsync($"/api/budget/transactions/{id}", linked.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
            response.EnsureSuccessStatusCode();
            return true;
        }

        public async Task<List<TodoItemDto>> GetTodosAsync(CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync("/api/todos", linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<TodoItemDto>>(linked.Token) ?? new List<TodoItemDto>();
        }

        public async Task<TodoItemDto> AddTodoAsync(CreateTodoRequest request, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await PostWithMetricsAsync("/api/todos", JsonContent.Create(request), linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TodoItemDto>(linked.Token) ?? new TodoItemDto();
        }

        public async Task<TodoItemDto?> UpdateTodoAsync(int id, UpdateTodoRequest request, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            EnsurePrimaryBaseAddress();
            var response = await _httpClient.PutAsync($"/api/todos/{id}", JsonContent.Create(request), linked.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<TodoItemDto>(linked.Token);
        }

        public async Task<bool> DeleteTodoAsync(int id, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            EnsurePrimaryBaseAddress();
            var response = await _httpClient.DeleteAsync($"/api/todos/{id}", linked.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
            response.EnsureSuccessStatusCode();
            return true;
        }

        public async Task<List<TodoGoalDto>> GetTodoGoalsAsync(CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync("/api/todos/goals", linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<TodoGoalDto>>(linked.Token) ?? new List<TodoGoalDto>();
        }

        public async Task<AiTodoPreviewDto> GenerateTodosAsync(GenerateTodosRequest request, CancellationToken cancellationToken = default)
        {
            // AI 本地大模型生成一组待办可能耗时 20s+，走长超时客户端（5 分钟），不用 QuickCallTimeout(15s)
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            EnsurePrimaryBaseAddress();
            var response = await _longHttpClient.PostAsync("/api/todos/ai-generate", JsonContent.Create(request), linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                var error = await TryGetErrorMessageAsync(response);
                throw new HttpRequestException(error ?? $"AI 生成失败（{(int)response.StatusCode}）");
            }
            return await response.Content.ReadFromJsonAsync<AiTodoPreviewDto>(linked.Token) ?? new AiTodoPreviewDto();
        }

        public async Task<TodoGoalDto> SaveGeneratedTodosAsync(SaveGeneratedTodosRequest request, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            EnsurePrimaryBaseAddress();
            var response = await _httpClient.PostAsync("/api/todos/ai-save", JsonContent.Create(request), linked.Token);
            if (!response.IsSuccessStatusCode)
            {
                var error = await TryGetErrorMessageAsync(response);
                throw new HttpRequestException(error ?? $"保存失败（{(int)response.StatusCode}）");
            }
            return await response.Content.ReadFromJsonAsync<TodoGoalDto>(linked.Token) ?? new TodoGoalDto();
        }

        public async Task<bool> DeleteTodoGoalAsync(int id, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            EnsurePrimaryBaseAddress();
            var response = await _httpClient.DeleteAsync($"/api/todos/goals/{id}", linked.Token);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
            response.EnsureSuccessStatusCode();
            return true;
        }

        public async Task<BudgetSummary> GetBudgetSummaryAsync(int? year = null, int? month = null, CancellationToken cancellationToken = default)
        {
            var qs = (year.HasValue || month.HasValue)
                ? "?" + string.Join('&', new[]
                {
                    year.HasValue ? $"year={year}" : "",
                    month.HasValue ? $"month={month}" : ""
                }.Where(s => s != ""))
                : "";
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync("/api/budget/summary" + qs, linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BudgetSummary>(linked.Token) ?? new BudgetSummary();
        }

        public async Task<List<OpenVinoCatalogItemDto>> GetOpenVinoCatalogAsync(CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync("/api/local-models/openvino/catalog", linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<OpenVinoCatalogItemDto>>(linked.Token) ?? new();
        }

        public async Task<List<OpenVinoInstalledModelDto>> GetOpenVinoInstalledAsync(CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync("/api/local-models/openvino/installed", linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<OpenVinoInstalledModelDto>>(linked.Token) ?? new();
        }

        public async Task<List<OpenVinoDownloadTaskDto>> GetOpenVinoDownloadsAsync(CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync("/api/local-models/openvino/downloads", linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<OpenVinoDownloadTaskDto>>(linked.Token) ?? new();
        }

        public async Task<OpenVinoDownloadTaskDto> StartOpenVinoDownloadAsync(string modelId, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await PostWithMetricsAsync("/api/local-models/openvino/download",
                JsonContent.Create(new OpenVinoDownloadRequest { ModelId = modelId }), linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<OpenVinoDownloadTaskDto>(linked.Token) ?? new();
        }

        public async Task<OpenVinoDownloadTaskDto> GetOpenVinoDownloadAsync(string taskId, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync($"/api/local-models/openvino/download/{taskId}", linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<OpenVinoDownloadTaskDto>(linked.Token) ?? new();
        }

        public async Task CancelOpenVinoDownloadAsync(string taskId, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await PostWithMetricsAsync($"/api/local-models/openvino/download/{taskId}/cancel", null, linked.Token);
            response.EnsureSuccessStatusCode();
        }

        public async Task<OpenVinoRunResult> RunOpenVinoModelAsync(string modelPath, string device = "GPU", CancellationToken cancellationToken = default)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var response = await PostWithMetricsAsync("/api/local-models/openvino/run",
                JsonContent.Create(new OpenVinoRunRequest { ModelPath = modelPath, Device = device }), linked.Token, _longHttpClient);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadFromJsonAsync<OpenVinoRunResult>(linked.Token);
                return err ?? new OpenVinoRunResult { Success = false, Error = $"HTTP {(int)response.StatusCode}" };
            }
            return await response.Content.ReadFromJsonAsync<OpenVinoRunResult>(linked.Token) ?? new OpenVinoRunResult();
        }

        public async Task<bool> StopOpenVinoModelAsync(int port, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await PostWithMetricsAsync("/api/local-models/openvino/stop",
                JsonContent.Create(new OpenVinoRunRequest { Port = port }), linked.Token);
            return response.IsSuccessStatusCode;
        }

        public async Task DeleteOpenVinoModelAsync(string path, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            EnsurePrimaryBaseAddress();
            var response = await _httpClient.DeleteAsync("/api/local-models/openvino/model?path=" + Uri.EscapeDataString(path), linked.Token);
            response.EnsureSuccessStatusCode();
        }

        public async Task<(bool Success, string? OmsId, bool AlreadyRegistered, string? Warning, string? Error)> RegisterOmsModelAsync(string modelPath, CancellationToken cancellationToken = default)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var response = await PostWithMetricsAsync("/api/local-models/openvino/register",
                JsonContent.Create(new OpenVinoRunRequest { ModelPath = modelPath }), linked.Token, _longHttpClient);
            var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(linked.Token);
            if (!response.IsSuccessStatusCode)
                return (false, null, false, null, body.TryGetProperty("error", out var e) ? e.GetString() : $"HTTP {(int)response.StatusCode}");
            return (
                body.TryGetProperty("success", out var s) && s.GetBoolean(),
                body.TryGetProperty("omsId", out var id) ? id.GetString() : null,
                body.TryGetProperty("alreadyRegistered", out var a) && a.GetBoolean(),
                body.TryGetProperty("warning", out var w) ? w.GetString() : null,
                null);
        }

        public async Task<AssistantSettingsDto> GetAssistantSettingsAsync(CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync("/api/assistant/settings", linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<AssistantSettingsDto>(linked.Token) ?? new AssistantSettingsDto();
        }

        public async Task SaveAssistantSettingsAsync(AssistantSettingsDto settings, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await PostWithMetricsAsync("/api/assistant/settings", JsonContent.Create(settings), linked.Token);
            response.EnsureSuccessStatusCode();
        }

        public async Task<AssistantAnalysisDto?> GetAssistantTodayAnalysisAsync(CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync("/api/assistant/analysis/today", linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<AssistantAnalysisDto?>(linked.Token);
        }

        public async Task<AssistantAnalysisDto> RunAssistantAnalysisAsync(CancellationToken cancellationToken = default)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var response = await PostWithMetricsAsync("/api/assistant/analysis/run", null, linked.Token, _longHttpClient);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(linked.Token);
                throw new Exception(err.Length > 200 ? err[..200] : err);
            }
            return await response.Content.ReadFromJsonAsync<AssistantAnalysisDto>(linked.Token) ?? new AssistantAnalysisDto();
        }

        public async Task<List<AssistantAnalysisDto>> GetAssistantHistoryAsync(int days = 14, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync($"/api/assistant/analysis/history?days={days}", linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<AssistantAnalysisDto>>(linked.Token) ?? new();
        }

        public async Task<List<UserActivityDto>> GetAssistantActivitiesAsync(CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync("/api/assistant/activities/today", linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<UserActivityDto>>(linked.Token) ?? new();
        }

        public async Task<Dictionary<string, int>> GetAssistantActivityCountsAsync(int days = 14, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync($"/api/assistant/activities/counts?days={days}", linked.Token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<Dictionary<string, int>>(linked.Token) ?? new();
        }

        public async Task<string> GetGlobalDetailLevelAsync(CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await GetWithMetricsAsync("/api/ai/detail-level", linked.Token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(linked.Token);
            return result.TryGetProperty("detailLevel", out var v) ? v.GetString() ?? "concise" : "concise";
        }

        public async Task SetGlobalDetailLevelAsync(string level, CancellationToken cancellationToken = default)
        {
            using var quick = new CancellationTokenSource(QuickCallTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, quick.Token);
            var response = await PostWithMetricsAsync("/api/ai/detail-level",
                JsonContent.Create(new { detailLevel = level }), linked.Token);
            response.EnsureSuccessStatusCode();
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

    }
}
