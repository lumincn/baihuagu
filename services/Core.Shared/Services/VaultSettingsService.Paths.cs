using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using TaskRunner.Data;
using TaskRunner.Data.Entities;
using TaskRunner.Contracts.Vaults;

namespace TaskRunner.Services;


public partial class VaultSettingsService
{
    public string VaultPath
    {
        get
        {
            var active = GetActiveVault();
            if (active != null && !string.IsNullOrEmpty(active.Path))
                return active.Path;
            return TaskRunner.Contracts.BaihuaPaths.Vaults;
        }
    }

    public string NotesPath
    {
        get
        {
            var active = GetActiveVault();
            if (active != null && !string.IsNullOrEmpty(active.Path))
                return Path.Combine(active.Path, "notes");
            return "";
        }
    }

    public string CardsPath
    {
        get
        {
            var active = GetActiveVault();
            if (active != null && !string.IsNullOrEmpty(active.Path))
                return Path.Combine(active.Path, "cards");
            return "";
        }
    }

    public (int added, int removed) SyncVaultsWithFilesystem(string rootPath)
    {
        int added = 0, removed = 0;

        if (!Directory.Exists(rootPath))
            return (added, removed);

        var dbVaults = GetVaults().ToDictionary(v => v.Path, v => v);
        var fsVaults = new HashSet<string>();

        // 递归查找所有包含 notes 或 cards 子目录的知识库
        void ScanDirectory(string currentDir)
        {
            foreach (var dir in Directory.EnumerateDirectories(currentDir))
            {
                var notesDir = Path.Combine(dir, "notes");
                var cardsDir = Path.Combine(dir, "cards");
                if (Directory.Exists(notesDir) || Directory.Exists(cardsDir))
                {
                    fsVaults.Add(dir);
                    if (!dbVaults.ContainsKey(dir))
                    {
                        var name = Path.GetFileName(dir);
                        try
                        {
                            AddVault(name, dir, "其他");
                            added++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "同步知识库时跳过重复: {Path}", dir);
                        }
                    }
                }
                else
                {
                    // 继续递归检查子目录
                    ScanDirectory(dir);
                }
            }
        }

        ScanDirectory(rootPath);

        foreach (var dbVault in dbVaults.Values)
        {
            if (!fsVaults.Contains(dbVault.Path) && !dbVault.Path.Contains("builtin"))
            {
                RemoveVault(dbVault.Id);
                removed++;
            }
        }

        return (added, removed);
    }

    private static List<string> ParseTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return new List<string>();
        return tags.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
    }
}
