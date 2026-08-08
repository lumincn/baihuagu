<#
百花 Family 版 - Windows (PowerShell) CLI — 全 Docker 模式
不再启动本地 dotnet 进程，全部服务通过 docker compose 管理。

用法: .\bh.ps1 [command] [args]
  bh.ps1                    打开 dashboard（自动 docker compose up -d，健康后自动登录打开浏览器）
  bh.ps1 dashboard          同上
  bh.ps1 start              启动所有服务（docker compose up -d，按需 build）
  bh.ps1 stop [name]        停止服务（不指定则停止全部）
  bh.ps1 restart [name]     重启服务（不指定则 down + up -d）
  bh.ps1 status             查看容器状态 + 健康检查
  bh.ps1 logs <name> [n]   查看某个服务日志（默认最近 50 行；末尾加 -f 跟随）
  bh.ps1 open               打开 Web 管理界面（Nginx 统一入口，默认 http://localhost/）
  bh.ps1 build              构建/重建 Docker 镜像（代码修改后执行）
  bh.ps1 down               停止并移除所有容器、网络（数据卷保留）
  bh.ps1 dev                开发模式（等价于 start + 跟随 webui 日志）
  bh.ps1 observe            启动 OpenObserve 可观测性（Docker profile: observability）
  bh.ps1 all                启动全部服务 + OpenObserve
  bh.ps1 setup              首次配置（交互：知识库路径）
  bh.ps1 version            显示脚本版本
  bh.ps1 help               显示帮助

说明:
- 全 Docker 模式：.NET 4 服务 (taskrunner / taskrunner-vault / taskrunner-ai / webui)
  + Nginx (baihua-nginx) + 可选 OpenObserve 全部通过 docker compose 管理
- 镜像: bh-family/taskrunner, bh-family/taskrunner-vault, bh-family/taskrunner-ai, bh-family/webui
- 数据持久化: ${env:LOCALAPPDATA}\baihua\ (data, logs, config)
- Windows Docker Desktop (WSL2 后端) 必须运行中
- Nginx 对外端口: 由环境变量 BAIHUA_NGINX_PORT 控制，默认 80
#>
[CmdletBinding()]
param(
    [string]$Command = 'dashboard',
    [string]$Arg,
    [string]$Browser = '',
    [switch]$NoLogin,
    [switch]$Force
)

# PowerShell 5.1 兼容性检查（推荐 pwsh 7+）
if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Host "[i] 检测到 Windows PowerShell $($PSVersionTable.PSVersion)（旧版）" -ForegroundColor Yellow
    Write-Host "    推荐使用 PowerShell 7+ (pwsh)：支持 UTF-8 无 BOM、并发等现代特性" -ForegroundColor Yellow
    Write-Host "    安装: winget install Microsoft.PowerShell" -ForegroundColor DarkGray
}

chcp 65001 | Out-Null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

$SCRIPT_VERSION = '3.0.0-docker'

