#requires -Version 5.1
<#
  baihua - Windows + Docker compose CLI
  Cell of the matrix: OS=windows, deployment=docker
  Manages the compose stack (family/ai/vault/webui/nginx/openobserve) via docker compose.

  Usage: .\bh-win-docker.ps1 <command> [args]
    build               docker compose build (family/ai/vault/webui)
    start               up -d (start all containers)
    stop                down (stop & remove containers, keep volumes)
    restart             down + start
    status              docker compose ps
    logs <svc> [n]      tail container logs (default 50)
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

# compose 服务列表（与 docker-compose.yml 对齐）
$Services = @('family', 'ai', 'vault', 'webui', 'nginx', 'openobserve')

function Invoke-Compose {
    param([string[]]$ComposeArgs)
    Push-Location $DockerDir
    try {
        # PS 5.1：docker compose 把进度写 stderr，2>&1 在 $ErrorActionPreference='Stop' 下会抛 NativeCommandError
        $prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & docker compose -f $Compose -f $ComposeWin @ComposeArgs 2>&1
        $code = $LASTEXITCODE
        $ErrorActionPreference = $prev
        if ($code -ne 0) { throw "docker compose failed (exit $code): $($ComposeArgs -join ' ')" }
    } finally {
        Pop-Location
    }
}

function Invoke-Build {
    # 构建核心 4 服务（nginx/openobserve 用官方镜像，无需构建）
    Invoke-Compose @('build', 'family', 'ai', 'vault', 'webui')
    Write-Host '[build] done'
}

function Start-Services {
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
    # down 保留 volumes（数据不丢）；加 -v 才删卷
    Invoke-Compose @('down')
    Write-Host '[stop] done'
}

function Show-Status {
    Invoke-Compose @('ps')
}

function Show-Logs($svcName, $n) {
    if ($svcName -notin $Services) { Write-Host "unknown service: $svcName ($($Services -join '|'))"; return }
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
