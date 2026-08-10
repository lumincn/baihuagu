#requires -Version 5.1
<#
  baihua - Windows + Docker compose + native AI CLI
  Cell of the matrix: OS=windows, deployment=docker (ai runs native for GPU)

  ai 服务（Baihua.AI + Baihua.AI.Provider）始终 native 运行（Windows 进程，直接访问
  Arc GPU 做 LlamaSharp/ONNX/OpenVINO 推理）；family/vault/webui/nginx/openobserve 走
  docker compose。compose 里 ai 服务带 profile "docker-ai"（默认不启动），容器通过
  host.docker.internal:8791 访问 native ai。

  Usage: .\bh-win-docker.ps1 <command> [args]
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
$DockerDir = Join-Path $Root 'docker'
$Compose = Join-Path $DockerDir 'docker-compose.yml'
$ComposeWin = Join-Path $DockerDir 'docker-compose.windows.yml'

# native ai 进程管理（与 bh-win-native.ps1 同套 pid/日志约定）
$AiOutDir = Join-Path $Root 'out\native\ai'
$AiPidFile = Join-Path $Root 'out\native\pids\ai.pid'
$AiLogFile = Join-Path $Root 'out\native\logs\ai.log'
$DataHome = if ($env:BAIHUA_HOME) { $env:BAIHUA_HOME } else { Join-Path $HOME '.baihua' }

# compose 服务列表（不含 ai——ai 是 native 的）
$Services = @('family', 'vault', 'webui', 'nginx', 'openobserve')

function Invoke-Compose {
    param([string[]]$ComposeArgs)
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

# ---- native ai ----

function Invoke-Build-Ai {
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
    Stop-Native-Ai
    Invoke-Compose @('down')
    Write-Host '[stop] done'
}

function Show-Status {
    $aiState = if (Test-PortOpen 8791) { 'RUNNING (port 8791)' } else { 'stopped' }
    Write-Host "ai (native):  $aiState"
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