# ============================ 路径 & 环境 ============================
function Get-HgRoot {
    if ($PSScriptRoot) { return $PSScriptRoot }
    if ($MyInvocation -and $MyInvocation.MyCommand -and $MyInvocation.MyCommand.Path) {
        return Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    return (Get-Location).Path
}

$HG_ROOT = Get-HgRoot
$DOCKER_DIR = Join-Path $HG_ROOT 'docker'
$COMPOSE_BASE = Join-Path $DOCKER_DIR 'docker-compose.yml'
$COMPOSE_WIN  = Join-Path $DOCKER_DIR 'docker-compose.windows.yml'

# 服务顺序（dashboard/start 里健康检查按此顺序等待）
$ServiceOrder = @('taskrunner-ai', 'taskrunner-vault', 'taskrunner', 'webui', 'nginx')
$DockerServiceMap = @{
    'ai'     = 'taskrunner-ai'
    'vault'  = 'taskrunner-vault'
    'family' = 'taskrunner'
    'taskrunner' = 'taskrunner'
    'webui'  = 'webui'
    'nginx'  = 'nginx'
    'openobserve' = 'openobserve'
}
$DisplayNameMap = @{
    'taskrunner-ai'    = 'AI'
    'taskrunner-vault' = 'Vault'
    'taskrunner'       = 'Family'
    'webui'            = 'WebUI'
    'nginx'            = 'Nginx'
    'openobserve'      = 'OpenObserve'
}
$HealthUrls = @{
    'taskrunner-ai'    = 'http://127.0.0.1:8791/health'
    'taskrunner-vault' = 'http://127.0.0.1:8790/health'
    'taskrunner'       = 'http://127.0.0.1:8788/health'
    'webui'            = 'http://127.0.0.1:5177/'
    'nginx'            = $null   # Nginx 通过 /mg/health 间接检查
    'openobserve'      = 'http://127.0.0.1:5082/'
}

# Docker compose 公共参数
$Global:ComposeArgs = @(
    'compose',
    '-f', $COMPOSE_BASE,
    '-f', $COMPOSE_WIN,
    '--project-directory', $DOCKER_DIR
)

# ============================ Docker / 环境检测 ============================
function Get-DockerCmd {
    foreach ($cmd in @('docker', 'docker.exe')) {
        try { Get-Command $cmd -ErrorAction Stop | Out-Null; return $cmd } catch {}
    }
    return $null
}

function Test-DockerRunning {
    $docker = Get-DockerCmd
    if (-not $docker) { return $false }
    try {
        $null = & $docker info 2>&1
        return ($LASTEXITCODE -eq 0)
    } catch { return $false }
}

function Ensure-DockerReady {
    $docker = Get-DockerCmd
    if (-not $docker) {
        Write-Host "[X] Docker 命令未找到。请先安装并启动 Docker Desktop for Windows。" -ForegroundColor Red
        Write-Host "    下载: https://www.docker.com/products/docker-desktop/" -ForegroundColor DarkGray
        return $false
    }
    if (-not (Test-DockerRunning)) {
        Write-Host "[X] Docker daemon 未运行。请先启动 Docker Desktop，等待状态变为 running。" -ForegroundColor Red
        return $false
    }
    if (-not (Test-Path $COMPOSE_BASE)) {
        Write-Host "[X] Compose 文件不存在: $COMPOSE_BASE" -ForegroundColor Red
        return $false
    }
    if (-not (Test-Path $COMPOSE_WIN)) {
        Write-Host "[X] Windows compose override 不存在: $COMPOSE_WIN" -ForegroundColor Red
        return $false
    }
    return $true
}

# ============================ 环境变量（注入 compose）============================
function Set-BaihuaEnv {
    # Windows 默认值（仅当未设置时才赋值，不覆盖用户显式设置）
    if ([string]::IsNullOrWhiteSpace($env:BAIHUA_HOME)) {
        $env:BAIHUA_HOME = Join-Path $env:LOCALAPPDATA 'baihua'
    }
    if ([string]::IsNullOrWhiteSpace($env:BAIHUA_NGINX_PORT)) {
        $env:BAIHUA_NGINX_PORT = '80'
    }
    if ([string]::IsNullOrWhiteSpace($env:BAIHUA_WEBUI_PREFIX)) {
        $env:BAIHUA_WEBUI_PREFIX = ''
    }
    if ([string]::IsNullOrWhiteSpace($env:BAIHUA_NGINX_CLIENT_MAX_BODY_SIZE)) {
        $env:BAIHUA_NGINX_CLIENT_MAX_BODY_SIZE = '100M'
    }
    if ([string]::IsNullOrWhiteSpace($env:VAULTS_DIR)) {
        $env:VAULTS_DIR = Join-Path $env:USERPROFILE 'Vaults'
    }

    # Windows Docker Desktop 通过 NGINX_ENVSUBST_FILTER 只渲染 BAIHUA_* 变量
    if ([string]::IsNullOrWhiteSpace($env:NGINX_ENVSUBST_FILTER)) {
        $env:NGINX_ENVSUBST_FILTER = '^BAIHUA_'
    }

    # 创建必需目录（容器卷挂载要求父目录存在；即使容器内用 root，Windows 侧也需创建）
    $dirs = @(
        (Join-Path $env:BAIHUA_HOME 'data'),
        (Join-Path $env:BAIHUA_HOME 'logs'),
        (Join-Path $env:BAIHUA_HOME 'logs\nginx'),
        (Join-Path $env:BAIHUA_HOME 'config\taskrunner'),
        (Join-Path $env:BAIHUA_HOME 'config\taskrunner-vault'),
        (Join-Path $env:BAIHUA_HOME 'config\taskrunner-ai'),
        (Join-Path $env:BAIHUA_HOME 'config\webui')
    )
    foreach ($d in $dirs) {
        if (-not (Test-Path $d)) {
            New-Item -ItemType Directory -Path $d -Force | Out-Null
        }
    }
    if (-not (Test-Path $env:VAULTS_DIR)) {
        New-Item -ItemType Directory -Path $env:VAULTS_DIR -Force | Out-Null
        Write-Host "[i] 已创建知识库目录: $env:VAULTS_DIR" -ForegroundColor DarkGray
    }
}

# ============================ Compose 调用包装 ============================
function Invoke-Compose {
    param([string[]]$Arguments)
    $docker = Get-DockerCmd
    $allArgs = $Global:ComposeArgs + $Arguments
    & $docker @allArgs
    return $LASTEXITCODE
}

function Invoke-ComposeOutput {
    param([string[]]$Arguments)
    $docker = Get-DockerCmd
    $allArgs = $Global:ComposeArgs + $Arguments
    return (& $docker @allArgs 2>&1)
}

function Test-ContainerRunning($svcName) {
    $lines = Invoke-ComposeOutput @('ps', '--format', '{{.Service}} {{.State}}', $svcName)
    foreach ($line in $lines) {
        if ($line -match "^$svcName\s+(running|healthy)") { return $true }
    }
    return $false
}

function Resolve-ServiceName($name) {
    if ([string]::IsNullOrWhiteSpace($name)) { return $null }
    $n = $name.ToLower()
    if ($DockerServiceMap.ContainsKey($n)) { return $DockerServiceMap[$n] }
    # 用户直接写 compose 服务名（taskrunner / taskrunner-ai 等）
    if ($DockerServiceMap.Values -contains $n) { return $n }
    return $null
}

# ============================ 工具函数 ============================
function Test-TcpPort([string]$hostname, [int]$port, [int]$timeoutMs = 2000) {
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $async = $tcp.BeginConnect($hostname, $port, $null, $null)
        $wait = $async.AsyncWaitHandle.WaitOne($timeoutMs, $false)
        if ($wait -and $tcp.Connected) { $tcp.Close(); return $true }
        $tcp.Close(); return $false
    } catch { return $false }
}

