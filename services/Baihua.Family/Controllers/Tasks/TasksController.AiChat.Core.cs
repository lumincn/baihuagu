using TaskRunner.Core.Shared;
using TaskRunner.Services;
using TaskRunner.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using TaskRunner.Models;
using TaskRunner.Contracts.Scene;

namespace TaskRunner.Controllers
{
    public partial class TasksController : ControllerBase
    {
        /// <summary>
        /// 根据行业名称或知识库 ID 解析对应的场景
        /// </summary>
        private AppScene? ResolveScene(string? industry, string? vaultId)
        {
            // 优先使用显式传入的行业
            var target = !string.IsNullOrWhiteSpace(industry) ? industry.Trim() : null;

            // 其次从知识库的 Industry 字段推导
            if (target == null && !string.IsNullOrWhiteSpace(vaultId))
            {
                var vault = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId);
                target = vault?.Industry;
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                return null;
            }

            AppScene? result = target switch
            {
                "开发" or "计算机" or "技术" => AppScene.Computer,
                "通用" => AppScene.General,
                "中医" or "中药" or "笔记" => AppScene.Tcm,
                _ => null
            };
            return result;
        }

        private async Task<AiCallResult> CallAiApiAsync(string query, string model, CancellationToken cancellationToken, string? customSystemPrompt = null, AppScene? scene = null, string? industry = null)
        {
            var providers = _aiSettings.GetAiProviders();

            // 根据模型名称找到对应的 provider（优先匹配模型名，否则回退到主 provider）
            var provider = providers.FirstOrDefault(p =>
                p.Models.Any(m => m.Name.Equals(model, StringComparison.OrdinalIgnoreCase)))
                ?? providers.FirstOrDefault(p => p.IsMain)
                ?? providers.FirstOrDefault();

            if (provider == null)
                throw new Exception("未找到可用的AI提供商");

            var apiEndpoint = provider.AiBaseUrl.TrimEnd('/') + "/chat/completions";

            // 使用自定义提示词 > 行业提示词 > 场景提示词 > 默认中医提示词
            // 注意：场景(Scene)只用于菜单分类，不允许影响生成笔记；行业(Industry)决定提示词
            string systemPrompt;
            if (!string.IsNullOrWhiteSpace(customSystemPrompt))
            {
                systemPrompt = customSystemPrompt;
            }
            else if (!string.IsNullOrWhiteSpace(industry))
            {
                // 优先根据行业名称查找模板（支持自定义场景配置）
                var template = _scenePromptService.GetTemplateByName(industry);
                systemPrompt = template.ChatSystemPrompt;
            }
            else if (scene.HasValue)
            {
                systemPrompt = _scenePromptService.GetTemplate(scene.Value).ChatSystemPrompt;
            }
            else
            {
                // 默认使用中医提示词（与Cloud版本保持一致）
                systemPrompt = _scenePromptService.GetTemplateByName("笔记").ChatSystemPrompt;
            }

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, query)
            };
            var options = Services.AiClientService.BuildChatOptions();

            ChatResponse response;
            try
            {
                response = await _aiClientService.GetChatResponseWithAutoStartAsync(
                    provider, model, messages, options, cancellationToken);
            }
            catch (ArgumentOutOfRangeException ex) when (ex.Message.Contains("index"))
            {
                // OpenAI SDK 在解析阿里云内容审核响应时（choices为空）会崩溃
                _logger.LogWarning(ex, "AI 返回内容审核失败响应（choices为空），可能是敏感内容触发阿里云拦截");
                throw new Exception("AI 内容审核未通过：输入内容可能包含敏感信息，请修改后重试。", ex);
            }
            var content = response.Text;

            return new AiCallResult
            {
                Content = content ?? throw new Exception("AI 返回内容为空。有可能是当前所用的 AI 模型不支持该问题，建议换一个 AI 提供商或模型再试试。"),
                ProviderId = provider.Id,
                ProviderName = provider.Name,
                Model = model,
                Endpoint = apiEndpoint
            };
        }

        /// <summary>
        /// AI API 调用结果，包含内容和请求详情
        /// </summary>
        private class AiCallResult
        {
            public string Content { get; set; } = "";
            public string ProviderId { get; set; } = "";
            public string ProviderName { get; set; } = "";
            public string Model { get; set; } = "";
            public string Endpoint { get; set; } = "";
        }
    }
}
