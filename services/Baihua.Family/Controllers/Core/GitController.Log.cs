using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Text;
using TaskRunner.Services;
using TaskRunner.Contracts.Git;

namespace TaskRunner.Controllers;

public partial class GitController
{
        public async Task<ActionResult<List<CommitResponse>>> GetLog([FromQuery] string vaultId, [FromQuery] int count = 10)
        {
            try
            {
                var vaultPath = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId)?.Path;
                if (string.IsNullOrEmpty(vaultPath))
                {
                    return BadRequest(new { error = "必须指定有效的知识库" });
                }

                var log = await RunGitCommand(vaultPath, $"log --oneline -{count} --format=\"%H|%s|%an|%ar\"");
                var commits = new List<CommitResponse>();
                
                foreach (var line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 4)
                    {
                        commits.Add(new CommitResponse
                        {
                            Hash = parts[0],
                            Message = parts[1],
                            Author = parts[2],
                            Date = parts[3]
                        });
                    }
                }
                
                return Ok(commits);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取提交日志失败");
                return Ok(new List<CommitResponse>());
            }
        }

        /// <summary>
        /// 获取待推送的提交
        /// </summary>
        [HttpGet("unpushed")]
        public async Task<ActionResult<List<CommitResponse>>> GetUnpushedCommits([FromQuery] string vaultId)
        {
            try
            {
                var vaultPath = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId)?.Path;
                if (string.IsNullOrEmpty(vaultPath))
                {
                    return BadRequest(new { error = "必须指定有效的知识库" });
                }

                // 获取当前分支
                var branch = await RunGitCommand(vaultPath, "rev-parse --abbrev-ref HEAD");
                
                // 获取待推送的提交
                var log = await RunGitCommand(vaultPath, $"log origin/{branch.Trim()}..HEAD --format=\"%H|%s|%an|%ar\"");
                var commits = new List<CommitResponse>();
                
                foreach (var line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 4)
                    {
                        commits.Add(new CommitResponse
                        {
                            Hash = parts[0],
                            Message = parts[1],
                            Author = parts[2],
                            Date = parts[3]
                        });
                    }
                }
                
                return Ok(commits);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取待推送提交失败");
                return Ok(new List<CommitResponse>());
            }
        }

        /// <summary>
        /// 撤销工作区更改
        /// </summary>
        [HttpPost("discard")]
        public async Task<ActionResult<GitResultResponse>> DiscardChanges([FromQuery] string vaultId)
        {
            try
            {
                var vaultPath = _vaultSettings.GetVaults().FirstOrDefault(v => v.Id == vaultId)?.Path;
                if (string.IsNullOrEmpty(vaultPath))
                {
                    return BadRequest(new GitResultResponse { Success = false, Message = "必须指定有效的知识库" });
                }

                // 检查是否有变更
                var status = await RunGitCommand(vaultPath, "status --porcelain");
                if (string.IsNullOrWhiteSpace(status))
                {
                    return Ok(new GitResultResponse { Success = true, Message = "没有需要撤销的更改" });
                }

                // 撤销所有工作区更改（不包括未跟踪的文件）
                await RunGitCommand(vaultPath, "checkout -- .");
                
                // 删除未跟踪的文件
                await RunGitCommand(vaultPath, "clean -fd");
                
                return Ok(new GitResultResponse
                {
                    Success = true,
                    Message = "已撤销所有工作区更改"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "撤销更改失败");
                return Ok(new GitResultResponse
                {
                    Success = false,
                    Message = $"撤销失败: {ex.Message}"
                });
            }
        }

}