function Wait-For-Url([string]$url, [int]$timeoutSec = 60, [int]$intervalSec = 2) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        try {
            $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
            if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 500) { return $true }
        } catch {}
        Start-Sleep -Seconds $intervalSec
    }
    return $false
}

# 启动 Docker 容器前，清理本地 dotnet 进程占用的端口（8788/8790/8791/5177/80）
# 避免"旧本地进程 + Docker 容器"端口绑定冲突（双实例跑数据目录也会 SQLite 锁冲突）
# 注意：只杀真正的本地 dotnet 服务进程；wslrelay/com.docker.backend 等 Docker 端口转发进程绝不能杀
function Stop-LocalDotnetServicesIfPortsOccupied {
    $ports = @(8788, 8790, 8791, 5177)
    $killedAny = $false
    foreach ($port in $ports) {
        try {
            $conns = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction Stop
        } catch { continue }
        foreach ($c in $conns) {
            $procId = $c.OwningProcess
            if ($procId -le 4) { continue }   # PID 4 = System (http.sys)，不杀
            try {
                $proc = Get-Process -Id $procId -ErrorAction Stop
                # 仅杀百花/dotnet 本地服务进程；Docker 转发进程(wslrelay/com.docker.backend)直接跳过
                $isDotnetFamily = ($proc.ProcessName -match '^(dotnet|bh-|baihua|taskrunner)$') -or
                                   ($proc.Path -and $proc.Path -match 'baihuagu|baihua|taskrunner')
                $isDockerProxy = $proc.ProcessName -match '^(wslrelay|com\.docker\.backend|com\.docker\.service|vpnkit|docker)$'
                if ($isDockerProxy) { continue }
                if ($isDotnetFamily) {
                    Write-Host "  [端口清理] 释放 :$port (PID $procId, $($proc.ProcessName))..." -ForegroundColor Yellow
                    try { taskkill /T /F /PID $procId 2>&1 | Out-Null } catch { Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue }
                    $killedAny = $true
                }
            } catch {}
        }
    }
    if ($killedAny) { Start-Sleep -Seconds 2 }
}

function Get-WebUrl {
    $port = if ($env:BAIHUA_NGINX_PORT) { $env:BAIHUA_NGINX_PORT } else { '80' }
    if ($port -eq '80') { return 'http://localhost/' }
    return "http://localhost:$port/"
}
function Get-LoginUrl { return (Get-WebUrl) }  # WebUI 根路径会自动跳登录页
function Get-CliTokenUrl {
    # CLI token 端点在 WebUI(5177)，不是 Family(8788)
    # Docker 模式直接访问 WebUI 容器映射端口；Nginx 会把 /api/* 转发到 Family，到不了 WebUI
    return 'http://127.0.0.1:5177/api/auth/cli-token'
}
function Get-DashboardUrl($token) {
    $base = Get-WebUrl
    $sep = if ($base -match '\?') { '&' } else { '?' }
    return "${base}${sep}cli-token=$token"
}

function Open-InBrowser([string]$url) {
    $browser = $script:Browser
    if ($browser) {
        Write-Host "Opening: $url (browser: $browser)"
        try { Start-Process $browser $url } catch { Write-Host "Cannot launch browser '${browser}': ${_}" }
    } else {
        Write-Host "Opening: $url"
        try { Start-Process $url } catch { Write-Host "Cannot open browser: ${_}" }
    }
}

function Wait-ServiceReady($svcName, [int]$timeoutSec = 90) {
    $display = if ($DisplayNameMap.ContainsKey($svcName)) { $DisplayNameMap[$svcName] } else { $svcName }
    $healthUrl = $HealthUrls[$svcName]
    if (-not $healthUrl) {
        # 没有 HTTP 健康检查的服务（如 nginx 本身），等容器进入 running 即可
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
            if (Test-ContainerRunning $svcName) { Write-Host "  ${display}: ✓ container up" -ForegroundColor Green; return $true }
            Start-Sleep -Seconds 2
        }
        Write-Host "  ${display}: ⚠ timeout after ${timeoutSec}s" -ForegroundColor Yellow
        return $false
    }
    Write-Host "  ${display} : " -NoNewline
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $dots = 0
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        try {
            $resp = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
            if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 500) {
                Write-Host "✓ ready" -ForegroundColor Green
                return $true
            }
        } catch {}
        Write-Host "." -NoNewline
        $dots++
        if ($dots % 15 -eq 0) { Write-Host ""; Write-Host "  ${display} : " -NoNewline }
        Start-Sleep -Seconds 2
    }
    Write-Host " ⚠ timeout after ${timeoutSec}s" -ForegroundColor Yellow
    return $false
}

# ============================ 子命令实现 ============================

