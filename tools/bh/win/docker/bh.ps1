#requires -Version 5.1
<#
  baihua - Windows + Docker compose + native AI CLI
  Cell of the matrix: OS=windows, deployment=docker (ai runs native for GPU)

  ai 服务（Baihua.AI + Baihua.AI.Provider）始终 native 运行（Windows 进程，直接访问
  Arc GPU 做 LlamaSharp/ONNX/OpenVINO 推理）；family/vault/webui/nginx/openobserve 走
  docker compose。compose 里 ai 服务带 profile "docker-ai"（默认不启动），容器通过
  host.docker.internal:8791 访问 native ai。

  Usage: .\tools\bh\win\docker\bh.ps1 <command> [args]
    build               dotnet publish ai + docker compose build (family/vault/webui)
    start               start native ai + compose up -d (family/vault/webui/nginx)
    stop                stop native ai + compose down
    restart             stop + start
    status              native ai process + docker compose ps
    logs <svc> [n]      tail logs (ai → out/native/logs/ai.log, 其余 → docker logs)
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
$DockerDir = Join-Path $Root 'docker'
$Compose = Join-Path $DockerDir 'docker-compose.yml'
$ComposeWin = Join-Path $DockerDir 'docker-compose.windows.yml'

# native ai 进程管理（与 tools/bh/win/native/bh.ps1 同套 pid/日志约定）
$AiOutDir = Join-Path $Root 'out\native\ai'
$AiPidFile = Join-Path $Root 'out\native\pids\ai.pid'
$AiLogFile = Join-Path $Root 'out\native\logs\ai.log'
$DataHome = if ($env:BAIHUA_HOME) { $env:BAIHUA_HOME } else { Join-Path $HOME '.baihua' }

# compose 服务列表（不含 ai——ai 是 native 的）
$Services = @('family', 'vault', 'webui', 'nginx', 'openobserve')

function Ensure-OpenObservePassword {
    # compose 要求 OPENOBSERVE_PASSWORD（OpenObserve 根密码，不再使用硬编码默认值）。
    # 缺失时生成随机密码并持久化到 docker/.env（已 gitignore），保证重启后一致。
    if ($env:OPENOBSERVE_PASSWORD) { return }
    $envFile = Join-Path $DockerDir '.env'
    if (Test-Path $envFile) {
        $line = Get-Content $envFile | Where-Object { $_ -match '^OPENOBSERVE_PASSWORD=' } | Select-Object -First 1
        if ($line) { $env:OPENOBSERVE_PASSWORD = ($line -split '=', 2)[1].Trim(); return }
    }
    $pass = -join ((48..57) + (97..122) + (65..90) | Get-Random -Count 24 | ForEach-Object { [char]$_ })
    Add-Content -Path $envFile -Value "OPENOBSERVE_PASSWORD=$pass"
    $env:OPENOBSERVE_PASSWORD = $pass
    Write-Host "[deps] OPENOBSERVE_PASSWORD 已生成并写入 docker/.env"
}

function Invoke-Compose {
    param([string[]]$ComposeArgs)
    Ensure-Docker
    Ensure-OpenObservePassword
    Push-Location $DockerDir
    try {
        $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & docker compose -f $Compose -f $ComposeWin @ComposeArgs 2>&1
        $code = $LASTEXITCODE
        $ErrorActionPreference = $prev
        if ($code -ne 0) { throw "docker compose failed (exit $code): $($ComposeArgs -join ' ')" }
    } finally {
        Pop-Location
    }
}

function Test-PortOpen($port) {
    $c = New-Object Net.Sockets.TcpClient
    try { $c.Connect('127.0.0.1', $port); return $true } catch { return $false } finally { $c.Dispose() }
}

# ---- OpenVINO 托管服务（Windows SCM 服务 BaihuaOpenVinoHost，端口 8866）----
# 本地模型推理（Arc GPU / OpenVINO）走宿主机托管服务，bh 只做启停编排，
# 注册/开机自启仍由 Windows SCM 负责（scripts/install-openvino-host-service.ps1）。
# 它是 native ai 本地推理的依赖：启动最先、停止最后（与"被依赖的先启动"一致）。
$OpenVinoServiceName = 'BaihuaOpenVinoHost'
$OpenVinoPort = 8866

function Wait-Port($port, $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-PortOpen $port) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

