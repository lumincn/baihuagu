using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text;
using TaskRunner.Services;
using TaskRunner.Contracts.Git;

namespace TaskRunner.Controllers;

public partial class GitController
{
        public async Task<ActionResult<GitResultResponse>> Commit([FromQuery] string vaultId, [FromBody] CommitRequest request)
        {
            try
            {
                var vaultPath = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId)?.Path;
                if (string.IsNullOrEmpty(vaultPath))
                {
                    return BadRequest(new GitResultResponse { Success = false, Message = "必须指定有效的知识库" });
                }

                if (string.IsNullOrWhiteSpace(request.Message))
                {
                    return BadRequest(new GitResultResponse { Success = false, Message = "提交信息不能为空" });
                }

                // 先检查是否有变更
                var status = await RunGitCommand(vaultPath, "status --porcelain");
                if (string.IsNullOrWhiteSpace(status))
                {
                    return Ok(new GitResultResponse { Success = true, Message = "没有需要提交的变更" });
                }

                // 添加所有变更
                await RunGitCommand(vaultPath, "add -A");
                
                // 提交
                var escapedMessage = request.Message.Replace("\"", "\\\"");
                var commitResult = await RunGitCommand(vaultPath, $"commit -m \"{escapedMessage}\"");
                
                return Ok(new GitResultResponse
                {
                    Success = true,
                    Message = "提交成功",
                    Output = commitResult
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Git 提交失败");
                return Ok(new GitResultResponse
                {
                    Success = false,
                    Message = $"提交失败: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// 推送到远程
        /// </summary>
        [HttpPost("push")]
        public async Task<ActionResult<GitResultResponse>> Push([FromQuery] string vaultId)
        {
            try
            {
                var vaultPath = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId)?.Path;
                if (string.IsNullOrEmpty(vaultPath))
                {
                    return BadRequest(new GitResultResponse { Success = false, Message = "必须指定有效的知识库" });
                }

                // 检查远程配置
                var remote = await RunGitCommand(vaultPath, "remote -v");
                if (string.IsNullOrWhiteSpace(remote))
                {
                    return Ok(new GitResultResponse { Success = false, Message = "未配置远程仓库" });
                }

                // 获取当前分支
                var branch = await RunGitCommand(vaultPath, "rev-parse --abbrev-ref HEAD");
                
                // 推送
                var pushResult = await RunGitCommand(vaultPath, $"push origin {branch.Trim()}", timeoutMs: 60000);
                
                return Ok(new GitResultResponse
                {
                    Success = true,
                    Message = "推送成功",
                    Output = pushResult
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Git 推送失败");
                return Ok(new GitResultResponse
                {
                    Success = false,
                    Message = $"推送失败: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// 拉取远程变更
        /// </summary>
        [HttpPost("pull")]
        public async Task<ActionResult<GitResultResponse>> Pull([FromQuery] string vaultId)
        {
            try
            {
                var vaultPath = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId)?.Path;
                if (string.IsNullOrEmpty(vaultPath))
                {
                    return BadRequest(new GitResultResponse { Success = false, Message = "必须指定有效的知识库" });
                }

                var pullResult = await RunGitCommand(vaultPath, "pull", timeoutMs: 60000);
                
                return Ok(new GitResultResponse
                {
                    Success = true,
                    Message = "拉取成功",
                    Output = pullResult
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Git 拉取失败");
                return Ok(new GitResultResponse
                {
                    Success = false,
                    Message = $"拉取失败: {ex.Message}"
                });
            }
        }

}