# 宿主机 dotnet 路径
function Get-DotnetCmd {
    foreach ($cmd in @('dotnet', 'dotnet.exe')) {
        try { Get-Command $cmd -ErrorAction Stop | Out-Null; return $cmd } catch {}
    }
    # 尝试默认安装路径
    $defaultPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path $defaultPath) { return $defaultPath }
    return $null
}

function Cmd-Build {
    Write-Host "=== 构建百花 Docker 镜像 ===" -ForegroundColor Cyan

    $dotnet = Get-DotnetCmd
    if (-not $dotnet) {
        Write-Host "[!] 未找到 dotnet，回退到容器内构建（可能较慢）" -ForegroundColor Yellow
        $exit = Invoke-Compose @('build')
        if ($exit -eq 0) { Write-Host "[✓] 镜像构建完成" -ForegroundColor Green }
        else { Write-Host "[✗] 镜像构建失败 (exit=$exit)" -ForegroundColor Red }
        return
    }

    # ---- 预构建模式：宿主机 publish → Docker 仅打包 ----
    Write-Host "[i] 使用预构建模式（宿主机 dotnet publish + Docker 打包）" -ForegroundColor DarkGray
    $publishRoot = Join-Path $DOCKER_DIR 'publish'

    $projects = @(
        @{ Name = 'Family'; Csproj = 'services\Baihua.Family\Baihua.Family.csproj'; Out = 'family';    Dockerfile = 'Dockerfile.taskrunner.prebuilt';     Image = 'bh-family/taskrunner:latest' }
        @{ Name = 'Vault';  Csproj = 'services\Baihua.Vault\Baihua.Vault.csproj';  Out = 'vault';     Dockerfile = 'Dockerfile.vault.prebuilt';          Image = 'bh-family/taskrunner-vault:latest' }
        @{ Name = 'AI';     Csproj = 'services\Baihua.AI\Baihua.AI.csproj';         Out = 'ai';        Dockerfile = 'Dockerfile.taskrunner.ai.prebuilt';  Image = 'bh-family/taskrunner-ai:latest' }
        @{ Name = 'WebUI';  Csproj = 'services\Baihua.Web\Baihua.Web.csproj';      Out = 'webui';     Dockerfile = 'Dockerfile.webui.prebuilt';           Image = 'bh-family/webui:latest' }
    )

    # 确保基础镜像存在
    $needBase = $false
    foreach ($img in @('bh-family/base-build:latest', 'bh-family/base-runtime:latest')) {
        $check = docker images --format '{{.Repository}}:{{.Tag}}' $img 2>$null
        if (-not $check) { $needBase = $true; break }
    }
    if ($needBase) {
        Write-Host "构建基础镜像..." -ForegroundColor DarkGray
        Push-Location $HG_ROOT
        try {
            docker build -f (Join-Path $DOCKER_DIR 'Dockerfile.base-build') -t bh-family/base-build:latest . 2>&1 | Write-Host
            docker build -f (Join-Path $DOCKER_DIR 'Dockerfile.base-runtime') -t bh-family/base-runtime:latest . 2>&1 | Write-Host
        } finally { Pop-Location }
    }

    $allOk = $true
    foreach ($p in $projects) {
        $outDir = Join-Path $publishRoot $p.Out
        $csprojPath = Join-Path $HG_ROOT $p.Csproj

        Write-Host ""
        Write-Host "--- $($p.Name) ---" -ForegroundColor Cyan
        Write-Host "  publish: $csprojPath → $outDir"
        & $dotnet publish $csprojPath -c Release -o $outDir --nologo 2>&1 | ForEach-Object {
            if ($_ -match 'error|Error') { Write-Host "  $_" -ForegroundColor Red }
            elseif ($_ -match '->') { Write-Host "  $_" -ForegroundColor DarkGray }
        }
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  [✗] $($p.Name) publish 失败" -ForegroundColor Red
            $allOk = $false
            continue
        }

        Write-Host "  docker build: $($p.Dockerfile) → $($p.Image)"
        Push-Location $DOCKER_DIR
        try {
            docker build -f $p.Dockerfile -t $p.Image . 2>&1 | ForEach-Object {
                if ($_ -match '^#[0-9]') { Write-Host "  $_" -ForegroundColor DarkGray }
                elseif ($_ -match 'ERROR|error') { Write-Host "  $_" -ForegroundColor Red }
                elseif ($_ -match 'naming to|DONE') { Write-Host "  $_" -ForegroundColor DarkGray }
            }
        } finally { Pop-Location }
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  [✗] $($p.Name) Docker 镜像构建失败" -ForegroundColor Red
            $allOk = $false
        } else {
            Write-Host "  [✓] $($p.Name) 镜像构建完成" -ForegroundColor Green
        }
    }

    if ($allOk) { Write-Host "`n[✓] 全部镜像构建完成" -ForegroundColor Green }
    else { Write-Host "`n[✗] 部分镜像构建失败，请检查上方日志" -ForegroundColor Red }
}

