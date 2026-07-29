using TaskRunner.Core.Shared;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskRunner.Contracts.Onboarding;
using TaskRunner.Data;
using TaskRunner.Data.Entities;
using TaskRunner.Services;

namespace TaskRunner.Controllers;

public partial class OnboardingController
{
    /// <summary>
    /// 创建示例知识库和笔记
    /// </summary>
    [HttpPost("create-sample-vault")]
    public async Task<ActionResult<CreateSampleVaultResponse>> CreateSampleVault([FromBody] CreateSampleVaultRequest request)
    {
        try
        {
            string vaultName = request.VaultName;
            string vaultType = request.VaultType;

            if (string.IsNullOrWhiteSpace(vaultName))
            {
                vaultName = vaultType == "tcm" ? "中医" : "计算机";
            }

            // 使用 VaultRootPathPreference 作为父目录，如果没有则使用默认路径
            var rootPath = _vaultSettings.VaultRootPathPreference;
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                rootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "vaults");
            }

            var industry = vaultType == "tcm" ? "中医" : "计算机";
            var vaultPath = Path.Combine(rootPath, "local", industry, vaultName);
            Directory.CreateDirectory(vaultPath);

            var vault = _vaultSettings.AddVault(vaultName, vaultPath, industry);

            var createdNotes = new List<string>();

            if (vaultType == "computer")
            {
                var notePath = "notes/AI知识入门.md";
                var noteContent = GetComputerSampleNote();
                await WriteVaultNoteAsync(vaultPath, notePath, noteContent);
                createdNotes.Add(notePath);
            }
            else if (vaultType == "tcm")
            {
                var notePath = "notes/脾胃病知识.md";
                var noteContent = GetTcmSampleNote();
                await WriteVaultNoteAsync(vaultPath, notePath, noteContent);
                createdNotes.Add(notePath);
            }

            return Ok(new CreateSampleVaultResponse
            {
                Success = true,
                VaultId = vault.Id,
                Message = $"知识库 '{vaultName}' 创建成功",
                CreatedNotes = createdNotes
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建示例知识库失败");
            return StatusCode(500, new CreateSampleVaultResponse
            {
                Success = false,
                Message = $"创建失败: {ex.Message}"
            });
        }
    }

    private static async Task WriteVaultNoteAsync(string vaultPath, string notePath, string content)
    {
        var fullPath = Path.Combine(vaultPath, notePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        await System.IO.File.WriteAllTextAsync(fullPath, content);
    }
}
