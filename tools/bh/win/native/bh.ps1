#requires -Version 5.1
<#
  baihua - Windows + dotnet native CLI
  Cell of the matrix: OS=windows, deployment=dotnet-native
  Manages the 4 .NET services (vault/ai/family/webui) as local processes.

  Usage: .\tools\bh\win\native\bh.ps1 <command> [args]
    build               dotnet publish the 4 services to out/native/
    start               start all 4 services (processes, pid files)
    stop                stop all 4 services
    restart             stop + start
    status              show port/process state per service
    logs <svc> [n]      tail service log (default 50 lines)
    dashboard           open browser with cli-token auto-login
    open                open browser to http://localhost:5177
    help                this help
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Command = 'help',
    [Parameter(Position = 1)]
    [string]$Arg1 = '',
    [Parameter(Position = 2)]
    [string]$Arg2 = ''
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSCommandPath
$Root = Split-Path -Parent $Root
$Root = Split-Path -Parent $Root
$Root = Split-Path -Parent $Root
$Root = Split-Path -Parent $Root
$OutDir = Join-Path $Root 'out\native'
$PidDir = Join-Path $OutDir 'pids'
$LogDir = Join-Path $OutDir 'logs'
$DataHome = if ($env:BAIHUA_HOME) { $env:BAIHUA_HOME } else { Join-Path $HOME '.baihua' }

$Services = @(
    @{ Name = 'vault';  Project = 'services\Baihua.Vault';  Exe = 'bh-vault.exe';  Port = 8790 },
    @{ Name = 'ai';     Project = 'services\Baihua.AI';     Exe = 'bh-ai.exe';     Port = 8791 },
    @{ Name = 'family'; Project = 'services\Baihua.Family'; Exe = 'bh-family.exe'; Port = 8788 },
    @{ Name = 'webui';  Project = 'services\Baihua.Web';    Exe = 'bh-webui.exe';  Port = 5177 }
)

function Help-Text {
    # 只取头部注释里的用法行（4 空格缩进）；否则函数体内的缩进行也会匹配输出
    Get-Content $PSCommandPath | Select-Object -First 20 | Where-Object { $_ -match '^\s{4}[a-z]' } | ForEach-Object { $_.Trim() }
}

function Ensure-Dotnet {
    if (Get-Command dotnet -ErrorAction SilentlyContinue) { return }
    Write-Host '[deps] dotnet 缺失，自动安装（winget install Microsoft.DotNet.SDK.10）...'
    & winget install --id Microsoft.DotNet.SDK.10 --accept-source-agreements --accept-package-agreements --silent
    if ($LASTEXITCODE -ne 0) {
        Write-Host '[deps] winget 安装失败，请手动安装 .NET SDK 10: https://dotnet.microsoft.com/download'
        exit 1
    }
    $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    Write-Host '[deps] dotnet 安装完成'
}

function Invoke-Build {
    Ensure-Dotnet
    # 服务进程会锁住 out/native 下的 dll，publish 前必须先停
    Stop-Services
    foreach ($svc in $Services) {
        Write-Host "[build] $($svc.Name) ..."
        # Project 是相对仓库根的路径，必须 Join-Path $Root（脚本可能从任意目录执行）
        $proj = Join-Path $Root $svc.Project
        $out = & dotnet publish $proj -c Release -r win-x64 --self-contained false -o (Join-Path $OutDir $svc.Name) 2>&1
        if ($LASTEXITCODE -ne 0) { throw "publish failed: $($svc.Name)`n$($out | Select-Object -Last 6)" }
    }
    Write-Host "[build] done -> $OutDir"
}

function Get-PidFile($name) { Join-Path $PidDir "$name.pid" }

function Test-PortOpen($port) {
    $c = New-Object Net.Sockets.TcpClient
    try { $c.Connect('127.0.0.1', $port); return $true } catch { return $false } finally { $c.Dispose() }
}