function Cmd-UpCore([switch]$WithObservability) {
    Set-BaihuaEnv
    Stop-LocalDotnetServicesIfPortsOccupied
    # 镜像不存在时先走预构建（宿主 publish 快），避免 compose 自动容器内编译（慢/易超时）
    $missing = @()
    foreach ($img in @('bh-family/taskrunner:latest', 'bh-family/taskrunner-vault:latest', 'bh-family/taskrunner-ai:latest', 'bh-family/webui:latest')) {
        $check = docker images --format '{{.Repository}}:{{.Tag}}' $img 2>$null
        if (-not $check) { $missing += $img }
    }
    if ($missing.Count -gt 0) {
        Write-Host "[i] 缺少镜像: $($missing -join ', ') → 先 build ..." -ForegroundColor Yellow
        Cmd-Build
    }
    $profiles = @()
    if ($WithObservability) { $profiles += '--profile'; $profiles += 'observability' }
    $upArgs = $profiles + @('up', '-d', '--remove-orphans')
    $exit = Invoke-Compose $upArgs
    if ($exit -ne 0) {
        Write-Host "[!] 启动失败，尝试先 build 再启动 ..." -ForegroundColor Yellow
        Cmd-Build
        $exit = Invoke-Compose $upArgs
    }
    return $exit
}

function Cmd-Start {
    Write-Host "=== 启动百花（全 Docker）===" -ForegroundColor Cyan
    if (-not (Ensure-DockerReady)) { return }
    $exit = Cmd-UpCore
    if ($exit -ne 0) { Write-Host "[✗] 启动失败 (exit=$exit)" -ForegroundColor Red; return }
    Write-Host ""
    Write-Host "等待服务就绪..." -ForegroundColor DarkGray
    foreach ($svc in $ServiceOrder) { Wait-ServiceReady $svc | Out-Null }
}

function Cmd-Stop {
    if (-not (Ensure-DockerReady)) { return }
    Set-BaihuaEnv
    if ($Arg) {
        $name = $Arg.ToLower()
        if ($name -eq 'all') {
            Write-Host "停止所有服务..." -ForegroundColor Cyan
            Invoke-Compose @('stop') | Out-Null
            return
        }
        $svc = Resolve-ServiceName $name
        if (-not $svc) {
            $valid = ($DockerServiceMap.Keys | Sort-Object) -join ', '
            Write-Host "[!] 未知服务: $name（可选: $valid, all）" -ForegroundColor Yellow
            return
        }
        Write-Host "停止 $svc ..." -ForegroundColor Cyan
        Invoke-Compose @('stop', $svc) | Out-Null
        return
    }
    Write-Host "停止所有服务 ..." -ForegroundColor Cyan
    Invoke-Compose @('stop') | Out-Null
}

function Cmd-Down {
    if (-not (Ensure-DockerReady)) { return }
    Set-BaihuaEnv
    Write-Host "⚠  这将移除所有容器和网络（数据卷保留在 ${env:BAIHUA_HOME}\）" -ForegroundColor Yellow
    if (-not $script:Force) {
        $confirm = Read-Host "确认? [y/N]"
        if ($confirm -notmatch '^[yY]') { Write-Host "已取消"; return }
    }
    Invoke-Compose @('down', '--remove-orphans') | Out-Null
    Write-Host "[✓] 已移除所有容器（数据保留在 ${env:BAIHUA_HOME}\）" -ForegroundColor Green
}

function Cmd-Restart {
    if (-not (Ensure-DockerReady)) { return }
    Set-BaihuaEnv
    if ($Arg) {
        $svc = Resolve-ServiceName $Arg.ToLower()
        if (-not $svc) {
            $valid = ($DockerServiceMap.Keys | Sort-Object) -join ', '
            Write-Host "[!] 未知服务: $Arg（可选: $valid）" -ForegroundColor Yellow
            return
        }
        Write-Host "重启 $svc ..." -ForegroundColor Cyan
        # 用 up --force-recreate 而不是 restart：restart 不会应用新构建的镜像
        Invoke-Compose @('up', '-d', '--no-deps', '--force-recreate', $svc) | Out-Null
        Start-Sleep -Seconds 2
        Wait-ServiceReady $svc | Out-Null
        return
    }
    # 整体重启：stop 再 up -d（保证环境变量和镜像最新）
    Write-Host "重启所有服务（stop → up -d）..." -ForegroundColor Cyan
    Invoke-Compose @('stop') | Out-Null
    Start-Sleep -Seconds 2
    $exit = Cmd-UpCore
    if ($exit -ne 0) { Write-Host "[✗] 重启失败 (exit=$exit)" -ForegroundColor Red; return }
    Write-Host ""
    foreach ($svc in $ServiceOrder) { Wait-ServiceReady $svc | Out-Null }
}

