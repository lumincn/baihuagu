using Microsoft.Extensions.Logging;
using TaskRunner.Core.Shared;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TaskRunner.Core.Shared.Hubs;
using TaskRunner.Data;

namespace TaskRunner.Services
{
    public class TaskManager
    {
        private readonly ConcurrentDictionary<string, TaskInfo> _tasks = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningCts = new();
        private readonly IHubContext<TaskProgressHub>? _hubContext;
        private readonly IDbContextFactory<FamilyDbContext> _dbContextFactory;
        private readonly ILogger<TaskManager>? _logger;

        public TaskManager(IDbContextFactory<FamilyDbContext> dbContextFactory, IHubContext<TaskProgressHub>? hubContext = null, ILogger<TaskManager>? logger = null)
        {
            _dbContextFactory = dbContextFactory;
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task NotifySupplementEventAsync(string taskId, string eventName, object? data = null)
        {
            if (_hubContext == null) return;
            try
            {
                await _hubContext.Clients.All.SendAsync("SupplementEvent", new { TaskId = taskId, Event = eventName, Data = data });
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("推送 SupplementEvent 失败：{Message}", ex.Message);
            }
        }

        public string CreateTask(string type, Dictionary<string, string>? parameters = null)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var taskId = Guid.NewGuid().ToString("N");
            var taskInfo = new TaskInfo
            {
                Id = taskId,
                Type = type,
                Status = RunnerTaskStatus.Pending,
                Parameters = parameters,
                Progress = new TaskProgress { Current = 0, Total = 1, Message = "任务已创建" },
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            _tasks[taskId] = taskInfo;
            
            // 持久化到 SQLite
            try
            {
                dbContext.Tasks.Add(new Data.Entities.TaskEntity
                {
                    TaskId = taskId,
                    TaskType = type,
                    Status = "Pending",
                    Input = parameters != null ? JsonSerializer.Serialize(parameters) : null,
                    Progress = 0,
                    ProgressMessage = "任务已创建"
                });
                dbContext.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "保存任务到数据库失败");
            }
            
            _ = NotifyTaskUpdateAsync(taskId);
            return taskId;
        }

        public TaskInfo? GetTask(string taskId)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            // 优先从内存获取
            if (_tasks.TryGetValue(taskId, out var task))
                return task;
            
            // 内存中没有，从 SQLite 加载
            var dbTask = dbContext.Tasks
                .FirstOrDefault(t => t.TaskId == taskId);
                
            if (dbTask == null) return null;
            
            task = MapFromEntity(dbTask);
            _tasks[taskId] = task;
            return task;
        }