function Wait-Port($port, $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-PortOpen $port) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Start-One($svc) {
    $exe = Join-Path $OutDir "$($svc.Name)\$($svc.Exe)"
    if (-not (Test-Path $exe)) { throw "not built: $exe (run 'build' first)" }
    if (Test-PortOpen $svc.Port) { Write-Warning "[$($svc.Name)] port $($svc.Port) already in use, skip"; return }
    $envBlock = @{
        BAIHUA_HOME = $DataHome
        BAIHUA_SKIP_MUTEX = 'true'
        ASPNETCORE_URLS = "http://127.0.0.1:$($svc.Port)"
        OpenObserve__Enabled = 'false'
    }
    if ($svc.Name -eq 'family') {
        $envBlock['BAIHUA_VAULT_URL'] = 'http://127.0.0.1:8790'
        $envBlock['BAIHUA_AI_URL'] = 'http://127.0.0.1:8791'
    }
    if ($svc.Name -eq 'webui') {
        $envBlock['WEBUI_CONFIG_DIR'] = $DataHome
        $envBlock['FamilyApi__BaseUrl'] = 'http://127.0.0.1:8788/'
        $envBlock['AiApi__BaseUrl'] = 'http://127.0.0.1:8791/'
        $envBlock['VaultApi__BaseUrl'] = 'http://127.0.0.1:8790/'
    }
    foreach ($k in $envBlock.Keys) { Set-Item -Path ('Env:' + $k) -Value $envBlock[$k] }
    New-Item -ItemType Directory -Force -Path $PidDir, $LogDir | Out-Null
    $logFile = Join-Path $LogDir "$($svc.Name).log"
    $p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -RedirectStandardOutput $logFile -RedirectStandardError "$logFile.err" -PassThru -WindowStyle Hidden
    Set-Content -Path (Get-PidFile $svc.Name) -Value $p.Id
    Write-Host "[$($svc.Name)] started pid=$($p.Id) port=$($svc.Port) log=$logFile"
}

function Start-Services {
    New-Item -ItemType Directory -Force -Path $DataHome | Out-Null
    foreach ($svc in $Services) { Start-One $svc }
    Write-Host "[start] waiting for health ..."
    $ok = $true
    foreach ($svc in $Services) {
        if (-not (Wait-Port $svc.Port 60)) { Write-Warning "[$($svc.Name)] port $($svc.Port) not ready in 60s"; $ok = $false }
        else { Write-Host "[$($svc.Name)] ready on $($svc.Port)" }
    }
    if ($ok) { Write-Host "[start] all services up. WebUI: http://localhost:5177" }
}

function Stop-One($svc) {
    $pf = Get-PidFile $svc.Name
    $stopped = $false
    if (Test-Path $pf) {
        $pid2 = [int](Get-Content $pf)
        $proc = Get-Process -Id $pid2 -ErrorAction SilentlyContinue
        if ($proc) { Stop-Process -Id $pid2 -Force -ErrorAction SilentlyContinue; Write-Host "[$($svc.Name)] stopped pid=$pid2"; $stopped = $true }
        Remove-Item $pf -Force -ErrorAction SilentlyContinue
    }
    if (-not $stopped -and (Test-PortOpen $svc.Port)) {
        # 兜底：pid 文件缺失但端口被占（如进程被外部启动），按端口杀
        $conn = Get-NetTCPConnection -LocalPort $svc.Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($conn) { Stop-Process -Id $conn.OwningProcess -Force -ErrorAction SilentlyContinue; Write-Host "[$($svc.Name)] stopped by port pid=$($conn.OwningProcess)" }
    }
}

function Stop-Services {
    foreach ($svc in $Services) { Stop-One $svc }
    Write-Host '[stop] done'
}

# 解析单服务参数（vault|ai|family|webui），返回 $true 表示已处理（含报错）
function Resolve-SingleService($name, [ref]$svcRef) {
    if (-not $name) { return $false }
    $svc = $Services | Where-Object { $_.Name -eq $name.ToLower() }
    if (-not $svc) { Write-Host "unknown service: $name (vault|ai|family|webui)" -ForegroundColor Yellow; return $true }
    $svcRef.Value = $svc
    return $true
}