function Cmd-Status {
    if (-not (Ensure-DockerReady)) { return }
    Set-BaihuaEnv
    Write-Host ""
    Write-Host "=== 百花服务状态 (Docker Compose) ===" -ForegroundColor Cyan
    Write-Host "  BAIHUA_HOME     : $env:BAIHUA_HOME"
    Write-Host "  VAULTS_DIR      : $env:VAULTS_DIR"
    Write-Host "  Nginx 入口      : $(Get-WebUrl)"
    Write-Host ""
    $lines = Invoke-ComposeOutput @('ps')
    if (-not $lines -or $lines.Count -eq 0 -or ($lines.Count -eq 1 -and [string]::IsNullOrWhiteSpace($lines[0]))) {
        Write-Host "  (no containers — 服务未启动。运行: .\bh.ps1 start)" -ForegroundColor Yellow
        return
    }
    $lines | ForEach-Object { Write-Host "  $_" }
    Write-Host ""
    Write-Host "--- 健康检查 ---" -ForegroundColor DarkGray
    foreach ($svc in $ServiceOrder) {
        $running = Test-ContainerRunning $svc
        $display = if ($DisplayNameMap.ContainsKey($svc)) { $DisplayNameMap[$svc] } else { $svc }
        if ($running) {
            $healthUrl = $HealthUrls[$svc]
            $ok = $false
            if ($healthUrl) {
                try {
                    $resp = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
                    $ok = ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 500)
                } catch {}
            } else { $ok = $true }
            if ($ok) { Write-Host "  ${display}: ✓ running / healthy" -ForegroundColor Green }
            else { Write-Host "  ${display}: ⚠ container up, HTTP not ready" -ForegroundColor Yellow }
        } else {
            Write-Host "  ${display}: ✗ stopped" -ForegroundColor DarkYellow
        }
    }
}

function Cmd-Logs {
    if (-not (Ensure-DockerReady)) { return }
    Set-BaihuaEnv

    $raw = $Arg
    if ([string]::IsNullOrWhiteSpace($raw)) {
        $valid = ($DockerServiceMap.Keys | Sort-Object) -join ', '
        Write-Host "请指定服务名: $valid" -ForegroundColor Yellow
        Write-Host "示例: .\bh.ps1 logs webui 100     # webui 最近 100 行"
        Write-Host "示例: .\bh.ps1 logs family -f     # 实时跟随 Family 日志" -ForegroundColor DarkGray
        return
    }

    # 解析 -f / lines
    $follow = $false
    $lines = 50
    $svcInput = $null
    $tokens = @()
    if (-not [string]::IsNullOrWhiteSpace($raw)) { $tokens += $raw }
    if (-not [string]::IsNullOrWhiteSpace($Browser)) { $tokens += $Browser }
    foreach ($t in $tokens) {
        if ($t -eq '-f' -or $t -eq '--follow') { $follow = $true }
        elseif ($t -match '^\d+$') { $lines = [int]$t }
        else { $svcInput = $t }
    }

    $svc = Resolve-ServiceName $svcInput
    if (-not $svc) {
        $valid = ($DockerServiceMap.Keys | Sort-Object) -join ', '
        Write-Host "[!] 未知服务: $svcInput（可选: $valid）" -ForegroundColor Yellow
        return
    }

    $composeArgs = @('logs', "--tail=$lines")
    if ($follow) { $composeArgs += '-f' }
    $composeArgs += $svc
    Write-Host "[i] $svc 日志 (tail=$lines$(if ($follow) {', follow'} else {''}))."
    Write-Host "    Ctrl+C 退出" -ForegroundColor DarkGray
    Invoke-Compose $composeArgs
}

function Cmd-Observe {
    if (-not (Ensure-DockerReady)) { return }
    Set-BaihuaEnv
    Write-Host "启动 OpenObserve（可观测性, profile: observability）..." -ForegroundColor Cyan
    $exit = Invoke-Compose @('--profile', 'observability', 'up', '-d', 'openobserve')
    if ($exit -ne 0) { Write-Host "[✗] OpenObserve 启动失败 (exit=$exit)" -ForegroundColor Red; return }
    Write-Host "等待 OpenObserve 就绪 (端口 5082)..."
    if (Wait-For-Url 'http://127.0.0.1:5082/' 60) {
        Write-Host "[✓] OpenObserve ready: http://127.0.0.1:5082/ (默认 root@localhost.com / Complexpass#123)" -ForegroundColor Green
        Open-InBrowser 'http://127.0.0.1:5082/'
    } else {
        Write-Host "[!] 60s 内未就绪，稍后手动打开: http://127.0.0.1:5082/" -ForegroundColor Yellow
    }
}

function Cmd-All {
    if (-not (Ensure-DockerReady)) { return }
    Write-Host "=== 百花 - 启动全部服务（含 OpenObserve）===" -ForegroundColor Cyan
    $exit = Cmd-UpCore -WithObservability
    if ($exit -ne 0) { Write-Host "[✗] 启动失败 (exit=$exit)" -ForegroundColor Red; return }
    Write-Host ""
    foreach ($svc in $ServiceOrder) { Wait-ServiceReady $svc | Out-Null }
    if (Wait-For-Url 'http://127.0.0.1:5082/' 60) {
        Write-Host "  OpenObserve: ✓ ready http://127.0.0.1:5082/" -ForegroundColor Green
    } else {
        Write-Host "  OpenObserve: ⚠ 启动中" -ForegroundColor Yellow
    }
}