function Get-OpenVinoHostService {
    Get-Service -Name $OpenVinoServiceName -ErrorAction SilentlyContinue
}

function Start-OpenVinoHost {
    $svc = Get-OpenVinoHostService
    if (-not $svc) {
        Write-Warning "[openvino] 未安装服务 $OpenVinoServiceName（scripts/install-openvino-host-service.ps1 安装），本地 OpenVINO 推理不可用（云端 AI 不受影响）"
        return
    }
    if ($svc.Status -eq 'Running') { Write-Host '[openvino] already running (service)'; return }
    try {
        Start-Service -Name $OpenVinoServiceName -ErrorAction Stop
        Write-Host "[openvino] service starting (port $OpenVinoPort) ..."
        if (-not (Wait-Port $OpenVinoPort 30)) { Write-Warning "[openvino] port $OpenVinoPort not ready in 30s" }
        else { Write-Host "[openvino] ready on $OpenVinoPort" }
    } catch {
        Write-Warning "[openvino] 启动失败: $($_.Exception.Message)（云端 AI 不受影响）"
    }
}

function Stop-OpenVinoHost {
    $svc = Get-OpenVinoHostService
    if (-not $svc) { return }
    if ($svc.Status -eq 'Running') {
        try { Stop-Service -Name $OpenVinoServiceName -Force -ErrorAction Stop; Write-Host '[openvino] service stopped' }
        catch { Write-Warning "[openvino] 停止失败: $($_.Exception.Message)" }
    }
}

# ---- native ai ----

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

function Ensure-Docker {
    if (Get-Command docker -ErrorAction SilentlyContinue) { return }
    Write-Host '[deps] docker 缺失。Docker Desktop 需 GUI 交互安装，无法自动完成：'
    Write-Host '        winget install --id Docker.DockerDesktop'
    Write-Host '        或手动下载: https://www.docker.com/products/docker-desktop/'
    Write-Host '        安装后需启动 Docker Desktop 并等待引擎就绪'
    exit 1
}

function Invoke-Build-Ai {
    Ensure-Dotnet
    Write-Host '[build] ai (native publish) ...'
    & dotnet publish (Join-Path $Root 'services\Baihua.AI\Baihua.AI.csproj') -c Release -r win-x64 --self-contained false -o $AiOutDir 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'publish failed: ai' }
    Write-Host '[build] ai done'
}

function Start-Native-Ai {
    $exe = Join-Path $AiOutDir 'bh-ai.exe'
    if (-not (Test-Path $exe)) { throw "ai not built: $exe (run 'build' first)" }
    if (Test-PortOpen 8791) {
        $pid2 = Get-Content $AiPidFile -ErrorAction SilentlyContinue
        $proc = Get-Process -Id $pid2 -ErrorAction SilentlyContinue
        if ($proc) { Write-Host '[ai] already running (pid='$pid2')'; return }
        Write-Warning '[ai] port 8791 in use by foreign process, skip'
        return
    }
    $envBlock = @{
        BAIHUA_HOME = $DataHome
        BAIHUA_SKIP_MUTEX = 'true'
        ASPNETCORE_URLS = 'http://127.0.0.1:8791'
        OpenObserve__Enabled = 'false'
        # 管理 API 访问控制：放行 Docker bridge / host-gateway 网段（webui 容器经 host.docker.internal 调用）
        BAIHUA_ADMIN_ALLOWED_NETS = '172.16.0.0/12,192.168.0.0/16'
    }
    foreach ($k in $envBlock.Keys) { Set-Item -Path ('Env:' + $k) -Value $envBlock[$k] }
    New-Item -ItemType Directory -Force -Path (Split-Path $AiPidFile), (Split-Path $AiLogFile) | Out-Null
    $p = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -RedirectStandardOutput $AiLogFile -RedirectStandardError "$AiLogFile.err" -PassThru -WindowStyle Hidden
    Set-Content -Path $AiPidFile -Value $p.Id
    Write-Host "[ai] started pid=$($p.Id) port=8791 log=$AiLogFile"
}