function Show-Status {
    foreach ($svc in $Services) {
        $portOpen = Test-PortOpen $svc.Port
        $pf = Get-PidFile $svc.Name
        $pidAlive = $false
        if (Test-Path $pf) {
            $pid2 = [int](Get-Content $pf)
            $pidAlive = [bool](Get-Process -Id $pid2 -ErrorAction SilentlyContinue)
        }
        $state = if ($portOpen -and $pidAlive) { 'RUNNING' } elseif ($portOpen) { 'PORT-OPEN(foreign)' } elseif ($pidAlive) { 'PROC-ALIVE' } else { 'stopped' }
        Write-Host ("{0,-8} port={1,-5} {2}" -f $svc.Name, $svc.Port, $state)
    }
}

function Show-Logs($svcName, $n) {
    $svc = $Services | Where-Object { $_.Name -eq $svcName }
    if (-not $svc) { Write-Host "unknown service: $svcName (vault|ai|family|webui)"; return }
    $log = Join-Path $LogDir "$svcName.log"
    if (-not (Test-Path $log)) { Write-Host "no log yet: $log"; return }
    # 编码容错：Windows 控制台中文环境（GBK）下 Get-Content -Tail 会因非法 UTF-8 静默返回空，
    # 改为整读 + 严格 UTF-8 解码，失败回退系统 ANSI 代码页。
    # FileShare.ReadWrite：服务进程正在写 log 时 ReadAllBytes 会独占失败。
    $fs = [System.IO.File]::Open($log, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $bytes = New-Object byte[] ([int]$fs.Length)
        [void]$fs.Read($bytes, 0, $bytes.Length)
    } finally { $fs.Dispose() }
    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    try { $text = $utf8.GetString($bytes) }
    catch { $text = [System.Text.Encoding]::GetEncoding([System.Globalization.CultureInfo]::CurrentCulture.TextInfo.ANSICodePage).GetString($bytes) }
    ($text -split "\r?\n") | Where-Object { $_ -ne '' } | Select-Object -Last $n
}

function Open-Dashboard {
    try {
        $resp = Invoke-WebRequest -Uri 'http://127.0.0.1:5177/api/auth/cli-token' -Method POST -UseBasicParsing -TimeoutSec 5
        $token = ($resp.Content | ConvertFrom-Json).token
        Start-Process "http://127.0.0.1:5177/?cli-token=$token"
        Write-Host "[dashboard] opened with cli-token"
    } catch {
        Write-Host "[dashboard] cli-token failed ($($_.Exception.Message)), opening plain URL"
        Start-Process 'http://127.0.0.1:5177'
    }
}

switch ($Command.ToLower()) {
    'build'     { Invoke-Build }
    'start'     {
        if ($Arg1) {
            $svc = $null; if (Resolve-SingleService $Arg1 ([ref]$svc)) { Start-One $svc; Write-Host "[start] $($svc.Name) starting... check 'bh status'" }
        } else { Start-Services }
    }
    'stop'      {
        if ($Arg1) {
            $svc = $null; if (Resolve-SingleService $Arg1 ([ref]$svc)) { Stop-One $svc; Write-Host "[stop] $($svc.Name) stopped" }
        } else { Stop-Services }
    }
    'restart'   {
        if ($Arg1) {
            $svc = $null; if (Resolve-SingleService $Arg1 ([ref]$svc)) { Stop-One $svc; Start-One $svc; Write-Host "[restart] $($svc.Name) restarting..." }
        } else { Stop-Services; Start-Services }
    }
    'status'    { Show-Status }
    'logs'      { $count = 50; if ($Arg2) { $count = [int]$Arg2 }; Show-Logs $Arg1 $count }
    'dashboard' { Open-Dashboard }
    'open'      { Start-Process 'http://127.0.0.1:5177' }
    'help'      { Help-Text }
    default     { Help-Text }
}