function Cmd-Setup {
    Write-Host "*** 百花首次配置（Docker 版）***" -ForegroundColor Cyan
    $vault = Read-Host "知识库路径 (默认: $env:USERPROFILE\Vaults)"
    if (-not [string]::IsNullOrWhiteSpace($vault)) {
        if (-not (Test-Path $vault)) { New-Item -ItemType Directory -Path $vault -Force | Out-Null; Write-Host "已创建: $vault" }
        # 保存为永久用户环境变量（下次打开终端也生效）
        [Environment]::SetEnvironmentVariable('VAULTS_DIR', $vault, 'User')
        $env:VAULTS_DIR = $vault
        Write-Host "[✓] 已永久设置 VAULTS_DIR=$vault（用户环境变量）" -ForegroundColor Green
    } else {
        Write-Host "(未设置，使用默认: $env:USERPROFILE\Vaults)"
    }
    $port = Read-Host "Nginx 对外端口 (默认: 80)"
    if (-not [string]::IsNullOrWhiteSpace($port)) {
        $pInt = 0
        if ([int]::TryParse($port, [ref]$pInt) -and $pInt -gt 0 -and $pInt -lt 65536) {
            [Environment]::SetEnvironmentVariable('BAIHUA_NGINX_PORT', $port, 'User')
            $env:BAIHUA_NGINX_PORT = $port
            Write-Host "[✓] 已永久设置 BAIHUA_NGINX_PORT=$port" -ForegroundColor Green
        } else { Write-Host "[!] 端口无效，保留默认 80" -ForegroundColor Yellow }
    }
    $home = Read-Host "数据/日志根目录 (默认: $env:LOCALAPPDATA\baihua)"
    if (-not [string]::IsNullOrWhiteSpace($home)) {
        [Environment]::SetEnvironmentVariable('BAIHUA_HOME', $home, 'User')
        $env:BAIHUA_HOME = $home
        Write-Host "[✓] 已永久设置 BAIHUA_HOME=$home" -ForegroundColor Green
    }
    Set-BaihuaEnv
    Write-Host ""
    Write-Host "配置完成。下次启动生效。立即启动: .\bh.ps1 start" -ForegroundColor Green
}

function Cmd-Dev {
    # Docker 版 dev：先 start（镜像若过期先 build），然后跟随 webui 日志
    Write-Host "=== 百花 Dev Mode (Docker，不支持热重载) ===" -ForegroundColor Cyan
    Write-Host "  注意：Docker 模式下改 .cs/.razor 不会自动热重载。" -ForegroundColor Yellow
    Write-Host "  修改代码后：先 .\bh.ps1 build 再 .\bh.ps1 restart [webui|family|...]" -ForegroundColor Yellow
    if (-not (Ensure-DockerReady)) { return }
    $exit = Cmd-UpCore
    if ($exit -ne 0) { Write-Host "[✗] 启动失败 (exit=$exit)" -ForegroundColor Red; return }
    Write-Host ""
    foreach ($svc in $ServiceOrder) { Wait-ServiceReady $svc | Out-Null }
    if (-not $NoLogin) { Cmd-OpenDashboardCore }
    Write-Host ""
    Write-Host "[i] 进入跟随 webui 日志（Ctrl+C 退出，服务不受影响）" -ForegroundColor DarkGray
    Invoke-Compose @('logs', '-f', '--tail=50', 'webui')
}

