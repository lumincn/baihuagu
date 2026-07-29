using Microsoft.AspNetCore.Mvc;
using Baihua.Contracts.Health;
using Baihua.Family.Services;

namespace Baihua.Family.Controllers;

public partial class HealthController
{
        [HttpPost("fix")]
        public async Task<ActionResult<HealthFixResultDto>> FixIssues(CancellationToken cancellationToken)
        {
            var result = new HealthFixResultDto();
            var fixes = new List<HealthFixItemDto>();

            try
            {
                // 先获取当前健康报告
                var report = await _healthService.GetHealthReportAsync(cancellationToken);

                foreach (var component in report.Components)
                {
                    if (component.Status == "healthy")
                    {
                        fixes.Add(new HealthFixItemDto
                        {
                            Component = component.Name,
                            Status = "skipped",
                            Message = "状态正常，无需修复"
                        });
                        continue;
                    }

                    switch (component.Name)
                    {
                        case "Ollama":
                            // 尝试启动 Ollama
                            try
                            {
                                var config = await _localAiConfig.GetLocalAiConfigAsync();
                                var ollamaUrl = config.Ollama?.BaseUrl ?? "http://localhost:11434";
                                var started = await _localAiAutoStarter.TryEnsureRunningAsync("ollama", ollamaUrl);
                                fixes.Add(new HealthFixItemDto
                                {
                                    Component = component.Name,
                                    Status = started ? "fixed" : "failed",
                                    Message = started ? "Ollama 服务已启动" : "Ollama 启动失败，请手动安装并启动"
                                });
                            }
                            catch (Exception ex)
                            {
                                fixes.Add(new HealthFixItemDto
                                {
                                    Component = component.Name,
                                    Status = "failed",
                                    Message = $"启动失败: {ex.Message}"
                                });
                            }
                            break;

                        case "Git":
                            fixes.Add(new HealthFixItemDto
                            {
                                Component = component.Name,
                                Status = "manual_required",
                                Message = "请安装 Git: sudo apt-get install git (Linux) 或访问 git-scm.com (Windows/macOS)"
                            });
                            break;

                        case "Python":
                            fixes.Add(new HealthFixItemDto
                            {
                                Component = component.Name,
                                Status = "manual_required",
                                Message = "请安装 Python: sudo apt-get install python3 (Linux) 或访问 python.org"
                            });
                            break;

                        case "Node.js":
                            fixes.Add(new HealthFixItemDto
                            {
                                Component = component.Name,
                                Status = "manual_required",
                                Message = "请安装 Node.js: 访问 nodejs.org 下载安装包"
                            });
                            break;

                        case "PIP":
                            fixes.Add(new HealthFixItemDto
                            {
                                Component = component.Name,
                                Status = "manual_required",
                                Message = "PIP 通常随 Python 一起安装。如缺失请运行: python3 -m ensurepip"
                            });
                            break;

                        case "API Key":
                            fixes.Add(new HealthFixItemDto
                            {
                                Component = component.Name,
                                Status = "manual_required",
                                Message = "请前往 WebUI → AI设置 页面配置 API Key"
                            });
                            break;

                        case "知识库":
                            fixes.Add(new HealthFixItemDto
                            {
                                Component = component.Name,
                                Status = "manual_required",
                                Message = "请前往 WebUI → 知识库 页面配置 Obsidian 知识库路径"
                            });
                            break;

                        case "Obsidian":
                            fixes.Add(new HealthFixItemDto
                            {
                                Component = component.Name,
                                Status = "manual_required",
                                Message = "请启动 Obsidian 桌面客户端"
                            });
                            break;

                        default:
                            fixes.Add(new HealthFixItemDto
                            {
                                Component = component.Name,
                                Status = "skipped",
                                Message = "暂不支持自动修复"
                            });
                            break;
                    }
                }

                // 重新检测
                var newReport = await _healthService.GetHealthReportAsync(cancellationToken);
                result.Success = newReport.Status != "critical";
                result.Message = $"修复完成。健康度: {newReport.HealthScore}%。需手动处理: {fixes.Count(f => f.Status == "manual_required")} 项";
                result.Fixes = fixes;
                result.NewReport = newReport;

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "一键修复失败");
                return StatusCode(500, new HealthFixResultDto
                {
                    Success = false,
                    Message = $"修复失败: {ex.Message}",
                    Fixes = fixes
                });
            }
        }

}
