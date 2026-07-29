using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using TaskRunner.Contracts.LocalModels;

namespace TaskRunner.Services;

public partial class HardwareInfoService
{
        private CpuInfoDto GetCpuInfo()
        {
            var cpu = new CpuInfoDto
            {
                LogicalProcessorCount = Environment.ProcessorCount,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            };

            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    EnrichCpuInfoWindows(cpu);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    EnrichCpuInfoLinux(cpu);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    EnrichCpuInfoMac(cpu);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CPU 信息检测失败");
            }

            if (string.IsNullOrEmpty(cpu.Name))
                cpu.Name = $"Unknown {cpu.Architecture} CPU";

            return cpu;
        }

        private void EnrichCpuInfoWindows(CpuInfoDto cpu)
        {
            var output = HardwareInfoHelper.RunCommand("wmic", "cpu get Name,NumberOfCores,NumberOfLogicalProcessors /value", 5000);
            if (string.IsNullOrEmpty(output)) return;

            cpu.Name = HardwareInfoHelper.ExtractWmicValue(output, "Name") ?? cpu.Name;
            if (int.TryParse(HardwareInfoHelper.ExtractWmicValue(output, "NumberOfCores"), out var cores))
                cpu.CoreCount = cores;
            if (int.TryParse(HardwareInfoHelper.ExtractWmicValue(output, "NumberOfLogicalProcessors"), out var logical))
                cpu.LogicalProcessorCount = logical;
        }

        private void EnrichCpuInfoLinux(CpuInfoDto cpu)
        {
            // 优先 lscpu，更可靠。使用 LC_ALL=C 确保输出为英文，不受系统语言影响
            var lscpu = HardwareInfoHelper.RunCommand("lscpu", "", 5000, new Dictionary<string, string> { ["LC_ALL"] = "C" });
            if (!string.IsNullOrEmpty(lscpu))
            {
                cpu.Name = HardwareInfoHelper.ExtractLineValue(lscpu, "Model name:") ?? cpu.Name;
                if (int.TryParse(HardwareInfoHelper.ExtractLineValue(lscpu, "Core(s) per socket:"), out var coresPerSocket))
                {
                    if (int.TryParse(HardwareInfoHelper.ExtractLineValue(lscpu, "Socket(s):"), out var sockets))
                        cpu.CoreCount = coresPerSocket * sockets;
                }
                if (int.TryParse(HardwareInfoHelper.ExtractLineValue(lscpu, "CPU(s):"), out var cpus))
                    cpu.LogicalProcessorCount = cpus;
                cpu.MaxFrequencyMHz = HardwareInfoHelper.ExtractLineValue(lscpu, "CPU max MHz:") ?? HardwareInfoHelper.ExtractLineValue(lscpu, "CPU MHz:");
                return;
            }

            // 回退到 /proc/cpuinfo
            try
            {
                var cpuinfo = File.ReadAllText("/proc/cpuinfo");
                var modelName = HardwareInfoHelper.ExtractRegex(cpuinfo, @"model name\s*:\s*(.+)", RegexOptions.Multiline);
                if (!string.IsNullOrEmpty(modelName))
                    cpu.Name = modelName;

                var physicalIds = Regex.Matches(cpuinfo, @"physical id\s*:\s*(\d+)")
                    .Select(m => m.Groups[1].Value)
                    .Distinct()
                    .Count();
                var coresPerCpu = HardwareInfoHelper.ExtractRegex(cpuinfo, @"cpu cores\s*:\s*(\d+)", RegexOptions.Multiline);
                if (int.TryParse(coresPerCpu, out var cpc) && physicalIds > 0)
                    cpu.CoreCount = cpc * physicalIds;
                else
                    cpu.CoreCount = cpu.LogicalProcessorCount;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "读取 /proc/cpuinfo 失败");
            }
        }

        private void EnrichCpuInfoMac(CpuInfoDto cpu)
        {
            cpu.Name = HardwareInfoHelper.RunCommand("sysctl", "-n machdep.cpu.brand_string", 5000)?.Trim() ?? cpu.Name;
            if (int.TryParse(HardwareInfoHelper.RunCommand("sysctl", "-n hw.physicalcpu", 5000)?.Trim(), out var phys))
                cpu.CoreCount = phys;
            if (int.TryParse(HardwareInfoHelper.RunCommand("sysctl", "-n hw.logicalcpu", 5000)?.Trim(), out var log))
                cpu.LogicalProcessorCount = log;
        }

    }