function Cmd-OpenDashboardCore {
    Write-Host "准备打开管理面板..." -ForegroundColor DarkGray
    $loginUrl = Get-LoginUrl
    if (-not (Wait-For-Url $loginUrl 30)) {
        Write-Host "[!] WebUI 尚未就绪，直接打开浏览器" -ForegroundColor Yellow
        Open-InBrowser (Get-WebUrl)
        return
    }
    try {
        $resp = Invoke-WebRequest -Uri (Get-CliTokenUrl) -Method POST -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
        if ($resp.StatusCode -eq 200) {
            $json = $resp.Content | ConvertFrom-Json
            $token = $json.token
            if ($token) {
                $url = Get-DashboardUrl $token
                Write-Host "[i] 已获取 CLI token，自动登录..."
                Open-InBrowser $url
                return
            }
        }
    } catch {
        Write-Host "[!] 获取 CLI token 失败: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    Open-InBrowser (Get-WebUrl)
}

function Cmd-Dashboard {
    Write-Host "=== 百花 Dashboard (Docker 模式) ===" -ForegroundColor Cyan
    if (-not (Ensure-DockerReady)) { return }
    $exit = Cmd-UpCore
    if ($exit -ne 0) { Write-Host "[✗] 启动失败 (exit=$exit)" -ForegroundColor Red; return }
    Write-Host ""
    foreach ($svc in $ServiceOrder) { Wait-ServiceReady $svc | Out-Null }
    if ($NoLogin) {
        Write-Host "[i] --nologin: 服务已就绪 → $(Get-WebUrl)" -ForegroundColor DarkGray
        return
    }
    Cmd-OpenDashboardCore
}

function Show-Help {
    Set-BaihuaEnv
    Write-Host ""
    Write-Host "百花 Family 版 CLI - 全 Docker 模式  v$SCRIPT_VERSION" -ForegroundColor Cyan
    Write-Host "============================================"
    Write-Host ""
    Write-Host "用法: .\bh.ps1 [command] [args]"
    Write-Host ""
    Write-Host "Commands:"
    Write-Host "  dashboard             启动容器并打开面板（默认）"
    Write-Host "  dashboard --nologin   同上但跳过浏览器自动登录"
    Write-Host "  start                 docker compose up -d 所有核心服务"
    Write-Host "  build                 docker compose build 重建镜像（改代码后）"
    Write-Host "  stop [name|all]       停止某个或所有服务（默认 all）"
    Write-Host "  restart [name]        重启某个或所有服务"
    Write-Host "  down                  停止并移除所有容器（数据卷保留）"
    Write-Host "  status                显示所有容器状态与健康检查"
    Write-Host "  logs <name> [lines]   查看日志（默认 50 行；末尾加 -f 跟随）"
    Write-Host "  open                  打开 WebUI（Nginx 统一入口 $(Get-WebUrl)）"
    Write-Host "  dev                   启动并跟随 webui 日志（改代码需先 build + restart）"
    Write-Host "  observe               启动 OpenObserve (端口 5082)"
    Write-Host "  all                   启动核心服务 + OpenObserve"
    Write-Host "  setup                 首次配置（知识库/端口/数据目录）"
    Write-Host "  version               显示版本"
    Write-Host "  help                  显示帮助"
    Write-Host ""
    Write-Host "服务名映射（name 参数）:"
    foreach ($k in ($DockerServiceMap.Keys | Sort-Object)) {
        $v = $DockerServiceMap[$k]
        $display = if ($DisplayNameMap.ContainsKey($v)) { "=$($DisplayNameMap[$v])" } else { "" }
        Write-Host "  $k -> $v ${display}"
    }
    Write-Host ""
    Write-Host "环境变量（可永久写入用户环境变量）:"
    Write-Host "  BAIHUA_HOME              数据/日志/配置根  （默认: $env:LOCALAPPDATA\baihua）"
    Write-Host "  VAULTS_DIR               知识库根目录      （默认: $env:USERPROFILE\Vaults）"
    Write-Host "  BAIHUA_NGINX_PORT        Nginx 对外端口    （默认: 80）"
    Write-Host "  BAIHUA_WEBUI_PREFIX      WebUI 路径前缀    （默认: 空）"
    Write-Host ""
}

# ============================ 主入口 ============================
function Main {
    param(
        [string]$CommandName,
        [string]$ServiceArg,
        [string]$LineArg,
        [string]$BrowserArg,
        [switch]$NoLoginFlag,
        [switch]$ForceFlag
    )

    # logs 特判：位置参数 arg1=服务名 arg2=lines/-f 或 Browser=lines/-f
    $isLogs = ($CommandName -ieq 'logs')
    if ($isLogs) {
        # 重写 Arg 作为 svc，Browser 作为 lines/-f
        $script:Arg = $ServiceArg
        $script:Browser = if ($LineArg) { $LineArg } else { $BrowserArg }
        $CommandName = 'logs'
    }

    switch ($CommandName.ToLower()) {
        'help'       { Show-Help; break }
        'version'    { Write-Host "bh.ps1 (全 Docker) v$SCRIPT_VERSION"; break }
        'setup'      { Cmd-Setup; break }
        'build'      { Cmd-Build; break }
        'start'      { Cmd-Start; break }
        'stop'       { Cmd-Stop; break }
        'restart'    { Cmd-Restart; break }
        'down'       { Cmd-Down; break }
        'status'     { Cmd-Status; break }
        'logs'       { Cmd-Logs; break }
        'open'       { if (-not (Ensure-DockerReady)) { return }; Set-BaihuaEnv; Open-InBrowser (Get-WebUrl); break }
        'observe'    { Cmd-Observe; break }
        'all'        { Cmd-All; break }
        'dev'        { Cmd-Dev; break }
        'dashboard'  { Cmd-Dashboard; break }
        default {
            Write-Host "[X] 未知命令: $CommandName" -ForegroundColor Red
            Write-Host "输入 .\bh.ps1 help 查看可用命令" -ForegroundColor Yellow
        }
    }
}

# dot-source 导入时不执行
if ($MyInvocation.InvocationName -eq '.') { return }

# 位置参数解析
if (-not $NoLogin -and ($Arg -eq '--nologin' -or $Browser -eq '--nologin')) {
    $NoLogin = $true
    if ($Arg -eq '--nologin') { $Arg = '' }
    if ($Browser -eq '--nologin') { $Browser = '' }
}
$cmd = $Command
$svcArg = $Arg
if ($Browser -match '^-f$|^--follow$|^\d+$') {
    $lineArg = $Browser
    $browserArg = ''
} else {
    $lineArg = ''
    $browserArg = $Browser
}
if (-not $NoLogin -and ($svcArg -eq '--nologin' -or $browserArg -eq '--nologin')) {
    $NoLogin = $true
    if ($svcArg -eq '--nologin') { $svcArg = '' }
    if ($browserArg -eq '--nologin') { $browserArg = '' }
}
$script:Browser = $browserArg
$script:Force = $Force

Main -CommandName $cmd -ServiceArg $svcArg -LineArg $lineArg -BrowserArg $browserArg -NoLoginFlag:$NoLogin -ForceFlag:$Force

exit 0
