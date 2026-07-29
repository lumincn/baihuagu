using Microsoft.AspNetCore.Mvc;
using TaskRunner.Contracts.Search;
using System.Diagnostics;

namespace TaskRunner.Vault.Controllers;

public partial class SearchController
{
        private async Task<List<SearchResult>?> SearchWithObsidianCli(string obsidianExe, string vaultName, string query)
        {
            try
            {
                var escapedQuery = query.Replace("\"", "\\\"");
                var searchProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = obsidianExe,
                    Arguments = $"vault=\"{vaultName}\" search query=\"{escapedQuery}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    // 关键：obsidian-cli 输出编码在 Windows 上经常不是 UTF-8
                    // 退回到系统默认编码，避免中文路径乱码。
                    StandardOutputEncoding = System.Text.Encoding.Default,
                    StandardErrorEncoding = System.Text.Encoding.Default
                });

                if (searchProcess == null) return null;
                
                // 15 秒超时
                var timeoutTask = Task.Delay(15000);
                var outputTask = searchProcess.StandardOutput.ReadToEndAsync();
                
                var completed = await Task.WhenAny(outputTask, timeoutTask);
                if (completed == timeoutTask)
                {
                    _logger.LogWarning("obsidian-cli 搜索超时");
                    searchProcess.Kill();
                    return null;
                }
                
                var output = await outputTask;
                await searchProcess.WaitForExitAsync();
                
                if (searchProcess.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                    return null;

                // 解析输出
                var results = new List<SearchResult>();
                foreach (var line in output.Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)))
                {
                    if (line.StartsWith("path") || line.StartsWith("-") || line.StartsWith("id"))
                        continue;
                    
                    var path = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (!string.IsNullOrEmpty(path) && path.EndsWith(".md"))
                    {
                        var relativePath = NormalizeRelPath(path, removeNotesPrefix: true);
                        var title = System.IO.Path.GetFileNameWithoutExtension(path);
                        
                        results.Add(new SearchResult
                        {
                            Id = title,
                            Title = title,
                            Path = relativePath,
                            Preview = $"[obsidian-cli] {path}",
                            Score = 10
                        });
                    }
                }

                return results.Count > 0 ? results : null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "obsidian-cli 搜索失败");
                return null;
            }
        }

        /// <summary>
        /// 直接扫描文件搜索
        /// </summary>
        private async Task<List<SearchResult>> SearchByScanningFiles(string vaultPath, string query)
        {
            var results = new List<SearchResult>();
            var queryLower = query.ToLower();

            var files = System.IO.Directory.GetFiles(vaultPath, "*.md", System.IO.SearchOption.AllDirectories);
            
            foreach (var file in files)
            {
                try
                {
                    var fileName = System.IO.Path.GetFileName(file);
                    if (fileName.Equals("README.md", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var content = await System.IO.File.ReadAllTextAsync(file);
                    var title = System.IO.Path.GetFileNameWithoutExtension(file);
                    var relativePath = NormalizeRelPath(
                        file.Substring(vaultPath.Length),
                        removeNotesPrefix: true
                    );
                    
                    var titleMatch = title.ToLower().Contains(queryLower);
                    var contentMatch = content.ToLower().Contains(queryLower);
                    
                    if (titleMatch || contentMatch)
                    {
                        var preview = ExtractPreview(content, queryLower);
                        
                        results.Add(new SearchResult
                        {
                            Id = title,
                            Title = title,
                            Path = relativePath,
                            Preview = preview,
                            Score = titleMatch ? 10 : 5
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "读取文件失败：{File}", file);
                }
            }

            return results.OrderByDescending(r => r.Score).Take(50).ToList();
        }

        /// <summary>
        /// 统一 Path 的分隔符为正斜杠，并在可能时剔除 "notes/" 前缀。
        /// 这样前端分组规则（按 "症状/" 等字符串匹配）在 Windows/Linux 都一致。
        /// </summary>
        private static string NormalizeRelPath(string raw, bool removeNotesPrefix)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            // raw 可能以 \ 或 / 开头（来自 file.Substring(vaultPath.Length)）
            var p = raw.Replace('\\', '/').Trim().TrimStart('/');

            if (p.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                p = p[..^3];
            }

            if (removeNotesPrefix && p.StartsWith("notes/", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring("notes/".Length);
            }

            return p;
        }

        private string ExtractPreview(string content, string query)
        {
            // 找到查询词的位置
            var index = content.ToLower().IndexOf(query);
            if (index < 0) index = 0;

            // 提取前后 100 个字符
            var start = Math.Max(0, index - 50);
            var length = Math.Min(200, content.Length - start);
            var preview = content.Substring(start, length);

            // 清理格式
            preview = preview.Replace("\n", " ").Replace("#", "").Replace("*", "");
            
            // 添加省略号
            if (start > 0) preview = "..." + preview;
            if (start + length < content.Length) preview = preview + "...";

            return preview.Trim();
        }
}
