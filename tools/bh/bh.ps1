#requires -Version 5.1
<#
  bh - baihua 统一 CLI 入口（Windows）
  路由到 tools/bh/<os>/<deployment>/ 下的 cell 脚本。

  Cells:
    native    Windows native（dotnet 进程管理）    win\native\bh.ps1
    docker    Windows docker（compose）            win\docker\bh.ps1
    k8s       Linux k3s（经 WSL，root）            linux\k8s\bh.sh

  用法:
    bh <cell> <command> [args]   路由到指定 cell
    bh <command> [args]          使用默认 cell（native，Windows 平台）
    bh install                   复制自包含定位器到 %USERPROFILE%\.local\bin（含 bh.cmd shim，加入用户 PATH）
    bh uninstall                 移除定位器与 PATH 项
#>
[CmdletBinding()]
param(
    # 注意：PowerShell 5.1 中 ValueFromRemainingArguments 会抢占位置参数绑定，
    # 因此不定义 Position 参数，全部裸参数收进 $All 后手动切分（$All[0] = 命令/cell）
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$All = @()
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSCommandPath          # tools/bh
$Repo = Split-Path -Parent (Split-Path -Parent $Root)   # 仓库根（分派器在 tools/bh，比 cell 脚本浅 2 级）

$Arg1 = if ($All.Count -gt 0) { $All[0] } else { '' }
# @(...) 强制数组：强类型数组的单元素范围索引（$All[1..1]）会返回裸 string，
# 后续 @splat 会把字符串拆成字符数组，必须包成 object[]
$Rest = @($All | Select-Object -Skip 1)

$Cells = @{
    'native' = @{ Script = 'win\native\bh.ps1'; Desc = 'Windows native（dotnet 进程）' }
    'docker' = @{ Script = 'win\docker\bh.ps1'; Desc = 'Windows docker（compose）' }
    'k8s'    = @{ Script = 'linux\k8s\bh.sh';    Desc = 'Linux k3s（经 WSL，root）' }
}
$DefaultCell = 'native'

function Show-Help {
    Write-Host 'bh - baihua 统一 CLI（Windows）'
    Write-Host ''
    Write-Host '用法:'
    Write-Host '  bh <cell> <command> [args]    路由到指定 cell'
    Write-Host '  bh <command> [args]           默认 cell（native）'
    Write-Host '  bh install / uninstall        加入 / 移出用户 PATH'
    Write-Host ''
    Write-Host 'cells:'
    foreach ($k in ($Cells.Keys | Sort-Object)) {
        Write-Host ('  {0,-8} {1}' -f $k, $Cells[$k].Desc)
    }
    Write-Host ''
    Write-Host 'cell 内可用命令: bh <cell> help'
}

function Invoke-Install {
    # 复制自包含定位器（locator.ps1 + bh.cmd shim）到 %USERPROFILE%\.local\bin，
    # 并把该目录加入用户 PATH。定位器每次调用时自动定位仓库根（BAIHUA_HOME > 常见路径 > 向上查找），
    # 仓库改名/移动后无需重装。
    $bin = Join-Path $HOME '.local\bin'
    New-Item -ItemType Directory -Force -Path $bin | Out-Null
    Copy-Item (Join-Path $Root 'locator.ps1') (Join-Path $bin 'bh.ps1') -Force
    Copy-Item (Join-Path $Root 'bh.cmd') (Join-Path $bin 'bh.cmd') -Force

    # 幂等：把 $bin 追加到用户 PATH（HKCU\Environment）
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @($userPath -split ';' | Where-Object { $_ -ne '' })
    $already = $parts | Where-Object { $_.TrimEnd('\') -ieq $bin }
    if ($already) {
        Write-Host "[install] 已在用户 PATH: $bin"
    } else {
        $newPath = (@($parts) + $bin) -join ';'
        [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
        Write-Host "[install] 已加入用户 PATH: $bin"
    }
    # 广播环境变量变更（explorer 与后续进程可见）
    Add-Type -Namespace Win32 -Name Native -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);
'@ -ErrorAction SilentlyContinue
    $null = [Win32.Native]::SendMessageTimeout([IntPtr]::Zero, 0x1A, [UIntPtr]::Zero, 'Environment', 2, 5000, [ref]([UIntPtr]::Zero))
    Write-Host '[install] 完成。新开终端后可直接使用: bh <command>'
    Write-Host '         当前会话请用: .\tools\bh\bh.ps1 <command>'
    Write-Host '[install] 定位器自动查找: $env:BAIHUA_HOME > 常见路径 > 当前目录向上；仓库改名/移动后无需重新安装'
}

function Invoke-Uninstall {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @($userPath -split ';' | Where-Object { $_ -ne '' })
    $bin = Join-Path $HOME '.local\bin'
    $kept = @($parts | Where-Object { $_.TrimEnd('\') -ine $bin })
    if ($kept.Count -eq $parts.Count) {
        Write-Host "[uninstall] 用户 PATH 中未找到: $bin"
    } else {
        [Environment]::SetEnvironmentVariable('Path', $kept -join ';', 'User')
        Write-Host "[uninstall] 已从用户 PATH 移除: $bin"
    }
    Remove-Item (Join-Path $bin 'bh.ps1') -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $bin 'bh.cmd') -Force -ErrorAction SilentlyContinue
    Write-Host '[uninstall] 已移除 %USERPROFILE%\.local\bin\bh.ps1 / bh.cmd'
}

$cell = $Arg1.ToLower()
if ($cell -in @('install', 'uninstall')) {
    if ($cell -eq 'install') { Invoke-Install } else { Invoke-Uninstall }
    exit 0
}

if ($Cells.ContainsKey($cell)) {
    # 显式 cell 路由
    if ($cell -eq 'k8s') {
        # Windows → WSL：路径映射 + root（bh.sh 需访问 buildkit socket）
        # 反斜杠转正斜杠：wsl.exe 传参会剥离 \，wslpath -u 接受正斜杠形式
        $wslRepo = (wsl wslpath -u ($Repo -replace '\\', '/') 2>$null | Out-String).Trim()
        if (-not $wslRepo) { Write-Error '[k8s] wslpath 不可用，请确认 WSL 已安装'; exit 1 }
        $inner = ($Rest | ForEach-Object { "'" + ($_ -replace "'", "'\''") + "'" }) -join ' '
        wsl -u root -e bash -lc "cd '$wslRepo' && tools/bh/linux/k8s/bh.sh $inner"
        exit $LASTEXITCODE
    }
    & (Join-Path $Root $Cells[$cell].Script) @Rest
    exit $LASTEXITCODE
}

# 默认 cell 透传（第一个参数视为 cell 内命令）
# 注意：$Arg1 是 string，不能 @splat（会把字符串拆成字符数组），直接位置传参；$Rest 是数组才 splat
& (Join-Path $Root $Cells[$DefaultCell].Script) $Arg1 @Rest
exit $LASTEXITCODE