function Stop-Native-Ai {
    $stopped = $false
    if (Test-Path $AiPidFile) {
        $pid2 = [int](Get-Content $AiPidFile)
        $proc = Get-Process -Id $pid2 -ErrorAction SilentlyContinue
        if ($proc) { Stop-Process -Id $pid2 -Force -ErrorAction SilentlyContinue; Write-Host "[ai] stopped pid=$pid2"; $stopped = $true }
        Remove-Item $AiPidFile -Force -ErrorAction SilentlyContinue
    }
    if (-not $stopped -and (Test-PortOpen 8791)) {
        $conn = Get-NetTCPConnection -LocalPort 8791 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($conn) { Stop-Process -Id $conn.OwningProcess -Force -ErrorAction SilentlyContinue; Write-Host "[ai] stopped by port pid=$($conn.OwningProcess)" }
    }
}

# ---- compose services ----

function Invoke-Build {
    Invoke-Build-Ai
    Write-Host '[build] compose images (family/vault/webui) ...'
    Invoke-Compose @('build', 'family', 'vault', 'webui')
    Write-Host '[build] done'
}

function Start-Services {
    # 本地推理依赖（OpenVINO 宿主）最先启动，再启动 native ai 与 compose 容器
    Start-OpenVinoHost
    Start-Native-Ai
    Invoke-Compose @('up', '-d')
    Write-Host '[start] waiting for health ...'
    $deadline = (Get-Date).AddSeconds(180)
    while ((Get-Date) -lt $deadline) {
        $unhealthy = & docker compose -f $Compose -f $ComposeWin ps --format '{{.Name}} {{.Status}}' 2>$null |
            Where-Object { $_ -notmatch '\(healthy\)' }
        if (-not $unhealthy) { Write-Host '[start] all services healthy'; return }
        Start-Sleep -Seconds 5
    }
    Write-Warning '[start] some services not healthy yet (check `status`)'
}

function Stop-Services {
    # 停止顺序：先停 compose 容器（family/vault/webui/nginx 依赖 native ai），
    # 再停 native ai，最后停本地推理依赖 OpenVINO 宿主。
    Invoke-Compose @('down')
    Stop-Native-Ai
    Stop-OpenVinoHost
    Write-Host '[stop] done'
}

function Show-Status {
    $aiState = if (Test-PortOpen 8791) { 'RUNNING (port 8791)' } else { 'stopped' }
    Write-Host "ai (native):  $aiState"
    $ovSvc = Get-OpenVinoHostService
    $ovState = if ($ovSvc) { $ovSvc.Status.ToString() } else { 'not installed' }
    if (Test-PortOpen $OpenVinoPort) { $ovState = 'RUNNING (port 8866)' }
    Write-Host "openvino:     $ovState (port $OpenVinoPort)"
    Write-Host ''
    Invoke-Compose @('ps')
}

function Show-Logs($svcName, $n) {
    if ($svcName -eq 'ai') {
        if (-not (Test-Path $AiLogFile)) { Write-Host "no log yet: $AiLogFile"; return }
        Get-Content $AiLogFile -Tail $n
        return
    }
    if ($svcName -notin $Services) { Write-Host "unknown service: $svcName (ai|$($Services -join '|'))"; return }
    Invoke-Compose @('logs', '--tail', "$n", $svcName)
}

function Open-Dashboard {
    try {
        $resp = Invoke-WebRequest -Uri 'http://127.0.0.1:5177/api/auth/cli-token' -Method POST -UseBasicParsing -TimeoutSec 5
        $token = ($resp.Content | ConvertFrom-Json).token
        Start-Process "http://127.0.0.1:5177/?cli-token=$token"
        Write-Host '[dashboard] opened with cli-token'
    } catch {
        Write-Host "[dashboard] cli-token failed ($($_.Exception.Message)), opening plain URL"
        Start-Process 'http://127.0.0.1:5177'
    }
}

switch ($Command.ToLower()) {
    'build'     { Invoke-Build }
    'start'     { Start-Services }
    'stop'      { Stop-Services }
    'restart'   { Stop-Services; Start-Services }
    'status'    { Show-Status }
    'logs'      { $count = 50; if ($Arg2) { $count = [int]$Arg2 }; Show-Logs $Arg1 $count }
    'dashboard' { Open-Dashboard }
    'open'      { Start-Process 'http://127.0.0.1:5177' }
    'help'      { Get-Content $PSCommandPath | Where-Object { $_ -match '^\s{4}[a-z]' } | ForEach-Object { $_.Trim() } }
    default     { Get-Content $PSCommandPath | Where-Object { $_ -match '^\s{4}[a-z]' } | ForEach-Object { $_.Trim() } }
}