        public List<TaskInfo> GetAllTasks(int limit = 100, int offset = 0)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            // 从 SQLite 查询任务历史
            return dbContext.Tasks
                .OrderByDescending(t => t.CreatedAt)
                .Skip(offset)
                .Take(limit)
                .Select(MapFromEntity)
                .ToList();
        }

        public List<TaskInfo> GetTasksByStatus(string status, int limit = 100)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            return dbContext.Tasks
                .Where(t => t.Status == status)
                .OrderByDescending(t => t.CreatedAt)
                .Take(limit)
                .Select(MapFromEntity)
                .ToList();
        }

        public bool DeleteTask(string taskId)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            _tasks.TryRemove(taskId, out _);
            try
            {
                var task = dbContext.Tasks.FirstOrDefault(t => t.TaskId == taskId);
                if (task != null)
                {
                    dbContext.Tasks.Remove(task);
                    dbContext.SaveChanges();
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "删除任务失败");
                return false;
            }
        }

        public int CleanupOldTasks(TimeSpan retentionPeriod)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            var cutoffDate = DateTime.UtcNow - retentionPeriod;
            try
            {
                // 清理内存中的旧任务
                var oldTasks = _tasks.Values.Where(t => t.UpdatedAt < cutoffDate).ToList();
                foreach (var task in oldTasks)
                {
                    _tasks.TryRemove(task.Id, out _);
                }
                
                // 清理数据库中的旧任务
                var cutoffStr = cutoffDate.ToString("yyyy-MM-dd HH:mm:ss");
                var oldDbTasks = dbContext.Tasks
                    .Where(t => t.CreatedAt < cutoffDate)
                    .ToList();
                    
                dbContext.Tasks.RemoveRange(oldDbTasks);
                dbContext.SaveChanges();
                
                return oldTasks.Count;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "清理旧任务失败");
                return 0;
            }
        }

        public int CleanupAllCompletedTasks()
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            try
            {
                // 清理内存中已完成的任务
                var completedStatuses = new[] { RunnerTaskStatus.Success, RunnerTaskStatus.Failed, RunnerTaskStatus.Timeout, RunnerTaskStatus.Cancelled };
                var completedTasks = _tasks.Values.Where(t => completedStatuses.Contains(t.Status)).ToList();
                foreach (var task in completedTasks)
                {
                    _tasks.TryRemove(task.Id, out _);
                }
                
                // 清理数据库中已完成的任务
                var completedDbStatuses = new[] { "Success", "Failed", "Cancelled" };
                var completedDbTasks = dbContext.Tasks
                    .Where(t => completedDbStatuses.Contains(t.Status))
                    .ToList();
                    
                dbContext.Tasks.RemoveRange(completedDbTasks);
                dbContext.SaveChanges();
                
                return completedTasks.Count;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "清理已完成任务失败");
                return 0;
            }
        }

        public int DeleteAllTasks()
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            try
            {
                // 清空内存中的所有任务
                _tasks.Clear();
                
                // 清空数据库中的所有任务
                var allTasks = dbContext.Tasks.ToList();
                dbContext.Tasks.RemoveRange(allTasks);
                dbContext.SaveChanges();
                
                return 1;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "清空所有任务失败");
                return 0;
            }
        }

        public int GetTaskCount()
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            return dbContext.Tasks.Count();
        }

        public CancellationTokenSource CreateTaskCts(string taskId, TimeSpan? timeout = null)
        {
            var cts = timeout.HasValue
                ? new CancellationTokenSource(timeout.Value)
                : new CancellationTokenSource();
            _runningCts[taskId] = cts;
            return cts;
        }

        public void RemoveTaskCts(string taskId)
        {
            if (_runningCts.TryRemove(taskId, out var cts))
            {
                try { cts.Dispose(); } catch { /* 已释放或已取消，无需处理 */ }
            }
        }

        public async Task<bool> CancelTaskAsync(string taskId)
        {
            var task = GetTask(taskId);
            if (task == null) return false;

            if (task.Status != RunnerTaskStatus.Running && task.Status != RunnerTaskStatus.Pending)
            {
                return false;
            }

            if (_runningCts.TryGetValue(taskId, out var cts))
            {
                try { cts.Cancel(); } catch { /* 已取消或已释放，无需处理 */ }
            }

            await UpdateStatus(taskId, RunnerTaskStatus.Cancelled, "用户已取消");
            return true;
        }

        public async Task UpdateStatus(string taskId, RunnerTaskStatus status, string? error = null, object? data = null)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            if (_tasks.TryGetValue(taskId, out var task))
            {
                task.Status = status;
                task.UpdatedAt = DateTime.UtcNow;
                
                var resultData = data != null ? JsonSerializer.Serialize(data) : null;
                var errorMsg = error;
                
                if (!string.IsNullOrEmpty(error) || data != null)
                {
                    task.Result = new TaskResult
                    {
                        Success = status == RunnerTaskStatus.Success,
                        Error = error,
                        Data = data
                    };
                }
                
                if (status == RunnerTaskStatus.Success && task.Progress.Total > 0)
                {
                    task.Progress.Current = task.Progress.Total;
                    task.Progress.Percentage = 100.0;
                    task.Progress.Message = "任务完成";
                }
                
                if (status == RunnerTaskStatus.Failed && !string.IsNullOrEmpty(task.Progress.Message))
                {
                    if (!task.Progress.Message.Contains("(失败)") && !task.Progress.Message.Contains("失败") && !task.Progress.Message.StartsWith("❌"))
                    {
                        task.Progress.Message += " (失败)";
                    }
                }
                
                // 更新 SQLite
                try
                {
                    var dbTask = dbContext.Tasks.FirstOrDefault(t => t.TaskId == taskId);
                    if (dbTask != null)
                    {
                        dbTask.Status = status.ToString();
                        dbTask.Error = errorMsg;
                        dbTask.Output = resultData;
                        dbTask.Progress = (int)task.Progress.Percentage;
                        dbTask.ProgressMessage = task.Progress.Message;
                        if (status == RunnerTaskStatus.Running && !dbTask.StartedAt.HasValue)
                            dbTask.StartedAt = DateTime.UtcNow;
                        if (status is RunnerTaskStatus.Success or RunnerTaskStatus.Failed or RunnerTaskStatus.Timeout)
                            dbTask.CompletedAt = DateTime.UtcNow;
                        dbContext.SaveChanges();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "更新任务状态到数据库失败");
                }
                
                await NotifyTaskUpdateAsync(taskId);
            }
        }

        public async Task UpdateProgress(string taskId, int current, int total, string message)
        {
            using var dbContext = _dbContextFactory.CreateDbContext();
            if (_tasks.TryGetValue(taskId, out var task))
            {
                // 首次更新进度时自动将 Pending 转为 Running
                var prevStatus = task.Status;
                if (task.Status == RunnerTaskStatus.Pending)
                {
                    task.Status = RunnerTaskStatus.Running;
                }

                var newProgress = new TaskProgress
                {
                    Current = current,
                    Total = total,
                    Message = message,
                    Percentage = total > 0 ? (double)current / total * 100 : 0
                };
                var oldProgress = task.Progress;
                task.Progress = newProgress;
                task.UpdatedAt = DateTime.UtcNow;
                
                // 更新 SQLite
                try
                {
                    var dbTask = dbContext.Tasks.FirstOrDefault(t => t.TaskId == taskId);
                    if (dbTask != null)
                    {
                        // 如果状态从 Pending 变为 Running，同步更新数据库状态
                        if (prevStatus == RunnerTaskStatus.Pending && task.Status == RunnerTaskStatus.Running)
                        {
                            dbTask.Status = "Running";
                            dbTask.StartedAt = DateTime.UtcNow;
                        }
                        dbTask.Progress = (int)task.Progress.Percentage;
                        dbTask.ProgressMessage = message;
                        dbContext.SaveChanges();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "更新任务进度到数据库失败");
                    // DB 写入失败时回滚内存状态，保持与数据库一致
                    task.Status = prevStatus;
                    task.Progress = oldProgress;
                }
                
                await NotifyTaskUpdateAsync(taskId);
            }
        }

        private async Task NotifyTaskUpdateAsync(string taskId)
        {
            if (_hubContext == null) return;
            
            try
            {
                var task = GetTask(taskId);
                if (task != null)
                {
                    await _hubContext.Clients.All.SendAsync("TaskUpdated", task);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("推送任务更新失败：{Message}", ex.Message);
            }
        }

        private static TaskInfo MapFromEntity(Data.Entities.TaskEntity entity)
        {
            var parameters = string.IsNullOrEmpty(entity.Input) 
                ? null 
                : JsonSerializer.Deserialize<Dictionary<string, string>>(entity.Input);
            
            object? resultData = null;
            if (!string.IsNullOrEmpty(entity.Output))
            {
                try { resultData = JsonSerializer.Deserialize<object>(entity.Output); } catch { /* 反序列化失败时返回null */ }
            }
            
            return new TaskInfo
            {
                Id = entity.TaskId,
                Type = entity.TaskType,
                Status = Enum.Parse<RunnerTaskStatus>(entity.Status),
                Parameters = parameters,
                Progress = new TaskProgress
                {
                    Current = entity.Progress,
                    Total = 100,
                    Message = entity.ProgressMessage ?? "",
                    Percentage = entity.Progress
                },
                Result = !string.IsNullOrEmpty(entity.Output) || !string.IsNullOrEmpty(entity.Error) 
                    ? new TaskResult
                    {
                        Success = entity.Status == "Success",
                        Error = entity.Error,
                        Data = resultData
                    } 
                    : null,
                CreatedAt = entity.CreatedAt,
                UpdatedAt = entity.UpdatedAt
            };
        }
    }



}
