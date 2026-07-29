using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using TaskRunner.Contracts.LocalModels;

namespace TaskRunner.Services;

public partial class HardwareInfoService
{
        private MemoryInfoDto GetMemoryInfo()
        {
            var mem = new MemoryInfoDto();
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    EnrichMemoryWindows(mem);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    EnrichMemoryLinux(mem);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    EnrichMemoryMac(mem);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "内存信息检测失败");
            }
            return mem;
        }

        private void EnrichMemoryWindows(MemoryInfoDto mem)
        {
            var output = HardwareInfoHelper.RunCommand("wmic", "computersystem get TotalPhysicalMemory /value", 5000);
            if (long.TryParse(HardwareInfoHelper.ExtractWmicValue(output ?? "", "TotalPhysicalMemory"), out var total))
                mem.TotalBytes = total;

            // Available memory via WMI
            var osOutput = HardwareInfoHelper.RunCommand("wmic", "os get FreePhysicalMemory /value", 5000);
            if (long.TryParse(HardwareInfoHelper.ExtractWmicValue(osOutput ?? "", "FreePhysicalMemory"), out var freeKb))
                mem.AvailableBytes = freeKb * 1024;
        }

        private void EnrichMemoryLinux(MemoryInfoDto mem)
        {
            try
            {
                var meminfo = File.ReadAllText("/proc/meminfo");
                mem.TotalBytes = HardwareInfoHelper.ParseMeminfoKB(meminfo, "MemTotal") * 1024;
                mem.AvailableBytes = HardwareInfoHelper.ParseMeminfoKB(meminfo, "MemAvailable") * 1024;

                // 如果 MemAvailable 不存在（旧内核），用 MemFree + Buffers + Cached
                if (mem.AvailableBytes == 0)
                {
                    var free = HardwareInfoHelper.ParseMeminfoKB(meminfo, "MemFree");
                    var buffers = HardwareInfoHelper.ParseMeminfoKB(meminfo, "Buffers");
                    var cached = HardwareInfoHelper.ParseMeminfoKB(meminfo, "Cached");
                    mem.AvailableBytes = (free + buffers + cached) * 1024;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "读取 /proc/meminfo 失败");
            }
        }

        private void EnrichMemoryMac(MemoryInfoDto mem)
        {
            if (long.TryParse(HardwareInfoHelper.RunCommand("sysctl", "-n hw.memsize", 5000)?.Trim(), out var total))
                mem.TotalBytes = total;

            // vm_statistics64 for available memory
            var vmStats = HardwareInfoHelper.RunCommand("vm_stat", "", 5000);
            if (!string.IsNullOrEmpty(vmStats))
            {
                var pageSize = 4096L; // default
                if (long.TryParse(HardwareInfoHelper.RunCommand("sysctl", "-n vm.pagesize", 5000)?.Trim(), out var ps))
                    pageSize = ps;

                var freePages = HardwareInfoHelper.ParseVmStat(vmStats, "Pages free");
                var inactivePages = HardwareInfoHelper.ParseVmStat(vmStats, "Pages inactive");
                mem.AvailableBytes = (freePages + inactivePages) * pageSize;
            }
        }

    }
